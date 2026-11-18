using System.Numerics;
using E = KTable.Core.Model.CrowdEntry;
using H = KTable.Core.Model.RigHumanoid;
using S = KTable.Core.Model.CrowdSlots;

namespace KTable.Core.Model;

enum CutZone : byte { Body, Head, Face }

sealed record Cut(Piece G, string Slot, byte Limb, float Pivot, float Pivot2, CutZone CutZone, int Bone);

sealed partial class Tailor
{
    public const byte Still = 0, LThigh = 1, RThigh = 2, LUpper = 3, RUpper = 4, LShin = 5, RShin = 6, LFore = 7, RFore = 8, Skirt = 9, Gun = 10;
    public const int Auto = -1;
    const float EaseShare = 0.009f, NeckShare = 0.028f;

    static readonly string[] TorsoKeys = [H.UpperLeg, H.Hips, H.Spine, H.Chest, H.UpperChest, H.UpperArm, H.Neck];

    static readonly IReadOnlyDictionary<string, float[][]> Prof = new Dictionary<string, float[][]>
    {
        [E.Man] = [[.088f, .060f, -.004f], [.095f, .066f, -.006f], [.083f, .058f, 0], [.100f, .068f, .006f], [.104f, .066f, .004f], [.098f, .055f, -.002f], [.044f, .040f, -.004f]],
        [E.Woman] = [[.098f, .064f, -.006f], [.106f, .072f, -.010f], [.072f, .055f, 0], [.092f, .074f, .012f], [.094f, .068f, .008f], [.090f, .052f, -.002f], [.040f, .036f, -.004f]],
    };

    readonly CrowdLook _look;
    readonly Kit _o;
    readonly bool _near;
    readonly int _seg, _gseg, _lseg, _es0, _es1;
    readonly float _wMul, _ease, _legX, _neckR;
    readonly float[] _keys;
    readonly float[][] _prof;
    public readonly float Ankle, Knee, Crotch, Hip, Waist, Chest, Armpit, Shoulder, NeckBase, Chin, Elbow, Wrist;
    public float ArmX { get; private set; }
    public float Elbow2 { get; private set; }
    public float Alpha { get; private set; }
    public readonly List<Cut> Cuts = new();
    CutZone _zone = CutZone.Body;

    public Kit Kit => _o;
    public float LegX => _legX;

    public Tailor(CrowdLook look, bool near)
    {
        (_look, _o, _near) = (look, Kit.Of(look), near);
        (_seg, _gseg, _lseg, _es0, _es1) = near ? (18, 32, 11, 12, 8) : (6, 7, 5, 5, 3);
        var j = CrowdBaseline.Joints(look.Band);
        float Y(string role) => (float)j[role];
        (Ankle, Knee, Crotch, Hip, Waist, Chest, Armpit, Shoulder) = (Y(H.Foot), Y(H.LowerLeg), Y(H.UpperLeg), Y(H.Hips), Y(H.Spine), Y(H.Chest), Y(H.UpperChest), Y(H.UpperArm));
        (NeckBase, Chin, Elbow, Wrist) = (Y(H.Neck), Y(H.Head), Y(H.LowerArm), Y(H.Hand));
        _wMul = (look.Band == E.Child ? 1.05f : look.Band == E.Youth ? 0.96f : 1) * (float)look.Build;
        _keys = TorsoKeys.Select(Y).ToArray();
        _prof = Prof[look.Sex == E.Woman ? E.Woman : E.Man];
        _ease = EaseShare;
        _legX = (look.Sex == E.Woman ? 0.05f : 0.047f) * _wMul;
        _neckR = NeckShare * (look.Band == E.Child ? 1.08f : 1) * (look.Sex == E.Woman ? 0.92f : 1);
    }

    public (float A, float B, float Z) BodyAt(float y)
    {
        int i = 0;
        while (i < _keys.Length - 2 && y > _keys[i + 1]) i++;
        float t = Math.Clamp((y - _keys[i]) / (_keys[i + 1] - _keys[i]), 0, 1);
        var a = _prof[i];
        var b = _prof[i + 1];
        return ((a[0] + (b[0] - a[0]) * t) * _wMul, (a[1] + (b[1] - a[1]) * t) * _wMul, a[2] + (b[2] - a[2]) * t);
    }

