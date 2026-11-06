using SkiaSharp;

namespace KTable.Converter;

public sealed partial class AssetStore
{
    readonly Dictionary<(string sha, int across, int down, int cell), CacheEntry?> _cells = new();
    (string sha, SKBitmap bmp)? _sheet;

    public CacheEntry? Cell(CacheEntry sheet, int across, int down, int cell)
    {
        if (sheet.Sha == null || cell < 0 || cell >= across * down) return null;
        var key = (sheet.Sha, across, down, cell);
        if (_cells.TryGetValue(key, out var had)) return had;
        var bmp = Sheet(sheet);
        CacheEntry? cut = null;
        if (bmp != null && bmp.Width >= across && bmp.Height >= down)
        {
            int w = bmp.Width / across, h = bmp.Height / down;
            using var part = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            using (var canvas = new SKCanvas(part))
            using (var image = SKImage.FromBitmap(bmp))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawImage(image, SKRect.Create(cell % across * w, cell / across * h, w, h), SKRect.Create(0, 0, w, h), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            }
            using var data = part.Encode(SKEncodedImageFormat.Png, 100);
            cut = Keep(data.ToArray(), sheet.Url ?? "");
        }
        _cells[key] = cut;
        return cut;
    }

    SKBitmap? Sheet(CacheEntry sheet)
    {
        if (_sheet is { } s && s.sha == sheet.Sha) return s.bmp;
        _sheet?.bmp.Dispose();
        _sheet = null;
        if (FilePath(sheet) is not { } path) return null;
        using var raw = SKBitmap.Decode(path);
        if (raw?.Copy(SKColorType.Rgba8888) is not { } rgba) return null;
        _sheet = (sheet.Sha!, rgba);
        return rgba;
    }
}
