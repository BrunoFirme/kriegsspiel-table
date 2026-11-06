using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    const int MostBakedSide = 4096;

    sealed record Part(CacheEntry Picture, double X, double Z, double W, double H, double Rot);

    public bool BakesChildren(ModuleObj o)
    {
        var (states, _) = o.StateList();
        return states.Count > 1 && states.Any(s => s.Children.Any())
            && states.All(s => FlatPicture(s) && s.Children.All(c => FlatPicture(c) && !c.Children.Any() && c.Lua.Trim().Length == 0 && c.StateList().states.Count == 1));
    }

    bool FlatPicture(ModuleObj o) => o.Name is "Custom_Token" or "Custom_Tile" && Entry(o.ImageUrl) is { W: > 0, H: > 0 } e && AssetStore.IsPicture(e.Ext);

    bool BakeStates(PieceDef def, List<ModuleObj> states, double baseScale, double r, PackOut pack)
    {
        var baked = new List<(string Sha, Size2 Size)>();
        foreach (var s in states)
        {
            if (Baked(s, Math.Abs(s.ScaleX) / baseScale * r, pack) is not { } b) return false;
            baked.Add(b);
        }
        def.Image = baked[0].Sha;
        def.Size = baked[0].Size;
        for (int i = 0; i < def.States.Count && i < baked.Count; i++)
        {
            def.States[i].Image = baked[i].Sha;
            var sz = baked[i].Size;
            def.States[i].Size = Math.Abs(sz.W - def.Size.W) > 0.01 * def.Size.W || Math.Abs(sz.H - def.Size.H) > 0.01 * def.Size.H ? sz : null;
        }
        return true;
    }

    (string Sha, Size2 Size)? Baked(ModuleObj s, double k, PackOut pack)
    {
        var own = Mul(BaseSize(s), k);
        double depthRatio = s.ScaleX == 0 ? 1 : Math.Abs(s.ScaleZ / s.ScaleX);
        var parts = new List<Part> { new(Entry(s.ImageUrl)!, 0, 0, own.W, own.H, 0) };
        foreach (var c in s.Children)
        {
            var size = Mul(BaseSize(c), Math.Abs(c.ScaleX) * k);
            parts.Add(new Part(Entry(c.ImageUrl)!, -c.PosX * k, c.PosZ * k * depthRatio, size.W, size.H, c.RotY));
        }
        double halfW = parts.Max(p => Math.Abs(p.X) + Reach(p).x), halfH = parts.Max(p => Math.Abs(p.Z) + Reach(p).z);
        if (halfW <= 0 || halfH <= 0) return null;
        double ppu = Math.Min(parts[0].Picture.W / own.W, MostBakedSide / (2 * Math.Max(halfW, halfH)));
        int w = Math.Max(1, (int)Math.Ceiling(2 * halfW * ppu)), h = Math.Max(1, (int)Math.Ceiling(2 * halfH * ppu));
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = true };
            foreach (var p in parts)
            {
                using var img = SKBitmap.Decode(_assets.FilePath(p.Picture));
                if (img == null) return null;
                using var image = SKImage.FromBitmap(img);
                canvas.Save();
                canvas.Translate((float)(w / 2.0 + p.X * ppu), (float)(h / 2.0 + p.Z * ppu));
                canvas.RotateDegrees((float)p.Rot);
                float pw = (float)(p.W * ppu), ph = (float)(p.H * ppu);
                canvas.DrawImage(image, new SKRect(-pw / 2, -ph / 2, pw / 2, ph / 2), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
                canvas.Restore();
            }
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var e = _assets.Keep(data.ToArray());
        Put(pack, e.Sha!, e.Ext!, _assets.FilePath(e)!, "image", e.W, e.H);
        return (e.Sha!, new Size2(Math.Round(2 * halfW, 4), Math.Round(2 * halfH, 4)));
    }

    static (double x, double z) Reach(Part p)
    {
        double a = p.Rot * Math.PI / 180, cos = Math.Abs(Math.Cos(a)), sin = Math.Abs(Math.Sin(a));
        return (cos * p.W / 2 + sin * p.H / 2, sin * p.W / 2 + cos * p.H / 2);
    }
}
