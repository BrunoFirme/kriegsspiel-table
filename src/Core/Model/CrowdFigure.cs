using System.Globalization;
using System.Numerics;
using System.Text;
using H = KTable.Core.Model.RigHumanoid;
using S = KTable.Core.Model.CrowdSlots;
using W = KTable.Core.Model.Wardrobe;

namespace KTable.Core.Model;

public sealed class CrowdShape
{
    public const int MostColours = 7;
    public const byte Still = 0, LeftLeg = 1, RightLeg = 2, LeftArm = 3, RightArm = 4, LeftShin = 5, RightShin = 6, LeftFore = 7, RightFore = 8, Skirt = 9, Gun = 10;
    public static readonly IReadOnlyList<string> Materials = [S.Wood, S.Metal, S.Brass, S.Lamp];

    public List<Vector3> Points { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<int> Triangles { get; } = new();
    public List<byte> Colour { get; } = new();
    public List<int> Bone { get; } = new();
    public List<byte> Limb { get; } = new();
    public List<float> Pivot { get; } = new();
    public List<float> Pivot2 { get; } = new();
    public List<float> Sway { get; } = new();
    public List<float> Shade { get; } = new();
    public List<string> Palette { get; } = new();

    public int Tris => Triangles.Count / 3;

    public bool Street { get; init; }

    public string SlotOf(byte colour) => colour < Palette.Count ? Palette[colour] : colour >= MostColours && colour - MostColours < Materials.Count ? Materials[colour - MostColours] : S.Outer;
}

public static class CrowdFigure
{
    public const int WholeColours = 24;
    const int BuildSteps = 3, GunBone = 22;
    const double LeastBuild = 0.93, BuildSpan = 0.2;
    const float FireLift = 0.97f, BraceLift = 0.6f, BraceUp = 0.52f, GunBack = 0.041f, Short = 0.5f;
    static readonly string[] PersonSpill = [S.Under, S.Outer, S.Linen, S.Accent, S.Legs];

    static readonly string[] Bones =
    [
        "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
        "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand", "Gun",
    ];

    static readonly int[] Parents = [-1, 0, 1, 2, 3, 4, 0, 6, 7, 8, 0, 10, 11, 12, 3, 14, 15, 16, 3, 18, 19, 20, -1];
    const int Hips = 0, Chest = 2, NeckBone = 4, HeadBone = 5, LThigh = 6, LShin = 7, LFoot = 8, RThigh = 10, RShin = 11, RFoot = 12;
    internal const int Spine = 1, Upper = 3, LArm = 15, LFore = 16, LHand = 17, RArm = 19, RFore = 20, RHand = 21;

    public static string BodyKey(CrowdLook look) => Key(look, false);

    public static string HeadKey(CrowdLook look) => Key(look, true);

    static int BuildStep(CrowdLook look) => Math.Clamp((int)((look.Build - LeastBuild) / BuildSpan * BuildSteps), 0, BuildSteps - 1);

    static CrowdLook Stepped(CrowdLook look)
    {
        var l = look.Clone();
        l.Build = LeastBuild + (BuildStep(look) + 0.5) * BuildSpan / BuildSteps;
        l.Stoop = Math.Round(look.Stoop * 10) / 10;
        return l;
    }

    static string Key(CrowdLook look, bool head)
    {
        var key = new StringBuilder().Append(look.Sex).Append('|').Append(look.Band).Append('|').Append(BuildStep(look)).Append('|').Append(Math.Round(look.Stoop * 10));
        foreach (var g in look.Worn.Where(g => IsHead(g.Part) == head))
            key.Append('|').Append(g.Part).Append(':').Append(g.Kind).Append(':').Append(g.Slot).Append(':')
                .Append(g.Hem?.ToString("0.00", CultureInfo.InvariantCulture)).Append(':').Append(g.Flare?.ToString("0.0", CultureInfo.InvariantCulture)).Append(':').Append(g.From);
        foreach (var d in (look.Details ?? []).Order(StringComparer.Ordinal)) key.Append("|+").Append(d);
        if (!head) key.Append("|item:").Append(look.Item);
        return key.ToString();
    }

    static bool IsHead(string part) => part is W.Hat or W.Hair or W.Beard;

    public static float LegAmp(CrowdLook look) => Kit.Of(look).LegAmp;

    public static int Hold(CrowdLook look) => Kit.Of(look).Hold;

    public static int Gait(CrowdLook look)
    {
        var kit = Kit.Of(look);
        return kit.Hold * 4 + (kit.LegAmp <= Short ? 2 : kit.LegAmp < 1 ? 1 : 0);
    }

