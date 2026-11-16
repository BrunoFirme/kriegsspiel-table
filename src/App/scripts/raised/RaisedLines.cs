using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedLines
{
    const float KerbWideM = 0.25f, KerbHighM = 0.15f, KerbInM = 0.1f, KerbEndM = 6, RevetmentM = 1.4f, StiltM = 0.22f;
    const float PieceM = 6, SinkM = 0.4f, MerlonM = 1.9f, PostApartM = 2.5f, PostM = 0.13f, RailM = 0.07f, FenceM = 1.2f, StonePick = 33.3f;

    readonly record struct Run(float WideM, float HighM, RaisedMesh.Paint Paint, bool Merlons);

    static Run? RunOf(BuiltPainter.Shape s, string? style)
    {
        int builders = RaisedLooks.Aged(style, s.Built);
        float pick = BuiltCanvas.Chanced(s.Uv[0] * BuiltCanvas.KeyScale, StonePick);
        return s.Class switch
        {
            "wall" => new Run(2.2f, Manners.Old(builders) ? 6 : 7.5f, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), true),
            "wardwall" => new Run(1.4f, 4, new(RaisedColours.Wall(Fabric.Mud, builders, pick), (int)Fabric.Mud), false),
            "palisade" => new Run(0.6f, 3.6f, new(RaisedColours.Wall(Fabric.Log, builders, pick), RaisedCodes.Bark), false),
            "hedge" => new Run(2.4f, 1.9f, new(RaisedColours.Hedge, RaisedCodes.Hedge), false),
            Footings.Revetment => new Run(0.6f, RevetmentM, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), false),
            "mole" => new Run(4.5f, 1.6f, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), false),
            "dam" => new Run(3, 2.5f, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), false),
            "jetty" => new Run(1.8f, 0.7f, new(RaisedColours.Wall(Fabric.Plank, builders, pick), RaisedCodes.Timber), false),
            _ => null,
        };
    }

    public static void Add(RaisedMesh m, BuiltPainter.Shape s, Vector3[] world, Func<Vector3, float> height, string? style)
    {
        if (world.Length < 2) return;
        int builders = RaisedLooks.Aged(style, s.Built);
        if (s.Class == Hallmarks.Aqueduct)
        {
            RaisedArcades.Aqueduct(m, world, height, new RaisedMesh.Paint(RaisedColours.Wall(Fabric.Stone, builders, 0.5f), (int)Fabric.Stone));
            return;
        }
        if (s.Class is "street" or "boulevard" or "paving" && Manners.Named(builders).Epoch is Manners.Later or Manners.Modern)
        {
            Kerbs(m.Small, world, height, BuiltHand.Of(MapStyles.Index(style)).Line(s.Class).Metres / 2);
            return;
        }
        if (s.Class == "fence")
        {
            Fence(m.Small, world, height);
            return;
        }
        if (RunOf(s, style) is not { } run) return;
        float half = run.WideM / 2 * m.PerMetre, high = run.HighM * m.PerMetre, sink = SinkM * m.PerMetre;
        foreach (var (p, q) in Pieces(world, PieceM * m.PerMetre))
        {
            var along = (q - p) with { Y = 0 };
            if (along.LengthSquared() < 1e-12f) continue;
            var side = new Vector3(-along.Z, 0, along.X).Normalized() * half;
            float hp = height(p), hq = height(q), bottom = Mathf.Min(hp, hq) - sink;
            Vector3 a = p + side, b = q + side, c = q - side, d = p - side;
            m.Wall(a, b, bottom, hq + high, p - side * 2, run.Paint, hp + high, hq + high);
            m.Wall(d, c, bottom, hq + high, p + side * 2, run.Paint, hp + high, hq + high);
            m.Face([a with { Y = hp + high }, b with { Y = hq + high }, c with { Y = hq + high }, d with { Y = hp + high }], Vector3.Up, along, a with { Y = hp + high }, run.Paint);
            if (run.Merlons) Merlons(m.Small, a, b, hp + high, hq + high, side, run.Paint);
        }
        Cap(m, world[0], world[1], height, half, high, sink, run.Paint);
        Cap(m, world[^1], world[^2], height, half, high, sink, run.Paint);
    }

    static void Kerbs(RaisedMesh m, Vector3[] world, Func<Vector3, float> height, float halfM)
    {
        if (m.Plain || halfM <= KerbInM + KerbWideM) return;
        float upm = m.PerMetre, half = KerbWideM / 2 * upm, total = 0, run = 0;
        for (int i = 1; i < world.Length; i++) total += ((world[i] - world[i - 1]) with { Y = 0 }).Length();
        foreach (var (p, q) in Pieces(world, PieceM * upm))
        {
            var along = (q - p) with { Y = 0 };
            run += along.Length();
            if (along.LengthSquared() < 1e-12f || run - along.Length() < KerbEndM * upm || run > total - KerbEndM * upm) continue;
            var across = new Vector3(-along.Z, 0, along.X).Normalized();
            foreach (int side in new[] { 1, -1 })
            {
                var off = across * (side * (halfM - KerbInM - KerbWideM / 2) * upm);
                float hp = height(p + off), hq = height(q + off);
                RaisedArcades.Prism(m, p + off, q + off, across * half, hp - KerbHighM * upm, hp + KerbHighM * upm, hq - KerbHighM * upm, hq + KerbHighM * upm,
                    new RaisedMesh.Paint(RaisedColours.Plinth.Lightened(0.2f), (int)Fabric.Stone), false);
            }
        }
    }

    static void Cap(RaisedMesh m, Vector3 end, Vector3 next, Func<Vector3, float> height, float half, float high, float sink, RaisedMesh.Paint paint)
    {
        var along = (next - end) with { Y = 0 };
        if (along.LengthSquared() < 1e-12f) return;
        var side = new Vector3(-along.Z, 0, along.X).Normalized() * half;
        float h = height(end);
        m.Wall(end - side, end + side, h - sink, h + high, end + along.Normalized() * half, paint);
    }

    static void Merlons(RaisedMesh m, Vector3 a, Vector3 b, float ta, float tb, Vector3 side, RaisedMesh.Paint paint)
    {
        float apart = MerlonM * m.PerMetre, run = (b - a).Length();
        int count = Mathf.Max(1, (int)(run / apart));
        var inward = -side.Normalized() * (0.5f * m.PerMetre);
        for (int k = 0; k < count; k++)
        {
            float t0 = (k + 0.25f) / count, t1 = (k + 0.75f) / count;
            Vector3 p0 = a.Lerp(b, t0), p1 = a.Lerp(b, t1);
            float y = Mathf.Lerp(ta, tb, (t0 + t1) / 2);
            RaisedRoofs.Block(m, [p0, p1, p1 + inward, p0 + inward], y, y + 0.9f * m.PerMetre, paint);
        }
    }

    public static void Stilts(RaisedMesh m, Vector3[] world, float floor)
    {
        var wood = new RaisedMesh.Paint(RaisedColours.Timber, RaisedCodes.Timber);
        float post = StiltM / 2 * m.PerMetre;
        foreach (var p in world)
            if (floor - p.Y > post) RaisedRoofs.Block(m.Small, [p + new Vector3(-post, 0, -post), p + new Vector3(post, 0, -post), p + new Vector3(post, 0, post), p
                + new Vector3(-post, 0, post)], p.Y - post, floor, wood);
    }

    static void Fence(RaisedMesh m, Vector3[] world, Func<Vector3, float> height)
    {
        var wood = new RaisedMesh.Paint(RaisedColours.Timber.Lightened(0.15f), RaisedCodes.Timber);
        float post = PostM / 2 * m.PerMetre, rail = RailM / 2 * m.PerMetre, tall = FenceM * m.PerMetre;
        foreach (var (p, q) in Pieces(world, PostApartM * m.PerMetre))
        {
            float hp = height(p), hq = height(q);
            RaisedRoofs.Block(m, [p + new Vector3(-post, 0, -post), p + new Vector3(post, 0, -post), p + new Vector3(post, 0, post), p + new Vector3(-post, 0, post)], hp - post, hp + tall, wood);
            var along = (q - p) with { Y = 0 };
            if (along.LengthSquared() < 1e-12f) continue;
            var side = new Vector3(-along.Z, 0, along.X).Normalized() * rail;
            foreach (float at in new[] { 0.45f, 0.85f })
            {
                float ya = hp + tall * at, yb = hq + tall * at;
                m.Face([(p + side) with { Y = ya + rail }, (q + side) with { Y = yb + rail }, (q - side) with { Y = yb + rail }, (p - side) with { Y = ya + rail }], Vector3.Up, along,
                    p with { Y = ya }, wood);
                m.Wall(p + side, q + side, ya - rail, yb + rail, p - side, wood, ya + rail, yb + rail);
                m.Wall(p - side, q - side, ya - rail, yb + rail, p + side, wood, ya + rail, yb + rail);
            }
        }
    }

    static IEnumerable<(Vector3 From, Vector3 To)> Pieces(Vector3[] line, float most)
    {
        for (int i = 0; i + 1 < line.Length; i++)
        {
            Vector3 a = line[i] with { Y = 0 }, b = line[i + 1] with { Y = 0 };
            int pieces = Mathf.Max(1, Mathf.CeilToInt((b - a).Length() / Mathf.Max(most, 1e-6f)));
            for (int k = 0; k < pieces; k++) yield return (a.Lerp(b, k / (float)pieces), a.Lerp(b, (k + 1) / (float)pieces));
        }
    }
}
