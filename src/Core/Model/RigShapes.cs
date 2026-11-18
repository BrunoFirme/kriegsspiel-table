using System.Numerics;

namespace KTable.Core.Model;

public static class RigShapes
{
    public const byte Paint = 0, Accent = 1, Canvas = 2, Load = 3, Coat = 4, Mane = 5, Dark = 6, Wood = 7, Metal = 8, Brass = 9, Lamp = 10;
    public const byte Still = 0, UpperLeg = 11, LowerLeg = 15, Neck = 19, Wheel = 20;

    sealed record Form(float Wh, float Len, float Girth, float NeckUp, float NeckFwd, float Head, float LegR, float Ear, float Stride, string[] Coats,
        float Belly = 0.56f, bool Mane = false, bool Tail = false, bool Horns = false, bool Thin = false, bool Hump = false);

    static readonly Dictionary<string, Form> Forms = new()
    {
        ["horse"] = new(1.5f, 1.55f, 0.27f, 0.52f, 0.48f, 0.56f, 0.05f, 1, 3.4f, ["#6b3f26", "#8a5230", "#2a2420", "#b9b4aa", "#5a3a28", "#b08d5a"], Mane: true, Tail: true),
        ["pony"] = new(1.3f, 1.35f, 0.25f, 0.42f, 0.42f, 0.5f, 0.045f, 1, 4.0f, ["#6b3f26", "#4a3324", "#8a5a3a", "#2a2420"], Mane: true, Tail: true),
        ["ox"] = new(1.4f, 1.75f, 0.36f, 0.06f, 0.45f, 0.5f, 0.06f, 1.2f, 4.0f, ["#7a4a30", "#cfc0a0", "#8a8580", "#5a3a2a", "#b08a5a"], Horns: true, Thin: true),
        ["buffalo"] = new(1.4f, 1.8f, 0.38f, 0.02f, 0.45f, 0.52f, 0.062f, 1.2f, 4.0f, ["#2f2a28", "#3a3330"], Horns: true, Thin: true),
        ["donkey"] = new(1.05f, 1.1f, 0.21f, 0.3f, 0.32f, 0.42f, 0.038f, 2.3f, 5.2f, ["#8b867e", "#6a5646", "#a09a90"], Thin: true),
        ["mule"] = new(1.4f, 1.45f, 0.26f, 0.45f, 0.44f, 0.56f, 0.046f, 1.8f, 3.6f, ["#4a372b", "#5a4636", "#2f2622"], Tail: true),
        ["camel"] = new(1.85f, 1.75f, 0.33f, 0.8f, 0.5f, 0.48f, 0.055f, 0.6f, 2.9f, ["#c4a473", "#a07e52", "#b8935f"], Belly: 0.6f, Hump: true, Thin: true),
    };

    public static double StrideOf(string animal) => Forms.TryGetValue(animal, out var f) ? f.Stride : 3.5;

    public static double BackOf(string animal) => Forms.TryGetValue(animal, out var f) ? f.Wh * 0.96 + (f.Hump ? 0.3 : 0.05) : 1.4;

    public static string CoatOf(string animal, int seed) => Forms.TryGetValue(animal, out var f) ? f.Coats[(int)((uint)seed % (uint)f.Coats.Length)] : "#6b3f26";

    static Vector3 V(float x, float y, float z) => new(x, y, z);

    sealed class Kit
    {
        public readonly CrowdShape Shape = new();

        public void Add(Piece g, byte slot, byte limb = Still, float pivot = 0, float pivot2 = 0, float sway = 0)
        {
            int first = Shape.Points.Count;
            var normals = g.Normals();
            for (int i = 0; i < g.P.Count; i++)
            {
                Shape.Points.Add(g.P[i]);
                Shape.Normals.Add(normals[i]);
                Shape.Colour.Add(slot);
                Shape.Bone.Add(0);
                Shape.Limb.Add(limb);
                Shape.Pivot.Add(pivot);
                Shape.Pivot2.Add(pivot2);
                Shape.Sway.Add(sway);
                Shape.Shade.Add(g.Ao[i]);
            }
            foreach (int t in g.T) Shape.Triangles.Add(first + t);
        }

