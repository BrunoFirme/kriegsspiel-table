using System.Numerics;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public sealed partial class RigMotion
{
    public const string Sit = "sit", Lie = "lie", Kneel = "kneel", Cower = "cower", Brace = "brace", Work = "work";
    public const string Melee = "melee", Ranged = "ranged", Throw = "throw", Shove = "shove", Cheer = "cheer", Yield = "yield", Guard = "guard", Carry = "carry";

    sealed record Bend(string Role, float Swing, float Roll = 0, float Spin = 0, float Beat = 0, float Lag = 0);

    sealed record ActRow(string Id, string Name, bool Whole, float Drop, float Rate, Bend[] Bends);

    static readonly string[] ArmRoles = [H.Shoulder, H.UpperArm, H.LowerArm, H.Hand];

    static Bend[] Both(string role, float swing, float roll = 0, float beat = 0, float lag = 0) =>
        [new(Of(H.Left, role), swing, roll, 0, beat, lag), new(Of(H.Right, role), swing, -roll, 0, beat, lag)];

    static Bend[] Side(string side, string role, float swing, float roll = 0, float beat = 0, float lag = 0) =>
        [new(Of(side, role), swing, side == H.Left ? roll : -roll, 0, beat, lag)];

    static readonly ActRow[] Rows =
    [
        new(Sit, "Sit", true, -0.25f, 0, [.. Both(H.UpperLeg, 88), .. Both(H.LowerLeg, -88), .. Both(H.Foot, 0), new(H.Spine, -4), .. Both(H.UpperArm, 18, 6), .. Both(H.LowerArm, 40)]),
        new(Lie, "Lie", true, -0.52f, 0, [new(H.Hips, 90), .. Both(H.UpperArm, 90, 8), .. Both(H.LowerArm, 6), .. Both(H.LowerLeg, -4)]),
        new(Kneel, "Kneel", true, -0.24f, 0,
            [.. Side(H.Left, H.UpperLeg, 85), .. Side(H.Left, H.LowerLeg, -88), .. Side(H.Right, H.UpperLeg, -8), .. Side(H.Right, H.LowerLeg, -95), .. Side(H.Right, H.Foot, -40),
                new(H.Spine, -6), .. Both(H.UpperArm, 10, 6), .. Both(H.LowerArm, 30)]),
        new(Cower, "Cower", true, -0.33f, 1.6f, [.. Both(H.UpperLeg, 100), .. Both(H.LowerLeg, -130), .. Both(H.Foot, 30), new(H.Spine, -35, 0, 0, 3), new(H.Chest, -15), new(H.Head, -20),
            .. Both(H.UpperArm, 150, -20), .. Both(H.LowerArm, 110)]),
        new(Brace, "Brace", true, -0.05f, 0, [.. Side(H.Left, H.UpperLeg, 28), .. Side(H.Left, H.LowerLeg, -30), .. Side(H.Right, H.UpperLeg, -18), .. Side(H.Right, H.LowerLeg, -8),
            new(H.Spine, -14), .. Both(H.UpperArm, 45, 8), .. Both(H.LowerArm, 35)]),
        new(Work, "Work", true, -0.03f, 0.8f, [new(H.Spine, -28, 0, 0, 6), .. Both(H.UpperLeg, 10), .. Both(H.LowerLeg, -14), .. Both(H.UpperArm, 20, 0, 25), .. Both(H.LowerArm, 30, 0, 12)]),
        new(Melee, "Strike", false, 0, 1.1f, [.. Side(H.Right, H.UpperArm, 75, 10, 65), .. Side(H.Right, H.LowerArm, 35, 0, 35, 0.15f), .. Side(H.Left, H.UpperArm, 35, 8), .. Side(H.Left, H.LowerArm, 60)]),
        new(Ranged, "Aim", false, 0, 0, [.. Side(H.Right, H.UpperArm, 75, -10), .. Side(H.Right, H.LowerArm, 25), .. Side(H.Left, H.UpperArm, 85, -5), .. Side(H.Left, H.LowerArm, 10)]),
        new(Throw, "Throw", false, 0, 0.6f, [.. Side(H.Right, H.UpperArm, 120, 15, 70), .. Side(H.Right, H.LowerArm, 40, 0, 30, 0.2f), .. Side(H.Left, H.UpperArm, 50, 10)]),
        new(Shove, "Shove", false, 0, 1.2f, [.. Both(H.UpperArm, 75, 4, 10), .. Both(H.LowerArm, 12, 0, 10)]),
        new(Cheer, "Cheer", false, 0, 2.5f, [.. Both(H.UpperArm, 160, 20, 14), .. Both(H.LowerArm, 12, 0, 10, 0.25f)]),
        new(Yield, "Hands up", false, 0, 0, [.. Both(H.UpperArm, 10, 80), .. Both(H.LowerArm, 0, 90)]),
        new(Guard, "Guard", false, 0, 0, [.. Side(H.Right, H.UpperArm, 30, 8), .. Side(H.Right, H.LowerArm, 75), .. Side(H.Left, H.UpperArm, 40, 6), .. Side(H.Left, H.LowerArm, 70)]),
        new(Carry, "Carry", false, 0, 0, [.. Both(H.UpperArm, 20, -4), .. Both(H.LowerArm, 65)]),
    ];

    public const char Joined = '+';
    const int MostJoined = 256;

    public static IReadOnlyList<(string Id, string Name, bool Whole)> Acts { get; } = Rows.Select(r => (r.Id, r.Name, r.Whole)).ToList();

    static readonly HashSet<string> WholeIds = Rows.Where(r => r.Whole).Select(r => r.Id).ToHashSet(), ArmIds = Rows.Where(r => !r.Whole).Select(r => r.Id).ToHashSet();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string?, string?)> JoinedParts = new();

    public static (string? Whole, string? Arm) Parts(string? act)
    {
        if (act == null) return (null, null);
        if (JoinedParts.TryGetValue(act, out var known)) return known;
        int join = act.IndexOf(Joined);
        if (join < 0) return WholeIds.Contains(act) ? (act, null) : (null, act);
        var parts = (act[..join], act[(join + 1)..]);
        if (JoinedParts.Count < MostJoined) JoinedParts[act] = parts;
        return parts;
    }

    public static string? Combined(string? whole, string? arm) => whole == null ? arm : arm == null ? whole : whole + Joined + arm;

    public static bool KnownAct(string? act)
    {
        if (act == null) return true;
        var (whole, arm) = Parts(act);
        return (whole == null || WholeIds.Contains(whole)) && (arm == null || ArmIds.Contains(arm)) && (whole ?? arm) != null;
    }

    public static bool WholeAct(string? act) => KnownAct(act) && Parts(act).Whole != null;

    public static string NameOf(string? act)
    {
        var (whole, arm) = Parts(act);
        string? Named(string? id) => id == null ? null : Rows.FirstOrDefault(r => r.Id == id)?.Name ?? id;
        return string.Join(" · ", new[] { Named(whole), Named(arm) }.OfType<string>().DefaultIfEmpty("None"));
    }

    static readonly IReadOnlyDictionary<int, Bend[]> Holding = new Dictionary<int, Bend[]>
    {
        [CrowdHolds.Spear] = [.. Side(H.Right, H.UpperArm, 0), .. Side(H.Right, H.LowerArm, 3)],
        [CrowdHolds.Gun] = [.. Side(H.Left, H.UpperArm, 5), .. Side(H.Left, H.LowerArm, 32)],
        [CrowdHolds.Shield] = [.. Side(H.Right, H.UpperArm, 0), .. Side(H.Right, H.LowerArm, 3), .. Side(H.Left, H.UpperArm, 6), .. Side(H.Left, H.LowerArm, 52)],
        [CrowdHolds.Hoe] = [.. Side(H.Right, H.UpperArm, 9), .. Side(H.Right, H.LowerArm, 126)],
        [CrowdHolds.Cloak] = [.. Both(H.UpperArm, 0), .. Both(H.LowerArm, 17)],
    };

    static readonly Bend[] SpearThrust = [.. Side(H.Right, H.UpperArm, 70), .. Side(H.Right, H.LowerArm, 10), .. Side(H.Left, H.UpperArm, 45), .. Side(H.Left, H.LowerArm, 50)];

    static readonly Bend[] SpearBrace = [.. Side(H.Right, H.UpperArm, -46), .. Side(H.Right, H.LowerArm, 6), .. Side(H.Left, H.UpperArm, 63), .. Side(H.Left, H.LowerArm, 40)];

    ActRow? _whole, _arm;
    float _wholePhase, _armPhase;
    int _hold;

    void HeldThrough(ActRow arm, Dictionary<int, Quaternion> own, Action<string, Quaternion> turn)
    {
        if (arm.Id is not (Melee or Guard or Shove)) return;
        if (_hold == CrowdHolds.Spear && arm.Id == Melee) Lock(SpearThrust, own, turn);
        else if (_hold is CrowdHolds.Spear or CrowdHolds.Gun or CrowdHolds.Shield && Holding.TryGetValue(_hold, out var held))
            Lock(HeldSide(_hold, held), own, turn);
    }

    void Lock(Bend[] bends, Dictionary<int, Quaternion> own, Action<string, Quaternion> turn)
    {
        for (int i = 0; i < bends.Length; i++)
        {
            string role = bends[i].Role;
            bool again = false;
            for (int j = 0; j < i && !again; j++) again = bends[j].Role == role;
            if (again || Bone(role) is not { } b) continue;
            own.Remove(b);
            if (_relax.TryGetValue(role, out var relaxed)) turn(role, relaxed);
        }
        foreach (var bend in bends) turn(bend.Role, Swing(bend.Swing) * Roll(bend.Roll));
    }
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Bend[]> HeldSides = new();

    static Bend[] HeldSide(int hold, Bend[] held) =>
        HeldSides.GetOrAdd(hold, h => held.Where(b => b.Role.StartsWith(h == CrowdHolds.Spear ? H.Right : H.Left, StringComparison.Ordinal)).ToArray());

    readonly Dictionary<string, RigClip?> _actClips = new();

    RigClip? ActClip(string act) => _actClips.TryGetValue(act, out var clip) ? clip : _actClips[act] = _rig.Clip(act, aside: false);

    float Acted(ActRow act, float phase, Dictionary<int, Quaternion> own, Action<string, Quaternion> turn)
    {
        if (!act.Whole)
            foreach (var side in SideNames)
                foreach (var role in ArmRoles)
                    if (Bone(Of(side, role)) is { } b)
                    {
                        own.Remove(b);
                        if (_relax.TryGetValue(Of(side, role), out var relaxed)) turn(Of(side, role), relaxed);
                    }
        foreach (var bend in act.Bends)
        {
            float beat = bend.Beat * MathF.Sin((phase - bend.Lag) * Tau);
            turn(bend.Role, Swing(bend.Swing + beat) * Roll(bend.Roll) * Spin(bend.Spin));
        }
        return act.Drop * _height;
    }
}
