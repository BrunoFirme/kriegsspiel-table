using System.Globalization;
using System.Numerics;
using System.Text;
using E = KTable.Core.Model.CrowdEntry;
using H = KTable.Core.Model.RigHumanoid;
using S = KTable.Core.Model.CrowdSlots;
using W = KTable.Core.Model.Wardrobe;

namespace KTable.Core.Model;

public sealed class CrowdShape
{
    public const int MostColours = 7;
    public const byte Still = 0, LeftLeg = 1, RightLeg = 2, LeftArm = 3, RightArm = 4, Skirt = 5;

    public List<Vector3> Points { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public List<int> Triangles { get; } = new();
    public List<byte> Colour { get; } = new();
    public List<int> Bone { get; } = new();
    public List<byte> Limb { get; } = new();
    public List<float> Pivot { get; } = new();
    public List<string> Palette { get; } = new();

    public int Tris => Triangles.Count / 3;
}

public static class CrowdFigure
{
    const float FootLong = 0.075f, FootHigh = 0.04f, Head = 0.058f, HeadDeep = 0.062f, Neck = 0.024f, Thigh = 0.043f, Shin = 0.034f, UpperArm = 0.026f, Forearm = 0.022f, Hand = 0.018f;
    const float Skin = 0.004f, Cloth = 0.012f, HandLong = 0.06f, Pole = 0.012f;
    const float HeadFront = 0.004f, EyeLine = 0.5f, MouthLine = 0.2f, BrowAbove = 0.011f, EyesApart = 0.36f;
    const float FeatureHigh = 0.004f, FeatureDeep = 0.0025f, EyeWide = 0.0065f, BrowWide = 0.009f, MouthWide = 0.012f;
    public const int WholeColours = 12;
    const int BuildSteps = 3;
    const double LeastBuild = 0.93, BuildSpan = 0.2;

    static readonly string[] Bones =
    [
        "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head",
        "LeftUpperLeg", "LeftLowerLeg", "LeftFoot", "LeftToes", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes",
        "LeftShoulder", "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand",
    ];

    static readonly int[] Parents = [-1, 0, 1, 2, 3, 4, 0, 6, 7, 8, 0, 10, 11, 12, 3, 14, 15, 16, 3, 18, 19, 20];
    const int Hips = 0, Spine = 1, Chest = 2, Upper = 3, NeckBone = 4, HeadBone = 5, LThigh = 6, LShin = 7, LFoot = 8, LToes = 9, RThigh = 10, RShin = 11, RFoot = 12, RToes = 13;
    const int LShoulder = 14, LArm = 15, LFore = 16, LHand = 17, RShoulder = 18, RArm = 19, RFore = 20, RHand = 21;

    static readonly string[] Draped = ["hood", "wimple", "veil", "palla", "headscarf", "chaperon"];
    static readonly string[] Brims = ["brim", "fedora", "homburg", "kasa", "jingasa", "petasos", "tricorne", "kettle", "bonnet"];
    static readonly string[] Tall = ["bork", "kalpak", "turban", "bigturban", "helmet", "galea"];

    readonly record struct Frame(IReadOnlyDictionary<string, double> J, float W, float D, float HipW, float HipD, float ChestW, float ChestD, float LegX, float ArmX);

    public static string BodyKey(CrowdLook look) => Key(look, false);

    public static string HeadKey(CrowdLook look) => Key(look, true);

    static int BuildStep(CrowdLook look) => Math.Clamp((int)((look.Build - LeastBuild) / BuildSpan * BuildSteps), 0, BuildSteps - 1);

    static string Key(CrowdLook look, bool head)
    {
        var key = new StringBuilder().Append(look.Sex).Append('|').Append(look.Band).Append('|').Append(BuildStep(look));
        foreach (var g in look.Worn.Where(g => IsHead(g.Part) == head))
            key.Append('|').Append(g.Part).Append(':').Append(g.Kind).Append(':').Append(g.Slot).Append(':')
                .Append(g.Hem?.ToString("0.00", CultureInfo.InvariantCulture)).Append(':').Append(g.Flare?.ToString("0.0", CultureInfo.InvariantCulture)).Append(':').Append(g.From);
        if (!head) key.Append("|item:").Append(look.Item);
        return key.ToString();
    }