    void Add(Piece g, string slot, byte limb = Still, float pivot = 0, float pivot2 = 0, int bone = Auto) => Cuts.Add(new Cut(g, slot, limb, pivot, pivot2, _zone, bone));

    static List<Ring> Sample(float y0, float y1, int n, Func<float, Ring> f) => Enumerable.Range(0, n).Select(i => f(y0 + (y1 - y0) * i / (n - 1))).ToList();

    static List<Ring> Smooth(List<Ring> rings)
    {
        for (int pass = 0; pass < 2; pass++)
            for (int i = 1; i < rings.Count - 1; i++)
            {
                rings[i].A = (rings[i - 1].A + 2 * rings[i].A + rings[i + 1].A) / 4;
                rings[i].B = (rings[i - 1].B + 2 * rings[i].B + rings[i + 1].B) / 4;
            }
        return rings;
    }

    Ring Torso(float y, float extra = 0)
    {
        var b = BodyAt(y);
        return new Ring(y, b.A + extra, b.B + extra, 0, b.Z);
    }

    bool LongRobe => _o.Robe is { Hem: < 0.2f };
    float RobeTop => _o.Robe is { } r ? (r.FromShoulder ? NeckBase - 0.012f : Waist) : 0;
    float Hem => _o.Robe is { } r ? MathF.Max(r.Hem, 0.012f) : 0;
    float LegOuter(float y) => _legX + (y > Knee ? 0.056f : 0.042f) * _wMul;
    float WaistG => BodyAt(Waist).A + _ease;
    float AHem => _o.Robe is { } r ? MathF.Max((BodyAt(Hip).A + _ease) * r.Flare * (LongRobe ? 1.08f : 0.97f), _legX + 0.072f * _wMul) : 0;

    public float RobeA(float y)
    {
        if (y >= Waist) return BodyAt(y).A + _ease;
        float s = Math.Clamp((Waist - y) / MathF.Max(Waist - Hem, 1e-3f), 0, 1), a = WaistG + (AHem - WaistG) * MathF.Pow(s, 0.8f);
        return MathF.Max(a, (y >= Crotch ? BodyAt(y).A : LegOuter(y)) + _ease);
    }

    public float RobeB(float y) => y >= Waist ? BodyAt(y).B + _ease : MathF.Max(MathF.Max(RobeA(y) * 0.8f, BodyAt(MathF.Max(y, Crotch)).B + _ease), 0.068f * _wMul);

    float CoatY => (_o.CoatHem ?? 0);

    public float CoatA(float y)
    {
        if (y >= Waist) return BodyAt(y).A + _ease * 1.7f;
        float s = MathF.Min(1, (Waist - y) / MathF.Max(Waist - CoatY, 1e-3f));
        return MathF.Max(BodyAt(Waist).A + _ease * 2 + (BodyAt(Hip).A * 1.34f - BodyAt(Waist).A) * s, (y >= Crotch ? BodyAt(y).A : LegOuter(y)) + _ease * 2);
    }

    public float CoatB(float y) => MathF.Max(BodyAt(MathF.Max(y, Crotch)).B + _ease * 1.7f, CoatA(y) * 0.74f);

    public float GarmentA(float y)
    {
        float a = (y >= Crotch ? BodyAt(y).A : LegOuter(y)) + _ease;
        if (_o.Robe != null && y <= RobeTop && y >= Hem) a = MathF.Max(a, RobeA(y));
        if (_o.CoatHem != null && y <= Shoulder && y >= CoatY) a = MathF.Max(a, CoatA(y));
        return a;
    }

    public float GarmentB(float y)
    {
        float b = BodyAt(MathF.Max(y, Crotch)).B + _ease;
        if (_o.Robe != null && y <= RobeTop && y >= Hem) b = MathF.Max(b, RobeB(y));
        if (_o.CoatHem != null && y <= Shoulder && y >= CoatY) b = MathF.Max(b, CoatB(y));
        return b;
    }

    public void Cut()
    {
        TorsoAndHead();
        _zone = CutZone.Head;
        HairAndHat();
        _zone = CutZone.Body;
        Legs();
        Garments();
        Arms();
        Items();
        Extras();
    }

