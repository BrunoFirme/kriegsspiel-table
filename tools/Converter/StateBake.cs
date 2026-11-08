using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    const int MostBakedSide = 4096;

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
        var backs = new List<string>();
        foreach (var s in states)
        {
            double k = Math.Abs(s.ScaleX) / baseScale * r;
            if (Baked(s, k, pack, underside: false) is not { } b || Baked(s, k, pack, underside: true) is not { } under) return false;
            baked.Add(b);
            backs.Add(under.Sha);
        }
        def.Image = baked[0].Sha;
        def.Back = backs[0];
        def.Size = baked[0].Size;
        def.TintsPictures = false;
        var (low, high) = BakedSpan(states[0]);
        def.Thickness = Math.Round(Math.Clamp((high - low) * Math.Abs(states[0].ScaleY) * heightScale, 0.02, 3), 3);
        for (int i = 0; i < def.States.Count && i < baked.Count; i++)
        {
            def.States[i].Image = baked[i].Sha;
            def.States[i].Back = backs[i];
            var sz = baked[i].Size;
            def.States[i].Size = Math.Abs(sz.W - def.Size.W) > 0.01 * def.Size.W || Math.Abs(sz.H - def.Size.H) > 0.01 * def.Size.H ? sz : null;
            var (sl, sh) = BakedSpan(states[i]);
            double thick = Math.Round(Math.Clamp((sh - sl) * Math.Abs(states[i].ScaleY) * heightScale, 0.02, 3), 3);
            def.States[i].Thickness = Math.Abs(thick - def.Thickness) > 0.001 ? thick : null;
        }
        _baked.Add(def);
        return true;
    }

    List<BakePart> Parts(ModuleObj s, double k)
    {
        var own = Mul(BaseSize(s), k);
        double th = BaseThickness(s);
        var parts = new List<BakePart> { new(Entry(s.ImageUrl)!, BackOf(s), 0, 0, own.W, own.H, 0, -th / 2, th / 2, BakePaint.TintOf(s)) };
        AddChildren(s, 0, 0, 0, 0, k, 1, parts);
        return parts;
    }

    void AddChildren(ModuleObj o, double x, double z, double y, double rot, double unit, double rise, List<BakePart> into)
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
            into.Add(new BakePart(Entry(c.ImageUrl)!, BackOf(c), cx, cz, size.W, size.H, rot + c.RotY, bottom, bottom + BaseThickness(c) * cRise, BakePaint.TintOf(c)));
            AddChildren(c, cx, cz, cy, rot + c.RotY, cUnit, cRise, into);
        }
    }

    CacheEntry? BackOf(ModuleObj o) => o.BackUrl is { } url && Entry(url) is { W: > 0, H: > 0 } e && AssetStore.IsPicture(e.Ext) ? e : null;

    (string Sha, Size2 Size)? Baked(ModuleObj s, double k, PackOut pack, bool underside)
    {
        var parts = Parts(s, k);
        double halfW = parts.Max(p => Math.Abs(p.X) + BakePaint.Reach(p).x), halfH = parts.Max(p => Math.Abs(p.Z) + BakePaint.Reach(p).z);
        if (halfW <= 0 || halfH <= 0) return null;
        double ppu = Math.Min(parts[0].Picture.W / parts[0].W, MostBakedSide / (2 * Math.Max(halfW, halfH)));
        int w = Math.Max(1, (int)Math.Ceiling(2 * halfW * ppu)), h = Math.Max(1, (int)Math.Ceiling(2 * halfH * ppu));
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            if (underside)
            {
                canvas.Translate(w, 0);
                canvas.Scale(-1, 1);
            }
            var order = underside ? parts.OrderByDescending(p => p.Bottom).ThenByDescending(parts.IndexOf) : parts.OrderBy(p => p.Top).ThenBy(parts.IndexOf);
            foreach (var p in order)
                if (!BakePaint.Draw(canvas, p, _assets.FilePath, w, h, ppu, underside)) return null;
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var e = _assets.Keep(data.ToArray());
        Put(pack, e.Sha!, e.Ext!, _assets.FilePath(e)!, "image", e.W, e.H);
        return (e.Sha!, new Size2(Math.Round(2 * halfW, 4), Math.Round(2 * halfH, 4)));
    }
}
