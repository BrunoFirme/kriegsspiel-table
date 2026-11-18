using Godot;

namespace KTable.App;

public readonly record struct AvatarPose(Vector3 At, float Yaw, float Height, string? Ride = null, string? Body = null, string? Gait = null, string? Act = null)
{
    public const int LongestName = 64, LongestRide = 128, NoColour = -1;

    public static float UnitsPerMetreOf(float height) => height / PersonMetres;
    public const float Smallest = 0.0005f, Largest = 50f, Usual = 1.2f, PersonMetres = 1.8f;

    public static float UnitsPerMetre(KTable.Core.Model.GameState g) => g.Calibration.UnitsPerMeter is > 0 and var u ? (float)u : Usual / PersonMetres;

    public bool IsSound => At.IsFinite() && float.IsFinite(Yaw) && Height >= Smallest && Height <= Largest && (Ride == null || Ride.Length <= LongestRide)
        && (Body == null || Body.Length <= LongestRide)
        && KTable.Core.Model.RigMotion.Known(Gait) && KTable.Core.Model.RigMotion.KnownAct(Act);

    public static string? RideOf(string wire) => wire.Length == 0 ? null : wire;
}