        public void Box(float w, float h, float d, float x, float y, float z, byte slot) => Add(Loft.Box(w, h, d, x, y, z), slot);

        public void Rod(Vector3 a, Vector3 b, float r, byte slot, int segs = 5) => Add(Loft.Rod(a, b, r, segs), slot);

        public static Piece Cylinder(float r, float len, Vector3 at, char axis, int segs)
        {
            var half = axis == 'x' ? V(len / 2, 0, 0) : axis == 'z' ? V(0, 0, len / 2) : V(0, len / 2, 0);
            return Loft.Rod(at - half, at + half, r, segs);
        }

        public void Cyl(float r, float len, float x, float y, float z, char axis, byte slot, int segs = 10) => Add(Cylinder(r, len, V(x, y, z), axis, segs), slot);

        public void Wheel(float r, float x, float z, string? style = null, byte rim = Wood)
        {
            void W(Piece g, byte slot) => Add(g, slot, RigShapes.Wheel, r, r, z);
            var hub = V(x, r, z);
            if (style == "solid")
            {
                W(Cylinder(r, 0.1f, hub, 'x', 14), Wood);
                W(Loft.Box(0.14f, r * 1.5f, 0.12f, x, r, z), Accent);
                return;
            }
            if (style == "tyre")
            {
                W(Hoop(hub, r - 0.07f, 0.075f, 'x', 14), Dark);
                W(Cylinder(r - 0.11f, 0.07f, hub, 'x', 10), Metal);
                return;
            }
            bool thin = style == "thin";
            W(Hoop(hub, r - 0.03f, 0.032f, 'x', 16), rim);
            int spokes = thin ? 3 : 4;
            for (int i = 0; i < spokes; i++)
                W(Loft.Box(thin ? 0.012f : 0.03f, 2 * r - 0.08f, thin ? 0.012f : 0.035f, x, r, z).TurnX(i * MathF.PI / spokes, r, z), rim);
            if (!thin) W(Cylinder(r * 0.16f, 0.16f, hub, 'x', 8), Wood);
        }
    }

    static Piece Hoop(Vector3 at, float radius, float tube, char axis, int segs)
    {
        var pts = new List<Vector3>();
        for (int k = 0; k <= segs; k++)
        {
            float a = k * MathF.Tau / segs, c = MathF.Cos(a) * radius, s = MathF.Sin(a) * radius;
            pts.Add(at + (axis == 'x' ? V(0, c, s) : V(c, s, 0)));
        }
        return Loft.Sweep(pts, pts.Select(_ => (tube, tube)).ToList(), 5, up: axis == 'x' ? Vector3.UnitX : Vector3.UnitZ);
    }

    static Piece Arch(float r, float high, float len, float y, float z)
    {
        var g = new Piece();
        const int segs = 10;
        for (int e = 0; e < 2; e++)
            for (int j = 0; j <= segs; j++)
            {
                float th = MathF.PI * j / segs;
                g.P.Add(V(r * MathF.Cos(th), y + high * MathF.Sin(th), z + (e == 0 ? -len / 2 : len / 2)));
                g.Ao.Add(0.95f);
            }
        for (int j = 0; j < segs; j++) g.T.AddRange([j, j + 1, segs + 1 + j, j + 1, segs + 2 + j, segs + 1 + j]);
        return g;
    }

