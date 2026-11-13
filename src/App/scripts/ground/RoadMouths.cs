using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

static class RoadMouths
{
    public const float LongM = 4.5f, RoundM = 1.6f, GaugeM = 0.72f, RutM = 0.13f;
    const float InM = 0.4f, TurnLongM = 6, RutInk = 0.55f, LeastTurn = 0.3f, ClearOfTheRound = 1.2f, ClearOfTheRutsM = 40, LeastRoundM = 0.1f;
    const int Steps = 10, RoomSteps = 8;
    static readonly Color RutShade = new(0.74f, 0.7f, 0.64f);

    public readonly record struct Way(BuiltPainter.Shape Road, Vector2[] Px, BuiltHand.LineLook Look, float M)
    {
        public float Half => Look.Metres * M / 2;
    }

    public readonly record struct Opening(Vector2 From, Vector2 Centre, Vector2[] Ring, Vector2[][] Corners);

    public readonly record struct Mouth(Vector2 Seed, float M, Vector2 Into, Vector2 Origin, Opening Open, Color Fill, Surface Kind, Color Ink, Color Groove, Vector2[][] Tracks);

    public static IEnumerable<Mouth> Of(IReadOnlyList<Way> ways, float surfaceFromPx)
    {
        for (int b = 0; b < ways.Count; b++)
        {
            if (ways[b].Px.Length < 2 || ways[b].Look.Metres * ways[b].M < surfaceFromPx) continue;
            foreach (bool first in new[] { true, false })
            {
                Vector2 end = first ? ways[b].Px[0] : ways[b].Px[^1], before = first ? ways[b].Px[1] : ways[b].Px[^2];
                if ((end - before).LengthSquared() < 1e-6f) continue;
                for (int a = 0; a < ways.Count; a++)
                {
                    if (a == b || Met(ways[a].Px, ways[a].Half, end, ways[a].Half + ways[b].Half) is not var (foot, along)) continue;
                    var others = ways.Where((_, i) => i != a && i != b).ToList();
                    if (Drawn(ways[b], end, (end - before).Normalized(), ways[a].Half, foot, along, (corner, away, most) => Room(others, corner, away, most)) is { } mouth) yield return mouth;
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

    static Mouth? Drawn(Way b, Vector2 end, Vector2 into, float roadHalf, Vector2 foot, Vector2 along, Func<Vector2, Vector2, float, float> room)
    {
        if (Laid(end, into, b.Half, foot, along, roadHalf, b.M, room) is not { } open) return null;
        var fill = (b.Look.Fill.A > 0 ? b.Look.Fill : b.Look.Close) with { A = 1 };
        var lining = SurfaceBook.Lining(b.Road.Class, b.Road.Manner);
        bool rutted = lining is Surface.Road or Surface.Track or Surface.Gravel;
        var tracks = new List<Vector2[]>();
        foreach (int turn in new[] { 1, -1 })
            foreach (int wheel in new[] { 1, -1 })
                if (rutted) tracks.Add(Track(open.From, open.Centre, open.Centre + along * (turn * (TurnLongM * b.M + b.Half)), wheel * GaugeM * b.M));
        var groove = new Color(fill.R * RutShade.R, fill.G * RutShade.G, fill.B * RutShade.B, RutInk);
        var origin = rutted ? open.From + into.Orthogonal() * (ClearOfTheRutsM * b.M) : open.From;
        return new(b.Road.Uv[0], b.M, into, origin, open, fill, lining == Surface.Street ? Surface.Paving : lining, b.Look.Ink with { A = 1 }, groove, [.. tracks]);
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
        return new(from, centre, ring, new[] { one, other }.Where(corner => corner.Length > 1).ToArray());
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
