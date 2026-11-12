using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class HoverPassTests
{
    const string U = Layers.Umpire;
    static readonly Vec2 OffThePieces = new(9, 7);

    static Vector2 Screen(OfflineTable t, Vec2 p) => t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(p));

    static bool FaceDown(OfflineTable t, string id) => t.S.View.Find(U, id)!.FaceDown;

    static async Task PressF(TestRunner r, OfflineTable t)
    {
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.F, Pressed = true });
        await r.Frames(2);
    }

    [NodeTest]
    static async Task F_turns_over_the_piece_under_the_pointer_and_the_selection_when_the_pointer_is_on_it_or_on_nothing(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frames(2);
        t.Table.Selection.Set(new[] { "p3" });
        t.Table.Hover.Update(Screen(t, t.S.View.Find(U, "p1")!.Pos), 0);
        Check.That(t.Table.Hover.Node == t.Node("p1"), "the pointer is over the first piece");
        await PressF(r, t);
        Check.That(FaceDown(t, "p1") && !FaceDown(t, "p3"), "F turns over the piece under the pointer and leaves the selected one elsewhere alone");
        t.Table.Hover.Update(Screen(t, t.S.View.Find(U, "p3")!.Pos), 0);
        await PressF(r, t);
        Check.That(FaceDown(t, "p3"), "with the pointer on a selected piece, F turns the selection over");
        t.Table.Hover.Update(Screen(t, OffThePieces), 0);
        Check.That(t.Table.Hover.Node == null, "the pointer is over the bare table");
        await PressF(r, t);
        Check.That(!FaceDown(t, "p3") && FaceDown(t, "p1"), "with the pointer on nothing, F turns the selection over");
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task With_the_setting_on_pointing_at_an_attached_piece_lights_the_piece_it_rides_on_too(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frames(2);
        t.S.Do(new Attach { Layer = U, Parent = "p3", Child = "p1" });
        await r.Frames(2);
        Check.That(t.S.View.Find(U, "p3")!.Attached.Contains("p1"), "the first piece rides on the third");
        var over = Screen(t, t.S.View.Find(U, "p1")!.Pos);
        t.Table.Hover.Update(over, 0);
        Check.That(t.Node("p1").Hovered && !t.Node("p3").Hovered, "with the setting off, only the piece under the pointer is lit");
        t.Table.Hover.Clear(over);
        t.Table.Settings.HoverLightsCarrier = true;
        t.Table.Hover.Update(over, 0);
        Check.That(t.Node("p1").Hovered && t.Node("p3").Hovered && !t.Node("p2").Hovered, "with it on, the piece it rides on is lit too, and no other");
        t.Table.Hover.Clear(over);
        Check.That(!t.Node("p1").Hovered && !t.Node("p3").Hovered, "and both go dark when the pointer leaves");
        t.Table.Settings.HoverLightsCarrier = false;
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_held_piece_rises_as_high_as_the_lift_setting_says(TestRunner r)
    {
        const float Low = 0.6f, High = 5f;
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        float usual = t.Table.Settings.LiftHeight;
        var at = t.S.View.Find(U, "p1")!.Pos;
        t.Table.Selection.Set(new[] { "p1" });
        async Task<float> Lifted(float lift)
        {
            t.Table.Settings.LiftHeight = lift;
            t.Table.Hand.Lift(at);
            await r.Frames(2);
            float y = t.Table.Hand.At.Y;
            t.Table.Hand.Drop();
            await r.Seconds(0.5);
            return y;
        }
        float low = await Lifted(Low), high = await Lifted(High);
        Check.That(Mathf.Abs(high - low - (High - Low)) < 0.01f, $"the hand holds the piece {High - Low} higher at {High} than at {Low}: {low} and {high}");
        t.Table.Settings.LiftHeight = 100;
        Check.That(await Lifted(100) < low + Settings.LiftMost, "a setting out of range is held to the most");
        t.Table.Settings.LiftHeight = usual;
        t.Table.QueueFree();
    }
}
