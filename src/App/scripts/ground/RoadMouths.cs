using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

static class RoadMouths
{
    public const float LongM = 4.5f, RoundM = 1.6f, GaugeM = 0.72f, RutM = 0.13f;
    const float InM = 0.4f, TurnLongM = 6, RutInk = 0.55f, LeastTurn = 0.3f, ClearOfTheRound = 1.2f, LeastRoundM = 0.1f, PatchRoomPx = 2;
    const int Steps = 10, RoomSteps = 8;
    static readonly Color RutShade = new(0.74f, 0.7f, 0.64f);
    static readonly string[] Ranks = ["track", "lane", "causeway", "street", "road", "chaussee", "highway"];

    public readonly record struct Way(BuiltPainter.Shape Road, Vector2[] Px, BuiltHand.LineLook Look, float M)
    {
        public float Half => Look.Metres * M / 2;
    }

    public readonly record struct Opening(Vector2 From, Vector2 Centre, Vector2[] Ring, Vector2[][] Corners, Vector2[][] Fillets);

    public readonly record struct Patch(int Through, Vector2[] Px, float FromPx);

    public readonly record struct Mouth(Vector2 Seed, float M, Vector2 Along, float RunPx, Opening Open, Color Fill, Surface Kind, Color Ink, Color Groove, Vector2[][] Tracks, Patch Under);

    public readonly record struct Cross(int Through, int Across, Vector2 At, float OnAcross, Vector2 Along, Vector2 Way, Patch Under);

    static float Long(Vector2[] px)
    {
        float run = 0;
        for (int i = 0; i + 1 < px.Length; i++) run += (px[i + 1] - px[i]).Length();
        return run;
    }

    static float RunTo(Vector2[] px, Vector2 p)
    {
        float run = 0, best = 0, least = float.MaxValue;
        for (int i = 0; i + 1 < px.Length; i++)
        {
            var leg = px[i + 1] - px[i];
            float length = leg.Length();
            if (length < 1e-6f) continue;
            float t = Mathf.Clamp((p - px[i]).Dot(leg) / (length * length), 0, 1), off = (p - (px[i] + leg * t)).Length();
            if (off < least) (least, best) = (off, run + length * t);
            run += length;
        }
        return best;
    }

    static Vector2[] Piece(Vector2[] px, float from, float to)
    {
        var piece = new List<Vector2>();
        float run = 0;
        for (int i = 0; i + 1 < px.Length; i++)
        {
            float leg = (px[i + 1] - px[i]).Length();
            if (leg < 1e-6f) continue;
            if (run + leg >= from && piece.Count == 0) piece.Add(px[i] + (px[i + 1] - px[i]) * ((from - run) / leg));
            if (run + leg >= to)
            {
                piece.Add(px[i] + (px[i + 1] - px[i]) * ((to - run) / leg));
                break;
            }
            if (piece.Count > 0) piece.Add(px[i + 1]);
            run += leg;
        }
        return [.. piece];
    }

    static Patch Under(IReadOnlyList<Way> ways, int through, float on, Vector2 along, Vector2 into, float half, float m)
    {
        var t = ways[through];
        float sine = Mathf.Max(LeastTurn, Mathf.Abs(along.Cross(into))), reach = (half + t.Half * Mathf.Abs(along.Dot(into))) / sine + RoundM * m + PatchRoomPx;
        float from = Mathf.Max(0, on - reach);
        return new(through, Piece(t.Px, from, Mathf.Min(Long(t.Px), on + reach)), from);
    }

    static Rect2 Box(Vector2[] px)
    {
        var box = new Rect2(px[0], Vector2.Zero);
        foreach (var p in px) box = box.Expand(p);
        return box;
    }

    static float Rank(Way w) => w.Half * 100 + Array.IndexOf(Ranks, w.Road.Class);

