using Godot;

namespace KTable.App;

public readonly record struct AvatarPose(Vector3 At, float Yaw, float Height, string? Ride = null)
{
    public const int LongestName = 64, LongestRide = 128;
    public const float Smallest = 0.05f, Largest = 50f, Usual = 1.2f;

    public bool IsSound => At.IsFinite() && float.IsFinite(Yaw) && Height >= Smallest && Height <= Largest && (Ride == null || Ride.Length <= LongestRide);

    public static string? RideOf(string wire) => wire.Length == 0 ? null : wire;
}
