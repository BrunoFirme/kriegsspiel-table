using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    const int MostBakedSide = 4096;

    sealed record Part(CacheEntry Picture, double X, double Z, double W, double H, double Rot, double Bottom, double Top, SKColor? Tint);

    readonly HashSet<PieceDef> _baked = new();

    public bool IsBaked(PieceDef def) => _baked.Contains(def);

    public bool BakesChildren(ModuleObj o)
    {
        var (states, _) = o.StateList();
        return states.Any(s => s.Children.Any()) && states.All(s => FlatPicture(s) && s.Children.All(Glues));
    }

    bool Glues(ModuleObj c) => FlatPicture(c) && c.Lua.Trim().Length == 0 && c.Children.All(Glues);

    bool FlatPicture(ModuleObj o) => o.Name is "Custom_Token" or "Custom_Tile" && Entry(o.ImageUrl) is { W: > 0, H: > 0 } e && AssetStore.IsPicture(e.Ext);

    public (double Low, double High) BakedSpan(ModuleObj s)
    {
        var parts = Parts(s, 1);
        return (parts.Min(p => p.Bottom), parts.Max(p => p.Top));
    }

    bool BakeStates(PieceDef def, List<ModuleObj> states, double baseScale, double r, double heightScale, PackOut pack)
    {
        var baked = new List<(string Sha, Size2 Size)>();
        foreach (var s in states)
        {
            if (Baked(s, Math.Abs(s.ScaleX) / baseScale * r, pack) is not { } b) return false;
            baked.Add(b);
        }
        def.Image = baked[0].Sha;
        def.Size = baked[0].Size;
        def.TintsPictures = false;
        var (low, high) = BakedSpan(states[0]);
        def.Thickness = Math.Round(Math.Clamp((high - low) * Math.Abs(states[0].ScaleY) * heightScale, 0.02, 3), 3);
        for (int i = 0; i < def.States.Count && i < baked.Count; i++)
        {
            def.States[i].Image = baked[i].Sha;
            var sz = baked[i].Size;
            def.States[i].Size = Math.Abs(sz.W - def.Size.W) > 0.01 * def.Size.W || Math.Abs(sz.H - def.Size.H) > 0.01 * def.Size.H ? sz : null;
        }
        _baked.Add(def);
        return true;
    }

    List<Part> Parts(ModuleObj s, double k)
    {
        var own = Mul(BaseSize(s), k);
        double th = BaseThickness(s);
        var parts = new List<Part> { new(Entry(s.ImageUrl)!, 0, 0, own.W, own.H, 0, -th / 2, th / 2, TintOf(s)) };
        AddChildren(s, 0, 0, 0, 0, k, 1, parts);
        return parts;
    }

    void AddChildren(ModuleObj o, double x, double z, double y, double rot, double unit, double rise, List<Part> into)
    {
        double depthRatio = o.ScaleX == 0 ? 1 : Math.Abs(o.ScaleZ / o.ScaleX);
        double a = rot * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        foreach (var c in o.Children)
        {
            double dx = -c.PosX * unit, dz = c.PosZ * unit * depthRatio;
            double cx = x + dx * cos - dz * sin, cz = z + dx * sin + dz * cos, cy = y + c.PosY * rise;
            double cUnit = Math.Abs(c.ScaleX) * unit, cRise = Math.Abs(c.ScaleY) * rise;
            var size = Mul(BaseSize(c), cUnit);
            double bottom = cy + LocalFloor(c) * cRise;
            into.Add(new Part(Entry(c.ImageUrl)!, cx, cz, size.W, size.H, rot + c.RotY, bottom, bottom + BaseThickness(c) * cRise, TintOf(c)));
            AddChildren(c, cx, cz, cy, rot + c.RotY, cUnit, cRise, into);
        }
    }

    static SKColor? TintOf(ModuleObj o)
    {
        if (o.Name != "Custom_Token" || o.Color is not { Length: >= 7 } hex) return null;
        byte At(int i) => Convert.ToByte(hex.Substring(i, 2), 16);
        var c = new SKColor(At(1), At(3), At(5), hex.Length >= 9 ? At(7) : byte.MaxValue);
        return c == SKColors.White ? null : c;
    }

    (string Sha, Size2 Size)? Baked(ModuleObj s, double k, PackOut pack)
    {
        var parts = Parts(s, k);
        double halfW = parts.Max(p => Math.Abs(p.X) + Reach(p).x), halfH = parts.Max(p => Math.Abs(p.Z) + Reach(p).z);
        if (halfW <= 0 || halfH <= 0) return null;
        double ppu = Math.Min(parts[0].Picture.W / parts[0].W, MostBakedSide / (2 * Math.Max(halfW, halfH)));
        int w = Math.Max(1, (int)Math.Ceiling(2 * halfW * ppu)), h = Math.Max(1, (int)Math.Ceiling(2 * halfH * ppu));
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            foreach (var p in parts.OrderBy(p => p.Top).ThenBy(parts.IndexOf))
                if (!Draw(canvas, p, w, h, ppu)) return null;
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var e = _assets.Keep(data.ToArray());
        Put(pack, e.Sha!, e.Ext!, _assets.FilePath(e)!, "image", e.W, e.H);
        return (e.Sha!, new Size2(Math.Round(2 * halfW, 4), Math.Round(2 * halfH, 4)));
    }

    bool Draw(SKCanvas canvas, Part p, int w, int h, double ppu)
    {
        using var img = SKBitmap.Decode(_assets.FilePath(p.Picture));
        if (img == null) return false;
        using var image = SKImage.FromBitmap(img);
        using var paint = new SKPaint { IsAntialias = true };
        if (p.Tint is { } tint) paint.ColorFilter = SKColorFilter.CreateBlendMode(tint, SKBlendMode.Modulate);
        canvas.Save();
        canvas.Translate((float)(w / 2.0 + p.X * ppu), (float)(h / 2.0 + p.Z * ppu));
        canvas.RotateDegrees((float)p.Rot);
        float pw = (float)(p.W * ppu), ph = (float)(p.H * ppu);
        canvas.DrawImage(image, new SKRect(-pw / 2, -ph / 2, pw / 2, ph / 2), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        canvas.Restore();
        return true;
    }

    static (double x, double z) Reach(Part p)
    {
        double a = p.Rot * Math.PI / 180, cos = Math.Abs(Math.Cos(a)), sin = Math.Abs(Math.Sin(a));
        return (cos * p.W / 2 + sin * p.H / 2, sin * p.W / 2 + cos * p.H / 2);
    }
}