    public static List<Cross> Crossings(IReadOnlyList<Way> ways, float surfaceFromPx)
    {
        var found = new List<Cross>();
        var boxes = ways.Select(w => Box(w.Px)).ToList();
        for (int a = 0; a < ways.Count; a++)
            for (int b = a + 1; b < ways.Count; b++)
            {
                if (ways[a].Px.Length < 2 || ways[b].Px.Length < 2 || ways[a].Look.Metres * ways[a].M < surfaceFromPx || ways[b].Look.Metres * ways[b].M < surfaceFromPx) continue;
                if (!boxes[a].Intersects(boxes[b], true)) continue;
                var (through, across) = Rank(ways[a]) >= Rank(ways[b]) ? (a, b) : (b, a);
                Way t = ways[through], x = ways[across];
                float tLong = Long(t.Px), xLong = Long(x.Px), tRun = 0;
                for (int i = 0; i + 1 < t.Px.Length; i++)
                {
                    var tLeg = t.Px[i + 1] - t.Px[i];
                    float xRun = 0;
                    for (int j = 0; j + 1 < x.Px.Length; j++)
                    {
                        var xLeg = x.Px[j + 1] - x.Px[j];
                        float turn = tLeg.Cross(xLeg);
                        if (Mathf.Abs(turn) > 1e-6f && Box([t.Px[i], t.Px[i + 1]]).Intersects(Box([x.Px[j], x.Px[j + 1]]), true))
                        {
                            float u = (x.Px[j] - t.Px[i]).Cross(xLeg) / turn, v = (x.Px[j] - t.Px[i]).Cross(tLeg) / turn;
                            float onT = tRun + u * tLeg.Length(), onX = xRun + v * xLeg.Length(), room = t.Half + x.Half;
                            Vector2 along = tLeg.Normalized(), way = xLeg.Normalized();
                            if (u is >= 0 and <= 1 && v is >= 0 and <= 1 && Mathf.Abs(along.Cross(way)) >= LeastTurn && onT > room && onT < tLong - room && onX > room && onX < xLong - room)
                                found.Add(new(through, across, t.Px[i] + tLeg * u, onX, along, way, Under(ways, through, onT, along, way, x.Half, x.M)));
                        }
                        xRun += xLeg.Length();
                    }
                    tRun += tLeg.Length();
                }
            }
        return found;
    }

    public static IEnumerable<Mouth> Of(IReadOnlyList<Way> ways, float surfaceFromPx)
    {
        if (ways.Count(w => w.Px.Length > 1 && w.Look.Metres * w.M >= surfaceFromPx) == 0) yield break;
        foreach (var crossing in Crossings(ways, surfaceFromPx))
        {
            var others = ways.Where((_, i) => i != crossing.Through && i != crossing.Across).ToList();
            foreach (int side in new[] { 1, -1 })
                if (Drawn(ways[crossing.Across], crossing.At, crossing.Way * side, crossing.OnAcross, side, ways[crossing.Through].Half, crossing.At, crossing.Along,
                        (corner, away, most) => Room(others, corner, away, most), crossing.Under) is { } mouth)
                    yield return mouth;
        }
        for (int b = 0; b < ways.Count; b++)
        {
            if (ways[b].Px.Length < 2 || ways[b].Look.Metres * ways[b].M < surfaceFromPx) continue;
            foreach (bool first in new[] { true, false })
            {
                Vector2 end = first ? ways[b].Px[0] : ways[b].Px[^1], before = first ? ways[b].Px[1] : ways[b].Px[^2];
                if ((end - before).LengthSquared() < 1e-6f) continue;
                var into = (end - before).Normalized();
                for (int a = 0; a < ways.Count; a++)
                {
                    if (a == b || Met(ways[a].Px, ways[a].Half, end, ways[a].Half + ways[b].Half) is not var (foot, along)) continue;
                    var others = ways.Where((_, i) => i != a && i != b).ToList();
                    var under = Under(ways, a, RunTo(ways[a].Px, foot), along, into, ways[b].Half, ways[b].M);
                    if (Drawn(ways[b], end, into, first ? 0 : Long(ways[b].Px), first ? -1 : 1, ways[a].Half, foot, along, (corner, away, most) => Room(others, corner, away, most), under,
                            false) is { } mouth) yield return mouth;
                    break;
                }
            }
        }
    }

    static float Off(Vector2[] road, Vector2 p)
    {
        float least = float.MaxValue;
        for (int i = 0; i + 1 < road.Length; i++) least = Mathf.Min(least, p.DistanceTo(Geometry2D.GetClosestPointToSegment(p, road[i], road[i + 1])));
        return least;
    }

    static float Room(List<Way> others, Vector2 corner, Vector2 away, float most)
    {
        for (int k = 0; k <= RoomSteps; k++)
            if (others.Any(w => Off(w.Px, corner + away * (most * 2 * k / RoomSteps)) < w.Half)) return most * Math.Max(0, k - 1) / RoomSteps;
        return most;
    }

    static Surface Earth(Surface lining) => lining switch
    {
        Surface.Street => Surface.Paving,
        Surface.Road => Surface.Trodden,
        Surface.Track => Surface.TroddenGrass,
        Surface.Gravel => Surface.TroddenStone,
        _ => lining,
    };

