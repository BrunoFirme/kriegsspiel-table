using System.Globalization;
using KTable.Client.Hand;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

sealed class FakePiece : ITablePiece
{
    public FakePiece(string id, double x, double z, float w, float d, double y = 0, double rot = 180, float thickness = 0.1f, bool prop = false)
    {
        Piece = new PieceInstance { Id = id, Def = "test.block", Pos = new Vec2(x, z), RotDeg = rot, Y = y };
        Footprint = new System.Numerics.Vector2(w, d);
        Thickness = thickness;
        IsProp = prop;
    }

    public string Layer { get; init; } = Layers.Umpire;
    public PieceInstance Piece { get; }
    public System.Numerics.Vector2 Footprint { get; }
    public System.Numerics.Vector2 GroundCentre { get; init; }
    public float Thickness { get; }
    public float TopY => (float)(Piece.Y + 0.03) + Thickness;
    public bool IsDie { get; init; }
    public bool IsEffect { get; init; }
    public bool IsProp { get; }
    public bool Placed { get; init; }
    public string CardTitle => "the " + Piece.Id;

    public bool Unbounded { get; init; }
    public int Asked { get; private set; }
    public float SurfaceReach => Unbounded ? float.PositiveInfinity : GroundCentre.Length() + Footprint.Length() / 2;

    public float? SurfaceAt(Vec2 p)
    {
        Asked++;
        return ContainsAt(new Vec2((float)p.X, (float)p.Z), Piece.Pos, Piece.RotDeg) ? TopY : null;
    }

    public bool ContainsAt(Vec2 point, Vec2 at, double rotDeg)
    {
        var middle = this.MiddleAt(at, rotDeg);
        double rad = rotDeg * Math.PI / 180, dx = point.X - middle.X, dz = point.Z - middle.Z;
        double lx = dx * -Math.Cos(rad) + dz * Math.Sin(rad), lz = dx * -Math.Sin(rad) + dz * -Math.Cos(rad);
        return Math.Abs(lx) <= Footprint.X / 2 && Math.Abs(lz) <= Footprint.Y / 2;
    }
}

static class HandLayouts
{
    public static IEnumerable<(string name, List<FakePiece> table, FakePiece dropped, Vec2 at, double rot)> Rests()
    {
        FakePiece B(string id, double x, double z, float w = 2, float d = 1, double y = 0, double rot = 180, float t = 0.1f, bool prop = false) => new(id, x, z, w, d, y, rot, t, prop);
        yield return ("bare table", new(), B("a", 0, 0), new Vec2(3, 4), 180);
        yield return ("on a big block", new() { B("base", 0, 0, 6, 6, t: 0.3f) }, B("a", 0, 0), new Vec2(0.5, 0.5), 180);
        yield return ("three high", new() { B("b1", 0, 0, 4, 4), B("b2", 0, 0, 3, 3, 0.1), B("b3", 0, 0, 2.5f, 2.5f, 0.2) }, B("a", 0, 0), new Vec2(0.2, -0.1), 180);
        yield return ("over an edge slides off", new() { B("base", 0, 0, 4, 4, t: 0.4f) }, B("a", 0, 0), new Vec2(2.7, 0), 180);
        yield return ("turned over an edge", new() { B("base", 0, 0, 4, 4, t: 0.4f) }, B("a", 0, 0), new Vec2(0, 2.45), 45);
        yield return ("between two blocks", new() { B("l", -1.2, 0, 2, 2, t: 0.25f), B("r", 1.2, 0, 2, 2, t: 0.5f) }, B("a", 0, 0, 1, 1), new Vec2(0.05, 0), 90);
        yield return ("prop is set down as let go", new() { B("base", 0, 0, 4, 4, t: 0.4f) }, B("p", 0, 0, 2, 2, prop: true), new Vec2(2, 0), 180);
        yield return ("other layer ignored", new() { new FakePiece("base", 0, 0, 6, 6, thickness: 0.3f) { Layer = "red" } }, B("a", 0, 0), new Vec2(0.5, 0.5), 180);
        yield return ("dice ignored", new() { new FakePiece("die", 0, 0, 6, 6, thickness: 0.3f) { IsDie = true } }, B("a", 0, 0), new Vec2(0.5, 0.5), 180);
        yield return ("on a turned block", new() { B("base", 1, 1, 5, 2, rot: 30, t: 0.2f) }, B("a", 0, 0, 1, 1), new Vec2(1.3, 1.2), 30);
    }

    public static string Describe(IEnumerable<Command> cmds) => string.Join("\n", cmds.Select(c => c switch
    {
        MovePiece m => FormattableString.Invariant($"move {m.Id} {m.Pos.X:R},{m.Pos.Z:R} rot {m.RotDeg:R} y {m.Y:R}"),
        Attach a => $"attach {a.Child} to {a.Parent}",
        Detach d => $"detach {d.Child}",
        _ => c.GetType().Name,
    }));

    public static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
