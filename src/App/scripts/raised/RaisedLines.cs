using System;
using System.Collections.Generic;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RaisedLines
{
    const float KerbWideM = 0.25f, KerbHighM = 0.15f, KerbInM = 0.1f, KerbEndM = 6, RevetmentM = 1.4f, StiltM = 0.22f, RevetmentReachM = 1.5f, CopingM = 0.25f;
    const float StairFromM = 14, StairEveryM = 90, StairDegrees = 38, StairWideM = 1.4f, StairMostM = 12, LevelFromM = 2.5f;
    const int LevelPieces = 2;
    const float PieceM = 6, SinkM = 0.4f, MerlonM = 1.9f, PostApartM = 2.5f, PostM = 0.13f, RailM = 0.07f, FenceM = 1.2f, StonePick = 33.3f;

    readonly record struct Run(float WideM, float HighM, RaisedMesh.Paint Paint, bool Merlons, float CrownWideM = 0, float CrownHighM = 0, RaisedMesh.Paint Crown = default);

    static readonly Color Whitewash = new(0.9f, 0.88f, 0.82f);

    static Run? RunOf(BuiltPainter.Shape s, string? style)
    {
        int builders = RaisedLooks.Aged(style, s.Built);
        float pick = BuiltCanvas.Chanced(s.Uv[0] * BuiltCanvas.KeyScale, StonePick);
        return s.Class switch
        {
            "wall" when builders is Manners.Japanese or Manners.Nippon => new Run(3, 4.2f, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), false, 0.8f, 2.2f,
                new(Whitewash, (int)Fabric.Lime)),
            "wall" => new Run(2.2f, Manners.Old(builders) ? 6 : 7.5f, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), true),
            "wardwall" => new Run(1.4f, 4, new(RaisedColours.Wall(Fabric.Mud, builders, pick), (int)Fabric.Mud), false),
            "palisade" => new Run(0.6f, 3.6f, new(RaisedColours.Wall(Fabric.Log, builders, pick), RaisedCodes.Bark), false),
            "hedge" => new Run(2.4f, 1.9f, new(RaisedColours.Hedge, RaisedCodes.Hedge), false),
            Settlements.GardenWall => new Run(0.45f, 1.9f, new(RaisedColours.Wall(Earthen(builders), builders, pick), (int)Earthen(builders)), false),
            "fence" => new Run(0.4f, 1.5f, new(RaisedColours.Wall(Fabric.Mud, builders, pick), (int)Fabric.Mud), false),
            Terraces.Wall => new Run(0.5f, 0.9f, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), false),
            Hallmarks.Boardwalk => new Run(1.4f, 0.18f, new(RaisedColours.Timber.Lightened(0.2f), RaisedCodes.Timber), false),
            Footings.Revetment => new Run(0.6f, RevetmentM, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), false),
            "mole" => new Run(4.5f, 1.6f, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), false),
            "dam" => new Run(3, 2.5f, new(RaisedColours.Wall(Fabric.Stone, builders, pick), (int)Fabric.Stone), false),
            "jetty" => new Run(1.8f, 0.7f, new(RaisedColours.Wall(Fabric.Plank, builders, pick), RaisedCodes.Timber), false),
            _ => null,
        };
    }

    public static float HighM(BuiltPainter.Shape s, string? style) => RunOf(s, style) is { } run ? run.HighM + run.CrownHighM : 0;

    static Fabric Earthen(int builders) => RoofBook.Flat(builders) ? Fabric.Mud : Fabric.Stone;

    public static void Add(RaisedMesh m, BuiltPainter.Shape s, Vector3[] world, Func<Vector3, float> height, string? style, Func<Vector3, bool>? stands = null)
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
        if (s.Class == Footings.Revetment && RunOf(s, style) is { } held)
        {
            Revetment(m, world, height, held);
            return;
        }
        if (s.Class is "fence" or "wire" && !(s.Class == "fence" && RoofBook.Flat(builders)))
        {
            Fence(m.Small, world, height);
            return;
        }
        if (RunOf(s, style) is not { } run) return;
        float half = run.WideM / 2 * m.PerMetre, high = run.HighM * m.PerMetre, sink = SinkM * m.PerMetre, walked = 0, stairAt = StairFromM * m.PerMetre;
        var within = RaisedFrame.Middle(world) with { Y = 0 };
        var pieces = new List<(Vector3 From, Vector3 To)>(Pieces(world, PieceM * m.PerMetre));
        if (pieces.Count == 0) return;
        var ground = new float[pieces.Count + 1];
        for (int i = 0; i < pieces.Count; i++) ground[i] = height(pieces[i].From);
        ground[^1] = height(pieces[^1].To);
        int level = run.HighM >= LevelFromM ? LevelPieces : 0;
        var crest = new float[ground.Length];
        for (int i = 0; i < ground.Length; i++)
            for (int j = Math.Max(0, i - level); j <= Math.Min(ground.Length - 1, i + level); j++) crest[i] = j == Math.Max(0, i - level) ? ground[j] : Mathf.Max(crest[i], ground[j]);
        for (int i = 0; i < pieces.Count; i++)
        {
            var (p, q) = pieces[i];
            var along = (q - p) with { Y = 0 };
            if (along.LengthSquared() < 1e-12f) continue;
            var side = new Vector3(-along.Z, 0, along.X).Normalized() * half;
            if (run.Merlons && side.Dot(((p + q) / 2 - within) with { Y = 0 }) < 0) side = -side;
            float tp = crest[i] + high, tq = crest[i + 1] + high;
            walked += along.Length();
            if (run.Merlons && !m.Plain && walked >= stairAt && Stair(m, p, along.Normalized(), -side, tp, height, run.Paint, stands)) stairAt = walked + StairEveryM * m.PerMetre;
            Vector3 a = p + side, b = q + side, c = q - side, d = p - side;
            float bottom = Mathf.Min(Mathf.Min(height(a), height(b)), Mathf.Min(height(c), height(d))) - sink;
            m.Wall(a, b, bottom, tq, p - side * 2, run.Paint, tp, tq);
            m.Wall(d, c, bottom, tq, p + side * 2, run.Paint, tp, tq);
            m.Face([a with { Y = tp }, b with { Y = tq }, c with { Y = tq }, d with { Y = tp }], Vector3.Up, along, a with { Y = tp }, run.Paint);
            if (run.Merlons) Merlons(m.Small, a, b, tp, tq, side, run.Paint);
            if (run.CrownHighM > 0)
            {
                var crown = side.Normalized() * (run.CrownWideM / 2 * m.PerMetre);
                float tall = run.CrownHighM * m.PerMetre, eave = 0.25f * m.PerMetre;
                RaisedArcades.Prism(m, p + side - crown, q + side - crown, crown, tp, tp + tall, tq, tq + tall, run.Crown, false);
                RaisedArcades.Prism(m.Small, p + side - crown, q + side - crown, crown * 1.5f, tp + tall, tp + tall + eave, tq + tall, tq + tall + eave,
                    new RaisedMesh.Paint(RaisedColours.Roof(Cover.Kawara, 0.5f), RaisedCodes.Roof + (int)Cover.Kawara), false);
            }
        }
        Cap(m, world[0], world[1], ground[0] - sink, crest[0] + high, half, run.Paint);
        Cap(m, world[^1], world[^2], ground[^1] - sink, crest[^1] + high, half, run.Paint);
    }

    static void Revetment(RaisedMesh m, Vector3[] world, Func<Vector3, float> height, Run run)
    {
        float upm = m.PerMetre, half = run.WideM / 2 * upm, reach = RevetmentReachM * upm, sink = SinkM * upm, coping = CopingM * upm;
        foreach (var (p, q) in Pieces(world, PieceM * upm))
        {
            var along = (q - p) with { Y = 0 };
            if (along.LengthSquared() < 1e-12f) continue;
            var side = new Vector3(-along.Z, 0, along.X).Normalized();
            float hp0 = height(p + side * reach), hp1 = height(p - side * reach), hq0 = height(q + side * reach), hq1 = height(q - side * reach);
            RaisedArcades.Prism(m, p, q, side * half, Mathf.Min(hp0, hp1) - sink, Mathf.Max(hp0, hp1) + coping, Mathf.Min(hq0, hq1) - sink, Mathf.Max(hq0, hq1) + coping, run.Paint);
        }
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

    static bool Stair(RaisedMesh m, Vector3 from, Vector3 along, Vector3 inward, float top, Func<Vector3, float> height, RaisedMesh.Paint paint, Func<Vector3, bool>? stands)
    {
        float upm = m.PerMetre, foot = height(from + inward * 2), run = (top - foot) / Mathf.Tan(Mathf.DegToRad(StairDegrees));
        var to = from + along * run;
        Vector3 wide = inward.Normalized() * (StairWideM * upm), a0 = from + inward, a1 = a0 + wide, b0 = to + inward, b1 = b0 + wide;
        if (run <= 0 || top - foot > StairMostM * upm || Mathf.Abs(height(b1) - foot) > (top - foot) * 0.3f) return false;
        if (stands != null && (stands(a1 + wide) || stands((a1 + b1) / 2 + wide) || stands(b1 + wide))) return false;
        float low = Mathf.Min(foot, height(b1)) - SinkM * upm;
        m.Face([a0 with { Y = foot }, a1 with { Y = foot }, b1 with { Y = top }, b0 with { Y = top }], Vector3.Up - along, along, a0 with { Y = foot }, paint with { Code = RaisedCodes.Plinth });
        m.Face([a1 with { Y = low }, b1 with { Y = low }, b1 with { Y = top }, a1 with { Y = foot }], inward, along, a1 with { Y = low }, paint);
        m.Face([b0 with { Y = low }, b1 with { Y = low }, b1 with { Y = top }, b0 with { Y = top }], along, inward, b0 with { Y = low }, paint);
        return true;
    }

    static void Cap(RaisedMesh m, Vector3 end, Vector3 next, float bottom, float top, float half, RaisedMesh.Paint paint)
    {
        var along = (next - end) with { Y = 0 };
        if (along.LengthSquared() < 1e-12f) return;
        var side = new Vector3(-along.Z, 0, along.X).Normalized() * half;
        m.Wall(end - side, end + side, bottom, top, end + along.Normalized() * half, paint);
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