    static Mouth? Drawn(Way b, Vector2 end, Vector2 into, float onEnd, int forward, float roadHalf, Vector2 foot, Vector2 along, Func<Vector2, Vector2, float, float> room, Patch under,
        bool crosses = true)
    {
        if (Laid(end, into, b.Half, foot, along, roadHalf, b.M, room) is not { } open) return null;
        var fill = (b.Look.Fill.A > 0 ? b.Look.Fill : b.Look.Close) with { A = 1 };
        var lining = SurfaceBook.Lining(b.Road.Class, KTable.Core.Model.Manners.Kin(b.Road.Built));
        bool rutted = lining is Surface.Road or Surface.Track or Surface.Gravel;
        var tracks = new List<Vector2[]>();
        foreach (int wheel in new[] { 1, -1 })
        {
            if (rutted && crosses) tracks.Add(Track(open.From, (open.From + open.Centre) / 2, open.Centre, wheel * GaugeM * b.M));
            foreach (int turn in new[] { 1, -1 })
                if (rutted && !crosses) tracks.Add(Track(open.From, open.Centre, open.Centre + along * (turn * (TurnLongM * b.M + b.Half)), wheel * GaugeM * b.M));
        }
        var groove = new Color(fill.R * RutShade.R, fill.G * RutShade.G, fill.B * RutShade.B, RutInk);
        float runPx = onEnd - forward * (end - open.From).Dot(into);
        return new(b.Road.Uv[0], b.M, into * forward, runPx, open, fill, Earth(lining), b.Look.Ink with { A = 1 }, groove, [.. tracks], under);
    }

    public static (Vector2 Foot, Vector2 Along)? Met(Vector2[] road, float half, Vector2 p, float within)
    {
        float least = within, run = 0, whole = 0;
        (Vector2, Vector2)? met = null;
        for (int i = 0; i + 1 < road.Length; i++) whole += (road[i + 1] - road[i]).Length();
        for (int i = 0; i + 1 < road.Length; i++)
        {
            var leg = road[i + 1] - road[i];
            float length = leg.Length();
            if (length < 1e-4f) continue;
            float t = Mathf.Clamp((p - road[i]).Dot(leg) / (length * length), 0, 1), off = (p - (road[i] + leg * t)).Length(), at = run + length * t;
            if (off < least && at > half && at < whole - half)
            {
                least = off;
                met = (road[i] + leg * t, leg / length);
            }
            run += length;
        }
        return met;
    }

    public static Opening? Laid(Vector2 end, Vector2 into, float half, Vector2 foot, Vector2 along, float roadHalf, float m, Func<Vector2, Vector2, float, float>? room = null)
    {
        var side = along.Orthogonal();
        if (side.Dot(into) > 0) side = -side;
        var rim = foot + side * roadHalf;
        if (Crossing(end, into, rim, along) is not { } edge || Crossing(end, into, foot, along) is not { } centre) return null;
        var across = into.Orthogonal();
        var away = along.Dot(across) < 0 ? -along : along;
        float round = RoundM * m;
        Vector2 left = Crossing(end + across * half, into, rim, along) ?? edge, right = Crossing(end - across * half, into, rim, along) ?? edge;
        var from = edge - into * Mathf.Max(LongM * m, Mathf.Max((edge - left).Dot(into), (edge - right).Dot(into)) + round * ClearOfTheRound);
        float onLeft = room?.Invoke(left, away, round) ?? round, onRight = room?.Invoke(right, -away, round) ?? round;
        Vector2[] one = onLeft < LeastRoundM * m ? [left] : Track(left - into * onLeft, left, left + away * onLeft, 0);
        Vector2[] other = onRight < LeastRoundM * m ? [right] : Track(right - away * onRight, right, right - into * onRight, 0);
        var within = -side * (InM * m);
        Vector2[] ring = [from + across * half, .. one, one[^1] + within, other[0] + within, .. other, from - across * half];
        var rounded = new[] { (Curve: one, Corner: left), (Curve: other, Corner: right) }.Where(c => c.Curve.Length > 1).ToArray();
        return new(from, centre, ring, [.. rounded.Select(c => c.Curve)], [.. rounded.Select(c => c.Curve.Append(c.Corner).ToArray())]);
    }

    public static Vector2[] Track(Vector2 from, Vector2 turn, Vector2 to, float beside)
    {
        var line = new Vector2[Steps + 1];
        for (int k = 0; k <= Steps; k++)
        {
            float t = k / (float)Steps;
            var way = (turn - from).Lerp(to - turn, t).Normalized();
            line[k] = from.Lerp(turn, t).Lerp(turn.Lerp(to, t), t) + way.Orthogonal() * beside;
        }
        return line;
    }

    static Vector2? Crossing(Vector2 p, Vector2 way, Vector2 q, Vector2 other)
    {
        float turn = way.Cross(other);
        return Mathf.Abs(turn) < LeastTurn ? null : p + way * ((q - p).Cross(other) / turn);
    }
}