    static bool IsHead(string part) => part is W.Hat or W.Hair or W.Beard;

    static Frame FrameOf(CrowdLook look)
    {
        var j = CrowdBaseline.Joints(look.Band);
        var girth = CrowdBaseline.Girths(look.Sex);
        float build = (float)(LeastBuild + (BuildStep(look) + 0.5) * BuildSpan / BuildSteps), kid = look.Band == E.Child ? 1.12f : 1;
        var (hipW, hipD) = girth[H.Hips];
        var (chestW, chestD) = girth[H.Chest];
        float legX = (float)hipW * 0.5f * build;
        return new Frame(j, build * kid, build, (float)hipW * build * kid, (float)hipD * build, (float)chestW * build * kid, (float)chestD * build, legX, (float)chestW * build * kid + UpperArm);
    }

    public static CrowdShape Body(CrowdLook look)
    {
        var f = FrameOf(look);
        var b = new Builder(look);
        float Y(string role) => (float)f.J[role];
        float hip = Y(H.Hips), knee = Y(H.LowerLeg), crotch = Y(H.UpperLeg), waist = Y(H.Spine), chest = Y(H.Chest), armpit = Y(H.UpperChest), shoulder = Y(H.UpperArm);
        float chin = Y(H.Head), elbow = Y(H.LowerArm), wrist = Y(H.Hand), ankle = Y(H.Foot);
        string thigh = SlotOf(look, W.Thigh, S.Legs), shin = SlotOf(look, W.Shin, S.Legs), feet = SlotOf(look, W.Feet, S.Accent), sleeves = SlotOf(look, W.Sleeves, S.Outer);
        var robe = look.Part(W.Robe);
        var coat = look.Part(W.Coat);
        string torso = coat?.Slot ?? (robe is { From: "shoulder" } ? robe.Slot : null) ?? SlotOf(look, W.Torso, S.Outer);
        foreach (var (_, x, upper, lower, foot, limb) in new[] { (1f, f.LegX, LThigh, LShin, LFoot, CrowdShape.LeftLeg), (-1f, -f.LegX, RThigh, RShin, RFoot, CrowdShape.RightLeg) })
        {
            b.Box(new(x, knee, 0), new(x, crotch, 0), Thigh * f.W, Thigh * f.D, thigh, upper, limb, hip);
            b.Box(new(x, ankle, 0), new(x, knee, 0), Shin * f.W, Shin * f.D, shin, lower, limb, hip);
            b.Box(new(x, 0, 0.022f), new(x, FootHigh, 0.022f), Shin * f.W, FootLong, feet, foot, limb, hip);
        }
        b.Box(new(0, crotch - 0.02f, 0), new(0, waist, 0), f.HipW, f.HipD, torso, Hips, CrowdShape.Still, 0);
        b.Box(new(0, waist, 0), new(0, armpit, 0), f.ChestW, f.ChestD, torso, Spine, CrowdShape.Still, 0);
        b.Box(new(0, armpit, 0), new(0, shoulder + 0.01f, 0), f.ChestW * 0.95f, f.ChestD * 0.85f, torso, Upper, CrowdShape.Still, 0);
        b.Box(new(0, shoulder, 0), new(0, chin + 0.01f, 0), Neck, Neck, S.Skin, NeckBone, CrowdShape.Still, 0);
        b.Box(new(0, chin, 0.004f), new(0, 1, 0.004f), Head * f.W / f.D, HeadDeep, S.Skin, HeadBone, CrowdShape.Still, 0);
        string hands = look.Part(W.Sleeves)?.Kind == "bare" ? S.Skin : sleeves;
        foreach (var (side, arm, fore, hand, limb) in new[] { (1f, LArm, LFore, LHand, CrowdShape.LeftArm), (-1f, RArm, RFore, RHand, CrowdShape.RightArm) })
        {
            float x = side * f.ArmX;
            b.Box(new(x, elbow, 0), new(x, shoulder + 0.01f, 0), UpperArm * f.D, UpperArm * f.D, hands, arm, limb, shoulder);
            b.Box(new(x, wrist, 0), new(x, elbow, 0), Forearm * f.D, Forearm * f.D, look.Part(W.Sleeves)?.Kind is "rolled" or "bare" ? S.Skin : hands, fore, limb, shoulder);
            b.Box(new(x, wrist - HandLong, 0), new(x, wrist, 0), Hand, Hand, S.Skin, hand, limb, shoulder);
        }
        if (robe != null) Skirt(b, f, robe, robe.From == "waist" ? waist : hip, hip);
        if (coat != null) Skirt(b, f, coat, waist, hip, 1.15);
        if (look.Part(W.Apron) is { } apron)
        {
            float bottom = (float)(apron.Hem ?? (apron.Kind == "smith" ? 0.3 : 0.18)), front = Math.Max(f.HipD, f.ChestD) * 1.25f + Cloth;
            b.Box(new(0, bottom, front), new(0, waist + 0.04f, front), f.HipW * 0.85f, 0.005f, apron.Slot ?? S.Linen, Hips, CrowdShape.Skirt, hip);
        }
        if (look.Part(W.Belt) is { } belt) b.Box(new(0, waist - 0.015f, 0), new(0, waist + 0.015f, 0), f.HipW + Cloth, f.HipD + Cloth, belt.Slot ?? S.Accent, Spine, CrowdShape.Still, 0);
        if (look.Part(W.Neck) is { } neck)
            b.Box(new(0, armpit + 0.01f, f.ChestD), new(0, shoulder + 0.01f, f.ChestD), Neck * 1.4f, 0.01f, neck.Slot ?? S.Linen, Upper, CrowdShape.Still, 0);
        if (look.Part(W.Side) is { } arms) b.Box(new(f.HipW + 0.01f, hip - 0.28f, -0.02f), new(f.HipW + 0.01f, hip + 0.02f, -0.02f), 0.008f, 0.02f, arms.Slot ?? S.Metal, Hips, CrowdShape.Still, 0);
        Item(b, f, look.Item, wrist, shoulder);
        return b.Shape;
    }

