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
        [new(H.Left + role, swing, roll, 0, beat, lag), new(H.Right + role, swing, -roll, 0, beat, lag)];

    static Bend[] Side(string side, string role, float swing, float roll = 0, float beat = 0, float lag = 0) =>
        [new(side + role, swing, side == H.Left ? roll : -roll, 0, beat, lag)];

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

    public static IReadOnlyList<(string Id, string Name, bool Whole)> Acts { get; } = Rows.Select(r => (r.Id, r.Name, r.Whole)).ToList();

    public static bool KnownAct(string? act) => act == null || Rows.Any(r => r.Id == act);

    public static bool WholeAct(string? act) => Rows.Any(r => r.Id == act && r.Whole);

    ActRow? _act;
    float _actPhase;
    readonly Dictionary<string, RigClip?> _actClips = new();

    RigClip? ActClip(string act) => _actClips.TryGetValue(act, out var clip) ? clip : _actClips[act] = _rig.Clip(act, aside: false);

    float Acted(ActRow act, Dictionary<int, Quaternion> own, Action<string, Quaternion> turn)
    {
        if (!act.Whole)
            foreach (var side in new[] { H.Left, H.Right })
                foreach (var role in ArmRoles)
                    if (Bone(side + role) is { } b)
                    {
                        own.Remove(b);
                        if (_relax.TryGetValue(side + role, out var relaxed)) turn(side + role, relaxed);
                    }
        foreach (var bend in act.Bends)
        {
            float beat = bend.Beat * MathF.Sin((_actPhase - bend.Lag) * Tau);
            turn(bend.Role, Swing(bend.Swing + beat) * Roll(bend.Roll) * Spin(bend.Spin));
        }
        return act.Drop * _height;
    }
}
