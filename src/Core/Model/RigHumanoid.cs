using System.Text;

namespace KTable.Core.Model;

public static class RigHumanoid
{
    public const string Hips = "hips", Spine = "spine", Chest = "chest", UpperChest = "upper-chest", Neck = "neck", Head = "head";
    public const string Shoulder = "shoulder", UpperArm = "upper-arm", LowerArm = "lower-arm", Hand = "hand";
    public const string UpperLeg = "upper-leg", LowerLeg = "lower-leg", Foot = "foot", Toes = "toes";
    public const string Left = "left-", Right = "right-";

    static readonly string[] Needed = [Hips, Spine, Head, Left + UpperLeg, Left + LowerLeg, Left + Foot, Right + UpperLeg, Right + LowerLeg, Right + Foot];
    static readonly string[] Prefixes = ["mixamorig", "j", "bip", "bip01", "bip001", "c", "def", "org", "cc", "base", "armature"];
    static readonly string[] Helpers = ["twist", "roll", "helper", "end", "nub", "ik", "pole", "target", "tip", "adj", "corrective", "jiggle", "breast", "bust", "hair",
        "skirt", "cloth", "tail", "ear", "eye", "jaw", "tongue", "teeth", "brow", "cheek", "lip", "sec", "headtop"];
    static readonly string[] Fingers = ["thumb", "index", "middle", "ring", "little", "pinky", "finger"];
    const int ShortWord = 4;

    readonly record struct Named(string Joined, int Side, bool Helper, bool Finger);

    public static Dictionary<string, int> Map(IReadOnlyList<RigBone> bones)
    {
        var map = new Dictionary<string, int>();
        var words = bones.Select(b => Words(b.Name)).ToList();
        int Depth(int i)
        {
            int d = 0;
            for (int p = bones[i].Parent; p >= 0; p = bones[p].Parent) d++;
            return d;
        }
        var order = Enumerable.Range(0, bones.Count).OrderBy(Depth).ThenBy(i => i).ToList();
        var centre = order.Where(i => words[i].Side == 0 && !words[i].Helper && !words[i].Finger).ToList();
        int hips = centre.FirstOrDefault(i => words[i].Joined is "hips" or "pelvis" or "hip", -1);
        if (hips < 0) return map;
        map[Hips] = hips;
        var inside = centre.Where(i => Below(bones, i, hips)).ToList();
        var trunk = inside.Where(i => words[i].Joined.StartsWith("spine", StringComparison.Ordinal) || words[i].Joined is "chest" or "upperchest").ToList();
        if (trunk.Count > 0) map[Spine] = trunk[0];
        if (trunk.Count > 1) map[Chest] = trunk[1];
        if (trunk.Count > 2) map[UpperChest] = trunk[^1];
        if (inside.FirstOrDefault(i => words[i].Joined == "neck", -1) is >= 0 and var neck) map[Neck] = neck;
        if (inside.FirstOrDefault(i => words[i].Joined == "head", -1) is >= 0 and var head) map[Head] = head;
        foreach (var (side, sign) in new[] { (Left, -1), (Right, 1) })
        {
            var limb = order.Where(i => words[i].Side == sign && !words[i].Helper && !words[i].Finger && Below(bones, i, hips)).ToList();
            foreach (var i in limb)
            {
                var w = words[i].Joined;
                var role = IsLeg(w) ? LegPart(w, map.ContainsKey(side + UpperLeg)) : IsArm(w) ? ArmPart(w, map.ContainsKey(side + UpperArm)) : null;
                if (role != null) map.TryAdd(side + role, i);
            }
        }
        return map;
    }

    public static bool Walks(IReadOnlyDictionary<string, int> map) => Needed.All(map.ContainsKey);

    static bool Below(IReadOnlyList<RigBone> bones, int i, int ancestor)
    {
        for (int p = bones[i].Parent; p >= 0; p = bones[p].Parent)
            if (p == ancestor) return true;
        return false;
    }

    static bool IsLeg(string w) => w.Contains("leg") || w.Contains("thigh") || w.Contains("knee") || w.Contains("calf") || w.Contains("shin")
        || w.Contains("foot") || w.Contains("ankle") || w.Contains("toe") || w == "ball";

    static bool IsArm(string w) => w.Contains("arm") || w.Contains("shoulder") || w.Contains("clavicle") || w.Contains("collar") || w.Contains("elbow")
        || w.Contains("hand") || w.Contains("wrist");

    static string? LegPart(string w, bool upperTaken) =>
        w.Contains("toe") || w == "ball" ? Toes
        : w.Contains("foot") || w.Contains("ankle") ? Foot
        : w.Contains("knee") || w.Contains("calf") || w.Contains("shin") || w.Contains("lowerleg") || w.Contains("lowleg") ? LowerLeg
        : w.Contains("upleg") || w.Contains("upperleg") || w.Contains("thigh") ? UpperLeg
        : w.Contains("leg") ? (upperTaken ? LowerLeg : UpperLeg)
        : null;

    static string? ArmPart(string w, bool upperTaken) =>
        w.Contains("shoulder") || w.Contains("clavicle") || w.Contains("collar") ? Shoulder
        : w.Contains("hand") || w.Contains("wrist") ? Hand
        : w.Contains("elbow") || w.Contains("forearm") || w.Contains("lowerarm") || w.Contains("lowarm") ? LowerArm
        : w.Contains("upperarm") || w.Contains("uparm") ? UpperArm
        : w.Contains("arm") ? (upperTaken ? LowerArm : UpperArm)
        : null;

    static Named Words(string name)
    {
        var spaced = new StringBuilder();
        char prev = ' ';
        foreach (var ch in name)
        {
            if (char.IsUpper(ch) && char.IsLower(prev)) spaced.Append(' ');
            spaced.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
            prev = ch;
        }
        int side = 0;
        var kept = new List<string>();
        foreach (var t in spaced.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (t is "l" or "left" or "lft") side = -1;
            else if (t is "r" or "right" or "rgt") side = 1;
            else if (t.StartsWith("left", StringComparison.Ordinal)) { side = -1; kept.Add(t["left".Length..]); }
            else if (t.StartsWith("right", StringComparison.Ordinal)) { side = 1; kept.Add(t["right".Length..]); }
            else kept.Add(t);
        }
        while (kept.Count > 1 && (Prefixes.Contains(kept[0]) || kept[0].StartsWith("mixamorig", StringComparison.Ordinal))) kept.RemoveAt(0);
        bool helper = kept.Any(t => Helpers.Any(h => t == h || h.Length >= ShortWord && t.StartsWith(h, StringComparison.Ordinal)));
        bool finger = kept.Any(t => Fingers.Any(f => t.StartsWith(f, StringComparison.Ordinal)));
        var raw = string.Concat(kept);
        var joined = raw.StartsWith("spine", StringComparison.Ordinal) ? raw : raw.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return new Named(joined.Length > 0 ? joined : raw, side, helper, finger);
    }
}