    static void Skirt(Builder b, Frame f, Garment g, float top, float hip, double usualFlare = 1.2)
    {
        float hem = (float)Math.Clamp(g.Hem ?? 0.3, 0.01, top - 0.05), flare = (float)(g.Flare ?? usualFlare), w = Math.Max(f.HipW, f.ChestW) + Cloth, d = Math.Max(f.HipD, f.ChestD) + Cloth;
        b.Flared(new(0, hem, 0), new(0, top, 0), w * flare, d * flare, w, d, g.Slot ?? S.Outer, Hips, CrowdShape.Skirt, hip);
    }

    static void Item(Builder b, Frame f, string? item, float wrist, float shoulder)
    {
        float x = -f.ArmX, hand = wrist - HandLong / 2;
        switch (item)
        {
            case "spear":
                b.Box(new(x, 0.02f, 0.05f), new(x, 1.35f, 0.05f), Pole, Pole, S.Wood, RHand, CrowdShape.RightArm, shoulder);
                break;
            case "hoe":
                b.Box(new(x, 0.05f, 0.06f), new(x, hand + 0.25f, 0.06f), Pole, Pole, S.Wood, RHand, CrowdShape.RightArm, shoulder);
                break;
            case "cane":
                b.Box(new(x, 0, 0.04f), new(x, hand, 0.04f), Pole * 0.8f, Pole * 0.8f, S.Wood, RHand, CrowdShape.RightArm, shoulder);
                break;
            case "musket" or "rifle" or "long-gun":
                b.Box(new(x - 0.02f, shoulder - 0.55f, -0.03f), new(x - 0.02f, shoulder + (item == "long-gun" ? 0.65f : 0.5f), -0.03f), Pole, Pole * 1.4f, S.Wood, RShoulder, CrowdShape.Still, 0);
                break;
            case "basket":
                b.Box(new(f.ArmX, hand - 0.12f, 0.02f), new(f.ArmX, hand + 0.02f, 0.02f), 0.07f, 0.06f, S.Wood, LHand, CrowdShape.LeftArm, shoulder);
                break;
            case "shield":
                b.Box(new(f.ArmX + 0.03f, wrist - 0.2f, 0.04f), new(f.ArmX + 0.03f, wrist + 0.35f, 0.04f), 0.008f, 0.16f, S.Outer, LFore, CrowdShape.LeftArm, shoulder);
                break;
            case "bow":
                b.Box(new(f.ArmX, hand - 0.6f, 0.04f), new(f.ArmX, hand + 0.6f, 0.04f), 0.008f, 0.01f, S.Wood, LHand, CrowdShape.LeftArm, shoulder);
                break;
            case "bundle":
                b.Box(new(0, shoulder - 0.3f, -f.ChestD - 0.1f), new(0, shoulder + 0.02f, -f.ChestD - 0.1f), 0.11f, 0.09f, S.Linen, Chest, CrowdShape.Still, 0);
                break;
            case "sack":
                b.Box(new(f.ArmX * 0.6f, shoulder - 0.05f, -0.05f), new(f.ArmX * 0.6f, shoulder + 0.18f, -0.05f), 0.08f, 0.09f, S.Linen, LShoulder, CrowdShape.Still, 0);
                break;
            case "torch" or "lantern":
                b.Box(new(x, hand - 0.02f, 0.05f), new(x, hand + (item == "torch" ? 0.45f : 0.15f), 0.05f), 0.02f, 0.02f, S.Wood, RHand, CrowdShape.RightArm, shoulder);
                break;
            case "hammer":
                b.Box(new(x, hand - 0.32f, 0.03f), new(x, hand, 0.03f), Pole, Pole, S.Wood, RHand, CrowdShape.RightArm, shoulder);
                break;
        }
    }

