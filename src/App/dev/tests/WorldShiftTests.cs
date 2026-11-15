using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class WorldShiftTests
{
    [NodeTest]
    static async Task A_close_camera_moves_the_drawn_world_near_its_eye_and_still_reads_table_places_and_a_far_camera_puts_it_back(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(3);
        var rig = t.Table.Rig;
        try
        {
            rig.MinDistance = CameraRig.LifeSizeMinDistance;
            var target = new Vector3(40.3f, 0, -30.2f);
            rig.Show(new CameraRig.View(target, 0.5f, rig.Yaw, -0.9f, false));
            await r.Frames(2);
            Check.Equal(new Vector3(40, 0, -30), WorldShift.By, "a close camera shifts the drawn world to the whole table unit under its target");
            Check.That(t.Table.Position.IsEqualApprox(-WorldShift.By), $"the table is drawn shifted: {t.Table.Position}");
            Check.That(rig.Cam.GlobalPosition.Length() < 2, $"the eye is drawn near the origin: {rig.Cam.GlobalPosition}");
            var centre = rig.GetViewport().GetVisibleRect().Size / 2;
            var seen = rig.PlaneAt(centre, rig.Target.Y) ?? throw new TestFailure("the screen's middle meets the table");
            Check.That(new Vector2(seen.X - target.X, seen.Z - target.Z).Length() < 0.01f, $"the middle of the screen is still read as the table place looked at: {seen}");
            Check.That(rig.Screen(rig.Target).DistanceTo(centre) < 2, $"the table place looked at is drawn in the middle of the screen: {rig.Screen(rig.Target)}");
            rig.Show(new CameraRig.View(target, 30, rig.Yaw, -0.9f, false));
            await r.Frames(2);
            Check.Equal(Vector3.Zero, WorldShift.By, "a far camera puts the drawn world back");
            Check.That(t.Table.Position.IsEqualApprox(Vector3.Zero), $"the table is drawn in place: {t.Table.Position}");
        }
        finally
        {
            WorldShift.Set(Vector3.Zero);
            t.Table.QueueFree();
        }
    }
}
