using System.Numerics;
using S = KTable.Core.Model.CrowdSlots;

namespace KTable.Core.Model;

sealed partial class Tailor
{
    public const float MusketShoulder = -0.08f, MusketElbow = -0.55f;
    const float HoeShoulder = -0.15f, HoeElbow = -2.2f, ShieldShoulder = -0.1f, ShieldElbow = -0.9f;
    const float ForeU = 0.45f, ForeV = -0.07f, LockU = 0.3f, LockV = -0.09f;
    public const int InRightHand = -2, InLeftHand = -3;

    static Vector3 V(float x, float y, float z) => new(x, y, z);

    Piece Rod(Vector3 a, Vector3 b, float r) => Loft.Rod(a, b, r, _near ? 8 : 4);

    void Held(Piece g, string slot, byte limb, (float Sh, float El)? pose = null) =>
        Add(pose is { } p ? g.Unpose(p.Sh, p.El, Shoulder, Elbow2) : g, slot, limb, Shoulder, Elbow2, limb == RFore ? InRightHand : limb == LFore ? InLeftHand : Auto);

    (Vector3 O, Vector3 D, Vector3 X, float Sc) GunFrame()
    {
        float armR = 0.03f * _wMul;
        var grip = Posed(HandC(1), MusketShoulder, MusketElbow);
        var shoulder = Loft.TurnX(V(ArmX, Shoulder - 0.004f, armR * 1.15f + 0.011f), MusketShoulder, Shoulder);
        var d = Vector3.Normalize(shoulder - grip);
        float length = (_o.Gun == "rifle" ? 0.68f : 0.9f) * (_o.Gun == "long" ? 1.05f : 1);
        var top = -Vector3.UnitZ;
        return (grip - d * 0.035f, d, Vector3.Normalize(top - d * Vector3.Dot(top, d)), length / 1.5f);
    }

    public (Vector3 Fore, Vector3 Lock) GunWrists()
    {
        var (o, d, x, sc) = GunFrame();
        return (o + d * (ForeU * sc) + x * (ForeV * sc), o + d * (LockU * sc) + x * (LockV * sc));
    }

