namespace KTable.Core.Model;

public static class GridSnap
{
    const int DivisionsMost = 20;

    public static bool On(GridDef? g) => g is { Pitch: > 0, Kind: "square" or "hex" };

    public static bool Snaps(GridDef? g) => Snaps(g, null);

    public static bool Snaps(GridDef? g, bool? piece) => On(g) && (piece ?? g!.Snap);

    public static Vec2 Nearest(GridDef g, Vec2 p) => g.Kind switch
    {
        "square" => SquareCentre(p, g.Pitch / Math.Clamp(g.Subdivisions, 1, DivisionsMost)),
        "hex" => GridCells.Centre(g, GridCells.CellOf(g, p)),
        _ => p,
    };

    static Vec2 SquareCentre(Vec2 p, double cell) => new((Math.Floor(p.X / cell) + 0.5) * cell, (Math.Floor(p.Z / cell) + 0.5) * cell);
}
