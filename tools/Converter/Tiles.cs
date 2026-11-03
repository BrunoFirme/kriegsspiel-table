using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public static class Tiles
{
    public static TileIndex Build(string src, string outDir, TilesCfg cfg)
    {
        var indexPath = Path.Combine(outDir, "index.json");
        var info = SKBitmap.DecodeBounds(src);
        if (File.Exists(indexPath))
        {
            var old = Json.Load<TileIndex>(indexPath);
            if (old.SourceW == info.Width && old.SourceH == info.Height && old.TileSize == cfg.TileSize && old.Levels.Count > 0) return old;
        }
        int ts = cfg.TileSize;
        bool webp = (double)info.Width * info.Height / 1e6 > cfg.PngMaxMegapixels;
        var idx = new TileIndex { TileSize = ts, SourceW = info.Width, SourceH = info.Height, Ext = webp ? "webp" : "png" };
        var webpOpts = new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, cfg.WebpQuality);
        var pngOpts = SKPngEncoderOptions.Default;

        var img = SKBitmap.Decode(src) ?? throw new InvalidDataException("cannot decode " + src);
        try
        {
            for (int level = 0; ; level++)
            {
                int w = img.Width, h = img.Height;
                int cols = (w + ts - 1) / ts, rows = (h + ts - 1) / ts;
                idx.Levels.Add(new TileLevel { Level = level, W = w, H = h, Cols = cols, Rows = rows });
                var dir = Path.Combine(outDir, level.ToString());
                Directory.CreateDirectory(dir);
                using var pix = img.PeekPixels();
                Parallel.For(0, cols * rows, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, i =>
                {
                    int x = i % cols, y = i / cols;
                    var rect = SKRectI.Create(x * ts, y * ts, Math.Min(ts, w - x * ts), Math.Min(ts, h - y * ts));
                    using var sub = pix.ExtractSubset(rect) ?? throw new InvalidOperationException("subset failed");
                    using var data = (webp ? sub.Encode(webpOpts) : sub.Encode(pngOpts)) ?? throw new InvalidOperationException("encode failed");
                    File.WriteAllBytes(Path.Combine(dir, $"{x}_{y}.{idx.Ext}"), data.ToArray());
                });
                Console.WriteLine($"    level {level}: {w}x{h} -> {cols}x{rows} tiles");
                if (w <= ts && h <= ts) break;
                var next = img.Resize(new SKImageInfo(Math.Max(1, (w + 1) / 2), Math.Max(1, (h + 1) / 2), img.ColorType, img.AlphaType),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)) ?? throw new InvalidOperationException("resize failed");
                img.Dispose();
                img = next;
            }
        }
        finally { img.Dispose(); }
        Json.Save(indexPath, idx);
        return idx;
    }
}
