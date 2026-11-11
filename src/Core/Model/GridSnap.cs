namespace KTable.Core.Model;

public static class GridSnap
{
    const double HexRowStep = 1.7320508075688772;
    const int DivisionsMost = 20;

    public static bool Snaps(GridDef? g) => g is { Snap: true, Pitch: > 0, Kind: "square" or "hex" };

    public static Vec2 Nearest(GridDef g, Vec2 p) => g.Kind switch
    {
        "square" => SquareCentre(p, g.Pitch / Math.Clamp(g.Subdivisions, 1, DivisionsMost)),
        "hex" => HexCentre(p, g.Pitch),
        _ => p,
    };

    static Vec2 SquareCentre(Vec2 p, double cell) => new((Math.Floor(p.X / cell) + 0.5) * cell, (Math.Floor(p.Z / cell) + 0.5) * cell);

    static Vec2 HexCentre(Vec2 p, double across)
    {
        var at = new Vec2(p.X / across, p.Z / across);
        var even = new Vec2(Math.Round(at.X), Math.Round(at.Z / HexRowStep) * HexRowStep);
        var odd = new Vec2(Math.Floor(at.X) + 0.5, (Math.Floor(at.Z / HexRowStep) + 0.5) * HexRowStep);
        var near = (at - even).Length <= (at - odd).Length ? even : odd;
        return new Vec2(near.X * across, near.Z * across);
    }
}
