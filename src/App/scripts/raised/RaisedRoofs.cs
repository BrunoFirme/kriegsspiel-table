using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class RaisedRoofs
{
    public const float OverhangM = 0.35f, GableOverhangM = 0.25f, ParapetM = 0.7f, MansardLowM = 2.6f, MansardInset = 0.18f, CourtDeepShare = 0.32f, CourtMostM = 8;
    const float HalfHipAt = 0.6f, ShallowPitch = 0.36f, DomeShare = 0.9f;
    const int Arcs = 12, Rings = 5, VaultSteps = 8;

    public readonly record struct Raise(RaisedMesh M, RaisedFrame F, float Eaves, float Bottom, RaisedMesh.Paint Wall, RaisedMesh.Paint Roof, float Pitch);

    static float Tan(float pitch) => Mathf.Tan(pitch);

    const float EdgeM = 0.16f, ThickEdgeM = 0.4f, CapWideM = 0.22f, CapHighM = 0.14f, EdgeDarker = 0.25f;

    static float Thick(Raise r) => (int)r.Roof.Code - RaisedCodes.Roof is (int)Cover.Thatch or (int)Cover.Palm or (int)Cover.Turf ? ThickEdgeM : EdgeM;

    static RaisedMesh.Paint Edged(Raise r) => r.Roof with { Tint = r.Roof.Tint.Darkened(EdgeDarker) };

    static void Rim(Raise r, Vector3 a, Vector3 b, Vector3 outward)
    {
        if (r.M.Plain) return;
        var down = Vector3.Down * (Thick(r) * r.M.PerMetre);
        r.M.Face([a, b, b + down, a + down], outward, b - a, a + down, Edged(r));
    }

    static void Rims(Raise r, Vector3[] eave)
    {
        var middle = RaisedFrame.Middle(eave);
        for (int i = 0; i < eave.Length; i++)
        {
            Vector3 a = eave[i], b = eave[(i + 1) % eave.Length];
            Rim(r, a, b, ((a + b) / 2 - middle) with { Y = 0 });
        }
    }

    static void Cap(Raise r, Vector3 r0, Vector3 r1, Vector3 across)
    {
        if ((r1 - r0).LengthSquared() < 1e-10f) return;
        float w = CapWideM * r.M.PerMetre;
        Vector3 up = Vector3.Up * (CapHighM * r.M.PerMetre), side = across * w, below = Vector3.Down * (w * Tan(r.Pitch));
        var fine = r.M.Small;
        fine.Face([r0 - side + below, r1 - side + below, r1 + up, r0 + up], Vector3.Up - across, r1 - r0, r0 - side + below, Edged(r));
        fine.Face([r0 + side + below, r1 + side + below, r1 + up, r0 + up], Vector3.Up + across, r1 - r0, r0 + side + below, Edged(r));
    }

    public static void Walls(Raise r, Vector3[] ring, float top, RaisedMesh.Paint wall, Vector3? doorA = null, Vector3? doorB = null, float doorAt = 0.5f, bool shop = false)
    {
        var inside = RaisedFrame.Middle(ring);
        foreach (var (a, b) in RaisedFrame.Sides(ring))
        {
            var paint = wall;
            if (doorA is { } da && doorB is { } db && Same(a, b, da, db, out bool reversed))
                paint = wall with { Door = (reversed ? 1 - doorAt : doorAt) * (b - a).Length() / r.M.PerMetre, Storeys = wall.Storeys + (shop ? 0.5f : 0) };
            r.M.Wall(a, b, r.Bottom, top, inside, paint);
        }
    }

    static bool Same(Vector3 a, Vector3 b, Vector3 da, Vector3 db, out bool reversed)
    {
        const float Near = 1e-4f;
        reversed = a.DistanceSquaredTo(db) < Near && b.DistanceSquaredTo(da) < Near;
        return reversed || a.DistanceSquaredTo(da) < Near && b.DistanceSquaredTo(db) < Near;
    }

    static Vector3 Up(Vector3 p, float y) => p with { Y = y };

    public static void Gable(Raise r)
    {
        var f = r.F;
        float o = OverhangM * r.M.PerMetre, og = GableOverhangM * r.M.PerMetre, rise = Tan(r.Pitch) * f.Short / 2, drop = o * Tan(r.Pitch);
        float top = r.Eaves + rise, low = r.Eaves - drop;
        Vector3 x = f.Along * og, y = f.Across * o;
        var r0 = Up(f.From - x, top);
        var r1 = Up(f.To + x, top);
        r.M.Face([Up(f.A0 - x - y, low), Up(f.A1 + x - y, low), r1, r0], Vector3.Up - f.Across, f.Along, Up(f.A0, r.Eaves), r.Roof);
        r.M.Face([Up(f.A3 - x + y, low), Up(f.A2 + x + y, low), r1, r0], Vector3.Up + f.Across, f.Along, Up(f.A3, r.Eaves), r.Roof);
        r.M.Face([Up(f.A0, r.Eaves), Up(f.A3, r.Eaves), Up(f.From, top)], -f.Along, f.Across, Up(f.A0, r.Bottom), r.Wall);
        r.M.Face([Up(f.A1, r.Eaves), Up(f.A2, r.Eaves), Up(f.To, top)], f.Along, f.Across, Up(f.A1, r.Bottom), r.Wall);
        Vector3 e0 = Up(f.A0 - x - y, low), e1 = Up(f.A1 + x - y, low), e2 = Up(f.A2 + x + y, low), e3 = Up(f.A3 - x + y, low);
        Rim(r, e0, e1, -f.Across);
        Rim(r, e3, e2, f.Across);
        Rim(r, e0, r0, -f.Along);
        Rim(r, e3, r0, -f.Along);
        Rim(r, e1, r1, f.Along);
        Rim(r, e2, r1, f.Along);
        Cap(r, r0, r1, f.Across);
    }

    public static void Hip(Raise r, float ridgeShare = 1)
    {
        var f = r.F;
        float o = OverhangM * r.M.PerMetre, rise = Tan(r.Pitch) * f.Short / 2, drop = o * Tan(r.Pitch);
        float far = Mathf.Min(0.5f, 0.5f * f.Short / Mathf.Max(f.Long, 1e-6f) * ridgeShare), top = r.Eaves + rise, low = r.Eaves - drop;
        var r0 = Up(f.From.Lerp(f.To, far), top);
        var r1 = Up(f.To.Lerp(f.From, far), top);
        var e = f.Grown(o);
        Vector3 e0 = Up(e.A0, low), e1 = Up(e.A1, low), e2 = Up(e.A2, low), e3 = Up(e.A3, low);
        r.M.Face([e0, e1, r1, r0], Vector3.Up - f.Across, f.Along, Up(f.A0, r.Eaves), r.Roof);
        r.M.Face([e3, e2, r1, r0], Vector3.Up + f.Across, f.Along, Up(f.A3, r.Eaves), r.Roof);
        r.M.Face([e0, e3, r0], Vector3.Up - f.Along, f.Across, Up(f.A0, r.Eaves), r.Roof);
        r.M.Face([e1, e2, r1], Vector3.Up + f.Along, f.Across, Up(f.A1, r.Eaves), r.Roof);
        Rims(r, [e0, e1, e2, e3]);
        Cap(r, r0, r1, f.Across);
    }

    public static (Vector3 At, float Y) Ridge(Raise r, Form form, float t)
    {
        var f = r.F;
        float tan = Tan(r.Pitch), far = Mathf.Min(0.5f, 0.5f * f.Short / Mathf.Max(f.Long, 1e-6f)), top = r.Eaves + tan * f.Short / 2;
        return form switch
        {
            Form.Hip => (f.From.Lerp(f.To, far).Lerp(f.To.Lerp(f.From, far), t), top),
            Form.Pyramid => (f.Centre, r.Eaves + tan * Mathf.Min(f.Short, f.Long) / 2),
            Form.Mansard => (f.At(Mathf.Lerp(0.3f, 0.7f, t), 0.5f), r.Eaves + MansardLowM * r.M.PerMetre + Tan(ShallowPitch) * f.Short * (0.5f - MansardInset)),
            Form.Valley => (f.At(t, 0.25f), r.Eaves + tan * f.Short / 4),
            Form.Lean => (f.At(t, 0.82f), r.Eaves + tan * f.Short * 0.82f),
            _ => (f.From.Lerp(f.To, t), top),
        };
    }

    public static void Pyramid(Raise r)
    {
        var f = r.F;
        float o = OverhangM * r.M.PerMetre, rise = Tan(r.Pitch) * Mathf.Min(f.Short, f.Long) / 2, low = r.Eaves - o * Tan(r.Pitch);
        var apex = Up(f.Centre, r.Eaves + rise);
        var e = f.Grown(o);
        var ring = e.Ring;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = Up(ring[i], low), b = Up(ring[(i + 1) % 4], low);
            var outward = ((a + b) / 2 - f.Centre) with { Y = 0 };
            r.M.Face([a, b, apex], Vector3.Up + outward.Normalized(), b - a, a, r.Roof);
        }
        Rims(r, [Up(ring[0], low), Up(ring[1], low), Up(ring[2], low), Up(ring[3], low)]);
    }

    public static void HalfHip(Raise r)
    {
        var f = r.F;
        float rise = Tan(r.Pitch) * f.Short / 2, o = OverhangM * r.M.PerMetre, low = r.Eaves - o * Tan(r.Pitch), top = r.Eaves + rise;
        float knee = r.Eaves + rise * HalfHipAt, inset = (1 - HalfHipAt) * f.Short / 2;
        Vector3 y = f.Across * o, q = f.Across * (f.Short * HalfHipAt / 2);
        Vector3 q0 = Up(f.A0 + q, knee), q1 = Up(f.A1 + q, knee), q2 = Up(f.A2 - q, knee), q3 = Up(f.A3 - q, knee);
        Vector3 r0 = Up(f.From + f.Along * inset, top), r1 = Up(f.To - f.Along * inset, top);
        r.M.Face([Up(f.A0 - y, low), Up(f.A1 - y, low), q1, r1, r0, q0], Vector3.Up - f.Across, f.Along, Up(f.A0, r.Eaves), r.Roof);
        r.M.Face([Up(f.A3 + y, low), Up(f.A2 + y, low), q2, r1, r0, q3], Vector3.Up + f.Across, f.Along, Up(f.A3, r.Eaves), r.Roof);
        r.M.Face([q0, q3, r0], Vector3.Up - f.Along, f.Across, q0, r.Roof);
        r.M.Face([q1, q2, r1], Vector3.Up + f.Along, f.Across, q1, r.Roof);
        r.M.Face([Up(f.A0, r.Eaves), Up(f.A3, r.Eaves), q3, q0], -f.Along, f.Across, Up(f.A0, r.Bottom), r.Wall);
        r.M.Face([Up(f.A1, r.Eaves), Up(f.A2, r.Eaves), q2, q1], f.Along, f.Across, Up(f.A1, r.Bottom), r.Wall);
        Rim(r, Up(f.A0 - y, low), Up(f.A1 - y, low), -f.Across);
        Rim(r, Up(f.A3 + y, low), Up(f.A2 + y, low), f.Across);
        Cap(r, r0, r1, f.Across);
    }

    public static void Lean(Raise r)
    {
        var f = r.F;
        float rise = Tan(r.Pitch) * f.Short, o = OverhangM * r.M.PerMetre;
        Vector3 x = f.Along * o, y = f.Across * o;
        r.M.Face([Up(f.A0 - x - y, r.Eaves - o * Tan(r.Pitch)), Up(f.A1 + x - y, r.Eaves - o * Tan(r.Pitch)), Up(f.A2 + x + y, r.Eaves + rise + o * Tan(r.Pitch)),
            Up(f.A3 - x + y, r.Eaves + rise + o * Tan(r.Pitch))], Vector3.Up - f.Across, f.Along, Up(f.A0, r.Eaves), r.Roof);
        r.M.Face([Up(f.A0, r.Eaves), Up(f.A3, r.Eaves), Up(f.A3, r.Eaves + rise)], -f.Along, f.Across, Up(f.A0, r.Bottom), r.Wall);
        r.M.Face([Up(f.A1, r.Eaves), Up(f.A2, r.Eaves), Up(f.A2, r.Eaves + rise)], f.Along, f.Across, Up(f.A1, r.Bottom), r.Wall);
        r.M.Wall(f.A3, f.A2, r.Eaves, r.Eaves + rise, f.Centre, r.Wall);
        float low = r.Eaves - o * Tan(r.Pitch), high = r.Eaves + rise + o * Tan(r.Pitch);
        Vector3 l0 = Up(f.A0 - x - y, low), l1 = Up(f.A1 + x - y, low), h0 = Up(f.A3 - x + y, high), h1 = Up(f.A2 + x + y, high);
        Rim(r, l0, l1, -f.Across);
        Rim(r, h0, h1, f.Across);
        Rim(r, l0, h0, -f.Along);
        Rim(r, l1, h1, f.Along);
    }

    public static void Flat(Raise r, bool parapet, bool merlons)
    {
        var f = r.F;
        r.M.Face([Up(f.A0, r.Eaves), Up(f.A1, r.Eaves), Up(f.A2, r.Eaves), Up(f.A3, r.Eaves)], Vector3.Up, f.Along, Up(f.A0, r.Eaves), r.Roof);
        if (parapet) Parapet(r.M, f.Ring, r.Eaves, ParapetM * r.M.PerMetre, r.Wall, merlons);
    }

    public static void Parapet(RaisedMesh m, Vector3[] ring, float at, float high, RaisedMesh.Paint wall, bool merlons, bool court = false)
    {
        float thick = 0.45f * m.PerMetre * (court ? -1 : 1), merlon = 1.9f * m.PerMetre;
        var middle = RaisedFrame.Middle(ring);
        var inner = new Vector3[ring.Length];
        for (int i = 0; i < ring.Length; i++) inner[i] = ring[i] + (middle - ring[i]).Normalized() * thick;
        for (int i = 0; i < ring.Length; i++)
        {
            Vector3 a = inner[i], b = inner[(i + 1) % ring.Length], c = ring[i], d = ring[(i + 1) % ring.Length];
            m.Wall(c, d, at, at + high, court ? c + (c - middle) : middle, wall);
            m.Wall(a, b, at, at + high, court ? middle : a + (a - middle), wall);
            m.Face([Up(c, at + high), Up(d, at + high), Up(b, at + high), Up(a, at + high)], Vector3.Up, b - a, Up(a, at + high), wall);
            if (!merlons) continue;
            var side = b - a;
            int count = Mathf.Max(1, (int)(side.Length() / merlon));
            for (int k = 0; k < count; k++)
            {
                float t0 = (k + 0.25f) / count, t1 = (k + 0.75f) / count;
                Vector3 p0 = c.Lerp(d, t0), p1 = c.Lerp(d, t1);
                Vector3 n0 = a.Lerp(b, t0), n1 = a.Lerp(b, t1);
                Block(m.Small, [p0, p1, n1, n0], at + high, at + high * 1.8f, wall);
            }
        }
    }

    public static void Block(RaisedMesh m, Vector3[] ring, float bottom, float top, RaisedMesh.Paint paint)
    {
        var middle = RaisedFrame.Middle(ring);
        foreach (var (a, b) in RaisedFrame.Sides(ring)) m.Wall(a, b, bottom, top, middle, paint);
        var lid = new Vector3[ring.Length];
        for (int i = 0; i < ring.Length; i++) lid[i] = Up(ring[i], top);
        m.Face(lid, Vector3.Up, ring[1] - ring[0], lid[0], paint);
    }

    public static void Mansard(Raise r)
    {
        var f = r.F;
        float k = MansardInset * f.Short, low = MansardLowM * r.M.PerMetre, o = OverhangM * r.M.PerMetre;
        var inner = f.Inset(k / f.Long, 1 - k / f.Long, MansardInset, 1 - MansardInset);
        var outer = f.Grown(o);
        float knee = r.Eaves + low;
        var e = outer.Ring;
        var q = inner.Ring;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = Up(e[i], r.Eaves - o), b = Up(e[(i + 1) % 4], r.Eaves - o), c = Up(q[(i + 1) % 4], knee), d = Up(q[i], knee);
            var outward = ((a + b) / 2 - f.Centre) with { Y = 0 };
            r.M.Face([a, b, c, d], Vector3.Up * 0.2f + outward.Normalized(), b - a, a, r.Roof);
        }
        Rims(r, [Up(e[0], r.Eaves - o), Up(e[1], r.Eaves - o), Up(e[2], r.Eaves - o), Up(e[3], r.Eaves - o)]);
        Hip(r with { F = inner, Eaves = knee, Pitch = ShallowPitch });
    }

    public static void Valley(Raise r)
    {
        foreach (var half in new[] { r.F.Part(0, 1, 0, 0.5f), r.F.Part(0, 1, 0.5f, 1) })
            Gable(r with { F = half });
    }

    public static void Dome(Raise r, Vector3 centre, float radiusX, float radiusZ, Vector3 along, Vector3 across, float at, float high, bool onion)
    {
        var profile = new List<Vector2>();
        if (onion)
        {
            profile.AddRange([new(1, 0), new(1.12f, 0.22f), new(1.08f, 0.45f), new(0.8f, 0.72f), new(0.42f, 0.95f), new(0.14f, 1.18f), new(0, 1.42f)]);
        }
        else
            for (int i = 0; i <= Rings; i++)
            {
                float a = i * Mathf.Pi / 2 / Rings;
                profile.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
        Turned(r.M, centre with { Y = at }, along * radiusX, across * radiusZ, high, profile, r.Roof);
    }

    public static void Turned(RaisedMesh m, Vector3 centre, Vector3 rx, Vector3 rz, float high, List<Vector2> profile, RaisedMesh.Paint paint, int arcs = Arcs)
    {
        for (int j = 0; j + 1 < profile.Count; j++)
            for (int i = 0; i < arcs; i++)
            {
                float a0 = i * Mathf.Tau / arcs, a1 = (i + 1) * Mathf.Tau / arcs;
                Vector3 d0 = rx * Mathf.Cos(a0) + rz * Mathf.Sin(a0), d1 = rx * Mathf.Cos(a1) + rz * Mathf.Sin(a1);
                Vector3 p0 = centre + d0 * profile[j].X + Vector3.Up * profile[j].Y * high, p1 = centre + d1 * profile[j].X + Vector3.Up * profile[j].Y * high;
                Vector3 p2 = centre + d1 * profile[j + 1].X + Vector3.Up * profile[j + 1].Y * high, p3 = centre + d0 * profile[j + 1].X + Vector3.Up * profile[j + 1].Y * high;
                var outward = (d0 + d1).Normalized() + Vector3.Up * 0.3f;
                if (profile[j + 1].X < 1e-4f) m.Face([p0, p1, p2], outward, p1 - p0, p0, paint);
                else m.Face([p0, p1, p2, p3], outward, p1 - p0, p0, paint);
            }
    }

    public static void Cone(Raise r, bool walls)
    {
        var f = r.F;
        float rx = f.Long / 2, rz = f.Short / 2, o = OverhangM * r.M.PerMetre, rise = Tan(r.Pitch) * Mathf.Min(rx, rz);
        if (walls) Turned(r.M, Up(f.Centre, r.Bottom), f.Along * rx, f.Across * rz, r.Eaves - r.Bottom, [new(1, 0), new(1, 1)], r.Wall);
        Turned(r.M, Up(f.Centre, r.Eaves - o * Tan(r.Pitch)), f.Along * (rx + o), f.Across * (rz + o), rise + o * Tan(r.Pitch), [new(1, 0), new(0, 1)], r.Roof);
    }

    public static void DomeOver(Raise r, bool round)
    {
        var f = r.F;
        float rx = f.Long / 2, rz = f.Short / 2;
        if (round) Turned(r.M, Up(f.Centre, r.Bottom), f.Along * rx, f.Across * rz, r.Eaves - r.Bottom, [new(1, 0), new(1, 1)], r.Wall);
        else Flat(r, false, false);
        float radius = Mathf.Min(rx, rz) * (round ? 1 : DomeShare);
        Dome(r, f.Centre, radius, radius, f.Along, f.Across, r.Eaves, radius, false);
    }

    public static void Court(Raise r)
    {
        var f = r.F;
        float deep = Mathf.Min(CourtDeepShare * f.Short, CourtMostM * r.M.PerMetre), rise = Tan(r.Pitch) * deep / 2, o = rise > 0 ? OverhangM * r.M.PerMetre : 0;
        var inner = f.Inset(deep / f.Long, 1 - deep / f.Long, deep / f.Short, 1 - deep / f.Short);
        var ridge = f.Inset(deep / 2 / f.Long, 1 - deep / 2 / f.Long, deep / 2 / f.Short, 1 - deep / 2 / f.Short);
        var outer = f.Grown(o);
        foreach (var (a, b) in RaisedFrame.Sides(inner.Ring))
        {
            var mid = (a + b) / 2;
            r.M.Wall(a, b, r.Bottom, r.Eaves, mid + (mid - f.Centre), r.Wall);
        }
        float top = r.Eaves + rise;
        var e = outer.Ring;
        var q = inner.Ring;
        var g = ridge.Ring;
        for (int i = 0; i < 4; i++)
        {
            Vector3 qa = Up(q[i], r.Eaves), qb = Up(q[(i + 1) % 4], r.Eaves);
            if (rise <= 0)
            {
                r.M.Face([Up(e[i], r.Eaves), Up(e[(i + 1) % 4], r.Eaves), qb, qa], Vector3.Up, f.Along, Up(f.A0, r.Eaves), r.Roof);
                continue;
            }
            Vector3 a = Up(e[i], r.Eaves - o * Tan(r.Pitch)), b = Up(e[(i + 1) % 4], r.Eaves - o * Tan(r.Pitch)), c = Up(g[(i + 1) % 4], top), d = Up(g[i], top);
            var outward = ((a + b) / 2 - f.Centre) with { Y = 0 };
            r.M.Face([a, b, c, d], Vector3.Up + outward.Normalized(), b - a, a, r.Roof);
            r.M.Face([qa, qb, c, d], Vector3.Up - outward.Normalized(), qb - qa, qa, r.Roof);
            Rim(r, a, b, outward);
        }
        if (rise > 0) return;
        Parapet(r.M, f.Ring, r.Eaves, ParapetM * r.M.PerMetre, r.Wall, false);
        Parapet(r.M, q, r.Eaves, ParapetM * r.M.PerMetre, r.Wall, false, true);
    }

    public static void Vault(Raise r)
    {
        var f = r.F;
        float radius = f.Short / 2;
        for (int i = 0; i < VaultSteps; i++)
        {
            float a0 = i * Mathf.Pi / VaultSteps, a1 = (i + 1) * Mathf.Pi / VaultSteps;
            Vector3 s0 = f.Across * (radius - radius * Mathf.Cos(a0)) + Vector3.Up * (radius * Mathf.Sin(a0) * 0.8f);
            Vector3 s1 = f.Across * (radius - radius * Mathf.Cos(a1)) + Vector3.Up * (radius * Mathf.Sin(a1) * 0.8f);
            Vector3 b0 = Up(f.A0, r.Eaves), b1 = Up(f.A1, r.Eaves);
            var outward = Vector3.Up * Mathf.Sin((a0 + a1) / 2) - f.Across * Mathf.Cos((a0 + a1) / 2);
            r.M.Face([b0 + s0, b1 + s0, b1 + s1, b0 + s1], outward, f.Along, b0, r.Roof);
            r.M.Face([Up(f.From, r.Eaves), b0 + s0, b0 + s1], -f.Along, f.Across, b0, r.Wall);
            r.M.Face([Up(f.To, r.Eaves), b1 + s0, b1 + s1], f.Along, f.Across, b1, r.Wall);
        }
    }
}
