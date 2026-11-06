using System;
using System.IO;
using BitMiracle.LibTiff.Classic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public static class TiffImages
{
    public static bool IsTiff(string path) => Path.GetExtension(path).ToLowerInvariant() is ".tif" or ".tiff";

    public static Image? Load(string path)
    {
        using var tiff = Tiff.Open(path, "r");
        if (tiff == null) return null;
        int w = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt(), h = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        if (w <= 0 || h <= 0 || w > 16384 || h > 16384) return null;
        var raster = new int[w * h];
        if (!tiff.ReadRGBAImage(w, h, raster)) return null;
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * w;
            int dst = y * w * 4;
            for (int x = 0; x < w; x++, src++, dst += 4)
            {
                int p = raster[src];
                data[dst] = (byte)Tiff.GetR(p);
                data[dst + 1] = (byte)Tiff.GetG(p);
                data[dst + 2] = (byte)Tiff.GetB(p);
                data[dst + 3] = (byte)Tiff.GetA(p);
            }
        }
        return Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
    }

    public static TileIndex Slice(string path, string tilesDir, int ts = 256, Action<double>? progress = null)
    {
        using var tiff = Tiff.Open(path, "r") ?? throw new Exception("cannot open the TIFF");
        if (!tiff.RGBAImageOK(out var why)) throw new Exception("TIFF not readable as RGBA: " + why);
        int w = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt(), h = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        if (w <= 0 || h <= 0) throw new Exception("TIFF has no size");
        var slicer = new TileSlicer(w, h, tilesDir, ts) { Progress = progress };

        var row = new byte[w * 4];
        void PushRow(int[] raster, int offset)
        {
            for (int x = 0, dst = 0; x < w; x++, dst += 4)
            {
                int p = raster[offset + x];
                row[dst] = (byte)Tiff.GetR(p);
                row[dst + 1] = (byte)Tiff.GetG(p);
                row[dst + 2] = (byte)Tiff.GetB(p);
                row[dst + 3] = (byte)Tiff.GetA(p);
            }
            slicer.PushRows(row, 1);
        }

        if (tiff.IsTiled())
        {
            int tw = tiff.GetField(TiffTag.TILEWIDTH)[0].ToInt(), th = tiff.GetField(TiffTag.TILELENGTH)[0].ToInt();
            var tile = new int[tw * th];
            for (int y0 = 0; y0 < h; y0 += th)
            {
                int rowsHere = Math.Min(th, h - y0);
                var strip = new int[w * rowsHere];
                for (int x0 = 0; x0 < w; x0 += tw)
                {
                    if (!tiff.ReadRGBATile(x0, y0, tile)) throw new Exception($"TIFF tile {x0},{y0} unreadable");
                    int colsHere = Math.Min(tw, w - x0);
                    for (int r = 0; r < rowsHere; r++)
                        Array.Copy(tile, (th - 1 - r) * tw, strip, r * w + x0, colsHere);
                }
                for (int r = 0; r < rowsHere; r++) PushRow(strip, r * w);
            }
        }
        else
        {
            int rps = tiff.GetField(TiffTag.ROWSPERSTRIP) is { } f && f[0].ToInt() > 0 ? Math.Min(f[0].ToInt(), h) : h;
            var raster = new int[w * rps];
            for (int y0 = 0; y0 < h; y0 += rps)
            {
                if (!tiff.ReadRGBAStrip(y0, raster)) throw new Exception($"TIFF strip at row {y0} unreadable");
                int rowsHere = Math.Min(rps, h - y0);
                for (int r = 0; r < rowsHere; r++) PushRow(raster, (rowsHere - 1 - r) * w);
            }
        }
        return slicer.Finish();
    }
}