    void TorsoAndHead()
    {
        Add(Loft.Rings(Smooth(Sample(Crotch, NeckBase, _near ? 15 : 5, y => Torso(y))), _seg, bottom: true), _o.Torso);
        Add(Loft.Rings([new Ring(NeckBase - 0.02f, _neckR * 1.08f, _neckR, 0, -0.004f, 0.85f), new Ring(Chin + 0.014f, _neckR * 0.95f, _neckR * 0.95f, 0, 0, 0.7f)], _seg), S.Skin);
        Head();
    }

    float Hh => 1 - Chin;
    float Cy => Chin + Hh * 0.5f;
    float Hw => Hh * 0.35f;
    float Hd => Hh * 0.43f;
    float Ht => Cy + Hh * 0.5f;
    float EyeY => Cy + Hh * 0.02f;

    float Front(float y)
    {
        float u = Math.Clamp((y - Cy) / (Hh * 0.5f), -1, 1), k = MathF.Sqrt(MathF.Max(0, 1 - u * u));
        return Hd * k * 0.94f + (u < -0.2f ? 0.035f * Hh * -u : -0.06f * Hh * MathF.Max(0, u));
    }

    void Head()
    {
        int rows = _near ? 14 : 4;
        var head = new List<Ring>();
        for (int i = 0; i < rows; i++)
        {
            float u = -0.95f + 1.9f * i / (rows - 1), k = MathF.Sqrt(MathF.Max(0, 1 - u * u)), jaw = u < -0.2f ? 0.74f + 0.26f * (u + 1) / 0.8f : 1;
            var ring = new Ring(Cy + u * Hh * 0.5f, Hw * k * jaw * (u > 0.3f ? 1.04f : 1), Hd * k, 0, u < -0.2f ? 0.035f * Hh * -u : -0.06f * Hh * MathF.Max(0, u), u < -0.65f ? 0.85f : 1);
            if (_near) ring.RAt = FaceShape(u);
            head.Add(ring);
        }
        Add(Loft.Rings(head, _near ? 20 : _seg, top: true, bottom: true), S.Skin);
        if (!_near) return;
        float[] ny = [EyeY + 0.03f * Hh, EyeY - 0.13f * Hh, EyeY - 0.247f * Hh, EyeY - 0.3f * Hh];
        float[] nz = [Front(ny[0]) + 0.0144f * Hh, Front(ny[1]) + 0.055f * Hh, Front(ny[2]) + 0.091f * Hh, Front(ny[3]) + 0.03f * Hh];
        Add(Loft.Sweep(ny.Select((y, i) => new Vector3(0, y, nz[i])).ToList(), [(0.018f * Hh, 0.012f * Hh), (0.024f * Hh, 0.02f * Hh), (0.038f * Hh, 0.028f * Hh), (0.048f * Hh, 0.012f * Hh)], 6,
            up: Vector3.UnitZ, caps: true), S.Skin);
        foreach (float s in new[] { 1f, -1f }) Add(Loft.Ellipsoid(s * Hw * 0.97f, EyeY - 0.1f * Hh, -Hh * 0.04f, Hh * 0.035f, Hh * 0.1f, Hh * 0.06f, 6, 4, 0.85f), S.Skin);
    }

    static Func<float, float> FaceShape(float u) => th =>
    {
        float off = MathF.Abs(MathF.Atan2(MathF.Sin(th - MathF.PI / 2), MathF.Cos(th - MathF.PI / 2))), f = MathF.Pow(MathF.Max(0, MathF.Cos(off)), 4);
        float m = 1 - 0.06f * f * (u > -0.75f && u < 0.45f ? 1 : 0);
        m -= 0.07f * 1.35f * MathF.Exp(-MathF.Pow(off - 0.4f, 2) / 0.016f) * MathF.Exp(-MathF.Pow(u - 0.04f, 2) / 0.018f);
        m += 0.035f * MathF.Exp(-MathF.Pow(off - 0.75f, 2) / 0.05f) * MathF.Exp(-MathF.Pow(u + 0.18f, 2) / 0.03f);
        return m;
    };