    public static CrowdShape Headwear(CrowdLook look)
    {
        var f = FrameOf(look);
        var b = new Builder(look);
        float chin = (float)f.J[H.Head], w = Head * f.W / f.D + Skin, d = HeadDeep + Skin, crown = 1.0f;
        var hat = look.Part(W.Hat);
        if (look.Part(W.Hair) is { } hair && hair.Kind != "none")
        {
            string slot = hair.Slot ?? S.Hair;
            switch (hair.Kind)
            {
                case "tonsure" or "balding":
                    b.Box(new(0, chin + 0.07f, -0.005f), new(0, crown - 0.04f, -0.005f), w, d, slot, HeadBone, 0, 0);
                    break;
                case "long" or "bob" or "braid":
                    b.Box(new(0, chin - (hair.Kind == "bob" ? 0.0f : 0.08f), -d * 0.6f), new(0, crown + 0.006f, -0.01f), w, d * 0.55f, slot, HeadBone, 0, 0);
                    break;
                default:
                    b.Box(new(0, crown - 0.05f, -0.004f), new(0, crown + 0.008f, -0.004f), w, d, slot, HeadBone, 0, 0);
                    break;
            }
            if (hair.Kind is "bun" or "queue" or "topknot" or "chonmage")
                b.Box(new(0, crown - (hair.Kind == "queue" ? 0.12f : 0.01f), -d * 0.9f), new(0, crown + 0.025f, -d * 0.7f), 0.02f, 0.02f, slot, HeadBone, 0, 0);
        }
        if (look.Part(W.Beard) is { } beard)
            b.Box(new(0, chin - (beard.Kind == "full" ? 0.03f : 0.0f), d - 0.01f), new(0, chin + (beard.Kind == "moustache" ? 0.035f : 0.03f), d - 0.01f), w * 0.7f, 0.015f, beard.Slot ?? S.Hair, HeadBone, 0, 0);
        if (hat != null)
        {
            string slot = hat.Slot ?? S.Hat;
            if (Draped.Contains(hat.Kind))
            {
                b.Box(new(0, chin + 0.04f, -0.01f), new(0, crown + 0.012f, -0.01f), w + 0.008f, d + 0.006f, slot, HeadBone, 0, 0);
                b.Box(new(0, chin - 0.12f, -d * 0.7f), new(0, chin + 0.06f, -d * 0.55f), w + 0.03f, d * 0.4f, slot, HeadBone, 0, 0);
            }
            else if (Brims.Contains(hat.Kind))
            {
                float brim = hat.Kind is "kasa" or "jingasa" ? 0.2f : hat.Kind == "tricorne" ? 0.14f : 0.11f;
                b.Box(new(0, crown - 0.03f, 0), new(0, crown - 0.02f, 0), brim, brim, slot, HeadBone, 0, 0);
                b.Box(new(0, crown - 0.03f, 0), new(0, crown + (hat.Kind is "kasa" or "jingasa" ? 0.04f : 0.06f), 0), w * 0.95f, d * 0.95f, slot, HeadBone, 0, 0);
            }
            else if (Tall.Contains(hat.Kind))
            {
                float width = hat.Kind is "turban" or "bigturban" ? (hat.Kind == "bigturban" ? 1.5f : 1.25f) : 1.05f;
                b.Box(new(0, crown - 0.05f, 0), new(0, crown + (hat.Kind is "bork" ? 0.16f : 0.06f), -0.005f), w * width, d * width, slot, HeadBone, 0, 0);
            }
            else b.Box(new(0, crown - 0.04f, -0.003f), new(0, crown + 0.02f, -0.003f), w + 0.004f, d + 0.004f, slot, HeadBone, 0, 0);
        }
        return b.Shape;
    }