    void Items()
    {
        var rh = HandAt(-1);
        var lh = HandAt(1);
        switch (_o.Item)
        {
            case "spear":
                Held(Rod(V(rh.X, 0.02f, 0.035f), V(rh.X, 1.26f, 0.035f), 0.009f), S.Wood, RFore);
                Held(Loft.Cone(V(rh.X, 1.26f, 0.035f), 0.016f, 0.1f, 6), S.Metal, RFore);
                break;
            case "hammer":
            {
                var hc = HandC(-1);
                Held(Rod(V(hc.X, hc.Y, hc.Z - 0.03f), V(hc.X, hc.Y, hc.Z + 0.17f), 0.008f), S.Wood, RFore);
                Held(Loft.Box(0.03f, 0.08f, 0.035f, hc.X, hc.Y - 0.01f, hc.Z + 0.17f), S.Metal, RFore);
                break;
            }
            case "basket":
            {
                float bx = lh.X + 0.03f, top = lh.Y - 0.02f;
                var hc = HandC(1);
                Held(Loft.Rings([new(top - 0.12f, 0.085f, 0.065f, bx, 0.02f), new(top, 0.1f, 0.075f, bx, 0.02f)], _near ? 14 : 5, bottom: true), S.Wood, LFore);
                var handle = Loft.Bez(V(bx - 0.09f, top, 0.02f), V(hc.X, hc.Y + 0.06f, hc.Z), V(bx + 0.09f, top, 0.02f), _near ? 7 : 3);
                Held(Loft.Sweep(handle, handle.Select(_ => (0.006f, 0.006f)).ToList(), _near ? 5 : 3), S.Wood, LFore);
                break;
            }
            case "bundle":
            {
                var c = BodyAt(Chest);
                Add(Loft.Box(0.22f, 0.2f, 0.1f, 0, Chest - 0.02f, -(c.B + 0.06f)), S.Linen);
                Add(Loft.Rings([new(Chest - 0.05f, c.A + 0.012f, c.B + 0.012f), new(Chest - 0.035f, c.A + 0.012f, c.B + 0.012f)], _seg), S.Accent);
                break;
            }
            case "sack":
                Add(Loft.Ellipsoid(ArmX * 0.8f, Shoulder + 0.04f, -0.03f, 0.09f, 0.06f, 0.13f, _es0 + 2, _es1), S.Linen, LUpper, Shoulder);
                break;
            case "lantern":
            {
                var hc = HandC(1);
                Held(Rod(V(hc.X, hc.Y, hc.Z + 0.02f), V(hc.X, hc.Y - 0.1f, hc.Z + 0.02f), 0.004f), S.Metal, LFore);
                Held(Loft.Box(0.05f, 0.075f, 0.05f, hc.X, hc.Y - 0.135f, hc.Z + 0.02f), S.Lamp, LFore);
                Held(Loft.Box(0.06f, 0.012f, 0.06f, hc.X, hc.Y - 0.095f, hc.Z + 0.02f), S.Metal, LFore);
                break;
            }
            case "torch":
            {
                var hc = HandC(-1);
                Held(Rod(V(hc.X, hc.Y - 0.03f, hc.Z + 0.02f), V(hc.X, hc.Y + 0.32f, hc.Z + 0.06f), 0.01f), S.Wood, RFore);
                Held(Loft.Ellipsoid(hc.X, hc.Y + 0.36f, hc.Z + 0.065f, 0.025f, 0.045f, 0.025f, _es0, _es1), S.Lamp, RFore);
                break;
            }
            case "cane":
                Held(Rod(V(rh.X, rh.Y, 0.02f), V(rh.X - 0.01f, 0, 0.09f), 0.008f), S.Wood, RFore);
                break;
            case "hoe":
            {
                var grip = Posed(HandC(-1), HoeShoulder, HoeElbow);
                var d = Vector3.Normalize(V(-ArmX, Shoulder + 0.035f, -0.004f) - grip);
                var p0 = grip - d * 0.12f;
                var p1 = p0 + d * 0.8f;
                Held(Rod(p0, p1, 0.009f), S.Wood, RFore, (HoeShoulder, HoeElbow));
                Held(Loft.Box(0.06f, 0.012f, 0.09f, p1.X, p1.Y - 0.01f, p1.Z - 0.03f), S.Metal, RFore, (HoeShoulder, HoeElbow));
                break;
            }
            case "bow":
            {
                var hc = HandC(1);
                var stave = Loft.Bez(V(hc.X, hc.Y - 0.42f, hc.Z + 0.02f), V(hc.X, hc.Y, hc.Z + 0.12f), V(hc.X, hc.Y + 0.42f, hc.Z + 0.02f), _near ? 9 : 4);
                Held(Loft.Sweep(stave, stave.Select(_ => (0.007f, 0.009f)).ToList(), _near ? 6 : 3, caps: true), S.Wood, LFore);
                break;
            }
            case "shield":
                Shield();
                break;
        }
        if (_o.Gun != null) Musket();
        Sword();
    }

    void Shield()
    {
        var hand = Posed(HandC(1), ShieldShoulder, ShieldElbow);
        var n = Vector3.Normalize(V(0.45f, 0, 0.89f));
        const float r = 0.3f, half = 0.62f;
        var boss = hand + n * 0.045f;
        var c = boss - n * r;
        float phi = MathF.Atan2(n.Z, n.X), y0 = boss.Y - 0.34f, y1 = boss.Y + 0.3f;
        Held(Loft.Rings([new Ring(y0, r, r, c.X, c.Z).Open(phi - half, phi + half), new Ring(y1, r, r, c.X, c.Z).Open(phi - half, phi + half)], _near ? 12 : 3), S.Trim, LFore,
            (ShieldShoulder, ShieldElbow));
        Held(Loft.Ellipsoid(boss.X + n.X * 0.006f, boss.Y, boss.Z + n.Z * 0.006f, 0.028f, 0.05f, 0.028f, _es0, _es1), S.Brass, LFore, (ShieldShoulder, ShieldElbow));
    }

