using Godot;

namespace KTable.App;

public readonly record struct AvatarPose(Vector3 At, float Yaw, float Height)
{
    public const int LongestName = 64;
    public const float Smallest = 0.05f, Largest = 50f, Usual = 1.2f;

    public bool IsSound => At.IsFinite() && float.IsFinite(Yaw) && Height >= Smallest && Height <= Largest;
}