    static Tailor Cut(CrowdLook look, bool near)
    {
        var t = new Tailor(Stepped(look), near);
        t.Cut();
        return t;
    }

    public static CrowdShape Body(CrowdLook look) => Assemble(Cut(look, false), look, z => z == CutZone.Body, true);

    public static CrowdShape Headwear(CrowdLook look) => Assemble(Cut(look, false), look, z => z == CutZone.Head, true);

    public static CrowdShape Face(CrowdLook look)
    {
        var t = Cut(look, true);
        t.Face();
        return Assemble(t, look, z => z == CutZone.Face, false);
    }

    public static (Rig Rig, CrowdShape Shape) Whole(CrowdLook look)
    {
        var t = Cut(look, true);
        t.Face();
        var shape = Assemble(t, look, _ => true, false);
        float scale = (float)look.HeightM;
        var joints = BoneSpots(t);
        var bones = Bones.Select((name, i) => new RigBone(name, Parents[i], (Parents[i] < 0 ? joints[i] : joints[i] - joints[Parents[i]]) * scale, Quaternion.Identity, 1)).ToList();
        int n = shape.Points.Count;
        var jointIndex = new ushort[n * Rig.Influences];
        var weights = new float[n * Rig.Influences];
        for (int v = 0; v < n; v++)
        {
            jointIndex[v * Rig.Influences] = (ushort)shape.Bone[v];
            weights[v * Rig.Influences] = 1;
            if (shape.Limb[v] != CrowdShape.Skirt || shape.Sway[v] <= 0) continue;
            float leg = shape.Sway[v] * 0.6f;
            jointIndex[v * Rig.Influences + 1] = (ushort)(shape.Pivot2[v] >= 0 ? LThigh : RThigh);
            (weights[v * Rig.Influences], weights[v * Rig.Influences + 1]) = (1 - leg, leg);
        }
        var rig = new Rig
        {
            Bones = bones, Points = shape.Points.Select(p => p * scale).ToArray(), Normals = shape.Normals.ToArray(), Coords = new Vector2[n],
            Joints = jointIndex, Weights = weights, Triangles = shape.Triangles.ToArray(),
        };
        foreach (var (role, bone) in H.Map(bones)) rig.Humanoid[role] = bone;
        return (rig, shape);
    }

    public static int Gun => GunBone;

    public static GunGrip? GripOf(CrowdLook look)
    {
        var t = new Tailor(Stepped(look), true);
        if (t.Kit.Gun == null) return null;
        t.Cut();
        var (fore, held) = t.GunWrists();
        var repose = TurnX(Tailor.MusketElbow, t.Elbow2) * TurnX(Tailor.MusketShoulder, t.Shoulder);
        var (fire, brace) = (Levelled(t, false), Levelled(t, true));
        return new GunGrip(repose * fire, repose * brace, Vector3.Transform(fore, fire), Vector3.Transform(held, fire), Vector3.Transform(fore, brace), Vector3.Transform(held, brace));
    }

    static Matrix4x4 Levelled(Tailor t, bool brace)
    {
        float gs = t.Shoulder, ga = 1.839f - (brace ? BraceUp : 0), lift = brace ? BraceLift : FireLift, c = 0.9818f, s = 0.1898f;
        var m = Matrix4x4.CreateTranslation(-0.237f * gs, -0.566f * gs, -0.205f * gs);
        m *= TurnX(ga, 0);
        m *= new Matrix4x4(c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1);
        m *= Matrix4x4.CreateTranslation(-0.11f * gs, lift * gs, GunBack);
        return m;
    }

    static Matrix4x4 TurnX(float angle, float pivotY) =>
        Matrix4x4.CreateTranslation(0, -pivotY, 0) * Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(0, pivotY, 0);

    static Vector3[] BoneSpots(Tailor t)
    {
        float lx = t.LegX, ax = t.ArmX;
        return
        [
            new(0, t.Hip, 0), new(0, t.Waist, 0), new(0, t.Chest, 0), new(0, t.Armpit, 0), new(0, t.NeckBase, 0), new(0, t.Chin, 0),
            new(lx, t.Crotch + 0.04f, 0), new(lx, t.Knee, 0), new(lx, t.Ankle, 0), new(lx, 0.01f, 0.075f),
            new(-lx, t.Crotch + 0.04f, 0), new(-lx, t.Knee, 0), new(-lx, t.Ankle, 0), new(-lx, 0.01f, 0.075f),
            new(ax * 0.35f, t.Shoulder, 0), new(ax, t.Shoulder, 0), new(ax, t.Elbow2, 0), new(ax, t.Wrist, 0),
            new(-ax * 0.35f, t.Shoulder, 0), new(-ax, t.Shoulder, 0), new(-ax, t.Elbow2, 0), new(-ax, t.Wrist, 0), Vector3.Zero,
        ];
    }