    void Musket()
    {
        var (o, d, _, sc) = GunFrame();
        Firearm(o, d, -Vector3.UnitZ, sc * 1.5f, _o.Gun!);
    }

    void Firearm(Vector3 o, Vector3 d, Vector3 top, float length, string kind)
    {
        var x = Vector3.Normalize(top - d * Vector3.Dot(top, d));
        var z = Vector3.Cross(x, d);
        float sc = length / 1.5f, slim = kind == "long" ? 0.85f : 1;
        Vector3 At(float u, float v, float w = 0) => o + d * (u * sc) + x * (v * sc) + z * (w * sc);
        float[] us = [0, 0.02f, 0.15f, 0.3f, 0.36f, 0.42f, 0.9f, 1.38f], tops = [0.045f, 0.046f, 0.035f, 0.022f, 0.016f, 0.016f, 0.013f, 0.012f];
        float[] bots = [-0.075f, -0.073f, -0.055f, -0.035f, -0.026f, -0.02f, -0.014f, -0.012f], wids = [0.021f, 0.021f, 0.02f, 0.018f, 0.015f, 0.016f, 0.014f, 0.012f];
        int[] pick = _near ? [0, 1, 2, 3, 4, 5, 6, 7] : [0, 3, 5, 7];
        var bits = new List<(Piece, string)>
        {
            (Loft.Sweep(pick.Select(i => At(us[i], (tops[i] + bots[i]) / 2)).ToList(), pick.Select(i => (wids[i] * sc * slim, (tops[i] - bots[i]) / 2 * sc)).ToList(), _near ? 10
                : 4, up: x, caps: true),
                S.Wood),
            (Loft.Sweep([At(0.33f, 0.024f), At(kind == "rifle" ? 1.38f : 1.5f, 0.02f)], [(0.011f * sc, 0.011f * sc), (0.009f * sc, 0.009f * sc)], _near ? 8 : 4, up: x, caps: true), S.Metal),
        };
        if (_near)
        {
            var lk = At(0.41f, 0.004f, 0.02f);
            bits.Add((Loft.Ellipsoid(lk.X, lk.Y, lk.Z, 0.012f * sc, 0.012f * sc, 0.012f * sc, 6, 4), S.Metal));
            foreach (float u in kind == "long" ? new[] { 0.7f, 0.95f, 1.15f, 1.35f } : new[] { 0.8f, 1.08f, 1.34f })
                bits.Add((Loft.Sweep([At(u - 0.008f, 0.004f), At(u + 0.008f, 0.004f)], [(0.02f * sc * slim, 0.025f * sc), (0.02f * sc * slim, 0.025f * sc)], 8, up: x, caps: true), kind == "rifle" ? S.Metal
                    : S.Brass));
            if (kind != "long")
            {
                var sling = Loft.Bez(At(0.8f, -0.02f), At(0.62f, -0.08f), At(0.42f, -0.04f), 6);
                bits.Add((Loft.Sweep(sling, sling.Select(_ => (0.012f * sc, 0.002f * sc)).ToList(), 4, up: z), kind == "rifle" ? S.Accent : S.Linen));
            }
        }
        if (kind == "musket") bits.Add((Loft.Sweep([At(1.5f, -0.006f), At(1.95f, -0.026f)], [(0.004f * sc, 0.012f * sc), (0.002f * sc, 0.003f * sc)], _near ? 6 : 3, up: x, caps: true), S.Metal));
        foreach (var (g, slot) in bits) Held(g, slot, Gun, (MusketShoulder, MusketElbow));
    }

