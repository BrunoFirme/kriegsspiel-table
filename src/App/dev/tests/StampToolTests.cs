using System.Threading.Tasks;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class StampToolTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task A_decal_stamps_where_it_is_clicked_on_the_table_or_a_piece_and_a_second_click_lifts_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        var blood = new PaletteDecal { Name = "Blood", Picture = "blood", Size = 0.5 };
        t.S.Do(new SetDecalPalette { Entries = new() { blood } });
        await r.Frames(2);
        t.Table.Decals.Arm(t.S.View.DecalPalette[0]);
        t.Table.SetTool(Tool.Decal);
        await r.Frames(2);
        Godot.Vector2 Screen(Vec2 at) => t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(at));

        var open = new Vec2(6, 5);
        t.Table.Decals.Press(open, Screen(open));
        await r.Frames(2);
        var stamped = t.S.View.DecalLayer(U);
        Check.Equal(1, stamped.Count, "stamped on the table");
        Check.That(stamped.Count == 1 && (new Vec2(stamped[0].X, stamped[0].Z) - open).Length < 1e-6 && stamped[0].H == 0.5, $"where it was clicked, at the palette's size: {(stamped.Count > 0 ? stamped[0].X : 0)}");
        t.Table.Decals.Press(open, Screen(open));
        await r.Frames(2);
        Check.Equal(0, t.S.View.DecalLayer(U).Count, "a second click lifts it off");

        var onPiece = new Vec2(1, 0);
        t.Table.Decals.Press(onPiece, Screen(onPiece));
        await r.Frames(2);
        Check.Equal(1, t.S.View.Find(U, "p1")!.Decals.Count, "stamped on the piece, which carries it");
        Check.Equal(0, t.S.View.DecalLayer(U).Count, "and not on the table under it");
        t.Table.Decals.Press(onPiece, Screen(onPiece));
        await r.Frames(2);
        Check.Equal(0, t.S.View.Find(U, "p1")!.Decals.Count, "lifted off the piece");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task With_a_decal_picked_the_wheel_still_zooms_and_ctrl_or_shift_wheel_sizes_or_turns_the_decal(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        t.S.Do(new SetDecalPalette { Entries = new() { new PaletteDecal { Name = "Blood", Picture = "blood", Size = 0.5 } } });
        await r.Frames(2);
        t.Table.Decals.Arm(t.S.View.DecalPalette[0]);
        t.Table.SetTool(Tool.Decal);
        await r.Frames(2);
        var at = t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(new Vec2(6, 5)));
        void Wheel(bool ctrl, bool shift) =>
            t.Table.Router.OnMouseButton(new Godot.InputEventMouseButton { ButtonIndex = Godot.MouseButton.WheelUp, Pressed = true, Position = at, CtrlPressed = ctrl, ShiftPressed = shift });

        float far = t.Table.Rig.ZoomGoal;
        Wheel(false, false);
        float near = t.Table.Rig.ZoomGoal;
        Check.That(near < far, $"the wheel zooms in: {far} → {near}");
        Check.That(t.Table.Decals.Growth == 1 && t.Table.Decals.Turn == 0, "and leaves the decal as it was");

        Wheel(true, false);
        Check.That(t.Table.Decals.Growth > 1, $"Ctrl+wheel sizes the decal: {t.Table.Decals.Growth}");
        Wheel(false, true);
        Check.That(t.Table.Decals.Turn != 0, $"Shift+wheel turns it: {t.Table.Decals.Turn}");
        Check.Equal(near, t.Table.Rig.ZoomGoal, "neither zooms");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