    static void LoadOn(Kit k, string? load, float w, float l, float y)
    {
        switch (load)
        {
            case "hay":
                k.Add(Loft.Ellipsoid(0, y + 0.42f, 0, w * 0.55f, 0.55f, l * 0.5f, 8, 5), Load);
                break;
            case "sacks":
                foreach (var (x, z, yy) in new[] { (-0.25f, -0.3f, 0f), (0.25f, -0.28f, 0f), (-0.2f, 0.22f, 0f), (0.25f, 0.25f, 0f), (0f, -0.05f, 0.3f) })
                    k.Add(Loft.Ellipsoid(x * w, y + 0.18f + yy, z * l, 0.24f, 0.17f, 0.36f, 6, 4), Canvas);
                break;
            case "barrels":
                foreach (var (x, z) in new[] { (-0.25f, -0.28f), (0.25f, -0.28f), (-0.25f, 0.22f), (0.25f, 0.22f) }) k.Cyl(0.26f, 0.6f, x * w, y + 0.3f, z * l, 'y', Wood, 8);
                break;
            case "jars":
                foreach (var (x, z) in new[] { (-0.25f, -0.3f), (0.25f, -0.3f), (-0.25f, 0.05f), (0.25f, 0.05f), (0f, 0.35f) })
                    k.Add(Loft.Ellipsoid(x * w, y + 0.3f, z * l, 0.17f, 0.32f, 0.17f, 7, 5), Load);
                break;
            case "crates":
                foreach (var (x, z, s) in new[] { (-0.22f, -0.3f, 0.5f), (0.24f, -0.25f, 0.42f), (0f, 0.3f, 0.55f) }) k.Box(s, s * 0.8f, s, x * w, y + s * 0.4f, z * l, Wood);
                break;
        }
    }

    public static CrowdShape Animal(string kind, string? tack)
    {
        var q = Forms.GetValueOrDefault(kind) ?? Forms["horse"];
        var k = new Kit();
        float wh = q.Wh, back = wh * 0.96f, belly = wh * q.Belly, cy = (back + belly) / 2, ry = (back - belly) / 2, rx = q.Girth, zF = q.Len / 2, zR = -q.Len / 2;
        float[] prof = [0.6f, 0.94f, 1, 1.05f, 1, 0.9f, 0.64f];
        k.Add(Loft.Sweep(prof.Select((_, i) => V(0, cy + (i is 0 or 6 ? 0.03f * wh : 0), zR + (zF - zR) * i / 6)).ToList(), prof.Select(f => (rx * f, ry * f)).ToList(), 8, caps: true), Coat);
        var nb = V(0, back - ry * 0.55f, zF - 0.12f);
        var poll = V(0, back + q.NeckUp, zF + q.NeckFwd);
        var nm = q.Hump ? V(0, back - ry * 0.3f, zF + q.NeckFwd) : V(0, back + q.NeckUp * 0.55f, zF + q.NeckFwd * 0.25f);
        var neck = Loft.Bez(nb, nm, poll, 6);
        float hs = q.Head / 0.55f;
        void N(Piece g, byte slot) => k.Add(g, slot, Neck, nb.Y, 0, nb.Z);
        N(Loft.Sweep(neck, neck.Select((_, i) => (ry * (0.95f - 0.5f * i / 5) * (q.Thin ? 0.8f : 1), rx * (0.62f - 0.3f * i / 5))).ToList(), 8, up: Vector3.UnitX), Coat);
        var muzzle = poll + V(0, -q.Head * 0.55f, q.Head * 0.82f);
        N(Loft.Sweep([poll + V(0, 0.04f, -0.07f), poll + V(0, -q.Head * 0.22f, q.Head * 0.38f), muzzle], [(0.135f * hs, 0.1f * hs), (0.115f * hs, 0.085f * hs), (0.07f * hs, 0.06f * hs)],
            8, up: Vector3.UnitX, caps: true), Coat);
        foreach (int s in new[] { 1, -1 }) N(Loft.Ellipsoid(s * 0.075f, poll.Y + 0.05f + 0.04f * q.Ear, poll.Z - 0.05f, 0.025f, 0.055f * q.Ear, 0.02f, 5, 4), Coat);
        if (q.Horns)
            foreach (int s in new[] { 1, -1 })
            {
                var h = Loft.Bez(V(s * 0.08f, poll.Y + 0.02f, poll.Z - 0.02f), V(s * 0.32f, poll.Y + 0.06f, poll.Z), V(s * 0.36f, poll.Y + 0.27f, poll.Z + 0.08f), 5);
                N(Loft.Sweep(h, h.Select((_, i) => (0.035f - 0.006f * i, 0.035f - 0.006f * i)).ToList(), 5, caps: true), Canvas);
            }
        if (q.Mane) N(Loft.Sweep(neck.Select(p => p + V(0, 0.075f, -0.085f)).ToList(), neck.Select(_ => (0.06f, 0.02f)).ToList(), 5, up: Vector3.UnitX, caps: true), Mane);
        var tail = Loft.Bez(V(0, back - 0.05f, zR - 0.02f), V(0, back - 0.1f, zR - 0.24f), V(0, belly - (q.Tail ? 0.18f : -0.1f), zR - 0.2f), 5);
        k.Add(Loft.Sweep(tail, tail.Select((_, i) => q.Tail ? (0.03f + 0.012f * i, 0.03f + 0.01f * i) : (0.018f + (i == 4 ? 0.02f : 0), 0.018f + (i == 4 ? 0.02f : 0))).ToList(), 5,
            up: Vector3.UnitX, caps: true), Mane);
        if (q.Hump) k.Add(Loft.Ellipsoid(0, back + 0.14f, -0.05f, rx * 0.72f, 0.32f, 0.4f, 8, 5), Coat);
        Legs(k, q, belly, ry, rx, zF, zR);
        Tack(k, q, tack, cy, ry, rx, back, zF);
        return k.Shape;
    }

