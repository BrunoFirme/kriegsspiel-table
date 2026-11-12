namespace KTable.Core.Model;

public static class PieceUnits
{
    const double TokenArea = 14;
    const string Cutout = "alpha";
    const double Unscaled = 0.5;

    public static Size2? PictureSize(PackageStore? store, PieceInstance p, PieceDef? def)
    {
        var shown = p.Picture is { } other ? store?.Def(other) : def;
        if (shown == null || store?.PackOfDef(shown.Id) is not { } pack) return null;
        var picture = (p.State > 0 && p.State < shown.States.Count ? shown.States[p.State].Image : null) ?? shown.Image;
        return picture != null && pack.Assets.TryGetValue(picture, out var a) && a.W > 0 && a.H > 0 ? new Size2(a.W, a.H) : null;
    }

    public static Size2 Of(PackageStore? store, PieceInstance p, PieceDef? def)
    {
        if (def?.LocalUnit is { } given) return given;
        if (def == null) return new Size2(Unscaled, Unscaled);
        var size = (p.State >= 0 && p.State < def.States.Count ? def.States[p.State].Size : null) ?? def.Size;
        var shape = (p.State > 0 && p.State < def.States.Count ? def.States[p.State].Shape : null) ?? def.Shape ?? "box";
        if (shape != Cutout) return new Size2(size.H / 2, size.H / 2);
        if (PictureSize(store, p, def) is not { } px)
        {
            var u = Math.Sqrt(size.W * size.H / TokenArea);
            return new Size2(u, u);
        }
        double aspect = px.W / px.H;
        return new Size2(size.W / Math.Sqrt(TokenArea * aspect), size.H / Math.Sqrt(TokenArea / aspect));
    }
}
