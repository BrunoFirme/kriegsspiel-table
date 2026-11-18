using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class SelectClickTests
{
    const string U = Layers.Umpire;
    const float CloseView = 9;
    const double Still = 1e-6;

    static Vector2 Screen(OfflineTable t, Vec2 p) => t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(p));

    static async Task<bool> Drag(TestRunner r, OfflineTable t, Vec2 from, Vec2 to, bool ctrl)
    {
        const int Steps = 6;
        bool held = false;
        Vector2 a = Screen(t, from), b = Screen(t, to);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = a, GlobalPosition = a });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a, CtrlPressed = ctrl, ButtonMask = MouseButtonMask.Left });
        await r.Frame();
        held |= t.Table.Hand.Holding;
        for (int i = 1; i <= Steps; i++)
        {
            var at = a.Lerp(b, i / (float)Steps);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (b - a) / Steps, ButtonMask = MouseButtonMask.Left, CtrlPressed = ctrl });
            await r.Frame();
            held |= t.Table.Hand.Holding;
        }
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b, CtrlPressed = ctrl });
        for (int i = 0; i < 120 && (t.Table.Hand.Holding || t.Table.Hand.Marching); i++) await r.Frame();
        await r.Frames(3);
        return held;
    }

    static Vec2 At(OfflineTable t, string id) => t.S.View.Find(U, id)!.Pos;

    static string Picked(OfflineTable t) => string.Join(", ", t.Table.Selection.Ids.OrderBy(id => id));

    [NodeTest]
    static async Task A_ctrl_click_only_toggles_a_piece_in_or_out_of_the_selection_even_dragged_while_a_plain_drag_still_picks_it_up(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(1.5, 0)), CloseView);
        await r.Frames(2);
        t.Table.SetTool(Tool.Select);
        Vec2 one = At(t, "p1"), two = At(t, "p2");
        bool held = await Drag(r, t, one, new Vec2(one.X, one.Z + 2), ctrl: true);
        Check.Equal("p1", Picked(t), "a ctrl-drag on a piece puts it in the selection");
        Check.That(!held && (At(t, "p1") - one).Length < Still, $"and never picks it up, leaving it where it stood: {At(t, "p1")}");
        held = await Drag(r, t, two, new Vec2(two.X, two.Z + 2), ctrl: true);
        Check.Equal("p1, p2", Picked(t), "a second ctrl-drag adds the other piece");
        Check.That(!held && (At(t, "p1") - one).Length < Still && (At(t, "p2") - two).Length < Still, $"and neither is carried: {At(t, "p1")}, {At(t, "p2")}");
        held = await Drag(r, t, one, new Vec2(one.X, one.Z - 2), ctrl: true);
        Check.Equal("p2", Picked(t), "a ctrl-drag on a picked piece takes it out");
        Check.That(!held && (At(t, "p1") - one).Length < Still, $"still without carrying it: {At(t, "p1")}");
        Check.That(await Drag(r, t, two, new Vec2(two.X, two.Z + 2), ctrl: false), "a plain drag still picks the piece up to carry it");
    }
}
