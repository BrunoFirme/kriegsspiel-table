using System;
using System.Collections.Generic;
using System.IO;
using BitMiracle.LibTiff.Classic;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class SheetExportFile
{
    public const string Png = ".png", Tiff = ".tif", Unfinished = ".part";

    public static string Named(string path) => TiffImages.IsTiff(path) || Path.GetExtension(path).Equals(Png, StringComparison.OrdinalIgnoreCase) ? path : path + Png;

    public static string TileRel(SheetExport.Source s, int x, int y) => Path.Combine("tiles", s.Sheet.Image, "0", $"{x}_{y}.{s.Index.Ext}");

    public static long Write(IReadOnlyList<SheetExport.Source> sources, SheetExport.Frame frame, Func<string, string, string> abs, string path, Func<bool> stopped, Action<double>? progress = null)
    {
        var part = path + Unfinished;
        byte[] Tile(SheetExport.Source s, int tx, int ty)
        {
            using var tile = SheetPicture.Tile(abs(s.Sheet.Package, TileRel(s, tx, ty))) ?? throw new IOException($"a map tile of {s.Sheet.Name} could not be read");
            return tile.GetData();
        }
        try
        {
            if (TiffImages.IsTiff(path))
            {
                using var tiff = new TiffRows(part, frame.W, frame.H);
                SheetExport.Render(sources, frame, Tile, tiff.Rows, stopped, progress);
            }
            else
            {
                using var file = File.Create(part);
                using var png = new PngWriter(file, frame.W, frame.H);
                SheetExport.Render(sources, frame, Tile, png.Rows, stopped, progress);
                png.Finish();
            }
            File.Move(part, path, true);
        }
        catch
        {
            BestEffort.Try(() => File.Delete(part));
            throw;
        }
        return new FileInfo(path).Length;
    }
}

sealed class TiffRows : IDisposable
{
    const long ClassicMostBytes = 3L << 30;
    const int Channels = 4, StripRows = 64;
    readonly Tiff _tiff;
    readonly int _w;
    int _row;

    public TiffRows(string path, int w, int h)
    {
        _tiff = Tiff.Open(path, (long)w * h * Channels > ClassicMostBytes ? "w8" : "w") ?? throw new IOException("the TIFF could not be opened for writing");
        _w = w;
        _tiff.SetField(TiffTag.IMAGEWIDTH, w);
        _tiff.SetField(TiffTag.IMAGELENGTH, h);
        _tiff.SetField(TiffTag.SAMPLESPERPIXEL, Channels);
        _tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
        _tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
        _tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
        _tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
        _tiff.SetField(TiffTag.EXTRASAMPLES, 1, new[] { (short)ExtraSample.UNASSALPHA });
        _tiff.SetField(TiffTag.COMPRESSION, Compression.ADOBE_DEFLATE);
        _tiff.SetField(TiffTag.PREDICTOR, Predictor.HORIZONTAL);
        _tiff.SetField(TiffTag.ROWSPERSTRIP, StripRows);
    }

    public void Rows(byte[] rgba, int rows)
    {
        for (int r = 0; r < rows; r++)
            if (!_tiff.WriteScanline(rgba, r * _w * Channels, _row++, 0)) throw new IOException("the TIFF could not be written");
    }

    public void Dispose() => _tiff.Dispose();
}
