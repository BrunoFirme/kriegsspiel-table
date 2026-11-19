using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class CameraPanTests
{
    const float Flattest = -0.12f, Size = 20, FarAway = 20, GrabReach = 3, Snug = 1e-3f;

    static async Task<CameraRig> LowDown(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        var rig = t.Table.Rig;
        rig.Frame(Vector3.Zero, Size);
        rig.Pitch = Flattest;
        rig.Apply();
        await r.Frames(2);
        return rig;
    }

    [NodeTest]
    static async Task A_drag_by_the_horizon_pans_no_faster_than_ground_three_times_as_far_as_the_middle(TestRunner r)
    {
        var rig = await LowDown(r);
        var ahead = (-rig.Cam.GlobalTransform.Basis.Z with { Y = 0 }).Normalized();
        var from = rig.Screen(rig.Target + ahead * rig.Distance * FarAway);
        var to = from + Vector2.Down;
        var before = rig.Target;
        float most = GrabReach * rig.MiddleStep(to - from);
        var jump = rig.PlaneAt(from, before.Y)!.Value - rig.PlaneAt(to, before.Y)!.Value;
        Check.That(jump.Length() > 10 * most, $"a pixel by the horizon spans far ground: {jump.Length():0.0} against {most:0.000}");
        var step = rig.PanScreen(from, to);
        Check.That(step is { } s && s.Length() > 0 && s.Length() <= most * (1 + Snug), $"the pan is held to {most:0.000}: {step}");
        Check.That(((rig.Target - before) with { Y = 0 }).Length() <= most * (1 + Snug), $"the camera stays put: moved {(rig.Target - before).Length():0.000}");
    }

    [NodeTest]
    static async Task A_drag_near_the_middle_keeps_the_ground_under_the_pointer(TestRunner r)
    {
        var rig = await LowDown(r);
        var from = rig.Screen(rig.Target);
        var to = from + new Vector2(3, 2);
        var grabbed = rig.PlaneAt(from, rig.Target.Y)!.Value;
        rig.PanScreen(from, to);
        var under = rig.PlaneAt(to, rig.Target.Y)!.Value;
        Check.That((under - grabbed).Length() < Snug * rig.Distance, $"the grabbed ground follows the pointer: {grabbed} now under {under}");
    }
}
