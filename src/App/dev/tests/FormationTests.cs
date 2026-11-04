using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.App;

static class FormationTests
{
    const string U = Layers.Umpire;

    static readonly string[] Six = { "p1", "p2", "p3", "p4", "p5", "p6" };

    static Vec2 At(OfflineTable t, string id) => t.S.View.Find(U, id)!.Pos;

    static async Task Settled(TestRunner r, OfflineTable t)
    {
        for (int i = 0; i < 120 && (t.Table.Hand.Holding || t.Table.Hand.Marching); i++) await r.Frame();
        await r.Frames(3);
    }

    static Vector2 Screen(OfflineTable t, Vec2 p) => t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(p));

    static async Task Mouse(TestRunner r, MouseButton button, Vector2 from, Vector2 to, bool shift = false, bool twice = false)
    {
        var mask = button == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = true, DoubleClick = twice, Position = from, GlobalPosition = from, ShiftPressed = shift, ButtonMask = mask });
        await r.Frame();
        const int Steps = 6;
        for (int i = 1; i <= Steps && from != to; i++)
        {
            var at = from.Lerp(to, i / (float)Steps);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (to - from) / Steps, ButtonMask = mask, ShiftPressed = shift });
            await r.Frame();
        }
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = to, GlobalPosition = to, ShiftPressed = shift });
        await r.Frames(2);
    }

    [NodeTest]
    static async Task In_the_formations_tool_a_drag_on_the_empty_table_selects_and_shift_adds(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frames(2);
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(new[] { "p6" });
        await Mouse(r, MouseButton.Left, Screen(t, new Vec2(0.3, 0.9)), Screen(t, new Vec2(3.5, -0.9)));
        Check.Equal("p1,p2,p3", string.Join(",", t.Table.Selection.Ids.OrderBy(x => x)), "the box took the first three in place of the one before");
        await Mouse(r, MouseButton.Left, Screen(t, new Vec2(4.6, 0.9)), Screen(t, new Vec2(5.4, -0.9)), shift: true);
        Check.Equal("p1,p2,p3,p5", string.Join(",", t.Table.Selection.Ids.OrderBy(x => x)), "Shift added the fifth");
        Check.That(Six.Select((id, i) => At(t, id) == new Vec2(i + 1, 0)).All(x => x), "selecting moved nothing");
        await Mouse(r, MouseButton.Left, Screen(t, new Vec2(4, 0)), Screen(t, new Vec2(4, 0)), twice: true);
        Check.Equal(6, t.Table.Selection.Ids.Count, "a double-click took every piece of that kind in view");
    }

    [NodeTest]
    static async Task A_right_drag_lays_the_front_out_and_a_right_click_still_opens_the_menu(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frames(2);
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        int menus = 0;
        t.Table.ContextMenu += (_, _) => menus++;
        await Mouse(r, MouseButton.Right, Screen(t, new Vec2(3.5, -2)), Screen(t, new Vec2(3.5, -2)));
        Check.Equal(1, menus, "a right-click opens the menu");
        Check.That(!t.Table.Hand.Holding, "and moves nothing");
        double across = Six.Select(id => t.Node(id).Footprint.X).Max() * (1 + t.Table.Forms.Gap);
        await Mouse(r, MouseButton.Right, Screen(t, new Vec2(3.5 - 1.5 * across, -2)), Screen(t, new Vec2(3.5 + 1.5 * across, -2)));
        await Settled(r, t);
        Check.Equal(1, menus, "a right-drag opens no menu");
        var ps = Six.Select(id => At(t, id)).ToList();
        Check.Equal(3, ps.Count(p => Math.Abs(p.Z + 2) < 0.15), "three in the front rank, on the dragged line: " + string.Join(" ", ps));
        Check.That(ps.All(p => p.Z < -2 + 0.15), "the rest behind it, away from the way they face");
        Check.That(Six.All(id => Math.Abs(t.S.View.Find(U, id)!.RotDeg - 180) < 1), "facing the top of the map, as dragged left to right");
    }

    [NodeTest]
    static async Task Pieces_formed_into_a_column_march_there_and_one_undo_takes_them_back(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        int undos = t.S.MyUndoable.Count;
        t.Table.Forms.Form(Formations.Find("column")!);
        Check.That(t.Table.Hand.Marching, "the pieces are on the march");
        await Settled(r, t);
        Check.That(!t.Table.Hand.Holding, "all set down");
        var xs = Six.Select(id => At(t, id).X).ToList();
        var zs = Six.Select(id => At(t, id).Z).ToList();
        Check.That(xs.Max() - xs.Min() < 1e-6, "one behind another: " + string.Join(", ", xs));
        Check.Equal(6, zs.Select(z => Math.Round(z, 4)).Distinct().Count(), "each at its own depth");
        Check.That(Math.Abs(xs.Average() - 3.5) < 1e-6, "round where they stood");
        Check.Equal(undos + 1, t.S.MyUndoable.Count, "one thing to undo");
        t.S.UndoLast();
        await r.Frame();
        for (int i = 0; i < 6; i++) Check.Equal(new Vec2(i + 1, 0), At(t, Six[i]), Six[i] + " back");
    }

    [NodeTest]
    static async Task A_line_turned_a_quarter_wheels_round_its_centre(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        t.Table.Forms.Form(Formations.Find("line")!);
        await Settled(r, t);
        var before = Six.Select(id => At(t, id)).ToList();
        Check.That(before.Max(p => p.Z) - before.Min(p => p.Z) < 1e-6, "a line across the front");
        t.Table.Forms.Turn(90);
        await Settled(r, t);
        var after = Six.Select(id => At(t, id)).ToList();
        Check.That(after.Max(p => p.X) - after.Min(p => p.X) < 1e-4, "the line now runs up and down: " + string.Join(" ", after));
        Check.That(Math.Abs(after.Average(p => p.X) - before.Average(p => p.X)) < 1e-4, "round the same centre");
        Check.That(Six.All(id => Math.Abs(t.S.View.Find(U, id)!.RotDeg - 90) < 1e-4), "every piece faces the new way");
    }

    [NodeTest]
    static async Task A_drag_sets_the_front_and_the_ranks_behind_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        t.Table.Forms.Form(Formations.Find("line")!);
        await Settled(r, t);
        double across = Six.Select(id => t.Node(id).Footprint.X).Max() * (1 + t.Table.Forms.Gap);
        var from = new Vec2(20, 10);
        Check.That(t.Table.Forms.Press(from), "the front is drawn from where the drag starts");
        t.Table.Forms.Drag(new Vec2(20 - 3 * across, 10));
        t.Table.Forms.Release();
        await Settled(r, t);
        var ps = Six.Select(id => At(t, id)).ToList();
        Check.Equal(2, ps.Select(p => Math.Round(p.Z, 4)).Distinct().Count(), "three wide, two ranks deep");
        Check.That(ps.All(p => p.Z >= 10 - 1e-6) || ps.All(p => p.Z <= 10 + 1e-6), "the ranks stand behind the front");
        Check.Equal(3, ps.Count(p => Math.Abs(p.Z - 10) < 1e-4), "the front rank on the dragged line");
    }
}