    public static CrowdShape Face(CrowdLook look)
    {
        var f = FrameOf(look);
        var b = new Builder(look);
        float chin = (float)f.J[H.Head], front = HeadFront + HeadDeep, across = Head * f.W / f.D;
        float eyes = chin + (1 - chin) * EyeLine, brows = eyes + BrowAbove, mouth = chin + (1 - chin) * MouthLine;
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * across * EyesApart;
            b.Box(new(x, eyes - FeatureHigh, front), new(x, eyes + FeatureHigh, front), EyeWide, FeatureDeep, S.Eye, HeadBone, CrowdShape.Still, 0);
            b.Box(new(x, brows - FeatureHigh / 2, front), new(x, brows + FeatureHigh / 2, front), BrowWide, FeatureDeep, S.Brow, HeadBone, CrowdShape.Still, 0);
        }
        b.Box(new(0, mouth - FeatureHigh / 2, front), new(0, mouth + FeatureHigh / 2, front), MouthWide, FeatureDeep, S.Lip, HeadBone, CrowdShape.Still, 0);
        return b.Shape;
    }

    public static (Rig Rig, CrowdShape Shape) Whole(CrowdLook look)
    {
        var body = Body(look);
        var head = Headwear(look);
        var shape = Merged(Merged(body, head), Face(look), WholeColours);
        float scale = (float)look.HeightM;
        var f = FrameOf(look);
        var joints = BoneSpots(f);
        var bones = Bones.Select((name, i) => new RigBone(name, Parents[i], (Parents[i] < 0 ? joints[i] : joints[i] - joints[Parents[i]]) * scale, Quaternion.Identity, 1)).ToList();
        int n = shape.Points.Count;
        var jointIndex = new ushort[n * Rig.Influences];
        var weights = new float[n * Rig.Influences];
        for (int v = 0; v < n; v++)
        {
            jointIndex[v * Rig.Influences] = (ushort)shape.Bone[v];
            weights[v * Rig.Influences] = 1;
        }
        var rig = new Rig
        {
            Bones = bones, Points = shape.Points.Select(p => p * scale).ToArray(), Normals = shape.Normals.ToArray(), Coords = new Vector2[n],
            Joints = jointIndex, Weights = weights, Triangles = shape.Triangles.ToArray(),
        };
        foreach (var (role, bone) in H.Map(bones)) rig.Humanoid[role] = bone;
        return (rig, shape);
    }

    static Vector3[] BoneSpots(Frame f)
    {
        float Y(string role) => (float)f.J[role];
        float shoulder = Y(H.UpperArm);
        return
        [
            new(0, Y(H.Hips), 0), new(0, Y(H.Spine), 0), new(0, Y(H.Chest), 0), new(0, Y(H.UpperChest), 0), new(0, Y(H.Neck), 0), new(0, Y(H.Head), 0),
            new(f.LegX, Y(H.UpperLeg), 0), new(f.LegX, Y(H.LowerLeg), 0), new(f.LegX, Y(H.Foot), 0), new(f.LegX, 0.01f, FootLong),
            new(-f.LegX, Y(H.UpperLeg), 0), new(-f.LegX, Y(H.LowerLeg), 0), new(-f.LegX, Y(H.Foot), 0), new(-f.LegX, 0.01f, FootLong),
            new(f.ArmX * 0.35f, shoulder, 0), new(f.ArmX, shoulder, 0), new(f.ArmX, Y(H.LowerArm), 0), new(f.ArmX, Y(H.Hand), 0),
            new(-f.ArmX * 0.35f, shoulder, 0), new(-f.ArmX, shoulder, 0), new(-f.ArmX, Y(H.LowerArm), 0), new(-f.ArmX, Y(H.Hand), 0),
        ];
    }