    void Sword()
    {
        float gaW = GarmentA(Waist), gbW = GarmentB(Waist);
        switch (_o.Side)
        {
            case "daisho" or "katana":
                Blade(V(gaW * 0.9f + 0.01f, Waist + 0.006f, gbW * 0.62f), V(0.12f, -0.57f, -0.81f), 0.45f, 0.15f,
                    new(0.0072f, 0.011f, 0.0066f, 0.0082f, S.Trim, S.Trim, "disc", 0.024f, S.Metal, Curve: 0.02f, Lift: 0.03f));
                if (_o.Side == "daisho")
                    Blade(V(gaW * 0.7f + 0.008f, Waist + 0.02f, gbW * 0.85f), V(0.2f, -0.7f, -0.68f), 0.3f, 0.1f,
                        new(0.0066f, 0.0098f, 0.006f, 0.0076f, S.Trim, S.Trim, "disc", 0.019f, S.Metal, Curve: 0.012f, Lift: 0.022f));
                break;
            case "smallsword" or "hanger":
            {
                bool shortOne = _o.Side == "hanger";
                float yG = Hip + 0.035f;
                var g = V(GarmentA(yG) + 0.014f, yG, 0.025f);
                Blade(g, V(0.07f, -0.86f, -0.5f), shortOne ? 0.36f : 0.48f, shortOne ? 0.09f : 0.08f,
                    new(0.0085f, shortOne ? 0.016f : 0.012f, 0.0065f, 0.0065f, S.Accent, shortOne ? S.Wood : S.Brass, "disc", shortOne ? 0.014f : 0.018f, S.Brass, Pommel: 0.01f, Chape: true, Lift: 0.03f));
                if (shortOne) Blade(g + V(0.006f, 0.01f, -0.03f), V(0.06f, -0.88f, -0.47f), 0.25f, 0, new(0.007f, 0.009f, 0, 0, S.Accent, S.Accent, null, 0, S.Brass, Chape: true));
                break;
            }
            case "arming":
            {
                float yG = Hip + 0.045f;
                Blade(V(GarmentA(yG) + 0.014f, yG, 0.02f), V(0.05f, -0.9f, -0.42f), 0.5f, 0.12f, new(0.01f, 0.016f, 0.008f, 0.008f, S.Accent, S.Accent, "cross", 0.06f, S.Metal, Pommel: 0.014f, Lift: 0.02f));
                break;
            }
            case "gladius":
            {
                float yG = Hip + 0.06f;
                Blade(V(-(GarmentA(yG) + 0.014f), yG, 0), V(-0.04f, -0.95f, -0.3f), 0.3f, 0.1f, new(0.011f, 0.02f, 0.009f, 0.009f, S.Accent, S.Wood, "disc", 0.024f, S.Brass, Pommel: 0.016f, Chape: true));
                break;
            }
            case "yatagan":
                Blade(V(gaW * 0.42f, Waist + 0.01f, gbW + 0.012f), V(0.55f, -0.75f, -0.18f), 0.4f, 0.1f,
                    new(0.0075f, 0.013f, 0.007f, 0.008f, S.Metal, S.Linen, null, 0, S.Linen, Pommel: 0.013f, Curve: 0.015f, Chape: true, Lift: 0.02f));
                break;
            case "bayonet":
            {
                float yG = Hip + 0.02f;
                Blade(V(GarmentA(yG) + 0.012f, yG, -0.03f), V(0.03f, -0.95f, -0.3f), 0.2f, 0.06f, new(0.007f, 0.012f, 0.007f, 0.008f, S.Accent, S.Wood, null, 0, S.Metal, Pommel: 0.009f, Chape: true));
                break;
            }
            case "dagger":
                Blade(V(-gaW * 0.12f, Waist + 0.01f, gbW + 0.012f), V(0.3f, -0.92f, -0.1f), 0.14f, 0.07f,
                    new(0.009f, 0.013f, 0.007f, 0.008f, S.Brass, S.Brass, null, 0, S.Brass, Pommel: 0.011f, Curve: 0.02f, Lift: 0.01f));
                break;
        }
    }