    static void Legs(Kit k, Form q, float belly, float ry, float rx, float zF, float zR)
    {
        float legTop = belly + ry * 0.5f, knee = q.Wh * 0.3f, r = q.LegR;
        foreach (var (sx, z, leg) in new[] { (1, zF - q.Len * 0.1f, 0), (-1, zF - q.Len * 0.1f, 1), (1, zR + q.Len * 0.1f, 2), (-1, zR + q.Len * 0.1f, 3) })
        {
            float x = sx * rx * 0.58f;
            byte upper = (byte)(UpperLeg + leg), lower = (byte)(LowerLeg + leg);
            k.Add(Loft.Rings([new Ring(knee, r, r, x, z), new Ring((knee + legTop) / 2, r * 1.5f, r * 1.7f, x, z), new Ring(legTop + ry * 0.4f, r * 1.9f, r * 2.4f, x * 0.9f, z)], 6),
                Coat, upper, legTop, 0, z);
            k.Add(Loft.Rings([new Ring(0.07f, r * 0.8f, r * 0.85f, x, z), new Ring(knee, r * 0.95f, r, x, z)], 6), Coat, lower, legTop, knee, z);
            k.Add(Loft.Rings([new Ring(0, r * 1.15f, r * 1.25f, x, z + 0.01f), new Ring(0.08f, r * 0.95f, r, x, z)], 6, bottom: true), Dark, lower, legTop, knee, z);
        }
    }

    static void Tack(Kit k, Form q, string? tack, float cy, float ry, float rx, float back, float zF)
    {
        switch (tack)
        {
            case "saddle":
                k.Add(Loft.Sweep([V(0, cy, -0.3f), V(0, cy, 0.32f)], [(rx * 1.07f, ry * 1.07f), (rx * 1.07f, ry * 1.07f)], 8), Paint);
                k.Add(Loft.Ellipsoid(0, back + 0.05f, 0.02f, rx * 0.7f, 0.075f, 0.28f, 8, 4), Accent);
                break;
            case "pack":
                k.Add(Loft.Sweep([V(0, cy, -0.28f), V(0, cy, 0.3f)], [(rx * 1.06f, ry * 1.06f), (rx * 1.06f, ry * 1.06f)], 8), Canvas);
                foreach (int s in new[] { 1, -1 }) k.Box(0.34f, 0.46f, 0.7f, s * (rx + 0.2f), cy + 0.12f, 0, Load);
                k.Add(Loft.Ellipsoid(0, back + 0.2f + (q.Hump ? 0.3f : 0), 0, rx * 0.8f, 0.16f, 0.36f, 7, 4), Load);
                break;
            case "collar":
                k.Add(Hoop(V(0, cy + 0.1f, zF - 0.1f), ry * 0.95f, 0.05f, 'z', 12).TurnX(-0.25f, cy + 0.1f, zF - 0.1f), Accent);
                k.Box(rx * 2.1f, 0.03f, 0.06f, 0, back + 0.02f, -0.1f, Accent);
                break;
        }
    }