    static CrowdShape Merged(CrowdShape a, CrowdShape b, int most = CrowdShape.MostColours)
    {
        var m = new CrowdShape();
        foreach (var s in new[] { a, b })
        {
            int first = m.Points.Count;
            m.Points.AddRange(s.Points);
            m.Normals.AddRange(s.Normals);
            m.Bone.AddRange(s.Bone);
            m.Limb.AddRange(s.Limb);
            m.Pivot.AddRange(s.Pivot);
            m.Triangles.AddRange(s.Triangles.Select(t => t + first));
            foreach (byte c in s.Colour)
            {
                string slot = s.Palette[c];
                int at = m.Palette.IndexOf(slot);
                if (at < 0 && m.Palette.Count < most) m.Palette.Add(slot);
                m.Colour.Add((byte)Math.Max(0, m.Palette.IndexOf(slot)));
            }
        }
        return m;
    }

    static string SlotOf(CrowdLook look, string part, string fallback) =>
        look.Part(part) is { } g ? (g.Kind == "bare" ? S.Skin : g.Slot ?? fallback) : fallback;

    public static IReadOnlyList<string> Colours(CrowdShape shape, CrowdLook look) =>
        shape.Palette.Select(slot => look.Colours.GetValueOrDefault(slot) ?? "#808080").ToList();

    sealed class Builder(CrowdLook look)
    {
        public readonly CrowdShape Shape = new();

        public void Box(Vector3 low, Vector3 high, float w, float d, string slot, int bone, byte limb, float pivot) => Flared(low, high, w, d, w, d, slot, bone, limb, pivot);

        public void Flared(Vector3 low, Vector3 high, float wLow, float dLow, float wHigh, float dHigh, string slot, int bone, byte limb, float pivot)
        {
            byte colour = Colour(slot);
            Vector3 P(int x, int y, int z) => y == 0 ? new(low.X + x * wLow, low.Y, low.Z + z * dLow) : new(high.X + x * wHigh, high.Y, high.Z + z * dHigh);
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 e)
            {
                var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                int first = Shape.Points.Count;
                foreach (var p in new[] { a, b, c, e })
                {
                    Shape.Points.Add(p);
                    Shape.Normals.Add(n);
                    Shape.Colour.Add(colour);
                    Shape.Bone.Add(bone);
                    Shape.Limb.Add(limb);
                    Shape.Pivot.Add(pivot);
                }
                Shape.Triangles.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            }
            Face(P(-1, 0, 1), P(1, 0, 1), P(1, 1, 1), P(-1, 1, 1));
            Face(P(1, 0, -1), P(-1, 0, -1), P(-1, 1, -1), P(1, 1, -1));
            Face(P(-1, 0, -1), P(-1, 0, 1), P(-1, 1, 1), P(-1, 1, -1));
            Face(P(1, 0, 1), P(1, 0, -1), P(1, 1, -1), P(1, 1, 1));
            Face(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
            Face(P(-1, 0, -1), P(1, 0, -1), P(1, 0, 1), P(-1, 0, 1));
        }

        byte Colour(string slot)
        {
            int at = Shape.Palette.IndexOf(slot);
            if (at >= 0) return (byte)at;
            if (Shape.Palette.Count < CrowdShape.MostColours)
            {
                Shape.Palette.Add(slot);
                return (byte)(Shape.Palette.Count - 1);
            }
            return (byte)Math.Max(0, Shape.Palette.IndexOf(look.Colours.ContainsKey(S.Outer) ? S.Outer : Shape.Palette[0]));
        }
    }
}