    static bool Leg(byte limb) => limb is CrowdShape.LeftLeg or CrowdShape.RightLeg or CrowdShape.LeftShin or CrowdShape.RightShin or CrowdShape.Skirt;

    static int BoneOf(Tailor t, Cut cut, Vector3 p) => cut.Bone >= 0 ? cut.Bone : cut.Bone == Tailor.InRightHand ? RHand : cut.Bone == Tailor.InLeftHand ? LHand
        : cut.CutZone != CutZone.Body ? HeadBone : cut.Limb switch
    {
        CrowdShape.LeftLeg => LThigh,
        CrowdShape.RightLeg => RThigh,
        CrowdShape.LeftShin => p.Y < t.Ankle + 0.015f ? LFoot : LShin,
        CrowdShape.RightShin => p.Y < t.Ankle + 0.015f ? RFoot : RShin,
        CrowdShape.LeftArm => LArm,
        CrowdShape.RightArm => RArm,
        CrowdShape.LeftFore => p.Y < t.Wrist + 0.005f ? LHand : LFore,
        CrowdShape.RightFore => p.Y < t.Wrist + 0.005f ? RHand : RFore,
        CrowdShape.Gun => GunBone,
        CrowdShape.Skirt => Hips,
        _ => p.Y >= t.Chin - 0.005f ? HeadBone : p.Y >= t.NeckBase - 0.025f ? NeckBone : p.Y >= t.Armpit ? Upper : p.Y >= t.Chest ? Chest : p.Y >= t.Waist ? Spine : Hips,
    };

    static CrowdShape Assemble(Tailor t, CrowdLook look, Func<CutZone, bool> take, bool street)
    {
        var shape = new CrowdShape { Street = street };
        float stoop = (float)Math.Round(look.Stoop * 10) / 10;
        foreach (var cut in t.Cuts.Where(c => take(c.CutZone)))
        {
            var g = cut.G;
            if (stoop > 0 && !Leg(cut.Limb))
                g.Each(p => p.Y > t.Hip ? new Vector3(p.X, p.Y - (p.Y - t.Hip) * stoop * 0.25f, p.Z + (p.Y - t.Hip) * stoop) : p);
            var normals = g.Normals();
            byte colour = Colour(shape, cut.Slot, street);
            int first = shape.Points.Count;
            bool skirt = cut.Limb == CrowdShape.Skirt;
            for (int i = 0; i < g.P.Count; i++)
            {
                var p = g.P[i];
                shape.Points.Add(p);
                shape.Normals.Add(normals[i]);
                shape.Colour.Add(colour);
                shape.Bone.Add(BoneOf(t, cut, p));
                shape.Limb.Add(cut.Limb);
                shape.Pivot.Add(cut.Pivot);
                shape.Pivot2.Add(skirt ? Math.Clamp(p.X / (t.LegX * 1.6f), -1, 1) : cut.Pivot2);
                shape.Sway.Add(skirt ? Math.Clamp((t.Waist - p.Y) / MathF.Max(t.Waist - t.Hip, 1e-3f), 0, 1) : 0);
                shape.Shade.Add(g.Ao[i] * (0.62f + 0.38f * Math.Clamp(p.Y / 0.14f, 0, 1)));
            }
            shape.Triangles.AddRange(g.T.Select(i => i + first));
        }
        return shape;
    }

    static byte Colour(CrowdShape shape, string slot, bool street)
    {
        if (street && CrowdShape.Materials.Contains(slot)) return (byte)(CrowdShape.MostColours + CrowdShape.Materials.ToList().IndexOf(slot));
        int at = shape.Palette.IndexOf(slot);
        if (at >= 0) return (byte)at;
        if (shape.Palette.Count < (street ? CrowdShape.MostColours : WholeColours))
        {
            shape.Palette.Add(slot);
            return (byte)(shape.Palette.Count - 1);
        }
        return (byte)Math.Max(0, PersonSpill.Select(slot => shape.Palette.IndexOf(slot)).FirstOrDefault(i => i >= 0));
    }

    public static IReadOnlyList<string> Colours(CrowdShape shape, CrowdLook look) =>
        shape.Palette.Select(slot => look.Colours.GetValueOrDefault(slot) ?? "#808080").ToList();

    public static string ColourOf(CrowdShape shape, CrowdLook look, byte colour) => look.Colours.GetValueOrDefault(shape.SlotOf(colour)) ?? "#808080";
}