    public static CrowdShape Vehicle(string what, string? style, string? load, bool painted)
    {
        var k = new Kit();
        Action<Kit> build = what switch
        {
            "cart2" => b => Cart(b, style, load),
            "wagon4" => b => Wagon(b, style, load, painted),
            "coach" => Coach,
            "chair" => b => Chair(b, style),
            "handcart" => b => Handcart(b, load),
            "barrow" => b => Barrow(b, load),
            "car" => Car,
            "lorry" => Lorry,
            "tram" => Tram,
            "bike" => Bike,
            _ => _ => { },
        };
        build(k);
        return k.Shape;
    }

    static void Cart(Kit k, string? style, string? load)
    {
        const float w = 1.3f, l = 2.2f, r = 0.62f, by = r + 0.08f, sh = 0.32f;
        k.Wheel(r, w / 2 + 0.09f, 0, style);
        k.Wheel(r, -w / 2 - 0.09f, 0, style);
        k.Cyl(0.045f, w + 0.3f, 0, r, 0, 'x', Wood, 6);
        k.Box(w, 0.07f, l, 0, by, 0, Wood);
        k.Box(0.05f, sh, l, w / 2, by + sh / 2, 0, Wood);
        k.Box(0.05f, sh, l, -w / 2, by + sh / 2, 0, Wood);
        k.Box(w, sh, 0.05f, 0, by + sh / 2, -l / 2, Wood);
        k.Box(w, sh * 0.8f, 0.05f, 0, by + sh * 0.4f, l / 2, Wood);
        foreach (int s in new[] { 1, -1 }) k.Rod(V(s * 0.42f, by, l / 2), V(s * 0.42f, 0.95f, l / 2 + 2.0f), 0.035f, Wood);
        LoadOn(k, load, w, l, by + 0.05f);
        if (style == "canopy") k.Add(Arch(w / 2 + 0.04f, 1.05f, l * 0.98f, by + sh, 0), Paint);
    }

    static void Wagon(Kit k, string? style, string? load, bool painted)
    {
        bool tyre = style == "tyre", ladder = style == "ladder";
        const float w = 1.4f, l = 3.4f;
        float rF = tyre ? 0.42f : 0.48f, rR = tyre ? 0.42f : 0.62f, by = rR + 0.12f, zf = l / 2 - 0.55f, zr = -l / 2 + 0.6f, sh = ladder ? 0.55f : 0.36f;
        string? wheels = tyre ? "tyre" : null;
        foreach (int s in new[] { 1, -1 })
        {
            k.Wheel(rF, s * (w / 2 + 0.1f), zf, wheels);
            k.Wheel(rR, s * (w / 2 + 0.1f), zr, wheels);
        }
        k.Cyl(0.045f, w + 0.3f, 0, rF, zf, 'x', Wood, 6);
        k.Cyl(0.045f, w + 0.3f, 0, rR, zr, 'x', Wood, 6);
        k.Box(w, 0.08f, l, 0, by, 0, Wood);
        if (ladder)
            foreach (int s in new[] { 1, -1 })
            {
                k.Rod(V(s * w / 2, by + 0.1f, -l / 2), V(s * w / 2, by + 0.1f, l / 2), 0.03f, Wood);
                k.Rod(V(s * (w / 2 + 0.2f), by + sh, -l / 2), V(s * (w / 2 + 0.2f), by + sh, l / 2), 0.03f, Wood);
                for (int i = 0; i < 7; i++)
                {
                    float z = -l / 2 + 0.15f + i * (l - 0.3f) / 6;
                    k.Rod(V(s * w / 2, by + 0.1f, z), V(s * (w / 2 + 0.2f), by + sh, z), 0.018f, Wood);
                }
            }
        else
        {
            k.Box(0.05f, sh, l, w / 2, by + sh / 2, 0, painted ? Paint : Wood);
            k.Box(0.05f, sh, l, -w / 2, by + sh / 2, 0, painted ? Paint : Wood);
            k.Box(w, sh, 0.05f, 0, by + sh / 2, -l / 2, Wood);
            k.Box(w, sh, 0.05f, 0, by + sh / 2, l / 2, Wood);
        }
        k.Rod(V(0, rF + 0.05f, zf), V(0, 0.85f, l / 2 + 2.6f), 0.04f, Wood);
        LoadOn(k, load, w, l * 0.8f, by + 0.06f);
        if (style == "tilt") k.Add(Arch(w / 2 + 0.05f, 1.15f, l * 0.94f, by + sh, -0.05f), Canvas);
    }