    public void Face()
    {
        _zone = CutZone.Face;
        foreach (float s in new[] { 1f, -1f })
        {
            Add(Loft.Ellipsoid(s * Hw * 0.36f, EyeY, Front(EyeY) - 0.014f * Hh * 1.35f, Hh * 0.04f, Hh * 0.024f, Hh * 0.02f, 8, 4), S.Eye);
            float by = EyeY + 0.085f * Hh;
            var brow =
                new[] { -0.15f, 0.3f,
                    0.6f }.Select(t => new Vector3(s * Hw * (0.36f + t * 0.4f), by + 0.01f * Hh * (1 - MathF.Abs(t - 0.15f) * 2), Front(by) - 0.002f * Hh - MathF.Abs(t) * 0.025f * Hh))
                .ToList();
            Add(Loft.Sweep(brow, brow.Select(_ => (0.011f * Hh, 0.007f * Hh)).ToList(), 4, up: Vector3.UnitZ, caps: true), S.Brow);
        }
        Add(Loft.Ellipsoid(0, EyeY - 0.33f * Hh, Front(EyeY - 0.33f * Hh) - 0.004f * Hh, Hh * 0.075f, Hh * 0.016f, Hh * 0.015f, 8, 3), S.Lip);
        _zone = CutZone.Body;
    }

    void Legs()
    {
        foreach (var (s, thighLimb, shinLimb) in new[] { (1f, LThigh, LShin), (-1f, RThigh, RShin) })
        {
            float x = s * _legX, bag = _o.Baggy ? 1.5f : 1, w = 0.001f * _wMul, sb = _o.Baggy ? 1.48f : 1;
            var thigh =
                new List<Ring> { new(Knee, 41 * w * bag, 43 * w * bag, x), new((Knee + Crotch) / 2, 54 * w * bag, 57 * w * bag, x, 0.004f),
                    new(Crotch + 0.05f, 62 * w * MathF.Min(bag, 1.2f), 64 * w * MathF.Min(bag, 1.2f), x * 0.92f) };
            if (!_near) thigh.RemoveAt(1);
            Add(Loft.Rings(thigh, _lseg), _o.Thigh, thighLimb, Hip);
            var shin =
                new List<Ring> { new(Ankle, 24 * w * sb, 26 * w * sb, x, -0.004f), new(Ankle + 0.05f, 29 * w * sb, 31 * w * sb, x, -0.005f), new(Knee - 0.07f, 40 * w * sb, 46 * w * sb, x, -0.008f),
                    new(Knee + 0.012f, 40 * w * sb, 41 * w * sb, x) };
            if (!_near) shin.RemoveAt(1);
            Add(Loft.Rings(shin, _lseg), _o.Shin, shinLimb, Hip, Knee);
            Add(_near ? Loft.Ellipsoid(x, 0.021f, 0.032f, 0.028f * _wMul, 0.022f, 0.068f, _es0, _es1, 0.8f)
                : Loft.Box(0.05f * _wMul, 0.04f, 0.13f, x, 0.02f, 0.03f, 0.8f), _o.Feet, shinLimb, Hip, Knee);
            if (_o.Has(CrowdDetails.Buckles) && _near) Add(Loft.Box(0.024f, 0.004f, 0.016f, x, 0.042f, 0.058f), S.Brass, shinLimb, Hip, Knee);
        }
    }

    Func<float, float>? Folds(Func<float, float> f) => _near ? f : null;

