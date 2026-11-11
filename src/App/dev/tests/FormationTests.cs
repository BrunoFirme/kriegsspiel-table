using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Hand;
using KTable.Core.Model;
using KTable.Core.Protocol;

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
    static async Task A_ready_piece_out_rides_its_holder_into_formation_and_groups_take_it_only_with_its_holder(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        var beside = new Vec2(0, 1.5);
        t.S.Do(new Stow { Layer = U, Holder = "p1", Id = "p6" });
        t.S.Do(new SetReady { Layer = U, Holder = "p1", Ready = new ReadyPiece { Id = "p6", Offset = beside }, Move = "p6" });
        t.S.Do(new ReleaseReady { Layer = U, Holder = "p1" });
        await r.Frames(3);
        t.Table.SetTool(Tool.Formation);
        string Picked() => string.Join(",", t.Table.Selection.Ids.OrderBy(x => x));
        string Units() => string.Join(",", t.Table.Forms.Units().Select(n => n.Piece.Id).OrderBy(x => x));
        t.Table.Selection.SetGroup(new[] { "p6", "p2", "p3" });
        Check.Equal("p2,p3", Picked(), "a group without the holder leaves its ready piece out");
        t.Table.Selection.Set(new[] { "p6", "p2", "p3" });
        Check.Equal("p2,p3", Units(), "picked by hand, the ready piece is still not formed up");
        t.Table.Selection.SelectAllEditable();
        Check.Equal("p1,p2,p3,p4,p5,p6", Picked(), "with its holder in the group it comes too");
        Check.Equal("p1,p2,p3,p4,p5", Units(), "only the holders and the rest form up");
        t.Table.Forms.Form(Formations.Find("column")!);
        await Settled(r, t);
        var holder = t.S.View.Find(U, "p1")!;
        var ready = t.S.View.Find(U, "p6")!;
        Check.That(At(t, "p1") != new Vec2(1, 0), "the holder moved into the column");
        var local = ReadyPiece.Turned(ready.Pos - holder.Pos, -holder.RotDeg);
        Check.That((local - beside).Length < 1e-3, "the ready piece kept its place beside its holder: " + local);
        Check.That(Math.Abs(Mathf.Wrap((float)(ready.RotDeg - holder.RotDeg), -180, 180)) < 0.5, "and its facing");
        t.S.Do(new RecallReady { Layer = U, Holder = "p1" });
        await r.Frames(3);
        var kept = t.S.View.Find(U, "p1")!.Ready!.Offset!.Value;
        Check.That((kept - beside).Length < 1e-3, "recalled, it keeps the same spot for next time: " + kept);
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

    [NodeTest]
    static async Task Interval_spaces_the_files_and_distance_the_ranks_and_shift_moves_only_the_distance(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        var forms = t.Table.Forms;
        forms.Form(Formations.Find("line")!);
        forms.SetDepth(2);
        forms.SetGap(0);
        forms.SetDistance(1);
        await Settled(r, t);
        double w = t.Node("p1").Footprint.X, h = t.Node("p1").Footprint.Y;
        void Spaced(double files, double ranks, string when)
        {
            var ps = Six.Select(id => At(t, id)).ToList();
            var rows = ps.GroupBy(p => Math.Round(p.Z, 3)).OrderBy(g => g.Key).ToList();
            Check.Equal(2, rows.Count, "two ranks " + when);
            Check.That(Math.Abs(rows[1].Key - rows[0].Key - ranks) < 1e-3, $"ranks {ranks:0.###} apart {when}: {rows[1].Key - rows[0].Key:0.###}");
            var xs = rows[0].Select(p => p.X).OrderBy(x => x).ToList();
            Check.That(xs.Zip(xs.Skip(1), (a, b) => b - a).All(d => Math.Abs(d - files) < 1e-3), $"files {files:0.###} apart {when}: " + string.Join(" ", xs));
        }
        Spaced(w, h + w, "at no interval and full distance");
        Check.That(forms.Key(new InputEventKey { Keycode = Key.Equal, PhysicalKeycode = Key.Equal, Pressed = true, ShiftPressed = true }, t.Table.Settings.Keys), "Shift+= is taken");
        await Settled(r, t);
        Check.That(Math.Abs(forms.Distance - 1.25) < 1e-9 && forms.Gap == 0, $"only the distance grew: {forms.Gap} {forms.Distance}");
        Spaced(w, h + 1.25 * w, "after Shift+=");
    }

    static Vec2 OnCircle(Vec2 centre, double radius, double deg) =>
        new(centre.X + radius * Math.Cos(deg * Math.PI / 180), centre.Z + radius * Math.Sin(deg * Math.PI / 180));

    static Vec2 Front(double rot) => new(-Math.Sin(rot * Math.PI / 180), -Math.Cos(rot * Math.PI / 180));

    static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;

    [NodeTest]
    static async Task A_right_drag_drawn_round_a_bend_strings_the_pieces_along_it_facing_out_of_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 4);
        await r.Frames(2);
        t.Table.SetTool(Tool.Formation);
        var four = Six.Take(4).ToArray();
        t.Table.Selection.Set(four);
        t.Table.Forms.Form(Formations.Find("stretch")!);
        await Settled(r, t);
        double across = four.Select(id => t.Node(id).Footprint.X).Max() * (1 + t.Table.Forms.Gap);
        var xs = four.Select(id => At(t, id).X).OrderBy(x => x).ToList();
        Check.That(four.All(id => Math.Abs(At(t, id).Z) < 1e-6), "Shift+R alone strings them out where they stand");
        Check.That(xs.Zip(xs.Skip(1)).All(p => Math.Abs(p.Second - p.First - across) < 1e-6), "a spacing apart: " + string.Join(" ", xs));
        int undos = t.S.MyUndoable.Count, menus = 0;
        t.Table.ContextMenu += (_, _) => menus++;
        var centre = new Vec2(2.5, -3.2);
        const double Radius = 1.8;
        const int Steps = 24;
        var stroke = Enumerable.Range(0, Steps + 1).Select(i => Screen(t, OnCircle(centre, Radius, 150 - 120.0 * i / Steps))).ToList();
        Input.ParseInputEvent(new InputEventMouseMotion { Position = stroke[0], GlobalPosition = stroke[0] });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = stroke[0], GlobalPosition = stroke[0], ButtonMask = MouseButtonMask.Right });
        await r.Frame();
        for (int i = 1; i < stroke.Count; i++)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Position = stroke[i], GlobalPosition = stroke[i], Relative = stroke[i] - stroke[i - 1], ButtonMask = MouseButtonMask.Right });
            await r.Frame();
        }
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = stroke[^1], GlobalPosition = stroke[^1] });
        await r.Frames(2);
        await Settled(r, t);
        Check.Equal(0, menus, "drawing opens no menu");
        var ps = four.Select(id => t.S.View.Find(U, id)!).ToList();
        Check.Equal(undos + 1, t.S.MyUndoable.Count, "one thing to undo");
        Check.That(ps.All(p => Math.Abs((p.Pos - centre).Length - Radius) < 0.02), "every piece on the drawn bend: " + string.Join(" ", ps.Select(p => p.Pos)));
        Check.That(ps.Any(p => (p.Pos - OnCircle(centre, Radius, 150)).Length < 0.02) && ps.Any(p => (p.Pos - OnCircle(centre, Radius, 30)).Length < 0.02), "one at each end");
        Check.That(ps.All(p => Dot(Front(p.RotDeg), p.Pos - centre) / Radius > 0.95), "drawn left to right, every front looks out of the bend, to the top");
        var angles = ps.Select(p => Math.Atan2(p.Pos.Z - centre.Z, p.Pos.X - centre.X) * 180 / Math.PI).OrderBy(a => a).ToList();
        Check.That(angles.Zip(angles.Skip(1)).All(p => Math.Abs(p.Second - p.First - 40) < 0.5), "evenly along it: " + string.Join(" ", angles.Select(a => a.ToString("0.0"))));
        t.S.UndoLast();
        await r.Frame();
        Check.That(four.All(id => Math.Abs(At(t, id).Z) < 1e-6), "one undo puts them back on their line");
    }

    [NodeTest]
    static async Task A_drawn_s_turned_keeps_its_shape_and_respaced_keeps_its_ends(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        t.Table.Settings.Formation = "stretch";
        string said = "";
        t.Table.Hint += h => said = h;
        double radius = 2 * Six.Select(id => t.Node(id).Footprint.X).Max();
        var s = Enumerable.Range(0, 41).Select(i => OnCircle(new Vec2(25, 10), radius, 180 - 4.5 * i))
            .Concat(Enumerable.Range(1, 40).Select(i => OnCircle(new Vec2(25 + 2 * radius, 10), radius, 180 + 4.5 * i))).ToList();
        Check.That(t.Table.Forms.Press(s[0]), "the line is drawn from where the drag starts");
        foreach (var p in s.Skip(1)) t.Table.Forms.Drag(p);
        t.Table.Forms.Release();
        await Settled(r, t);
        var laid = Six.Select(id => At(t, id)).ToList();
        Check.That(laid.Any(p => (p - s[0]).Length < 1e-3) && laid.Any(p => (p - s[^1]).Length < 1e-3), "from one end of the S to the other");
        var centre = new Vec2(laid.Average(p => p.X), laid.Average(p => p.Z));
        t.Table.Forms.Turn(90);
        await Settled(r, t);
        var turned = Six.Select(id => At(t, id)).ToList();
        Check.That(Enumerable.Range(0, 6).All(i => Enumerable.Range(0, 6).All(j => Math.Abs((laid[i] - laid[j]).Length - (turned[i] - turned[j]).Length) < 1e-4)), "turned whole, its form kept");
        var ends = new[] { s[0], s[^1] }.Select(e => centre + Formation.Turned(e - centre, Math.PI / 2)).ToList();
        t.Table.Forms.SetGap(1);
        await Settled(r, t);
        var spaced = Six.Select(id => At(t, id)).ToList();
        Check.That(ends.All(e => spaced.Any(p => (p - e).Length < 1e-3)), "wider spacing keeps both ends of the turned S: " + string.Join(" ", spaced));
        Check.That(!said.Contains("past its end"), "the S is long enough: " + said);
        t.Table.Forms.SetGap(Formations.MostGap);
        await Settled(r, t);
        Check.That(said.Contains("the last 2 run on past its end"), "too short at the widest spacing, and it says so: " + said);
    }
    [NodeTest]
    static async Task Drawing_back_over_a_stretch_shortens_it_instead_of_folding_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 4);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        var four = Six.Take(4).ToArray();
        t.Table.Selection.Set(four);
        t.Table.Settings.Formation = "stretch";
        double across = four.Select(id => t.Node(id).Footprint.X).Max() * (1 + t.Table.Forms.Gap);
        Check.That(t.Table.Forms.Press(new Vec2(0, 5)), "the line is drawn from where the drag starts");
        for (int i = 1; i <= 60; i++) t.Table.Forms.Drag(new Vec2(i * across * 0.25, 5));
        for (int i = 1; i <= 20; i++) t.Table.Forms.Drag(new Vec2((15 - i * 0.25) * across, 5 + across * 0.02));
        t.Table.Forms.Release();
        await Settled(r, t);
        var xs = four.Select(id => At(t, id).X).OrderBy(x => x).ToList();
        Check.That(Math.Abs(xs[^1] - 10 * across) < across * 0.1, $"the last stands where the line was pulled back to, 10 widths out: {xs[^1] / across:0.00}");
        Check.That(four.All(id => Math.Abs(At(t, id).Z - 5) < across * 0.05), "all on the one line, none folded back beside it");
    }

    [NodeTest]
    static async Task A_drawn_stretch_turned_in_place_with_the_wheel_keeps_its_line_when_spaced(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 4);
        await r.Frame();
        t.Table.SetTool(Tool.Formation);
        var four = Six.Take(4).ToArray();
        t.Table.Selection.Set(four);
        t.Table.Settings.Formation = "stretch";
        double across = four.Select(id => t.Node(id).Footprint.X).Max() * (1 + t.Table.Forms.Gap);
        double radius = 4 * across;
        Vec2 Arc(double deg) => new(radius * Math.Cos(deg * Math.PI / 180), radius * Math.Sin(deg * Math.PI / 180));
        Check.That(t.Table.Forms.Press(Arc(0)), "the line is drawn from where the drag starts");
        for (int i = 1; i <= 90; i++) t.Table.Forms.Drag(Arc(i));
        t.Table.Forms.Release();
        await Settled(r, t);
        bool OnTheArc() => four.All(id => Math.Abs(At(t, id).Length - radius) < across * 0.1);
        Check.That(OnTheArc(), "strung along the drawn quarter circle");
        var facing = four.ToDictionary(id => id, id => t.S.View.Find(U, id)!.RotDeg);
        t.Table.Rig.Frame(Coords.ToWorld(At(t, four[1])), (float)(radius * 2));
        await r.Frame();
        var over = Screen(t, At(t, four[1]));
        t.Table.Router.OnMouseButton(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = over });
        t.Table.Poses.Flush();
        await r.Frame();
        Check.That(four.All(id => Math.Abs(Mathf.Wrap((float)(t.S.View.Find(U, id)!.RotDeg - facing[id] - 10), -180, 180)) < 1e-3), "the wheel over a piece turns each 10° in place");
        double gap = t.Table.Forms.Gap;
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Equal, Pressed = true });
        Check.That(t.Table.Forms.Gap > gap, $"= spaces them wider: {gap} → {t.Table.Forms.Gap}");
        await Settled(r, t);
        Check.That(OnTheArc(), "turned in place and spaced wider, still on the drawn line, not a straight one: " + string.Join(", ", four.Select(id => $"{At(t, id).Length / across:0.00}")));
    }

    [NodeTest]
    static async Task In_the_formations_tool_the_wheel_over_the_pieces_turns_them_in_place_and_the_keys_set_spacing_and_ranks(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frames(2);
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(Six);
        t.Table.Forms.Form(Formations.Find("line")!);
        await Settled(r, t);
        double gap = t.Table.Forms.Gap;
        void Press(Key key) => t.Table.Keys.OnKey(new InputEventKey { Keycode = key, Pressed = true });
        Press(Key.Equal);
        Check.That(t.Table.Forms.Gap > gap, $"= spaces them wider: {gap} → {t.Table.Forms.Gap}");
        Press(Key.Bracketright);
        await Settled(r, t);
        Check.Equal(2, Six.Select(id => Math.Round(At(t, id).Z, 4)).Distinct().Count(), "] puts them in two ranks");
        var before = Six.ToDictionary(id => id, id => At(t, id));
        var over = Screen(t, At(t, "p3"));
        float far = t.Table.Rig.ZoomGoal;
        void Wheel(bool alt) => t.Table.Router.OnMouseButton(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = over, AltPressed = alt });
        Wheel(alt: false);
        t.Table.Poses.Flush();
        await r.Frame();
        Check.That(Six.All(id => Math.Abs(Mathf.Wrap((float)(t.S.View.Find(U, id)!.RotDeg - 10), -180, 180)) < 1e-3), "the wheel over the pieces turns each of them 10° a notch");
        Check.That(Six.All(id => (At(t, id) - before[id]).Length < 1e-6), "in place, none moves");
        Check.That(t.Table.Rig.ZoomGoal == far, "and the view does not zoom");
        Wheel(alt: true);
        await r.Frame();
        Check.That(t.Table.Rig.ZoomGoal > far, $"Alt+wheel zooms instead: {far} → {t.Table.Rig.ZoomGoal}");
        Press(Key.Bracketright);
        await Settled(r, t);
        var (_, back) = Formations.Axes(10);
        Check.Equal(3, Six.Select(id => Math.Round(At(t, id).X * back.X + At(t, id).Z * back.Z, 3)).Distinct().Count(), "] after the turn still steps the ranks: three, facing as the pieces were turned");
    }
}