    static void Coach(Kit k)
    {
        const float l = 2.8f, w = 1.5f, bl = l * 0.62f;
        foreach (int s in new[] { 1, -1 })
        {
            k.Wheel(0.48f, s * 0.85f, l / 2 - 0.3f);
            k.Wheel(0.66f, s * 0.85f, -l / 2 + 0.45f);
        }
        k.Cyl(0.045f, 1.9f, 0, 0.48f, l / 2 - 0.3f, 'x', Wood, 6);
        k.Cyl(0.045f, 1.9f, 0, 0.66f, -l / 2 + 0.45f, 'x', Wood, 6);
        k.Box(0.5f, 0.1f, l + 0.3f, 0, 0.78f, 0, Wood);
        k.Box(w, 1.25f, bl, 0, 1.52f, -0.1f, Paint);
        k.Box(w + 0.12f, 0.08f, bl + 0.14f, 0, 2.18f, -0.1f, Accent);
        k.Box(w + 0.02f, 0.42f, bl * 0.34f, 0, 1.78f, -0.1f, Dark);
        k.Box(w + 0.04f, 0.05f, bl, 0, 1.52f, -0.1f, Brass);
        k.Box(1.15f, 0.5f, 0.42f, 0, 1.3f, l / 2 - 0.08f, Accent);
        k.Box(1.1f, 0.5f, 0.4f, 0, 1.25f, -l / 2 + 0.05f, Accent);
        foreach (int s in new[] { 1, -1 }) k.Box(0.09f, 0.14f, 0.09f, s * 0.72f, 1.75f, bl / 2 - 0.02f, Lamp);
        k.Rod(V(0, 0.6f, l / 2), V(0, 0.85f, l / 2 + 2.7f), 0.04f, Wood);
    }