    void Garments()
    {
        if (_o.Robe is { } robe)
        {
            if (robe.FromShoulder) Add(Loft.Rings(Smooth(Sample(Waist, RobeTop, _near ? 9 : 3, y => Torso(y, _ease))), _seg), robe.Slot);
            bool woman = _look.Sex == E.Woman;
            Add(Loft.Rings(Smooth(Sample(Hem, Waist, _near ? 12 : 3, y => new Ring(y, RobeA(y), RobeB(y), 0, BodyAt(MathF.Max(y, Crotch)).Z * 0.5f))), _gseg,
                fold: Folds(t => (0.012f + 0.065f * MathF.Pow(1 - t, 1.6f)) * (LongRobe ? 1 : 0.6f)), k: woman ? 11 : 9, phase: 0.7f), robe.Slot, Skirt, Hip);
        }
        if (_o.CoatHem != null)
        {
            const float gT = 0.22f, gB = 0.78f;
            void Open(List<Ring> rs, float g0, float g1)
            {
                for (int i = 0; i < rs.Count; i++)
                {
                    float t = (float)i / (rs.Count - 1), g = g0 + (g1 - g0) * t;
                    rs[i].Open(MathF.PI / 2 + g, MathF.PI / 2 - g + MathF.Tau);
                }
            }
            var body = Smooth(Sample(Waist, Shoulder - 0.012f, _near ? 8 : 3, y => Torso(y, _ease * 1.7f)));
            Open(body, gT, 0.1f);
            Add(Loft.Rings(body, _gseg), S.Outer);
            var skirt = Smooth(Sample(CoatY, Waist, _near ? 8 : 3, y => new Ring(y, CoatA(y), CoatB(y), 0, BodyAt(MathF.Max(y, Crotch)).Z * 0.5f)));
            Open(skirt, gB, gT);
            Add(Loft.Rings(skirt, _gseg, fold: Folds(t => 0.008f + 0.03f * (1 - t)), k: 7, phase: 0.3f), S.Outer, Skirt, Hip);
        }
        if (_o.Apron is { } apron)
        {
            const float half = 0.8f;
            Add(Loft.Rings(Sample(apron.Bottom, apron.Top, _near ? 8 : 2, y => new Ring(y, GarmentA(y) + _ease * 0.7f, GarmentB(y) + _ease * 0.7f, 0, BodyAt(MathF.Max(y, Crotch)).Z * 0.5f)
                .Open(MathF.PI / 2 - half, MathF.PI / 2 + half)), _near ? 12 : 3, fold: Folds(t => 0.01f * (1 - t)), k: 6), apron.Slot, Skirt, Hip);
        }
        Trims();
        if (_o.Belt is { } belt)
        {
            float bh = _o.Obi ? 0.075f : 0.028f, yB = _o.Has(CrowdDetails.LowBelt) ? Hip : Waist;
            Ring At(float y) => new(y, GarmentA(y) + _ease * 0.6f, GarmentB(y) + _ease * 0.6f, 0, BodyAt(y).Z * 0.5f);
            Add(Loft.Rings([At(yB - bh / 2), At(yB + bh / 2)], _seg), belt);
        }
    }

    void Trims()
    {
        bool fur = _o.Has(CrowdDetails.FurTrim);
        if (!fur && !_o.Has(CrowdDetails.TrimEdge)) return;
        float t = fur ? 0.022f : 0.01f;
        if (_o.Robe != null)
        {
            Ring R(float y) => new(y, RobeA(y) + 0.004f, RobeB(y) + 0.004f, 0, BodyAt(MathF.Max(y, Crotch)).Z * 0.5f);
            Add(Loft.Rings([R(Hem), R(Hem + t)], _gseg), S.Trim, Skirt, Hip);
        }
        if (_o.CoatHem != null)
        {
            Ring C(float y) => new Ring(y, CoatA(y) + 0.005f, CoatB(y) + 0.005f, 0, BodyAt(MathF.Max(y, Crotch)).Z * 0.5f).Open(MathF.PI / 2 + 0.78f, MathF.PI / 2 - 0.78f + MathF.Tau);
            Add(Loft.Rings([C(CoatY), C(CoatY + t)], _gseg), S.Trim, Skirt, Hip);
        }
        if (fur)
        {
            var low = Torso(NeckBase - 0.035f, _ease * 2.6f);
            low.Z = BodyAt(NeckBase).Z;
            Add(Loft.Rings([low, new Ring(NeckBase + 0.005f, _neckR * 1.7f, _neckR * 1.6f)], _seg), S.Trim);
        }
    }

