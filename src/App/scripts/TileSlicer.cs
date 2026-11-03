using System;
using System.IO;
using Godot;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public sealed class TileSlicer
{
    readonly int _w, _h, _ts, _cols, _rows;
    readonly bool _png;
    readonly string _tilesDir, _dir0;
    readonly TileIndex _index;
    readonly byte[] _band;
    int _bandTop, _bandRows;
    long _rowsPushed;

    public Action<double>? Progress { get; set; }

    public TileSlicer(int w, int h, string tilesDir, int ts = 256)
    {
        if (w <= 0 || h <= 0) throw new ArgumentException("picture has no size");
        _w = w; _h = h; _ts = ts; _tilesDir = tilesDir;
        _png = (long)w * h <= 4_000_000;
        _index = new TileIndex { TileSize = ts, SourceW = w, SourceH = h, Ext = _png ? "png" : "webp" };
        _cols = (w + ts - 1) / ts; _rows = (h + ts - 1) / ts;
        _index.Levels.Add(new TileLevel { Level = 0, W = w, H = h, Cols = _cols, Rows = _rows });
        _dir0 = Path.Combine(tilesDir, "0");
        Directory.CreateDirectory(_dir0);
        _band = new byte[w * ts * 4];
    }

    public void PushRows(byte[] rgba, int rows)
    {
        int at = 0;
        while (rows > 0)
        {
            int take = Math.Min(rows, _ts - _bandRows);
            Buffer.BlockCopy(rgba, at, _band, _bandRows * _w * 4, take * _w * 4);
            _bandRows += take; at += take * _w * 4; rows -= take; _rowsPushed += take;
            if (_bandRows == _ts) FlushBand();
        }
        Progress?.Invoke(0.75 * _rowsPushed / _h);
    }

    void FlushBand()
    {
        if (_bandRows == 0) return;
        int ty = _bandTop / _ts;
        for (int x = 0; x < _cols; x++)
        {
            int tw = Math.Min(_ts, _w - x * _ts);
            var data = new byte[tw * _bandRows * 4];
            for (int r = 0; r < _bandRows; r++) Buffer.BlockCopy(_band, (r * _w + x * _ts) * 4, data, r * tw * 4, tw * 4);
            var img = Image.CreateFromData(tw, _bandRows, false, Image.Format.Rgba8, data);
            Save(img, Path.Combine(_dir0, $"{x}_{ty}.{_index.Ext}"));
        }
        _bandTop += _bandRows; _bandRows = 0;
    }

    public TileIndex Finish()
    {
        if (_rowsPushed != _h) throw new InvalidOperationException($"{_rowsPushed} of {_h} rows were pushed");
        FlushBand();
        int level = 0;
        long coarseTiles = 0, coarseDone = 0;
        for (int w = _w, h = _h; !(w <= _ts && h <= _ts);) { w = Math.Max(1, w / 2); h = Math.Max(1, h / 2); coarseTiles += (long)((w + _ts - 1) / _ts) * ((h + _ts - 1) / _ts); }
        while (!(_index.Levels[level].Cols == 1 && _index.Levels[level].Rows == 1))
        {
            var prev = _index.Levels[level];
            level++;
            int lw = Math.Max(1, prev.W / 2), lh = Math.Max(1, prev.H / 2);
            var lv = new TileLevel { Level = level, W = lw, H = lh, Cols = (lw + _ts - 1) / _ts, Rows = (lh + _ts - 1) / _ts };
            _index.Levels.Add(lv);
            var dir = Path.Combine(_tilesDir, level.ToString());
            Directory.CreateDirectory(dir);
            var prevDir = Path.Combine(_tilesDir, prev.Level.ToString());
            for (int y = 0; y < lv.Rows; y++)
                for (int x = 0; x < lv.Cols; x++)
                {
                    int rw = Math.Min(2 * _ts, prev.W - 2 * x * _ts), rh = Math.Min(2 * _ts, prev.H - 2 * y * _ts);
                    var region = Image.CreateEmpty(rw, rh, false, Image.Format.Rgba8);
                    for (int j = 0; j < 2; j++)
                        for (int i = 0; i < 2; i++)
                        {
                            int sx = 2 * x + i, sy = 2 * y + j;
                            if (sx >= prev.Cols || sy >= prev.Rows) continue;
                            var t = Image.LoadFromFile(Path.Combine(prevDir, $"{sx}_{sy}.{_index.Ext}"));
                            if (t == null) continue;
                            t.Convert(Image.Format.Rgba8);
                            region.BlitRect(t, new Rect2I(0, 0, t.GetWidth(), t.GetHeight()), new Vector2I(i * _ts, j * _ts));
                        }
                    region.Resize(Math.Min(_ts, lw - x * _ts), Math.Min(_ts, lh - y * _ts), Image.Interpolation.Cubic);
                    Save(region, Path.Combine(dir, $"{x}_{y}.{_index.Ext}"));
                    coarseDone++;
                    if ((coarseDone & 15) == 0) Progress?.Invoke(0.75 + 0.25 * coarseDone / Math.Max(1, coarseTiles));
                }
        }
        Json.Save(Path.Combine(_tilesDir, "index.json"), _index);
        Progress?.Invoke(1);
        return _index;
    }

    void Save(Image img, string file)
    {
        if (_png) img.SavePng(file); else img.SaveWebp(file, lossy: false);
    }
}
