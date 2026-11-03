using System;
using System.IO;
using Godot;
using KTable.Core;
using Json = KTable.Core.Json;
using KTable.Core.Model;

namespace KTable.App;

public static class SheetImporter
{
    public const string Package = "sheets";

    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".xcf" };
    public const string Filter = "*.png, *.jpg, *.jpeg, *.webp, *.bmp, *.tif, *.tiff, *.xcf ; Map images";

    public static (string package, string sha, int w, int h) Import(string path, string packagesRoot) => Import(path, packagesRoot, null);

    public static (string package, string sha, int w, int h) Import(string path, string packagesRoot, Action<string>? note, Action<double>? progress = null)
    {
        string sha;
        using (var f = File.OpenRead(path)) sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f)).ToLowerInvariant();
        var pkgDir = Path.Combine(packagesRoot, Package);
        var tilesDir = Path.Combine(pkgDir, "tiles", sha);
        var indexPath = Path.Combine(tilesDir, "index.json");
        if (File.Exists(indexPath)) { var done = Json.Load<TileIndex>(indexPath); return (Package, sha, done.SourceW, done.SourceH); }
        if (TiffImages.IsTiff(path))
        {
            var idx = TiffImages.Slice(path, tilesDir, progress: progress);
            return (Package, sha, idx.SourceW, idx.SourceH);
        }
        Image img;
        Directory.CreateDirectory(Path.Combine(pkgDir, "assets"));
        if (Xcf.IsXcf(path))
        {
            using var xcf = XcfFile.Open(path);
            if (xcf.Width > GodotImageMax || xcf.Height > GodotImageMax)
            {
                var idx = SliceXcf(xcf, tilesDir, progress);
                foreach (var warning in xcf.Warnings) note?.Invoke(warning);
                return (Package, sha, idx.SourceW, idx.SourceH);
            }
            var flat = xcf.Flatten();
            foreach (var warning in flat.Warnings) note?.Invoke(warning);
            img = Image.CreateFromData(flat.Width, flat.Height, false, Image.Format.Rgba8, flat.Rgba);
            img.SavePng(Path.Combine(pkgDir, "assets", sha + ".png"));
        }
        else
        {
            img = Image.LoadFromFile(path) ?? throw new Exception("cannot decode image (PNG, JPG and WebP up to 16384 px a side; TIFF of any size; GIMP .xcf)");
            File.Copy(path, Path.Combine(pkgDir, "assets", sha + Path.GetExtension(path).ToLowerInvariant()), true);
        }
        int w0 = img.GetWidth(), h0 = img.GetHeight();

        const int ts = 256;
        bool small = (long)w0 * h0 <= 4_000_000;
        var index = new TileIndex { TileSize = ts, SourceW = w0, SourceH = h0, Ext = small ? "png" : "webp" };
        int level = 0;
        while (true)
        {
            int w = img.GetWidth(), h = img.GetHeight();
            int cols = (w + ts - 1) / ts, rows = (h + ts - 1) / ts;
            index.Levels.Add(new TileLevel { Level = level, W = w, H = h, Cols = cols, Rows = rows });
            var dir = Path.Combine(tilesDir, level.ToString());
            Directory.CreateDirectory(dir);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    var tile = img.GetRegion(new Rect2I(x * ts, y * ts, Math.Min(ts, w - x * ts), Math.Min(ts, h - y * ts)));
                    var file = Path.Combine(dir, $"{x}_{y}.{index.Ext}");
                    if (small) tile.SavePng(file); else tile.SaveWebp(file, lossy: false);
                }
            if (cols == 1 && rows == 1) break;
            img.Resize(Math.Max(1, w / 2), Math.Max(1, h / 2), Image.Interpolation.Cubic);
            level++;
        }
        Json.Save(Path.Combine(tilesDir, "index.json"), index);
        return (Package, sha, w0, h0);
    }

    const int GodotImageMax = 16384;

    static TileIndex SliceXcf(XcfFile xcf, string tilesDir, Action<double>? progress)
    {
        const int band = 256;
        var slicer = new TileSlicer(xcf.Width, xcf.Height, tilesDir, band) { Progress = progress };
        var rgba = new byte[xcf.Width * band * 4];
        for (int y = 0; y < xcf.Height; y += band)
        {
            int rows = Math.Min(band, xcf.Height - y);
            xcf.Render(y, rows, rgba);
            slicer.PushRows(rgba, rows);
        }
        return slicer.Finish();
    }
}