    sealed record Hilt(float Sa, float Sb, float Ha, float Hb, string Scabbard, string Grip, string? Guard, float Ga, string GuardSlot, float Pommel = 0, float Curve = 0, bool Chape = false,
        float Lift = 0);

    void Blade(Vector3 g, Vector3 dir, float length, float hilt, Hilt q)
    {
        dir = Vector3.Normalize(dir);
        var tip = g + dir * length;
        var mid = Vector3.Lerp(g, tip, 0.5f) + V(0, q.Curve, 0);
        var pts = Loft.Bez(g, mid, tip, _near ? 7 : 3);
        Add(Loft.Sweep(pts, pts.Select((_, i) => (q.Sa * (1 - 0.3f * i / (pts.Count - 1)), q.Sb * (1 - 0.25f * i / (pts.Count - 1)))).ToList(), _near ? 8 : 4, caps: true), q.Scabbard);
        if (q.Chape) Add(Loft.Ellipsoid(tip.X, tip.Y, tip.Z, q.Sa * 0.95f, q.Sb * 0.9f, q.Sa * 0.95f, _es0, _es1), q.GuardSlot);
        if (hilt <= 0) return;
        var end = g - dir * hilt + V(0, q.Lift, 0);
        var gd = Vector3.Normalize(end - g);
        Add(Loft.Sweep([g, end], [(q.Ha, q.Hb), (q.Ha * 0.92f, q.Hb * 0.92f)], _near ? 7 : 4, caps: true), q.Grip);
        if (q.Guard == "disc") Add(Loft.Sweep([g - gd * 0.002f, g + gd * 0.003f], [(q.Ga, q.Ga), (q.Ga, q.Ga)], _near ? 12 : 5, caps: true), q.GuardSlot);
        if (q.Guard == "cross")
        {
            var sd = Vector3.Normalize(Vector3.Cross(gd, Vector3.UnitZ));
            Add(Loft.Sweep([g - sd * q.Ga, g + sd * q.Ga], [(0.006f, 0.006f), (0.006f, 0.006f)], 5, caps: true), q.GuardSlot);
        }
        if (q.Pommel > 0) Add(Loft.Ellipsoid(end.X, end.Y, end.Z, q.Pommel, q.Pommel, q.Pommel, _es0, _es1), q.GuardSlot);
    }

    Vector3 OnGarment(float x, float y, float back, float lift = 0.004f)
    {
        float ga = GarmentA(y) + lift, gb = GarmentB(y) + lift, k = MathF.Min(0.995f, MathF.Abs(x) / ga), z0 = BodyAt(MathF.Max(y, Crotch)).Z * 0.5f;
        return V(x, y, back * gb * MathF.Sqrt(1 - k * k) + z0);
    }