    void Arms()
    {
        float armR = 0.03f * _wMul, sx = BodyAt(Armpit).A + armR * (_near ? 0.2f : 0.02f), yHand = Wrist - 0.03f;
        string[] blades = ["smallsword", "hanger", "arming", "gladius", "bayonet", "daisho", "katana"];
        float hipBlade = _o.Side != null && blades.Contains(_o.Side) ? GarmentA(Hip + 0.03f) + 0.032f : 0;
        float need = MathF.Max(MathF.Max(GarmentA(Wrist), GarmentA(yHand)), MathF.Max(GarmentA((Wrist + Elbow) / 2), hipBlade - 0.004f)) + 0.024f * _wMul + 0.004f;
        Alpha = Math.Clamp(MathF.Asin(Math.Clamp((need - sx) / (Shoulder - yHand), -1, 1)), 0.035f, 0.42f);
        ArmX = sx;
        Elbow2 = Shoulder - (Shoulder - Elbow) * MathF.Cos(Alpha);
        float wide = _o.WideSleeves ? 2.1f : 1;
        bool longSleeves = _o.Has(CrowdDetails.LongSleeves);
        foreach (var (s, upperLimb, foreLimb) in new[] { (1f, LUpper, LFore), (-1f, RUpper, RFore) })
        {
            float x = s * sx;
            Piece Tilt(Piece g) => g.TurnZ(s * Alpha, x, Shoulder);
            var upper = new List<Ring> { new(Elbow, armR * 0.82f, armR * 0.86f, x), new((Elbow + Shoulder) / 2, armR * 0.98f, armR, x), new(Shoulder - 0.012f, armR * 1.08f, armR * 1.12f, x) };
            if (!_near) upper.RemoveAt(1);
            Add(Tilt(Loft.Rings(upper, _lseg, top: true)), _o.Sleeves, upperLimb, Shoulder);
            Add(Tilt(_near ? Loft.Ellipsoid(x - s * armR * 0.35f, Shoulder - 0.016f, 0, armR * 1.12f, armR * 1.05f, armR * 1.08f, _es0, _es1)
                : Loft.Ellipsoid(x - s * armR * 0.3f, Shoulder - 0.012f, 0, armR * 1.35f, armR * 1.15f, armR * 1.15f, 4, 2)), _o.Sleeves, upperLimb, Shoulder);
            float wristY = longSleeves ? Wrist - 0.14f : Wrist;
            var fore = new List<Ring>
            {
                new(wristY, armR * 0.66f * wide, armR * (longSleeves ? 2.6f : 0.72f * wide), x, _o.WideSleeves ? -armR * (longSleeves ? 1.2f : 0.6f) : 0),
                new((Wrist + Elbow) / 2, armR * 0.8f * MathF.Min(wide, 1.9f), armR * 0.86f * MathF.Min(wide, 1.9f), x, _o.WideSleeves ? -armR * 0.4f : 0),
                new(Elbow + 0.012f, armR * 0.84f, armR * 0.88f, x),
            };
            if (!_near) fore.RemoveAt(1);
            Add(Tilt(Loft.Rings(fore, _lseg, bottom: !_near)), _o.Rolled ? S.Skin : _o.Sleeves, foreLimb, Shoulder, Elbow2);
            if (_o.Rolled
                && _near) Add(Tilt(Loft.Rings([new(Elbow - 0.01f, armR * 0.98f, armR * 1.02f, x), new(Elbow + 0.03f, armR, armR * 1.04f, x)], _lseg)), _o.Sleeves, foreLimb, Shoulder, Elbow2);
            if (_o.CoatHem != null
                && _near) Add(Tilt(Loft.Rings([new(Wrist + 0.002f, armR * 0.95f, armR, x), new(Wrist + 0.05f, armR, armR * 1.05f, x)], _lseg)), _o.Cuffs, foreLimb, Shoulder, Elbow2);
            if (_near)
            {
                Add(Tilt(Loft.Ellipsoid(x, Wrist - 0.035f, 0.004f, 0.019f * _wMul, 0.042f, 0.028f, _es0, _es1)), S.Skin, foreLimb, Shoulder, Elbow2);
                Add(Tilt(Loft.Ellipsoid(x - s * 0.008f, Wrist - 0.024f, 0.02f, 0.008f, 0.019f, 0.009f, 6, 4)), S.Skin, foreLimb, Shoulder, Elbow2);
            }
        }
    }

    public Vector3 HandAt(float s) => new(s * ArmX + s * MathF.Sin(Alpha) * (Shoulder - (Wrist - 0.03f)), Shoulder - MathF.Cos(Alpha) * (Shoulder - (Wrist - 0.03f)), 0);

    public Vector3 HandC(float s)
    {
        float yc = Wrist - 0.035f;
        return new Vector3(s * ArmX + s * MathF.Sin(Alpha) * (Shoulder - yc), Shoulder - MathF.Cos(Alpha) * (Shoulder - yc), 0.004f);
    }

    public Vector3 Posed(Vector3 v, float shoulder, float elbow) => Loft.TurnX(Loft.TurnX(v, elbow, Elbow2), shoulder, Shoulder);
}
