using System.Numerics;
using KTable.Client.Geometry;
using static KTable.Client.Scenery.PropAtlas;

namespace KTable.Client.Scenery;

public sealed class PropMaker
{
    readonly PerlinNoise _noise;

    public PropMaker(PerlinNoise noise) => _noise = noise;

    static Basis Yaw(float deg) => new(Axis.Up, Mathf.DegToRad(deg));
    static float R(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    static void FirePit(PropMesh m, Vector3 at, Random rng)
    {
        m.Cylinder(at, at + new Vector3(0, 0.008f, 0), 0.16f, M.Ash, 18, M.Ash, capA: false);
        int n = 11;
        for (int i = 0; i < n; i++)
        {
            float t = i * Mathf.Tau / n + R(rng, -0.12f, 0.12f), rr = R(rng, 0.17f, 0.2f);
            var size = new Vector3(R(rng, 0.06f, 0.09f), R(rng, 0.045f, 0.065f), R(rng, 0.05f, 0.07f));
            m.Lump(at + new Vector3(Mathf.Cos(t) * rr, size.Y * 0.32f, Mathf.Sin(t) * rr), size, Yaw(R(rng, 0, 180)), M.Stone, rng.Next(), 0.22f);
        }
        for (int i = 0; i < 4; i++)
        {
            float t = i * Mathf.Tau / 4 + R(rng, 0.2f, 0.6f);
            var d = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
            m.Cylinder(at + d * 0.13f + new Vector3(0, 0.015f, 0), at - d * 0.01f + new Vector3(0, R(rng, 0.08f, 0.11f), 0), R(rng, 0.016f, 0.022f), M.Char, 7, M.Char);
        }
    }

    static void Tripod(PropMesh m, Vector3 at, float yaw)
    {
        var apex = at + new Vector3(0, 0.42f, 0);
        for (int i = 0; i < 3; i++)
        {
            float t = Mathf.DegToRad(yaw + i * 120);
            m.Cylinder(at + new Vector3(Mathf.Cos(t) * 0.2f, 0, Mathf.Sin(t) * 0.2f), apex + new Vector3(Mathf.Cos(t) * 0.02f, 0.03f, Mathf.Sin(t) * 0.02f), 0.009f, M.Bark, 6, M.EndGrain);
        }
        m.Cylinder(apex, at + new Vector3(0, 0.2f, 0), 0.003f, M.Iron, 4);
        m.Cylinder(at + new Vector3(0, 0.12f, 0), at + new Vector3(0, 0.195f, 0), 0.055f, M.Iron, 14, M.Char, 0.06f);
        m.Cylinder(at + new Vector3(0, 0.195f, 0), at + new Vector3(0, 0.205f, 0), 0.064f, M.Iron, 14, M.Iron);
    }

    static void Tent(PropMesh m, Vector3 at, float yaw, Random rng, float len = 0.78f, float wid = 0.56f)
    {
        var r = Yaw(yaw);
        Vector3 P(float x, float y, float z) => at + r * new Vector3(x, y, z);
        float hl = len / 2, hw = wid / 2, wall = 0.13f, ridge = 0.43f;
        var inside = at + new Vector3(0, 0.2f, 0);
        m.Quad(P(-hl, 0.006f, -hw), P(hl, 0.006f, -hw), P(hl, 0.006f, hw), P(-hl, 0.006f, hw), M.EarthDark, at - Axis.Up);
        foreach (var s in new[] { -1f, 1f })
        {
            m.Sheet(P(-hl, wall, s * hw), P(hl, wall, s * hw), P(hl, 0, s * hw * 1.03f), P(-hl, 0, s * hw * 1.03f), M.CanvasDark, inside, 0.006f, 6, 2);
            m.Sheet(P(-hl, ridge, 0), P(hl, ridge, 0), P(hl, wall, s * hw), P(-hl, wall, s * hw), M.Canvas, inside, 0.025f, 8, 4,
                bump: (u, v) => r * new Vector3(0, 0, s * 0.006f * Mathf.Sin(u * 23 + v * 5)));
        }
        var back = new[] { P(-hl, 0, -hw), P(-hl, wall, -hw), P(-hl, ridge, 0), P(-hl, wall, hw), P(-hl, 0, hw) };
        m.Tri(back[0], back[1], back[3], Uv(M.CanvasDark, 0, 1), Uv(M.CanvasDark, 0, 0.6f), Uv(M.CanvasDark, 1, 0.6f), (back[1] - inside) with { Y = 0 });
        m.Tri(back[0], back[3], back[4], Uv(M.CanvasDark, 0, 1), Uv(M.CanvasDark, 1, 0.6f), Uv(M.CanvasDark, 1, 1), (back[1] - inside) with { Y = 0 });
        m.Tri(back[1], back[2], back[3], Uv(M.CanvasDark, 0, 0.6f), Uv(M.CanvasDark, 0.5f, 0), Uv(M.CanvasDark, 1, 0.6f), (back[1] - inside) with { Y = 0 });
        var fr = new[] { P(hl, 0, -hw), P(hl, wall, -hw), P(hl, ridge, 0), P(hl, wall, hw), P(hl, 0, hw) };
        var doorL = P(hl, 0, -0.06f);
        var doorR = P(hl, 0, 0.06f);
        var top = P(hl, ridge * 0.92f, 0);
        var outF = P(hl + 1, 0.2f, 0) - P(hl, 0.2f, 0);
        m.Tri(fr[0], fr[1], doorL, Uv(M.Canvas, 0, 1), Uv(M.Canvas, 0, 0.6f), Uv(M.Canvas, 0.4f, 1), outF);
        m.Tri(fr[1], top, doorL, Uv(M.Canvas, 0, 0.6f), Uv(M.Canvas, 0.5f, 0.05f), Uv(M.Canvas, 0.4f, 1), outF);
        m.Tri(fr[1], fr[2], top, Uv(M.Canvas, 0, 0.6f), Uv(M.Canvas, 0.5f, 0), Uv(M.Canvas, 0.5f, 0.05f), outF);
        m.Tri(fr[3], fr[4], doorR, Uv(M.Canvas, 1, 0.6f), Uv(M.Canvas, 1, 1), Uv(M.Canvas, 0.6f, 1), outF);
        m.Tri(fr[3], doorR, top, Uv(M.Canvas, 1, 0.6f), Uv(M.Canvas, 0.6f, 1), Uv(M.Canvas, 0.5f, 0.05f), outF);
        m.Tri(fr[2], fr[3], top, Uv(M.Canvas, 0.5f, 0), Uv(M.Canvas, 1, 0.6f), Uv(M.Canvas, 0.5f, 0.05f), outF);
        m.Tri(doorL, top, P(hl + 0.05f, 0, -0.17f), Uv(M.CanvasDark, 0, 1), Uv(M.CanvasDark, 0.5f, 0), Uv(M.CanvasDark, 1, 1), outF);
        m.Tri(doorR, top, P(hl + 0.05f, 0, 0.17f), Uv(M.CanvasDark, 0, 1), Uv(M.CanvasDark, 0.5f, 0), Uv(M.CanvasDark, 1, 1), outF);
        foreach (var x in new[] { -hl, hl }) m.Cylinder(P(x, 0, 0), P(x, ridge + 0.05f, 0), 0.008f, M.Walnut, 6, M.EndGrain);
        m.Cylinder(P(-hl - 0.02f, ridge + 0.004f, 0), P(hl + 0.02f, ridge + 0.004f, 0), 0.006f, M.Walnut, 6, M.EndGrain);
        foreach (var (x, z) in new[] { (-hl, -1f), (-hl, 1f), (hl, -1f), (hl, 1f), (0f, -1f), (0f, 1f) })
        {
            var from = P(x * 0.95f, wall + 0.01f, z * hw);
            var peg = P(x * 1.05f, 0, z * (hw + 0.2f));
            m.Cylinder(from, peg + Axis.Up * 0.02f, 0.0025f, M.Iron, 4);
            m.Box(peg + Axis.Up * 0.012f, new Vector3(0.012f, 0.03f, 0.012f), r, M.Walnut);
        }
        foreach (var x in new[] { -hl - 0.18f, hl + 0.18f })
        {
            var peg = P(x, 0, 0);
            m.Cylinder(P(x * 0.9f, ridge, 0), peg + Axis.Up * 0.02f, 0.0025f, M.Iron, 4);
            m.Box(peg + Axis.Up * 0.012f, new Vector3(0.012f, 0.03f, 0.012f), r, M.Walnut);
        }
    }

    static void Hut(PropMesh m, Vector3 at, float yaw, float len = 0.82f, float wid = 0.62f)
    {
        var r = Yaw(yaw);
        Vector3 P(float x, float y, float z) => at + r * new Vector3(x, y, z);
        float hl = len / 2, hw = wid / 2, ridge = 0.4f;
        var inside = at + new Vector3(0, 0.15f, 0);
        m.Quad(P(-hl * 0.95f, 0.006f, -hw * 0.8f), P(hl * 0.95f, 0.006f, -hw * 0.8f), P(hl * 0.95f, 0.006f, hw * 0.8f), P(-hl * 0.95f, 0.006f, hw * 0.8f), M.Straw, at - Axis.Up, 0, 0, 1, 0.8f);
        var noise = m.Noise((int)(yaw * 10) + 77, 4f);
        foreach (var s in new[] { -1f, 1f })
        {
            Vector3 Bump(float u, float v) => r * new Vector3(0, 0.012f * noise.GetNoise2D(u * 3, v * 2), s * 0.02f * (noise.GetNoise2D(u * 5 + 9, v * 3) * 0.5f + 0.5f));
            m.Sheet(P(-hl - 0.03f, ridge + 0.03f, 0), P(hl + 0.03f, ridge + 0.03f, 0), P(hl + 0.04f, -0.0f, s * (hw + 0.04f)), P(-hl - 0.04f, -0.0f, s * (hw + 0.04f)), M.Straw, inside, 0.02f, 8, 5,
                1.6f, Bump);
            m.Sheet(P(-hl, ridge, 0), P(hl, ridge, 0), P(hl, 0.02f, s * hw), P(-hl, 0.02f, s * hw), M.Straw, P(0, ridge + 0.5f, -s * 2f), 0.02f, 4, 3, 1.6f);
        }
        m.Tri(P(-hl, 0, -hw), P(-hl, 0, hw), P(-hl, ridge, 0), Uv(M.Bark, 0, 1), Uv(M.Bark, 1, 1), Uv(M.Bark, 0.5f, 0), Vector3.Zero - (r * Axis.Right));
        foreach (var x in new[] { -hl, hl })
        {
            m.Cylinder(P(x, 0, -0.012f), P(x, ridge + 0.06f, -0.03f), 0.011f, M.Bark, 6, M.EndGrain);
            m.Cylinder(P(x, 0, 0.012f), P(x, ridge + 0.06f, 0.03f), 0.011f, M.Bark, 6, M.EndGrain);
        }
        m.Cylinder(P(-hl - 0.06f, ridge + 0.035f, 0), P(hl + 0.06f, ridge + 0.035f, 0), 0.012f, M.Bark, 7, M.EndGrain);
    }

    static void Musket(PropMesh m, Vector3 butt, Vector3 muzzle, Vector3 flat, float spin)
    {
        var along = (muzzle - butt).Normalized();
        var across = flat.Cross(along).Normalized();
        var face = along.Cross(across).Normalized();
        var basis = new Basis(across, along, face);
        var wrist = butt.Lerp(muzzle, 0.38f);
        m.Box(butt.Lerp(wrist, 0.5f), new Vector3(0.012f, (wrist - butt).Norm(), 0.03f), basis, M.Walnut);
        m.Box(wrist.Lerp(muzzle, 0.45f), new Vector3(0.01f, (muzzle - wrist).Norm() * 0.9f, 0.014f), basis, M.Walnut);
        m.Cylinder(wrist - along * 0.01f + face * 0.012f, muzzle + face * 0.012f, 0.0055f, M.Iron, 6, M.Iron);
        var tip = muzzle + face * 0.012f;
        m.Box(tip + along * 0.045f + face * 0.008f, new Vector3(0.003f, 0.09f, 0.01f), basis, M.Steel);
        _ = spin;
    }

    static void PiledArms(PropMesh m, Vector3 at, float yaw)
    {
        var top = at + new Vector3(0, 0.36f, 0);
        for (int i = 0; i < 3; i++)
        {
            float t = Mathf.DegToRad(yaw + i * 120);
            var dir = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
            var butt = at + dir * 0.11f;
            Musket(m, butt, top - dir * 0.012f, dir, i);
        }
    }

    static void Knapsack(PropMesh m, Vector3 at, float yaw, int seed)
    {
        var r = Yaw(yaw);
        m.Lump(at + new Vector3(0, 0.035f, 0), new Vector3(0.1f, 0.075f, 0.055f), r, M.Walnut, seed, 0.05f, 0.55f);
        m.Cylinder(at + r * new Vector3(-0.055f, 0.085f, 0), at + r * new Vector3(0.055f, 0.085f, 0), 0.016f, M.Stone, 8, M.Stone);
    }

    static void Barrel(PropMesh m, Vector3 at)
    {
        const float h = 0.16f, rEnd = 0.05f, rMid = 0.058f;
        m.Cylinder(at, at + new Vector3(0, h / 2, 0), rEnd, M.Plank, 14, M.EndGrain, rMid, capB: false);
        m.Cylinder(at + new Vector3(0, h / 2, 0), at + new Vector3(0, h, 0), rMid, M.Plank, 14, M.EndGrain, rEnd, capA: false);
        foreach (var y in new[] { 0.02f, h - 0.02f, h * 0.35f, h * 0.65f })
        {
            float rr = Mathf.Lerp(rEnd, rMid, 1 - Mathf.Abs(y / h - 0.5f) * 2) + 0.002f;
            m.Cylinder(at + new Vector3(0, y - 0.005f, 0), at + new Vector3(0, y + 0.005f, 0), rr, M.Iron, 14, M.Iron, capA: false, capB: false);
        }
    }

    static void Woodpile(PropMesh m, Vector3 at, float yaw, Random rng)
    {
        var r = Yaw(yaw);
        for (int i = 0; i < 4; i++)
        {
            var c = at + r * new Vector3(0, 0.025f, (i - 1.5f) * 0.05f);
            m.Cylinder(c - r * new Vector3(0.14f, 0, 0), c + r * new Vector3(0.14f, 0, 0), R(rng, 0.021f, 0.026f), M.Bark, 8, M.EndGrain);
        }
        for (int i = 0; i < 3; i++)
        {
            var c = at + r * new Vector3(0, 0.07f, (i - 1f) * 0.05f);
            m.Cylinder(c - r * new Vector3(0.13f, 0, 0), c + r * new Vector3(0.13f, 0, 0), R(rng, 0.02f, 0.025f), M.Bark, 8, M.EndGrain);
        }
    }

    static void Log(PropMesh m, Vector3 at, float yaw, float len = 0.36f, float rad = 0.032f)
    {
        var d = Yaw(yaw) * new Vector3(len / 2, 0, 0);
        m.Cylinder(at + new Vector3(0, rad * 0.85f, 0) - d, at + new Vector3(0, rad * 0.85f, 0) + d, rad, M.Bark, 9, M.EndGrain);
    }

    public PropMade Bivouac()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1805);
        var fires = new List<Vector2>();
        m.GroundPatch(2.35f, 1.6f, 48);
        foreach (var (x, z) in new[] { (-1.2f, 0.3f), (0.08f, 0.5f), (1.3f, 0.22f) }) { FirePit(m, new Vector3(x, 0, z), rng); fires.Add(new Vector2(x, z)); }
        Tripod(m, new Vector3(0.08f, 0, 0.5f), 20);
        Tent(m, new Vector3(-1.35f, 0, -0.8f), 4, rng);
        Tent(m, new Vector3(-0.42f, 0, -0.92f), -3, rng);
        Hut(m, new Vector3(0.68f, 0, -0.8f), 2);
        Hut(m, new Vector3(1.6f, 0, -0.7f), -9, 0.74f);
        PiledArms(m, new Vector3(-0.55f, 0, 0.9f), 10);
        PiledArms(m, new Vector3(0.72f, 0, 1.0f), -5);
        PiledArms(m, new Vector3(1.78f, 0, 0.88f), 20);
        foreach (var (x, z, a) in new[] { (-1.2f, 0.7f, 5f), (0.08f, 0.94f, -10f), (1.3f, 0.64f, 8f), (-1.62f, 0.25f, 82f), (1.72f, 0.2f, 95f) }) Log(m, new Vector3(x, 0, z), a);
        for (int i = 0; i < 5; i++) Knapsack(m, new Vector3(-0.32f + i * 0.13f, 0, 1.18f + (i % 2) * 0.03f), R(rng, -15, 15), 300 + i);
        Barrel(m, new Vector3(0.14f, 0, -0.45f));
        Barrel(m, new Vector3(0.27f, 0, -0.5f));
        Woodpile(m, new Vector3(-1.85f, 0, 0.75f), 70, rng);
        return Done(m, fires);
    }

    public PropMade PicketPost()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1806);
        m.GroundPatch(0.82f, 0.72f, 30);
        FirePit(m, new Vector3(0.15f, 0, 0.22f), rng);
        Tripod(m, new Vector3(0.15f, 0, 0.22f), 40);
        Hut(m, new Vector3(-0.08f, 0, -0.38f), 6, 0.72f, 0.56f);
        PiledArms(m, new Vector3(-0.45f, 0, 0.28f), 15);
        Log(m, new Vector3(0.22f, 0, 0.6f), -12);
        Knapsack(m, new Vector3(-0.28f, 0, 0.55f), 20, 41);
        return Done(m, new List<Vector2> { new(0.15f, 0.22f) });
    }

    public PropMade CampFire()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1807);
        m.GroundPatch(0.5f, 0.46f, 24);
        FirePit(m, Vector3.Zero, rng);
        Log(m, new Vector3(0, 0, 0.33f), 4, 0.32f);
        Log(m, new Vector3(-0.32f, 0, -0.08f), 76, 0.3f);
        return Done(m, new List<Vector2> { Vector2.Zero });
    }

    public PropMade Hedgehog()
    {
        var m = new PropMesh(_noise);
        const float half = 0.38f, leg = 0.06f, th = 0.012f;
        var tilt = Basis.Arc(new Vector3(1, 1, 1).Normalized(), Axis.Up);
        var c = new Vector3(0, half / Mathf.Sqrt(3) - 0.025f, 0);
        var axes = new[] { Axis.Right, Axis.Up, Axis.Back };
        for (int i = 0; i < 3; i++)
        {
            var dir = tilt * axes[i];
            var a1 = tilt * axes[(i + 1) % 3];
            var a2 = tilt * axes[(i + 2) % 3];
            var basis = new Basis(dir, a1, a2);
            m.Box(c + basis * new Vector3(0, leg / 2, th / 2), new Vector3(half * 2, leg, th), basis, M.Steel);
            m.Box(c + basis * new Vector3(0, th / 2, leg / 2), new Vector3(half * 2, th, leg), basis, M.Steel);
        }
        m.Lump(c, new Vector3(0.09f, 0.09f, 0.09f), tilt, M.Steel, 9, 0.05f, 0.8f);
        var made = Done(m, new List<Vector2>());
        return made;
    }

    public PropMade Crater(float radius, int seed)
    {
        var m = new PropMesh(_noise);
        var noise = m.Noise(seed, 1.6f);
        var rng = new Random(seed);
        const int rings = 26, segs = 48;
        const float outer = 1.35f;
        float rim = radius * 0.22f;
        Vector3 At(int ri, int si)
        {
            float rr = ri / (float)rings * outer, a = si * Mathf.Tau / segs;
            float wob = 1 + 0.16f * noise.GetNoise2D(Mathf.Cos(a) * 2, Mathf.Sin(a) * 2) + 0.05f * noise.GetNoise2D(rr * 9, a * 3);
            float y = rr < 1 ? rim * Mathf.Pow(rr, 2.0f) : rim * Mathf.Pow(Mathf.Clamp(1 - (rr - 1) / (outer - 1), 0, 1), 1.6f);
            return new Vector3(Mathf.Cos(a) * rr * radius, Mathf.Max(0.004f, y * wob), Mathf.Sin(a) * rr * radius);
        }
        Vector2 T(Vector3 p) => CraterUv(0.5f + p.X / (radius * outer * 2), 0.5f + p.Z / (radius * outer * 2));
        var grid = new Vector3[rings + 1, segs + 1];
        for (int ri = 0; ri <= rings; ri++) for (int si = 0; si <= segs; si++) grid[ri, si] = At(ri, si % segs);
        var nrm = new Vector3[rings + 1, segs + 1];
        for (int ri = 0; ri < rings; ri++)
            for (int si = 0; si < segs; si++)
            {
                var n = (grid[ri, si + 1] - grid[ri, si]).Cross(grid[ri + 1, si] - grid[ri, si]);
                if (n.Y < 0) n = -n;
                nrm[ri, si] += n;
                nrm[ri + 1, si] += n;
                nrm[ri, si + 1] += n;
                nrm[ri + 1, si + 1] += n;
            }
        for (int ri = 0; ri <= rings; ri++) for (int si = 0; si <= segs; si++) nrm[ri, si] = (nrm[ri, si] + nrm[ri, (si + segs) % segs] * 0 + Axis.Up * 1e-4f).Normalized();
        for (int ri = 0; ri < rings; ri++)
            for (int si = 0; si < segs; si++)
            {
                var a = grid[ri, si];
                var b = grid[ri + 1, si];
                var c = grid[ri + 1, si + 1];
                var d = grid[ri, si + 1];
                m.TriN(a, b, c, nrm[ri, si], nrm[ri + 1, si], nrm[ri + 1, si + 1], T(a), T(b), T(c));
                m.TriN(a, c, d, nrm[ri, si], nrm[ri + 1, si + 1], nrm[ri, si + 1], T(a), T(c), T(d));
            }
        int clods = (int)(10 + radius * 22);
        for (int i = 0; i < clods; i++)
        {
            float a = R(rng, 0, Mathf.Tau), rr = R(rng, 0.9f, 1.3f) * radius;
            var p = new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr);
            float h = rr / radius < 1 ? rim * Mathf.Pow(rr / radius, 2) : rim * Mathf.Pow(Mathf.Clamp(1 - (rr / radius - 1) / (outer - 1), 0, 1), 1.6f);
            float sz = R(rng, 0.03f, 0.06f) * Mathf.Clamp(radius / 0.4f, 0.8f, 1.6f);
            m.Lump(p + new Vector3(0, h + sz * 0.15f, 0), new Vector3(sz, sz * 0.6f, sz * 0.8f), Yaw(R(rng, 0, 180)), rng.Next(4) == 0 ? M.Stone : M.EarthDark, rng.Next(), 0.3f);
        }
        return Done(m, new List<Vector2>());
    }

    public PropMade Trench(Vector2[] path)
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1916);
        var lo = path.Aggregate(new Vector2(float.MaxValue, float.MaxValue), (a, q) => new Vector2(Mathf.Min(a.X, q.X), Mathf.Min(a.Y, q.Y)));
        var hi = path.Aggregate(new Vector2(float.MinValue, float.MinValue), (a, q) => new Vector2(Mathf.Max(a.X, q.X), Mathf.Max(a.Y, q.Y)));
        var centre = (lo + hi) / 2;
        const float margin = 0.9f;
        var line = new TrenchLine(path.Select(q => q - centre).ToArray(), _noise);
        line.Earth(m, (hi.X - lo.X) / 2 + margin, (hi.Y - lo.Y) / 2 + margin);
        line.Timber(m, rng);
        return Done(m, new List<Vector2>());
    }

    sealed class TrenchLine
    {
        const float cut = 0.16f, g = 0.035f;
        readonly Vector2[] path;
        readonly IPropNoise noise;

        public TrenchLine(Vector2[] path, PerlinNoise noise)
        {
            this.path = path;
            this.noise = noise(14, 2.5f);
        }

        (float d, bool front) Near(Vector2 q)
        {
            float best = float.MaxValue;
            Vector2 at = q;
            for (int k = 0; k + 1 < path.Length; k++)
            {
                var a = path[k];
                var b = path[k + 1];
                var ab = b - a;
                float t = Mathf.Clamp((q - a).Dot(ab) / ab.NormSquared(), 0, 1);
                var c = a + ab * t;
                float dd = (q - c).Norm();
                if (dd < best) { best = dd; at = c; }
            }
            return (best, q.Y - at.Y > 0.02f);
        }

        float Height(Vector2 q)
        {
            var (d, front) = Near(q);
            float n = noise.GetNoise2D(q.X, q.Y);
            if (d < cut) return front && d > cut - 0.065f ? 0.06f : 0;
            float wallH = front ? 0.19f : 0.14f, crest = (front ? 0.22f : 0.155f) + 0.025f * n, reach = front ? 0.5f : 0.36f;
            float e = d - cut;
            if (e < 0.03f) return Mathf.Lerp(front ? 0.06f : 0, wallH, e / 0.03f);
            if (e < 0.1f) return Mathf.Lerp(wallH, crest, Sm(0.03f, 0.1f, e));
            if (e < reach) return crest * (1 - Sm(0.1f, reach, e)) * (1 + 0.15f * noise.GetNoise2D(q.X * 3, q.Y * 3));
            return 0.004f;
        }

        public void Earth(PropMesh m, float hx, float hz)
        {
            int nx = (int)Mathf.Ceil(hx * 2 / g), nz = (int)Mathf.Ceil(hz * 2 / g);
            var pts = new Vector3[nx + 1, nz + 1];
            for (int x = 0; x <= nx; x++)
                for (int z = 0; z <= nz; z++)
                {
                    var q = new Vector2(-hx + x * g, -hz + z * g);
                    pts[x, z] = new Vector3(q.X, Mathf.Max(0.004f, Height(q)), q.Y);
                }
            Vector3 Normal(int x, int z)
            {
                var l = pts[Math.Max(0, x - 1), z];
                var r = pts[Math.Min(nx, x + 1), z];
                var dn = pts[x, Math.Max(0, z - 1)];
                var up = pts[x, Math.Min(nz, z + 1)];
                var n = (up - dn).Cross(r - l);
                return (n.Y < 0 ? -n : n).Normalized();
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int x = 0; x < nx; x++)
                    for (int z = 0; z < nz; z++)
                    {
                        var c = (pts[x, z] + pts[x + 1, z + 1]) / 2;
                        var (d, front) = Near(new Vector2(c.X, c.Z));
                        float top = Mathf.Max(Mathf.Max(pts[x, z].Y, pts[x + 1, z].Y), Mathf.Max(pts[x, z + 1].Y, pts[x + 1, z + 1].Y));
                        bool ground = d > cut + 0.06f && top < 0.012f;
                        if ((pass == 0) != ground) continue;
                        Vector2 T(int xi, int zi)
                        {
                            if (ground) return GroundUv(xi / (float)nx, zi / (float)nz);
                            var mat = d < cut - 0.005f ? (front && c.Y > 0.03f ? M.Plank : M.Mud) : d < cut + 0.035f ? M.Plank : front ? M.Earth : M.EarthDark;
                            return Uv(mat, (xi % 6 + (xi > x ? 0 : 0)) / 6f + (xi > x ? 0 : 0), (zi % 6) / 6f);
                        }
                        Vector2 Tq(int xi, int zi)
                        {
                            if (ground) return T(xi, zi);
                            var mat = d < cut - 0.005f ? (front && c.Y > 0.03f ? M.Plank : M.Mud) : d < cut + 0.035f ? M.Plank : front ? M.Earth : M.EarthDark;
                            float u = (x % 6 + (xi - x)) / 6f, v = (z % 6 + (zi - z)) / 6f;
                            return Uv(mat, u, v);
                        }
                        m.TriN(pts[x, z], pts[x + 1, z], pts[x + 1, z + 1], Normal(x, z), Normal(x + 1, z), Normal(x + 1, z + 1), Tq(x, z), Tq(x + 1, z), Tq(x + 1, z + 1));
                        m.TriN(pts[x, z], pts[x + 1, z + 1], pts[x, z + 1], Normal(x, z), Normal(x + 1, z + 1), Normal(x, z + 1), Tq(x, z), Tq(x + 1, z + 1), Tq(x, z + 1));
                    }
                if (pass == 0) m.Ground = m.V.Count;
            }
        }

        public void Timber(PropMesh m, Random rng)
        {
            for (int k = 0; k + 1 < path.Length; k++)
            {
                var a = new Vector3(path[k].X, 0, path[k].Y);
                var b = new Vector3(path[k + 1].X, 0, path[k + 1].Y);
                var along = (b - a).Normalized();
                var side = new Vector3(-along.Z, 0, along.X);
                if (side.Z < 0) side = -side;
                float len = (b - a).Norm();
                var basis = new Basis(along, Axis.Up, along.Cross(Axis.Up).Normalized());
                float s0 = k == 0 ? 0.05f : cut, s1 = len - (k == path.Length - 2 ? 0.05f : cut);
                if (s1 > s0)
                {
                    var mid = a + along * (s0 + s1) / 2;
                    foreach (var o in new[] { -0.045f, 0.045f }) m.Box(mid + side * o + Axis.Up * 0.012f, new Vector3(s1 - s0, 0.01f, 0.012f), basis, M.Plank);
                    for (float t = s0 + 0.02f; t < s1; t += 0.052f) m.Box(a + along * t + Axis.Up * 0.02f, new Vector3(0.028f, 0.008f, 0.13f), basis, M.Plank);
                }
                bool facesFront = Mathf.Abs(along.X) > 0.7f;
                for (float t = 0.12f; t < len - 0.05f; t += 0.38f)
                    foreach (var sg in new[] { -1f, 1f })
                    {
                        var foot = a + along * t + side * sg * (cut + 0.012f) + Axis.Up * (sg > 0 && facesFront ? 0.06f : 0);
                        m.Cylinder(foot, foot + Axis.Up * (sg > 0 && facesFront ? 0.16f : 0.15f), 0.011f, M.Bark, 6, M.EndGrain);
                    }
                if (!facesFront) continue;
                for (int course = 0; course < 2; course++)
                    for (float t = (k == 0 ? 0.05f : 0.2f) + course * 0.06f; t < len - (k == path.Length - 2 ? 0.05f : 0.2f); t += 0.12f)
                    {
                        var q = a + along * t + side * (cut + 0.075f + course * 0.015f);
                        float y = Height(new Vector2(q.X, q.Z));
                        m.Lump(q + Axis.Up * (y + 0.018f + course * 0.038f), new Vector3(0.11f, 0.045f, 0.07f), basis * new Basis(Axis.Up, R(rng, -0.15f, 0.15f)), M.Sandbag, rng.Next(), 0.07f, 0.5f);
                    }
            }
        }
    }

    static void Terrain(PropMesh m, float hx, float hz, float g, Func<Vector2, (float h, M mat)> at, Vector3 offset = default)
    {
        int nx = (int)Mathf.Ceil(hx * 2 / g), nz = (int)Mathf.Ceil(hz * 2 / g);
        var pts = new Vector3[nx + 1, nz + 1];
        var mats = new M[nx + 1, nz + 1];
        for (int x = 0; x <= nx; x++)
            for (int z = 0; z <= nz; z++)
            {
                var q = new Vector2(-hx + x * hx * 2 / nx, -hz + z * hz * 2 / nz);
                var (h, mat) = at(q);
                pts[x, z] = new Vector3(q.X, Mathf.Max(0, h), q.Y) + offset;
                mats[x, z] = mat;
            }
        Vector3 Nrm(int x, int z)
        {
            var l = pts[Math.Max(0, x - 1), z];
            var r = pts[Math.Min(nx, x + 1), z];
            var dn = pts[x, Math.Max(0, z - 1)];
            var up = pts[x, Math.Min(nz, z + 1)];
            var n = (up - dn).Cross(r - l);
            return (n.Y < 0 ? -n : n).Normalized();
        }
        for (int x = 0; x < nx; x++)
            for (int z = 0; z < nz; z++)
            {
                var mat = mats[x, z];
                Vector2 T(int xi, int zi) => Uv(mat, (x % 5 + (xi - x)) / 5f, (z % 5 + (zi - z)) / 5f);
                bool Up(params (int, int)[] c) => c.Any(k => pts[k.Item1, k.Item2].Y > 0.0004f);
                if ((x + z) % 2 == 0)
                {
                    if (Up((x, z), (x + 1, z), (x + 1, z + 1))) m.TriN(pts[x, z], pts[x + 1, z], pts[x + 1, z + 1], Nrm(x, z), Nrm(x + 1, z), Nrm(x + 1, z + 1), T(x, z), T(x + 1, z), T(x + 1, z + 1));
                    if (Up((x, z), (x + 1, z + 1), (x, z + 1))) m.TriN(pts[x, z], pts[x + 1, z + 1], pts[x, z + 1], Nrm(x, z), Nrm(x + 1, z + 1), Nrm(x, z + 1), T(x, z), T(x + 1, z + 1), T(x, z + 1));
                }
                else
                {
                    if (Up((x, z), (x + 1, z), (x, z + 1))) m.TriN(pts[x, z], pts[x + 1, z], pts[x, z + 1], Nrm(x, z), Nrm(x + 1, z), Nrm(x, z + 1), T(x, z), T(x + 1, z), T(x, z + 1));
                    if (Up((x + 1, z), (x + 1, z + 1), (x, z + 1))) m.TriN(pts[x + 1, z], pts[x + 1, z + 1], pts[x, z + 1], Nrm(x + 1, z), Nrm(x + 1, z + 1), Nrm(x, z + 1), T(x + 1, z),
                        T(x + 1, z + 1), T(x, z + 1));
                }
            }
    }

    static float EndTaper(float x, float hl, float run) => Sm(0, run, hl - Mathf.Abs(x));

    static void Stake(PropMesh m, Vector3 foot, Vector3 tip, float r)
    {
        var shoulder = foot.Lerp(tip, 0.8f);
        m.Cylinder(foot, shoulder, r, M.Bark, 7, M.EndGrain, capB: false);
        m.Cylinder(shoulder, tip, r, M.EndGrain, 7, M.EndGrain, 0.002f, capA: false, capB: false);
    }

    static void Gabion(PropMesh m, Vector3 at, float r, float h, int seed)
    {
        m.Cylinder(at, at + Axis.Up * h, r, M.Wicker, 14, M.Earth, r * 0.97f, capA: false, capB: false);
        m.Lump(at + Axis.Up * (h - 0.004f), new Vector3(r * 2, r * 0.7f, r * 2), Basis.Identity, M.Earth, seed, 0.12f);
    }

    public PropMade Breastwork()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1809);
        const float hl = 1.5f;
        Terrain(m, hl + 0.2f, 0.6f, 0.03f, q =>
        {
            float t = EndTaper(q.X, hl, 0.3f);
            float emb = 1 - 0.55f * Sm(0.22f, 0.1f, Mathf.Abs(q.X));
            float z = q.Y, h;
            if (z < -0.5f) h = 0;
            else if (z < -0.12f) h = 0.2f * Sm(-0.5f, -0.12f, z);
            else if (z < 0.08f) h = 0.2f;
            else if (z < 0.5f) h = 0.2f * (1 - Sm(0.08f, 0.5f, z));
            else h = 0;
            h *= t * (z < 0.1f ? emb : 1);
            h *= 1 + 0.06f * Mathf.Sin(q.X * 7 + z * 3);
            return (h, z < -0.1f ? M.EarthDark : z > 0.12f ? M.Turf : M.Earth);
        });
        for (float x = -hl + 0.12f; x < hl - 0.08f; x += 0.125f)
        {
            if (Mathf.Abs(x) < 0.2f) continue;
            float base0 = 0.2f * EndTaper(x, hl, 0.3f);
            Gabion(m, new Vector3(x, base0 - 0.01f, 0.03f), 0.058f, 0.15f, rng.Next());
        }
        foreach (var sx in new[] { -1f, 1f })
            for (int k = 0; k < 2; k++)
            {
                float y = 0.2f + 0.15f + 0.03f + k * 0.055f;
                m.Cylinder(new Vector3(sx * 0.24f, y, 0.03f + (k - 0.5f) * 0.02f), new Vector3(sx * (hl - 0.12f), y - 0.02f, 0.03f + (k - 0.5f) * 0.02f), 0.028f, M.Brush, 9, M.Brush);
            }
        return Done(m, new List<Vector2>());
    }

    public PropMade Earthwork()
    {
        var m = new PropMesh(_noise);
        const float hl = 2f;
        Terrain(m, hl + 0.15f, 1.05f, 0.035f, q =>
        {
            float z = q.Y, t = EndTaper(q.X, hl, 0.45f), h;
            M mat;
            const float GlacisOuterEdge = 1.0f, GlacisCrest = 0.72f, DitchOuterEdge = 0.62f, DitchInnerEdge = 0.4f, ParapetOuterFoot = 0.18f,
                ParapetCrest = -0.02f, FireStepEdge = -0.1f, FireStepBack = -0.3f, BackSlopeFoot = -0.95f;
            const float GlacisHeight = 0.07f, ParapetHeight = 0.36f, ParapetInnerHeight = 0.34f, FireStepHeight = 0.22f, DitchFloorHeight = 0.004f;
            if (z > GlacisOuterEdge) { h = 0; mat = M.Turf; }
            else if (z > GlacisCrest) { h = GlacisHeight * Sm(GlacisOuterEdge, GlacisCrest, z); mat = M.Turf; }
            else if (z > DitchOuterEdge) { h = GlacisHeight * Sm(DitchOuterEdge, GlacisCrest, z); mat = M.Earth; }
            else if (z > DitchInnerEdge) { h = DitchFloorHeight; mat = M.Mud; }
            else if (z > ParapetOuterFoot) { h = ParapetHeight * Sm(DitchInnerEdge, ParapetOuterFoot, z); mat = M.Earth; }
            else if (z > ParapetCrest) { h = ParapetHeight - 0.02f * Sm(ParapetOuterFoot, ParapetCrest, z); mat = M.Turf; }
            else if (z > FireStepEdge) { h = Mathf.Lerp(ParapetInnerHeight, FireStepHeight, Sm(ParapetCrest, FireStepEdge, z)); mat = M.EarthDark; }
            else if (z > FireStepBack) { h = FireStepHeight; mat = M.Earth; }
            else if (z > BackSlopeFoot) { h = FireStepHeight * (1 - Sm(FireStepBack, BackSlopeFoot, z)); mat = M.Turf; }
            else { h = 0; mat = M.Turf; }
            h *= t * (1 + 0.05f * Mathf.Sin(q.X * 5.3f + z * 2));
            return (h, mat);
        });
        return Done(m, new List<Vector2>());
    }

    public PropMade ChevauxDeFrise()
    {
        var m = new PropMesh(_noise);
        const float hl = 0.85f, len = 0.56f;
        float y = len / 2 * Mathf.Sin(Mathf.DegToRad(45)) + 0.004f;
        m.Box(new Vector3(0, y, 0), new Vector3(hl * 2, 0.07f, 0.07f), Basis.Identity, M.Bark, M.Bark);
        foreach (var x in new[] { -hl, hl })
            m.Quad(new Vector3(x, y - 0.035f, -0.035f), new Vector3(x, y - 0.035f, 0.035f), new Vector3(x, y + 0.035f, 0.035f), new Vector3(x, y + 0.035f, -0.035f), M.EndGrain, new Vector3(0, y, 0));
        int i = 0;
        for (float x = -hl + 0.08f; x <= hl - 0.07f; x += 0.11f, i++)
        {
            float a = Mathf.DegToRad(i % 2 == 0 ? 45 : -45);
            var dir = new Vector3(0, Mathf.Sin(Mathf.Abs(a)), Mathf.Cos(a) * Mathf.Sign(a) * (i % 2 == 0 ? 1 : 1));
            dir = new Vector3(0, Mathf.Cos(Mathf.DegToRad(45)), (i % 2 == 0 ? 1 : -1) * Mathf.Sin(Mathf.DegToRad(45)));
            var c = new Vector3(x, y, 0);
            Stake(m, c, c + dir * len / 2, 0.016f);
            Stake(m, c, c - dir * len / 2, 0.016f);
        }
        return Done(m, new List<Vector2>());
    }

    public PropMade Stakes()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1415);
        for (int row = 0; row < 3; row++)
            for (float x = -1.1f + (row % 2) * 0.09f; x <= 1.1f; x += 0.18f)
            {
                var foot = new Vector3(x + R(rng, -0.03f, 0.03f), 0, -0.22f + row * 0.22f + R(rng, -0.03f, 0.03f));
                float lean = R(rng, 30, 42), turn = R(rng, -10, 10), len = R(rng, 0.3f, 0.36f);
                var dir = Yaw(turn) * new Vector3(0, Mathf.Cos(Mathf.DegToRad(lean)), Mathf.Sin(Mathf.DegToRad(lean)));
                m.Lump(foot + new Vector3(0, 0.006f, 0), new Vector3(0.1f, 0.035f, 0.1f), Yaw(R(rng, 0, 180)), M.Earth, rng.Next(), 0.3f);
                Stake(m, foot - dir * 0.02f, foot + dir * len, R(rng, 0.018f, 0.023f));
            }
        return Done(m, new List<Vector2>());
    }

    public PropMade Palisade()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(-50);
        const float hl = 1.2f;
        Terrain(m, hl + 0.1f, 0.22f, 0.025f, q =>
        {
            float h = 0.06f * (1 - Sm(0.0f, 0.2f, Mathf.Abs(q.Y))) * EndTaper(q.X, hl, 0.1f);
            return (h * (1 + 0.2f * Mathf.Sin(q.X * 13)), M.EarthDark);
        });
        for (float x = -hl + 0.04f; x <= hl - 0.03f; x += 0.072f)
        {
            float r = R(rng, 0.03f, 0.037f), hgt = R(rng, 0.44f, 0.52f);
            var foot = new Vector3(x, 0, R(rng, -0.006f, 0.006f));
            var tip = foot + new Vector3(R(rng, -0.01f, 0.01f), hgt, R(rng, -0.01f, 0.01f));
            var shoulder = foot.Lerp(tip, 0.86f);
            m.Cylinder(foot, shoulder, r, M.Bark, 8, M.EndGrain, capA: false, capB: false);
            m.Cylinder(shoulder, tip, r, M.EndGrain, 8, M.EndGrain, 0.003f, capA: false, capB: false);
        }
        foreach (var y in new[] { 0.14f, 0.36f })
            m.Cylinder(new Vector3(-hl, y, -0.05f), new Vector3(hl, y, -0.05f), 0.02f, M.Bark, 8, M.EndGrain);
        return Done(m, new List<Vector2>());
    }

    public PropMade Moat()
    {
        var m = new PropMesh(_noise);
        const float hl = 1.6f, half = 0.32f;
        float D(Vector2 q) { float x = Mathf.Max(0, Mathf.Abs(q.X) - hl); return Mathf.Sqrt(x * x + q.Y * q.Y); }
        Terrain(m, hl + 0.8f, 0.85f, 0.03f, q =>
        {
            float d = D(q), h;
            M mat;
            if (d < half) { h = 0.008f; mat = M.Mud; }
            else if (d < half + 0.08f) { h = 0.008f + 0.1f * Sm(half, half + 0.08f, d); mat = M.EarthDark; }
            else if (d < half + 0.2f) { h = 0.108f; mat = M.Turf; }
            else if (d < half + 0.52f) { h = 0.108f * (1 - Sm(half + 0.2f, half + 0.52f, d)); mat = M.Turf; }
            else { h = 0; mat = M.Turf; }
            return (h * (1 + 0.08f * Mathf.Sin(q.X * 6 + q.Y * 4)), mat);
        });
        const int seg = 40;
        var ring = new List<Vector3>();
        for (int i = 0; i <= seg; i++) { float a = -Mathf.Pi / 2 + Mathf.Pi * i / seg; ring.Add(new Vector3(hl + Mathf.Cos(a) * (half + 0.03f), 0.06f, Mathf.Sin(a) * (half + 0.03f))); }
        for (int i = 0; i <= seg; i++) { float a = Mathf.Pi / 2 + Mathf.Pi * i / seg; ring.Add(new Vector3(-hl + Mathf.Cos(a) * (half + 0.03f), 0.06f, Mathf.Sin(a) * (half + 0.03f))); }
        var centre = new Vector3(0, 0.06f, 0);
        float W = hl + half + 0.03f;
        Vector2 WT(Vector3 p) => Uv(M.Water, 0.5f + p.X / (W * 2), 0.5f + p.Z / (W * 2) * 0.4f);
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            m.Tri(centre, a, b, WT(centre), WT(a), WT(b), Axis.Up);
        }
        return Done(m, new List<Vector2>());
    }

    static void Wheel(PropMesh m, Vector3 at, Vector3 axis, float r)
    {
        axis = axis.Normalized();
        m.Cylinder(at - axis * 0.012f, at + axis * 0.012f, r, M.Painted, 18, M.Walnut, capA: false, capB: false);
        m.Cylinder(at - axis * 0.012f, at + axis * 0.012f, r * 0.9f, M.Painted, 18, M.Walnut, capA: false, capB: false);
        m.Cylinder(at - axis * 0.03f, at + axis * 0.03f, r * 0.17f, M.Walnut, 10, M.Iron);
        var side = Mathf.Abs(axis.Y) < 0.9f ? axis.Cross(Axis.Up).Normalized() : axis.Cross(Axis.Right).Normalized();
        var up = side.Cross(axis);
        for (int i = 0; i < 10; i++)
        {
            float a = i * Mathf.Tau / 10;
            var d = side * Mathf.Cos(a) + up * Mathf.Sin(a);
            m.Cylinder(at + d * r * 0.15f, at + d * r * 0.9f, 0.007f, M.Walnut, 5);
        }
        for (int i = 0; i < 18; i++)
        {
            float a0 = i * Mathf.Tau / 18, a1 = (i + 1) * Mathf.Tau / 18;
            var d0 = side * Mathf.Cos(a0) + up * Mathf.Sin(a0);
            var d1 = side * Mathf.Cos(a1) + up * Mathf.Sin(a1);
            foreach (var s in new[] { -1f, 1f })
            {
                var o = at + axis * 0.012f * s;
                m.Quad(o + d0 * r * 0.9f, o + d1 * r * 0.9f, o + d1 * r, o + d0 * r, M.Painted, at - axis * s);
            }
        }
    }

    public PropMade Pavises()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1346);
        for (float x = -0.98f; x <= 0.99f; x += 0.28f)
        {
            float turn = R(rng, -6, 6);
            var rot = Yaw(turn) * new Basis(Axis.Right, Mathf.DegToRad(R(rng, 8, 14)));
            var foot = new Vector3(x + R(rng, -0.02f, 0.02f), 0, R(rng, -0.02f, 0.02f));
            var centre = foot + rot * new Vector3(0, 0.17f, 0);
            m.Box(centre, new Vector3(0.22f, 0.34f, 0.025f), rot, M.Painted, M.Plank);
            m.Box(centre + rot * new Vector3(0, 0, -0.016f), new Vector3(0.05f, 0.3f, 0.01f), rot, M.Plank);
            m.Cylinder(foot + rot * new Vector3(0, 0.26f, 0.014f), foot + Yaw(turn) * new Vector3(0, 0, 0.16f), 0.009f, M.Bark, 6, M.EndGrain);
        }
        return Done(m, new List<Vector2>());
    }

    public PropMade TateShields()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1575);
        for (float x = -0.94f; x <= 0.95f; x += 0.215f)
        {
            var rot = Yaw(R(rng, -2, 2)) * new Basis(Axis.Right, Mathf.DegToRad(R(rng, 3, 6)));
            var foot = new Vector3(x, 0, R(rng, -0.01f, 0.01f));
            m.Box(foot + rot * new Vector3(0, 0.19f, 0), new Vector3(0.205f, 0.38f, 0.03f), rot, M.Plank, M.EndGrain);
            m.Box(foot + rot * new Vector3(0, 0.27f, 0.021f), new Vector3(0.2f, 0.025f, 0.012f), rot, M.Walnut);
            m.Cylinder(foot + rot * new Vector3(0, 0.27f, 0.025f), foot + new Vector3(0, 0, 0.2f), 0.01f, M.Walnut, 6, M.EndGrain);
        }
        return Done(m, new List<Vector2>());
    }

    public PropMade CampCurtain()
    {
        var m = new PropMesh(_noise);
        const float hx = 0.8f, hz = 0.55f, low = 0.06f, high = 0.34f, pole = 0.4f, bay = 0.4f;
        var centre = new Vector3(0, 0.2f, 0);
        var sides = new[] { (new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz)), (new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz)),
            (new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz)), (new Vector3(-hx, 0, hz), new Vector3(-hx, 0, -hz)) };
        for (int side = 0; side < sides.Length; side++)
        {
            var (from, to) = sides[side];
            int bays = Math.Max(1, (int)Math.Ceiling(Vector3.Distance(from, to) / bay));
            for (int i = 0; i < bays; i++)
            {
                var a = from.Lerp(to, i / (float)bays);
                var b = from.Lerp(to, (i + 1) / (float)bays);
                m.Cylinder(a, a + Axis.Up * pole, 0.011f, M.Walnut, 6, M.EndGrain);
                if (side == 2 && i == bays / 2) continue;
                var mid = (a + b) / 2 + Axis.Up * 0.2f;
                var outside = mid + (mid - centre) * 2;
                foreach (var inside in new[] { centre, outside })
                    m.Sheet(a + Axis.Up * high, b + Axis.Up * high, b + Axis.Up * low, a + Axis.Up * low, M.Canvas, inside, 0.018f, 6, 3);
                m.Cylinder(a + Axis.Up * (high + 0.008f), b + Axis.Up * (high + 0.008f), 0.004f, M.Bark, 5, M.Bark);
            }
        }
        return Done(m, new List<Vector2>());
    }

    public PropMade Barricade()
    {
        var m = new PropMesh(_noise);
        var rng = new Random(1830);
        var cartAt = new Vector3(-0.35f, 0, 0);
        void CartPanel(Vector3 offset, Vector3 size) => m.Box(cartAt + offset, size, Basis.Identity, M.Painted);
        void CartShaft(Vector3 from, Vector3 to) => m.Cylinder(cartAt + from, cartAt + to, 0.018f, M.Walnut, 6, M.EndGrain);
        Vector3 floor = new(0, 0.17f, 0.02f), sideUp = new(0, 0.335f, -0.11f), sideDown = new(0, 0.012f, -0.11f);
        CartPanel(floor, new Vector3(0.66f, 0.34f, 0.025f));
        CartPanel(sideUp, new Vector3(0.66f, 0.025f, 0.24f));
        CartPanel(sideDown, new Vector3(0.66f, 0.025f, 0.24f));
        foreach (var x in new[] { -0.33f, 0.33f }) CartPanel(new Vector3(x, 0.17f, -0.11f), new Vector3(0.025f, 0.34f, 0.24f));
        Wheel(m, cartAt + new Vector3(0.05f, 0.4f, 0.0f), Axis.Up, 0.19f);
        for (int i = 0; i < 3; i++)
            m.Lump(cartAt + new Vector3(-0.2f + i * 0.2f, 0.07f + (i % 2) * 0.1f, -0.1f), new Vector3(0.16f, 0.1f, 0.14f), Yaw(R(rng, -20, 20)), M.Sandbag, rng.Next(), 0.1f, 0.4f);
        CartShaft(new Vector3(-0.25f, 0.17f, 0.05f), new Vector3(-0.55f, 0.05f, 0.45f));
        CartShaft(new Vector3(-0.25f, 0.28f, 0.05f), new Vector3(-0.6f, 0.2f, 0.45f));
        Barrel(m, new Vector3(0.15f, 0, -0.04f));
        Barrel(m, new Vector3(0.28f, 0, 0.02f));
        {
            var a = new Vector3(0.46f, 0.055f, 0.02f);
            var b = a + new Vector3(0.16f, 0, 0.05f);
            m.Cylinder(a, a.Lerp(b, 0.5f), 0.05f, M.Plank, 14, M.EndGrain, 0.058f, capB: false);
            m.Cylinder(a.Lerp(b, 0.5f), b, 0.058f, M.Plank, 14, M.EndGrain, 0.05f, capA: false);
        }
        var tableAt = new Vector3(0.82f, 0, -0.02f);
        m.Box(tableAt + new Vector3(0, 0.016f, 0), new Vector3(0.36f, 0.03f, 0.24f), Yaw(8), M.Plank);
        foreach (var (lx, lz) in new[] { (-0.15f, -0.09f), (0.15f, -0.09f), (-0.15f, 0.09f), (0.15f, 0.09f) })
        {
            var f = tableAt + Yaw(8) * new Vector3(lx, 0.03f, lz);
            m.Box(f + Axis.Up * 0.1f, new Vector3(0.025f, 0.2f, 0.025f), Yaw(8), M.Walnut);
        }
        foreach (var (x, len, lean, yaw) in new[] { (-0.7f, 0.62f, 55f, 10f), (0.0f, 0.55f, 60f, -6f), (0.62f, 0.5f, 58f, 4f) })
        {
            var foot = new Vector3(x, 0.02f, 0.3f);
            var dir = Yaw(yaw) * new Vector3(0, Mathf.Sin(Mathf.DegToRad(lean)), -Mathf.Cos(Mathf.DegToRad(lean)));
            m.Box(foot + dir * len / 2, new Vector3(0.06f, len, 0.05f), new Basis(Yaw(yaw).X, dir, Yaw(yaw).X.Cross(dir)), M.Bark);
        }
        m.Box(new Vector3(0.3f, 0.16f, 0.2f), new Vector3(0.3f, 0.32f, 0.02f), new Basis(Axis.Right, Mathf.DegToRad(-28)), M.Painted);
        for (int i = 0; i < 5; i++)
            m.Lump(new Vector3(-0.15f + i * 0.13f, 0.05f, 0.14f + (i % 2) * 0.03f), new Vector3(0.12f, 0.08f, 0.09f), Yaw(R(rng, -20, 20)), M.Sandbag, rng.Next(), 0.1f, 0.4f);
        Terrain(m, 1.0f, 0.24f, 0.03f, q =>
        {
            float h = 0.08f * (1 - Sm(0, 0.22f, Mathf.Abs(q.Y))) * EndTaper(q.X, 0.9f, 0.3f) * (1 + 0.2f * Mathf.Sin(q.X * 9 + q.Y * 5));
            return (h, M.Earth);
        }, new Vector3(0, 0, 0.3f));
        for (int i = 0; i < 220; i++)
        {
            float x = R(rng, -0.95f, 0.95f), z = R(rng, -0.22f, 0.22f);
            float hz = 0.08f * (1 - Sm(0, 0.22f, Mathf.Abs(z))) * EndTaper(x, 0.9f, 0.3f);
            if (hz < 0.004f && rng.Next(3) != 0) continue;
            m.Lump(new Vector3(x, hz + 0.01f, z + 0.3f), new Vector3(0.045f, 0.03f, 0.04f), Yaw(R(rng, 0, 180)) * new Basis(Axis.Right, R(rng, -0.3f, 0.3f)), M.Cobble, rng.Next(), 0.06f, 0.65f);
        }
        return Done(m, new List<Vector2>());
    }

    public PropMade Tree()
    {
        var m = new PropMesh(_noise);
        m.Cylinder(Vector3.Zero, new Vector3(0, 1.5f, 0), 0.18f, M.Bark, 9, M.EndGrain, r2: 0.11f);
        foreach (var (x, z, d) in new[] { (0.7f, 0.25f, 35f), (-0.65f, -0.3f, 160f), (0.1f, -0.7f, 260f) })
            m.Cylinder(new Vector3(0, 1.0f, 0), new Vector3(x, 1.45f, z), 0.06f, M.Bark, 6, M.EndGrain, r2: 0.035f);
        var crown = new[]
        {
            (new Vector3(0, 1.65f, 0), new Vector3(2.8f, 1.5f, 2.8f)), (new Vector3(0.85f, 1.35f, 0.4f), new Vector3(1.7f, 1.1f, 1.7f)),
            (new Vector3(-0.8f, 1.4f, -0.35f), new Vector3(1.8f, 1.1f, 1.6f)), (new Vector3(0.2f, 2.0f, -0.6f), new Vector3(1.6f, 1.0f, 1.5f)),
            (new Vector3(-0.3f, 2.05f, 0.6f), new Vector3(1.5f, 0.9f, 1.4f)),
        };
        for (int i = 0; i < crown.Length; i++) m.Lump(crown[i].Item1, crown[i].Item2, Yaw(i * 47), M.Turf, 31 + i, 0.24f);
        return Done(m, new List<Vector2>());
    }

    public PropMade SmallBox()
    {
        var m = new PropMesh(_noise);
        m.Box(new Vector3(0, 0.19f, 0), new Vector3(0.96f, 0.38f, 0.58f), Basis.Identity, M.Painted);
        m.Box(new Vector3(0, 0.43f, 0), new Vector3(1.0f, 0.1f, 0.62f), Basis.Identity, M.Painted);
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            m.Box(new Vector3(sx * 0.47f, 0.19f, sz * 0.28f), new Vector3(0.04f, 0.38f, 0.04f), Basis.Identity, M.Walnut);
        foreach (var x in new[] { -0.5f, 0.5f })
        {
            m.Cylinder(new Vector3(x, 0.27f, -0.1f), new Vector3(x, 0.27f, 0.1f), 0.014f, M.Iron, 6, M.Iron);
            foreach (var z in new[] { -0.1f, 0.1f }) m.Box(new Vector3(x * 0.98f, 0.27f, z), new Vector3(0.03f, 0.05f, 0.03f), Basis.Identity, M.Iron);
        }
        m.Box(new Vector3(0, 0.37f, 0.3f), new Vector3(0.08f, 0.1f, 0.02f), Basis.Identity, M.Iron);
        return Done(m, new List<Vector2>());
    }

    public PropMade Chest()
    {
        var m = new PropMesh(_noise);
        m.Box(new Vector3(0, 0.3f, 0), new Vector3(1.4f, 0.6f, 0.86f), Basis.Identity, M.Plank);
        m.Box(new Vector3(0, 0.7f, 0), new Vector3(1.46f, 0.2f, 0.92f), Basis.Identity, M.Plank);
        foreach (var x in new[] { -0.45f, 0.45f })
        {
            m.Box(new Vector3(x, 0.3f, 0), new Vector3(0.07f, 0.6f, 0.9f), Basis.Identity, M.Iron);
            m.Box(new Vector3(x, 0.7f, 0), new Vector3(0.07f, 0.21f, 0.96f), Basis.Identity, M.Iron);
        }
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            m.Box(new Vector3(sx * 0.69f, 0.3f, sz * 0.42f), new Vector3(0.06f, 0.6f, 0.06f), Basis.Identity, M.Iron);
        foreach (var x in new[] { -0.725f, 0.725f })
        {
            m.Cylinder(new Vector3(x, 0.42f, -0.16f), new Vector3(x, 0.42f, 0.16f), 0.022f, M.Iron, 8, M.Iron);
            foreach (var z in new[] { -0.16f, 0.16f }) m.Box(new Vector3(x * 0.98f, 0.42f, z), new Vector3(0.03f, 0.06f, 0.04f), Basis.Identity, M.Iron);
        }
        m.Box(new Vector3(0, 0.5f, 0.435f), new Vector3(0.18f, 0.2f, 0.02f), Basis.Identity, M.Iron);
        m.Box(new Vector3(0, 0.63f, 0.465f), new Vector3(0.06f, 0.14f, 0.02f), Basis.Identity, M.Iron);
        return Done(m, new List<Vector2>());
    }

    public PropMade Crate()
    {
        var m = new PropMesh(_noise);
        m.Box(new Vector3(0, 0.5f, 0), new Vector3(1.78f, 0.98f, 1.13f), Basis.Identity, M.Straw);
        foreach (var y in new[] { 0.12f, 0.5f, 0.88f })
        {
            foreach (var z in new[] { -0.6f, 0.6f }) m.Box(new Vector3(0, y, z), new Vector3(1.8f, 0.22f, 0.05f), Basis.Identity, M.Plank);
            foreach (var x in new[] { -0.93f, 0.93f }) m.Box(new Vector3(x, y, 0), new Vector3(0.05f, 0.22f, 1.2f), Basis.Identity, M.Plank);
        }
        foreach (var (sx, sz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            m.Box(new Vector3(sx * 0.91f, 0.5f, sz * 0.58f), new Vector3(0.1f, 1.0f, 0.1f), Basis.Identity, M.Walnut);
        foreach (var z in new[] { -0.45f, -0.15f, 0.15f, 0.45f })
            m.Box(new Vector3(0, 1.02f, z), new Vector3(1.86f, 0.04f, 0.2f), Basis.Identity, M.Plank);
        foreach (var z in new[] { -0.635f, 0.635f })
            m.Box(new Vector3(0, 0.5f, z), new Vector3(1.85f, 0.1f, 0.03f), new Basis(Axis.Back, Mathf.DegToRad(CrateBrace)), M.Walnut);
        return Done(m, new List<Vector2>());
    }

    const float CrateBrace = 26.6f;

    static PropMade Done(PropMesh m, List<Vector2> fires)
    {
        var shift = m.Settle();
        m.Bake();
        return new PropMade(m, fires.Select(f => f + new Vector2(shift.X, shift.Z)).ToList());
    }
}

public sealed record PropMade(PropMesh Mesh, List<Vector2> Fires);