    static void Chair(Kit k, string? style)
    {
        switch (style)
        {
            case "sedan":
                k.Box(0.74f, 1.4f, 0.84f, 0, 0.95f, 0, Paint);
                k.Box(0.84f, 0.07f, 0.94f, 0, 1.68f, 0, Accent);
                k.Box(0.76f, 0.36f, 0.4f, 0, 1.3f, 0.05f, Dark);
                k.Box(0.76f, 0.04f, 0.86f, 0, 0.9f, 0, Brass);
                foreach (int s in new[] { 1, -1 }) k.Rod(V(s * 0.42f, 0.8f, -1.95f), V(s * 0.42f, 0.8f, 1.95f), 0.028f, Wood);
                break;
            case "kago":
                k.Rod(V(0, 1.36f, -1.75f), V(0, 1.36f, 1.75f), 0.04f, Wood);
                k.Box(0.7f, 0.05f, 0.95f, 0, 0.5f, 0, Wood);
                k.Box(0.72f, 0.3f, 0.05f, 0, 0.66f, -0.47f, Load);
                foreach (int s in new[] { 1, -1 }) k.Box(0.04f, 0.22f, 0.95f, s * 0.35f, 0.62f, 0, Load);
                k.Box(0.86f, 0.035f, 1.1f, 0, 1.2f, 0, Load);
                foreach (float z in new[] { -0.42f, 0.42f })
                    foreach (int s in new[] { 1, -1 }) k.Rod(V(s * 0.3f, 0.5f, z), V(0, 1.36f, z * 0.6f), 0.012f, Accent);
                break;
            case "norimono":
                k.Rod(V(0, 1.42f, -2.3f), V(0, 1.42f, 2.3f), 0.05f, Accent);
                k.Box(0.85f, 0.92f, 1.25f, 0, 0.82f, 0, Paint);
                k.Box(0.97f, 0.07f, 1.4f, 0, 1.32f, 0, Accent);
                k.Box(0.87f, 0.3f, 0.5f, 0, 0.98f, 0, Dark);
                k.Box(0.87f, 0.03f, 1.27f, 0, 0.6f, 0, Brass);
                break;
            default:
                foreach (int s in new[] { 1, -1 }) k.Rod(V(s * 0.5f, 1.4f, -1.75f), V(s * 0.5f, 1.4f, 1.75f), 0.035f, Wood);
                k.Box(0.9f, 0.22f, 1.9f, 0, 1.22f, 0, Wood);
                k.Box(0.8f, 0.12f, 1.5f, 0, 1.38f, -0.1f, Paint);
                k.Add(Loft.Ellipsoid(0, 1.5f, 0.6f, 0.3f, 0.1f, 0.22f, 6, 4), Canvas);
                foreach (float z in new[] { -0.9f, 0.9f })
                    foreach (int s in new[] { 1, -1 }) k.Rod(V(s * 0.42f, 1.3f, z), V(s * 0.42f, 2.15f, z), 0.02f, Wood);
                k.Box(0.95f, 0.05f, 1.95f, 0, 2.17f, 0, Canvas);
                foreach (int s in new[] { 1, -1 }) k.Box(0.02f, 0.45f, 1.9f, s * 0.45f, 1.93f, 0, Canvas);
                break;
        }
    }

    static void Handcart(Kit k, string? load)
    {
        const float r = 0.55f;
        k.Wheel(r, 0.72f, -0.2f);
        k.Wheel(r, -0.72f, -0.2f);
        k.Cyl(0.045f, 1.5f, 0, r, -0.2f, 'x', Wood, 6);
        k.Box(1.2f, 0.08f, 2.4f, 0, r + 0.1f, 0, Wood);
        foreach (int s in new[] { 1, -1 }) k.Rod(V(s * 0.5f, r + 0.1f, 1.2f), V(s * 0.5f, 0.82f, 2.45f), 0.03f, Wood);
        k.Rod(V(-0.5f, 0.82f, 2.45f), V(0.5f, 0.82f, 2.45f), 0.03f, Wood);
        LoadOn(k, load, 1.2f, 2.2f, r + 0.14f);
    }

    static void Barrow(Kit k, string? load)
    {
        k.Wheel(0.24f, 0, 0.78f);
        k.Box(0.6f, 0.28f, 0.9f, 0, 0.5f, 0.1f, Wood);
        foreach (int s in new[] { 1, -1 })
        {
            k.Rod(V(s * 0.2f, 0.4f, 0.78f), V(s * 0.3f, 0.76f, -1.0f), 0.025f, Wood);
            k.Rod(V(s * 0.26f, 0.36f, -0.3f), V(s * 0.26f, 0, -0.3f), 0.025f, Wood);
        }
        LoadOn(k, load, 0.5f, 0.8f, 0.6f);
    }

    static void Car(Kit k)
    {
        foreach (int s in new[] { 1, -1 })
            foreach (float z in new[] { 1.25f, -1.2f })
            {
                k.Wheel(0.34f, s * 0.72f, z, "tyre");
                k.Box(0.26f, 0.1f, 0.9f, s * 0.72f, 0.74f, z, Accent);
            }
        k.Box(1.5f, 0.5f, 3.9f, 0, 0.66f, 0, Paint);
        k.Box(1.2f, 0.34f, 1.3f, 0, 1.02f, 1.25f, Paint);
        k.Box(1.42f, 0.52f, 1.9f, 0, 1.17f, -0.35f, Paint);
        k.Box(1.44f, 0.3f, 1.74f, 0, 1.22f, -0.35f, Dark);
        k.Box(1.3f, 0.06f, 1.7f, 0, 1.45f, -0.35f, Paint);
        k.Box(0.9f, 0.3f, 0.06f, 0, 0.9f, 1.93f, Metal);
        foreach (int s in new[] { 1, -1 }) k.Box(0.16f, 0.16f, 0.08f, s * 0.52f, 0.95f, 1.95f, Lamp);
    }