    void Extras()
    {
        if (_o.Has(CrowdDetails.Cross))
        {
            foreach (float back in new[] { 1f, -1f })
            {
                int n = _near ? 8 : 3;
                var pts = new List<Vector3>();
                var ups = new List<Vector3>();
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / (n - 1), y = Shoulder - 0.012f + (Hip - Shoulder + 0.022f) * t, ga = GarmentA(y) + 0.004f, gb = GarmentB(y)
                        + 0.004f, z0 = BodyAt(MathF.Max(y, Crotch)).Z * 0.5f;
                    var p = OnGarment(ga * (0.6f - 1.52f * t), y, back);
                    pts.Add(p);
                    ups.Add(Vector3.Normalize(V(p.X / (ga * ga), 0, (p.Z - z0) / (gb * gb))));
                }
                Add(Loft.Sweep(pts, pts.Select(_ => (0.017f, 0.0028f)).ToList(), _near ? 6 : 4, ups: ups), S.Linen);
            }
            Add(Loft.Box(0.11f, 0.075f, 0.04f, -GarmentA(Hip) * 0.55f, Hip - 0.005f, -(GarmentB(Hip) + 0.024f)), S.Accent);
        }
        Coat();
        Neckwear();
        if (_o.Has(CrowdDetails.ObiKnot)) Add(Loft.Ellipsoid(0, Waist - 0.01f, -(GarmentB(Waist) + 0.03f), 0.1f, 0.08f, 0.035f, _es0 + 2, _es1), S.Under);
        if (_o.Has(CrowdDetails.Clavi)) Clavi();
        if (_o.Paenula) Paenula();
        float gaW = GarmentA(Waist), gbW = GarmentB(Waist);
        if (_o.Has(CrowdDetails.Pouches))
            foreach (float sd in new[] { 1f, -1f }) Add(Loft.Box(0.05f, 0.04f, 0.025f, sd * gaW * 0.45f, Waist - 0.012f, gbW + 0.012f), S.Accent);
        if (_o.Has(CrowdDetails.Pouch)) Add(Loft.Box(0.05f, 0.05f, 0.025f, -gaW * 0.6f, Waist - 0.035f, gbW * 0.82f), S.Accent);
    }

    void Coat()
    {
        if (_o.CoatHem == null) return;
        if (_o.Has(CrowdDetails.Lapels))
            foreach (float sd in new[] { 1f, -1f })
            {
                var rs = Smooth(Sample(Waist + 0.04f, Shoulder - 0.03f, _near ? 6 : 2, y => Torso(y, _ease * 2.3f)));
                foreach (var r in rs) r.Open(sd > 0 ? MathF.PI / 2 - 0.62f : MathF.PI / 2 + 0.1f, sd > 0 ? MathF.PI / 2 - 0.1f : MathF.PI / 2 + 0.62f);
                Add(Loft.Rings(rs, _near ? 6 : 2), S.Trim);
            }
        if (!_near) return;
        if (_o.Has(CrowdDetails.Buttons))
        {
            bool lapels = _o.Has(CrowdDetails.Lapels);
            for (int i = 0; i < 6; i++)
            {
                float y = Waist + 0.02f + i * (Chest + 0.04f - Waist - 0.02f) / 5, e = _ease * (lapels ? 2.7f : 2);
                var b = BodyAt(y);
                foreach (float sd in new[] { 1f, -1f })
                {
                    float th = MathF.PI / 2 - sd * (lapels ? 0.42f : 0.3f);
                    Add(Loft.Ellipsoid((b.A + e) * MathF.Cos(th), y, b.Z + (b.B + e) * MathF.Sin(th), 0.0055f, 0.0055f, 0.004f, 6, 4), S.Brass);
                }
            }
        }
        if (_o.Has(CrowdDetails.Flaps))
            foreach (float sd in new[] { 1f, -1f })
            {
                float c0 = MathF.PI / 2 - sd * 0.95f, y0 = Hip - 0.03f;
                Add(Loft.Rings(new[] { y0, y0
                    + 0.035f }.Select(y => new Ring(y, CoatA(y) + _ease * 0.6f, CoatB(y) + _ease * 0.6f, 0, BodyAt(MathF.Max(y, Crotch)).Z * 0.5f).Open(c0 - 0.28f, c0 + 0.28f)).ToList(),
                    6),
                    S.Outer, Skirt, Hip);
            }
    }

    void Neckwear()
    {
        if (_o.Cravat)
        {
            Add(Loft.Rings([new(NeckBase - 0.012f, _neckR * 1.35f, _neckR * 1.3f, 0, 0.002f), new(NeckBase + 0.022f, _neckR * 1.18f, _neckR * 1.15f, 0, 0.001f)], _seg), S.Linen);
            if (_near) Add(Loft.Ellipsoid(0, NeckBase - 0.02f, _neckR * 1.3f + BodyAt(NeckBase).Z, 0.016f, 0.03f, 0.012f, _es0, _es1), S.Linen);
        }
        if (_o.Tie) Add(Loft.Box(0.018f, 0.17f, 0.004f, 0, Chest - 0.02f, BodyAt(Chest - 0.02f).B + BodyAt(Chest).Z + 0.004f), S.Trim);
        if (_o.Fichu) Add(Loft.Rings(Smooth(Sample(Chest + 0.01f, NeckBase + 0.005f, _near ? 5 : 2, y => Torso(y, _ease * 2.2f))), _seg), S.Linen);
        if (!_o.Collar) return;
        foreach (var (sd, off, slot) in new[] { (-1f, 0.003f, S.Linen), (1f, 0.006f, S.Outer) })
        {
            int n = _near ? 7 : 3;
            var pts = new List<Vector3>();
            var ups = new List<Vector3>();
            float e = _o.Robe is { FromShoulder: true } ? _ease : 0.002f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1), y = NeckBase + 0.004f + (Chest - 0.035f - NeckBase - 0.004f) * t;
                var b0 = BodyAt(y);
                float a = b0.A + e, b = b0.B + e, x = sd * (0.045f * (1 - t) - 0.012f * t), k = MathF.Min(0.99f, MathF.Abs(x) / a), z = b0.Z + b * MathF.Sqrt(1 - k * k) + off;
                pts.Add(V(x, y, z));
                ups.Add(Vector3.Normalize(V(x / (a * a), 0, (z - b0.Z) / (b * b))));
            }
            Add(Loft.Sweep(pts, pts.Select(_ => (0.012f, 0.0025f)).ToList(), _near ? 5 : 3, ups: ups), slot);
        }
    }

    void Clavi()
    {
        foreach (float sd in new[] { 1f, -1f })
        {
            int n = _near ? 6 : 2;
            var pts = new List<Vector3>();
            var ups = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1), y = Shoulder - 0.02f + (Hem + 0.01f - Shoulder + 0.02f) * t, a = GarmentA(y), b = GarmentB(y), x = sd * 0.04f, k = MathF.Min(0.99f, MathF.Abs(x) / a);
                float z0 = BodyAt(MathF.Max(y, Crotch)).Z * (y < Waist ? 0.5f : 1), z = z0 + b * MathF.Sqrt(1 - k * k) + 0.003f;
                pts.Add(V(x, y, z));
                ups.Add(Vector3.Normalize(V(x / (a * a), 0, (z - z0) / (b * b))));
            }
            Add(Loft.Sweep(pts, pts.Select(_ => (0.008f, 0.002f)).ToList(), 4, ups: ups), S.Under);
        }
    }

    void Paenula()
    {
        float armR = 0.03f * _wMul, top = NeckBase + 0.004f, shY = Shoulder + 0.01f, wide = ArmX + armR * 1.6f;
        Add(Loft.Rings([new(0.4f, wide * 1.15f, (BodyAt(Waist).B + 0.05f) * 1.1f), new(Waist, wide * 1.06f, BodyAt(Waist).B + 0.045f), new(shY, wide, BodyAt(Shoulder).B + 0.035f),
            new(top, _neckR * 1.5f, _neckR * 1.4f)], _gseg, fold: Folds(t => 0.02f * (1 - t)), k: 8), S.Hat);
        Add(Loft.Shell(0, Cy - Hh * 0.05f, -Hd * 0.55f, Hw * 1.15f, Hh * 0.45f, Hd * 0.75f, 0.5f * MathF.PI, -MathF.PI / 2 - 0.3f, Hws, Hs, 0.9f), S.Hat);
    }
}
