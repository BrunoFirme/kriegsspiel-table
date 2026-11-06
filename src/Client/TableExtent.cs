using KTable.Core.Model;

namespace KTable.Client;

public readonly record struct TableArea(Vec2 Centre, Size2 Size)
{
    public bool Holds(TableArea other) =>
        Math.Abs(other.Centre.X - Centre.X) + other.Size.W / 2 <= Size.W / 2 &&
        Math.Abs(other.Centre.Z - Centre.Z) + other.Size.H / 2 <= Size.H / 2;
}

public static class TableExtent
{
    public const double LeastSide = 600, LeastMargin = 100, MarginShare = 0.3, PieceReach = 2;

    public static TableArea Of(GameState g)
    {
        double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
        void Take(Vec2 p, double reach)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Z)) return;
            x0 = Math.Min(x0, p.X - reach);
            x1 = Math.Max(x1, p.X + reach);
            z0 = Math.Min(z0, p.Z - reach);
            z1 = Math.Max(z1, p.Z + reach);
        }
        foreach (var s in g.Sheets) Take(s.Pos, Math.Sqrt(s.Size.W * s.Size.W + s.Size.H * s.Size.H) / 2);
        foreach (var layer in g.Layers.Values) foreach (var p in layer) Take(p.Pos, PieceReach);
        foreach (var strokes in g.Drawings.Values) foreach (var st in strokes) foreach (var p in st.Points) Take(p, st.Width);
        foreach (var z in g.Zones) foreach (var p in z.Polygon) Take(p, 0);
        if (x0 > x1) return new TableArea(new Vec2(0, 0), new Size2(LeastSide, LeastSide));
        double margin = Math.Max(LeastMargin, Math.Max(x1 - x0, z1 - z0) * MarginShare);
        var size = new Size2(Math.Max(LeastSide, x1 - x0 + 2 * margin), Math.Max(LeastSide, z1 - z0 + 2 * margin));
        return new TableArea(new Vec2((x0 + x1) / 2, (z0 + z1) / 2), size);
    }
}