    static void Lorry(Kit k)
    {
        foreach (int s in new[] { 1, -1 })
        {
            k.Wheel(0.45f, s * 0.9f, 2.5f, "tyre");
            k.Wheel(0.45f, s * 0.92f, -1.5f, "tyre");
        }
        k.Box(1.1f, 0.18f, 6.4f, 0, 0.75f, 0.3f, Accent);
        k.Box(1.5f, 0.62f, 1.1f, 0, 1.15f, 2.85f, Paint);
        k.Box(1.95f, 1.05f, 1.35f, 0, 1.4f, 1.7f, Paint);
        k.Box(1.97f, 0.4f, 0.9f, 0, 1.6f, 1.85f, Dark);
        k.Box(2.05f, 0.1f, 3.7f, 0, 1.0f, -1.0f, Wood);
        foreach (int s in new[] { 1, -1 }) k.Box(0.05f, 0.5f, 3.7f, s * 1.0f, 1.3f, -1.0f, Paint);
        k.Box(2.05f, 0.5f, 0.05f, 0, 1.3f, -2.85f, Paint);
        k.Add(Arch(1.02f, 1.25f, 3.6f, 1.5f, -1.0f), Canvas);
        foreach (int s in new[] { 1, -1 }) k.Box(0.18f, 0.18f, 0.08f, s * 0.55f, 1.1f, 3.42f, Lamp);
    }

    static void Tram(Kit k)
    {
        foreach (int s in new[] { 1, -1 })
            foreach (float z in new[] { 2.6f, -2.6f }) k.Wheel(0.4f, s * 0.72f, z, "tyre");
        k.Box(2.1f, 1.0f, 9.2f, 0, 1.0f, 0, Paint);
        k.Box(2.12f, 0.78f, 8.7f, 0, 1.88f, 0, Dark);
        k.Box(2.1f, 0.24f, 9.2f, 0, 2.38f, 0, Canvas);
        k.Box(1.9f, 0.1f, 8.8f, 0, 2.55f, 0, Accent);
        k.Box(2.14f, 0.05f, 9.2f, 0, 1.5f, 0, Canvas);
        k.Rod(V(0, 2.6f, -0.5f), V(0, 3.9f, -3.0f), 0.03f, Accent);
        k.Box(0.2f, 0.2f, 0.06f, 0, 1.2f, 4.62f, Lamp);
    }

    static void Bike(Kit k)
    {
        k.Wheel(0.34f, 0, 0.53f, "thin", Accent);
        k.Wheel(0.34f, 0, -0.53f, "thin", Accent);
        (Vector3, Vector3)[] frame =
        [
            (V(0, 0.34f, -0.53f), V(0, 0.9f, -0.2f)), (V(0, 0.9f, -0.2f), V(0, 0.92f, 0.36f)), (V(0, 0.3f, 0.02f), V(0, 0.9f, -0.2f)),
            (V(0, 0.3f, 0.02f), V(0, 0.88f, 0.36f)), (V(0, 0.34f, 0.53f), V(0, 1.02f, 0.34f)), (V(0, 0.3f, 0.02f), V(0, 0.34f, -0.53f)),
        ];
        foreach (var (a, b) in frame) k.Rod(a, b, 0.016f, Accent);
        k.Rod(V(-0.24f, 1.02f, 0.34f), V(0.24f, 1.02f, 0.34f), 0.014f, Metal);
        k.Box(0.14f, 0.05f, 0.26f, 0, 0.94f, -0.24f, Dark);
    }
}
