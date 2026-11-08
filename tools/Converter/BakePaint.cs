using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

sealed record BakePart(CacheEntry Picture, CacheEntry? Back, ModuleTurn Turn, double X, double Y, double Z, double W, double H, double Low, double High, SKColor? Tint)
{
    const double EdgeOn = 0.05;

    public bool Seen => Math.Abs(Turn.UpY) >= EdgeOn;

    bool TopFaces(bool above) => above == (Turn.UpY >= 0);

    public double FaceHeight(bool above)
    {
        double local = TopFaces(above) ? High : Low;
        return Y + Turn.Apply(0, local, 0).Y;
    }

    public IEnumerable<double> Heights()
    {
        foreach (var lx in new[] { -W / 2, W / 2 })
            foreach (var ly in new[] { Low, High })
                foreach (var lz in new[] { -H / 2, H / 2 })
                    yield return Y + Turn.Apply(lx, ly, lz).Y;
    }

    public (CacheEntry Picture, SKMatrix Map) Face(bool above)
    {
        bool top = TopFaces(above);
        bool ownBack = !top && Back != null;
        var across = Turn.Apply(ownBack ? 1 : -1, 0, 0);
        var deep = Turn.Apply(0, 0, 1);
        var at = Turn.Apply(0, top ? High : Low, 0);
        float sign = above ? -1 : 1;
        var map = new SKMatrix((float)(sign * across.X), (float)(sign * deep.X), (float)(sign * (X + at.X)), (float)across.Z, (float)deep.Z, (float)(Z + at.Z), 0, 0, 1);
        return (ownBack ? Back! : Picture, map);
    }

    public IEnumerable<SKPoint> Corners(bool above)
    {
        var map = Face(above).Map;
        foreach (var ix in new[] { -W / 2, W / 2 })
            foreach (var iy in new[] { -H / 2, H / 2 })
                yield return map.MapPoint((float)ix, (float)iy);
    }
}

static class BakePaint
{
    public static SKColor? TintOf(ModuleObj o)
    {
        if (o.Name != "Custom_Token" || o.Color is not { Length: >= 7 } hex) return null;
        byte At(int i) => Convert.ToByte(hex.Substring(i, 2), 16);
        var c = new SKColor(At(1), At(3), At(5), hex.Length >= 9 ? At(7) : byte.MaxValue);
        return c == SKColors.White ? null : c;
    }

    public static bool Draw(SKCanvas canvas, BakePart p, Func<CacheEntry, string?> fileOf, int w, int h, double ppu, bool above)
    {
        var (picture, map) = p.Face(above);
        using var img = SKBitmap.Decode(fileOf(picture));
        if (img == null) return false;
        using var image = SKImage.FromBitmap(img);
        using var paint = new SKPaint { IsAntialias = true };
        if (p.Tint is { } tint) paint.ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.Modulate);
        canvas.Save();
        canvas.Translate((float)(w / 2.0), (float)(h / 2.0));
        canvas.Scale((float)ppu);
        canvas.Concat(in map);
        float hw = (float)(p.W / 2), hh = (float)(p.H / 2);
        canvas.DrawImage(image, new SKRect(-hw, -hh, hw, hh), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        canvas.Restore();
        return true;
    }
}
