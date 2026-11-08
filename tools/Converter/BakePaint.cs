using SkiaSharp;

namespace KTable.Converter;

sealed record BakePart(CacheEntry Picture, CacheEntry? Back, double X, double Z, double W, double H, double Rot, double Bottom, double Top, SKColor? Tint);

static class BakePaint
{
    public static SKColor? TintOf(ModuleObj o)
    {
        if (o.Name != "Custom_Token" || o.Color is not { Length: >= 7 } hex) return null;
        byte At(int i) => Convert.ToByte(hex.Substring(i, 2), 16);
        var c = new SKColor(At(1), At(3), At(5), hex.Length >= 9 ? At(7) : byte.MaxValue);
        return c == SKColors.White ? null : c;
    }

    public static (double x, double z) Reach(BakePart p)
    {
        double a = p.Rot * Math.PI / 180, cos = Math.Abs(Math.Cos(a)), sin = Math.Abs(Math.Sin(a));
        return (cos * p.W / 2 + sin * p.H / 2, sin * p.W / 2 + cos * p.H / 2);
    }

    public static bool Draw(SKCanvas canvas, BakePart p, Func<CacheEntry, string?> fileOf, int w, int h, double ppu, bool underside)
    {
        bool ownBack = underside && p.Back != null;
        using var img = SKBitmap.Decode(fileOf(ownBack ? p.Back! : p.Picture));
        if (img == null) return false;
        using var image = SKImage.FromBitmap(img);
        using var paint = new SKPaint { IsAntialias = true };
        if (p.Tint is { } tint) paint.ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.Modulate);
        canvas.Save();
        canvas.Translate((float)(w / 2.0 + p.X * ppu), (float)(h / 2.0 + p.Z * ppu));
        canvas.RotateDegrees((float)p.Rot);
        if (ownBack)
            canvas.Scale(-1, 1);
        float pw = (float)(p.W * ppu), ph = (float)(p.H * ppu);
        canvas.DrawImage(image, new SKRect(-pw / 2, -ph / 2, pw / 2, ph / 2), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        canvas.Restore();
        return true;
    }
}
