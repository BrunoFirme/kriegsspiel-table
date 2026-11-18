using System;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class HoverWheelTests
{
    const string U = Layers.Umpire;

    static Vector2 Screen(OfflineTable t, Vec2 at) => t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(at));

    static void Wheel(OfflineTable t, Vec2 at, bool ctrl = false, bool alt = false) =>
        t.Table.Router.OnMouseButton(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = Screen(t, at), CtrlPressed = ctrl, AltPressed = alt });

    static double Turned(OfflineTable t, string id)
    {
        t.Table.Poses.Flush();
        return t.S.View.Find(U, id)!.RotDeg;
    }

    [NodeTest]
    static async Task With_the_setting_off_the_wheel_over_an_unselected_piece_zooms_and_leaves_it_as_it_was(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(2);
        Check.That(!t.Table.Settings.WheelTurnsHovered, "the setting starts off");
        double before = t.S.View.Find(U, "p1")!.RotDeg;
        float far = t.Table.Rig.ZoomGoal;
        Wheel(t, new Vec2(1, 0));
        Check.That(t.Table.Rig.ZoomGoal < far, $"the wheel zooms: {far} → {t.Table.Rig.ZoomGoal}");
        Check.Equal(before, Turned(t, "p1"), "the piece is not turned");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task With_the_setting_on_the_wheel_turns_the_piece_under_the_pointer_but_not_a_locked_one_and_zooms_elsewhere(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        t.Table.Settings.WheelTurnsHovered = true;
        await r.Frames(2);
        double start = Turned(t, "p1");

        float zoom = t.Table.Rig.ZoomGoal;
        Wheel(t, new Vec2(1, 0));
        Check.Equal(Table.Norm360(start - 10), Turned(t, "p1"), "a notch turns it 10° without selecting it");
        Check.Equal(zoom, t.Table.Rig.ZoomGoal, "and does not zoom");
        Check.Equal(0, t.Table.Selected.Count, "nothing is selected");

        Wheel(t, new Vec2(1, 0), ctrl: true);
        Check.Equal(Table.Norm360(start - 12), Turned(t, "p1"), "Ctrl turns it 2°");

        Wheel(t, new Vec2(1, 0), alt: true);
        Check.That(t.Table.Rig.ZoomGoal < zoom, "Alt+wheel zooms over it");
        Check.Equal(Table.Norm360(start - 12), Turned(t, "p1"), "and leaves it turned as it was");

        t.S.Do(new SetLocked { Layer = U, Id = "p2", Value = true });
        await r.Frames(1);
        double locked = Turned(t, "p2");
        zoom = t.Table.Rig.ZoomGoal;
        Wheel(t, new Vec2(2, 0));
        Check.Equal(locked, Turned(t, "p2"), "a locked piece is not turned");
        Check.That(t.Table.Rig.ZoomGoal < zoom, "the wheel zooms over it instead");

        zoom = t.Table.Rig.ZoomGoal;
        Wheel(t, new Vec2(6, 5));
        Check.That(t.Table.Rig.ZoomGoal < zoom, "over the empty table the wheel zooms");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task Hovering_a_piece_highlights_it_and_a_middle_click_without_a_drag_turns_it_over(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(2);
        var node = t.Node("p1");
        var at = Screen(t, new Vec2(1, 0));
        t.Table.Hover.Update(at, 0);
        Check.That(node.Hovered && t.Table.HoveredNode == node, "the piece under the pointer is highlighted");

        void Middle(Vector2 down, Vector2 up)
        {
            t.Table.Router.OnMouseButton(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true, Position = down });
            t.Table.Router.OnMouseButton(new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = false, Position = up });
        }
        Middle(at, at);
        await r.Frames(1);
        Check.That(t.S.View.Find(U, "p1")!.FaceDown, "a middle click turns it over");
        Middle(at, at + new Vector2(40, 0));
        await r.Frames(1);
        Check.That(t.S.View.Find(U, "p1")!.FaceDown, "a middle drag pans and leaves it as it lies");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }
}
