using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class SheetThingTests
{
    const string U = Layers.Umpire;
    const double Near = 1e-6;
    static readonly Vec2 OnFirst = new(2, 2), OnSecond = new(30, 2), OffBoth = new(15, 40);

    static SheetDef Sheet(HudTests.Rig h, string id) => h.S.View.Sheets.Single(s => s.Id == id);

    static async Task<HudTests.Rig> TwoSheets(TestRunner r, int pieces = 0, bool free = true)
    {
        var h = await HudTests.Host(r, pieces: pieces, sheets: 2);
        h.S.Do(new MoveSheet { Id = "s1", Pos = new Vec2(30, 0), RotDeg = 0, Z = 1 });
        if (free) foreach (var id in new[] { "s0", "s1" }) h.S.Do(new LockSheet { Id = id, Locked = false });
        h.Table.FrameAll();
        await r.Seconds(0.6);
        return h;
    }

    static InputEventMouseButton Button(HudTests.Rig h, Vec2 g, bool pressed, bool alt)
    {
        var at = h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(g));
        return new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, AltPressed = alt, ButtonMask = pressed ? MouseButtonMask.Left : 0,
        };
    }

    static async Task Click(TestRunner r, HudTests.Rig h, Vec2 g, bool alt = false)
    {
        h.Table.Router.OnMouseButton(Button(h, g, true, alt));
        h.Table.Router.OnMouseButton(Button(h, g, false, alt));
        await r.Frames(2);
    }

    static InputEventKey Key(Key key, bool shift = false, bool ctrl = false) => new() { Keycode = key, Pressed = true, ShiftPressed = shift, CtrlPressed = ctrl };

    static List<SpinBox> Fields(HudTests.Rig h) => WindowTests.Descendants(h.Hud.Numbers.Panel.Body).OfType<SpinBox>().ToList();

    static List<string> Said(HudTests.Rig h) => WindowTests.Descendants(h.Hud.Numbers.Panel.Body).OfType<Label>().Select(l => l.Text).ToList();

    [NodeTest]
    static async Task A_click_picks_a_free_sheet_a_locked_one_is_ground_and_takes_Alt_and_a_click_off_it_lets_go(TestRunner r)
    {
        var h = await TwoSheets(r, pieces: 1);
        h.S.Do(new LockSheet { Id = "s1", Locked = true });
        await r.Frames(2);
        await Click(r, h, OnFirst);
        Check.Equal("s0", h.Table.SelectedSheet, "a click on a free sheet picks it");
        await Click(r, h, OnSecond);
        Check.That(h.Table.SelectedSheet == null, "a click on a locked sheet is a click on the ground: the first is let go and the locked one is not picked");
        await Click(r, h, OnSecond, alt: true);
        Check.Equal("s1", h.Table.SelectedSheet, "Alt+click picks the locked sheet");
        await Click(r, h, OffBoth);
        Check.That(h.Table.SelectedSheet == null, "a click off every sheet lets go of it");
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SelectSheet("s0");
        Check.That(h.Table.Selection.Ids.Count == 0 && h.Table.SelectedSheet == "s0", "picking a sheet lets go of the pieces");
    }

    [NodeTest]
    static async Task The_Map_page_shows_the_sheet_picked_on_the_table_and_nothing_once_it_is_let_go(TestRunner r)
    {
        var h = await TwoSheets(r);
        h.Hud.Drawer.ShowPage("map");
        h.Table.SelectSheet("s0");
        await r.Frames(3);
        var page = h.Hud.Drawer.Pages["map"].page!;
        List<Control> Live() => WindowTests.Descendants(page).OfType<Control>().Where(c => !c.IsQueuedForDeletion()).ToList();
        Button Row(string id) => Live().OfType<Button>().Single(b => b.Text == id && b.ToggleMode);
        Check.That(Row("s0").ButtonPressed && !Row("s1").ButtonPressed, "the page shows the sheet picked in its list");
        await Click(r, h, OnSecond);
        await r.Frames(3);
        Check.That(Row("s1").ButtonPressed && !Row("s0").ButtonPressed, "a sheet picked on the table is the one the page shows");
        Check.That(Live().OfType<Label>().Count(l => l.Text.Equals("Selected sheet", StringComparison.OrdinalIgnoreCase)) == 1, "and the one its form is for");
        h.Table.Keys.OnKey(Key(Godot.Key.Escape));
        await r.Frames(3);
        Check.That(h.Table.SelectedSheet == null && !Live().OfType<Label>().Any(l => l.Text.Equals("Selected sheet", StringComparison.OrdinalIgnoreCase)), "with nothing picked the form is gone");
    }

    [NodeTest]
    static async Task The_turn_and_resize_window_takes_a_sheets_place_turn_width_depth_and_order_and_says_Locked_for_a_locked_one(TestRunner r)
    {
        var h = await TwoSheets(r);
        bool keeps = h.Hud.Cfg.SheetKeepsShape;
        h.Hud.Cfg.SheetKeepsShape = true;
        h.Table.SelectSheet("s0");
        h.Table.SetTool(Tool.Transform);
        await r.Frames(3);
        Check.Equal(6, Fields(h).Count, "X, Y, Turn, Width, Depth and Order");
        Fields(h)[0].Value = 12.5;
        Fields(h)[1].Value = -3.25;
        Fields(h)[2].Value = 90;
        await r.Frames(3);
        var s = Sheet(h, "s0");
        Check.That(Math.Abs(s.Pos.X - 12.5) < Near && Math.Abs(s.Pos.Z + 3.25) < Near && Math.Abs(s.RotDeg - 90) < Near, $"typed place and turn are where the sheet lies: {s.Pos} {s.RotDeg}");
        Fields(h)[3].Value = 20;
        await r.Frames(3);
        Check.That(Math.Abs(Sheet(h, "s0").Size.W - 20) < Near && Math.Abs(Sheet(h, "s0").Size.H - 20) < Near, "a typed width keeps the proportions");
        Check.That(Math.Abs(Fields(h)[4].Value - 20) < 0.01, "and Depth says what the depth became");
        h.Hud.Cfg.SheetKeepsShape = false;
        Fields(h)[4].Value = 5;
        Fields(h)[5].Value = 3;
        await r.Frames(3);
        Check.That(Math.Abs(Sheet(h, "s0").Size.W - 20) < Near && Math.Abs(Sheet(h, "s0").Size.H - 5) < Near, "with Keep the proportions off a typed depth leaves the width");
        Check.Equal(3, Sheet(h, "s0").Z, "Order is which sheet lies on top");
        h.S.Do(new LockSheet { Id = "s0", Locked = true });
        await r.Frames(3);
        Check.That(Fields(h).Count == 0 && Said(h).Contains("Locked"), "a locked sheet shows no fields and says Locked: " + string.Join(" | ", Said(h)));
        h.Hud.Cfg.SheetKeepsShape = keeps;
        h.Table.SetTool(Tool.Select);
    }

    static List<Vector3> OutlineOf(SheetNode node) =>
        node.GetChildren().OfType<MeshInstance3D>().Where(m => m.Mesh is ImmediateMesh && !m.IsQueuedForDeletion())
            .SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).SelectMany(i => m.Mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex].AsVector3Array())).ToList();

    [NodeTest]
    static async Task A_picked_sheets_outline_is_a_hairline_outside_its_edge_whatever_the_zoom_and_only_corner_marks_while_it_moves(TestRunner r)
    {
        const float Pixels = 3.5f;
        var h = await TwoSheets(r);
        h.Table.SelectSheet("s0");
        await r.Frames(3);
        h.Table.Sheets.TryGet("s0", out var node);
        float half = (float)Sheet(h, "s0").Size.W / 2;
        float Over() => OutlineOf(node).Max(v => Math.Max(Math.Abs(v.X), Math.Abs(v.Z))) - half;
        float Inside() => half - OutlineOf(node).Min(v => Math.Max(Math.Abs(v.X), Math.Abs(v.Z)));
        float far = h.Table.Sheets.TablePerPixel();
        Check.That(Over() > 0 && Over() <= Pixels * far, $"the outline is a few pixels wide: {Over()} u at {far} u a pixel");
        Check.That(Inside() < 1e-4f, $"and none of it lies on the sheet: {Inside()}");
        h.Table.Rig.ZoomTo(h.Table.Rig.ZoomGoal / 4);
        await r.Seconds(0.8);
        float near = h.Table.Sheets.TablePerPixel();
        Check.That(near < far / 2 && Over() <= Pixels * near, $"zoomed in it is as thin on screen, so thinner on the table: {Over()} u at {near} u a pixel");
        h.Table.Sheets.Dragged = true;
        await r.Frames(3);
        var marks = OutlineOf(node);
        Check.That(marks.Count > 0 && marks.All(v => Math.Abs(v.X) > half / 2 && Math.Abs(v.Z) > half / 2 || Math.Abs(v.X) > half * 0.999f || Math.Abs(v.Z) > half * 0.999f),
            "while it is dragged only marks are drawn");
        Check.That(!marks.Any(v => Math.Abs(v.X) < half / 4 || Math.Abs(v.Z) < half / 4), "and the middle of every edge is clear, to see the seam");
        h.Table.Sheets.Dragged = false;
    }

    [NodeTest]
    static async Task Edges_snap_from_a_few_pixels_away_so_zooming_in_gives_finer_placing_and_a_met_edge_is_shown(TestRunner r)
    {
        var h = await TwoSheets(r);
        var sheets = h.Table.Sheets;
        sheets.TryGet("s1", out var moving);
        double reach = 8 * sheets.TablePerPixel();
        var close = sheets.SnapTo(moving, new Vec2(10 + reach / 2, 0));
        Check.That(Math.Abs(close.X - 10) < Near && sheets.SnappedAcross != null, $"within reach the edges meet and the seam is shown: {close}");
        var apart = sheets.SnapTo(moving, new Vec2(10 + reach * 2, 0));
        Check.That(Math.Abs(apart.X - (10 + reach * 2)) < Near && sheets.SnappedAcross == null, "beyond it the sheet stays where it is put");
        var free = sheets.SnapTo(moving, new Vec2(10 + reach / 2, 0), free: true);
        Check.That(Math.Abs(free.X - (10 + reach / 2)) < Near && sheets.SnappedAcross == null && sheets.SnappedUp == null, "and Alt places it freely");
        h.Table.Rig.ZoomTo(h.Table.Rig.ZoomGoal / 4);
        await r.Seconds(0.8);
        double finer = 8 * sheets.TablePerPixel();
        Check.That(finer < reach / 2, $"zoomed in the reach is shorter on the table: {finer} against {reach}");
        var kept = sheets.SnapTo(moving, new Vec2(10 + reach / 2, 0));
        Check.That(Math.Abs(kept.X - (10 + reach / 2)) < Near, "so a sheet can rest closer to another than before without meeting it");
    }

    [NodeTest]
    static async Task Arrows_nudge_and_the_turn_and_size_keys_act_on_a_picked_sheet_as_on_a_piece_and_arrows_nudge_pieces_too(TestRunner r)
    {
        var h = await TwoSheets(r, pieces: 1);
        var keys = h.Table.Keys;
        h.Table.SelectSheet("s0");
        var was = Sheet(h, "s0").Pos;
        var step = h.Table.Sheets.ScreenStep(Godot.Key.Right, 0.05);
        Check.That(Math.Abs(Math.Abs(step.X) + Math.Abs(step.Z) - 0.05) < Near, $"an arrow is one step along one side of the table: {step}");
        keys.OnKey(Key(Godot.Key.Right));
        Check.That((Sheet(h, "s0").Pos - (was + step)).Length < Near, "Right nudges the sheet a step to the right of the screen");
        keys.OnKey(Key(Godot.Key.Right, ctrl: true));
        Check.That(Math.Abs((Sheet(h, "s0").Pos - was).Length - 0.06) < Near, "Ctrl is the finest step, 0.01");
        keys.OnKey(Key(Godot.Key.Right, shift: true));
        Check.That(Math.Abs((Sheet(h, "s0").Pos - was).Length - 1.06) < Near, "Shift is a whole unit");
        double turned = Sheet(h, "s0").RotDeg, wide = Sheet(h, "s0").Size.W;
        keys.OnKey(Key(Godot.Key.E));
        Check.That(Math.Abs(Table.Norm360(Sheet(h, "s0").RotDeg - turned) - 15) < Near, "E turns the sheet 15° as it turns a piece");
        keys.OnKey(Key(Godot.Key.Equal));
        Check.That(Math.Abs(Sheet(h, "s0").Size.W - wide * 1.1) < 1e-4, "= makes it a tenth bigger");
        h.S.Do(new LockSheet { Id = "s0", Locked = true });
        var held = Sheet(h, "s0").Pos;
        keys.OnKey(Key(Godot.Key.Left));
        Check.That((Sheet(h, "s0").Pos - held).Length < Near, "a locked sheet stays");
        h.Table.Selection.Set(new[] { "p1" });
        var stood = h.S.View.Find(U, "p1")!.Pos;
        keys.OnKey(Key(Godot.Key.Right));
        h.Table.Poses.Flush();
        await r.Frames(3);
        Check.That((h.S.View.Find(U, "p1")!.Pos - (stood + step)).Length < 1e-4, "with a piece picked the arrow nudges the piece");
    }

    [NodeTest]
    static async Task Pieces_standing_on_a_sheet_go_with_it_when_With_its_pieces_is_ticked(TestRunner r)
    {
        var h = await TwoSheets(r, pieces: 1);
        bool carried = h.Hud.Cfg.SheetCarries;
        h.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(1, 1), RotDeg = 0 });
        h.Hud.Cfg.SheetCarries = false;
        h.Table.Sheets.Move(Sheet(h, "s0"), new Vec2(0, 2), 0);
        Check.That((h.S.View.Find(U, "p1")!.Pos - new Vec2(1, 1)).Length < Near, "unticked, a moved sheet leaves its pieces where they stand");
        h.Hud.Cfg.SheetCarries = true;
        h.Table.Sheets.Move(Sheet(h, "s0"), new Vec2(5, 2), 0);
        Check.That((h.S.View.Find(U, "p1")!.Pos - new Vec2(6, 1)).Length < 1e-3, $"ticked, the piece moves with it: {h.S.View.Find(U, "p1")!.Pos}");
        int steps = h.S.MyUndoable.Count;
        h.S.UndoLast();
        Check.That((h.S.View.Find(U, "p1")!.Pos - new Vec2(1, 1)).Length < 1e-3 && Math.Abs(Sheet(h, "s0").Pos.X) < Near && h.S.MyUndoable.Count == steps - 1,
            "and one undo puts both back");
        h.Hud.Cfg.SheetCarries = carried;
        await r.Frames(1);
    }

    static PopupPanel Card(HudTests.Rig h) =>
        WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible) ?? throw new TestFailure("no right-click card is up");

    static Button Row(PopupPanel card, string text) =>
        WindowTests.Descendants(card).OfType<Button>().FirstOrDefault(b => WindowTests.Descendants(b).OfType<Label>().Any(l => l.Text == text))
        ?? throw new TestFailure($"no row reads {text}");

    [NodeTest]
    static async Task Right_click_on_a_sheet_names_it_says_its_size_and_locks_frames_picks_or_removes_it(TestRunner r)
    {
        var h = await TwoSheets(r);
        var at = h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(OnFirst));
        h.Hud.Menus.Show(at, null);
        await r.Frames(2);
        var card = Card(h);
        var labels = WindowTests.Descendants(card).OfType<Label>().Select(l => l.Text).ToList();
        var s0 = Sheet(h, "s0");
        Check.That(labels.Contains(SheetAsk.Named(s0)) && labels.Contains(Fmt.Footprint(s0.Size.W, s0.Size.H, h.S.View)), "the card names the sheet and says how big it is: " + string.Join(" | ", labels));
        foreach (var row in new[] { "Pick it", "Locked", "Hide on my screen", "Frame it", "Remove…", "Select all" }) Row(card, row);
        Row(card, "Locked").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(Sheet(h, "s0").Locked, "Locked on the card locks the sheet, as it does a piece");
        h.Hud.Menus.Show(at, null);
        await r.Frames(2);
        Row(Card(h), "Pick it").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal("s0", h.Table.SelectedSheet, "Pick it picks a locked sheet without Alt");
        var off = h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(OffBoth));
        h.Hud.Menus.Show(off, null);
        await r.Frames(2);
        Check.That(!WindowTests.Descendants(Card(h)).OfType<Label>().Any(l => l.Text == "Frame it"), "off every sheet the card is the table's own");
        Card(h).Hide();
    }

    [NodeTest]
    static async Task A_sheet_added_from_this_PC_arrives_picked_ready_to_be_dragged_into_place(TestRunner r)
    {
        var h = await TwoSheets(r);
        h.Table.Sheets.PickTheNextToArrive();
        h.S.Do(new AddSheet { Sheet = new SheetDef { Name = "New", Pos = new Vec2(60, 0), Size = new Size2(10, 10), Locked = false, Z = 2 } });
        await r.Frames(4);
        var added = h.S.View.Sheets.Single(s => s.Name == "New");
        Check.Equal(added.Id, h.Table.SelectedSheet, "the new sheet is the one picked");
    }

    [NodeTest]
    static async Task The_script_editor_keeps_what_was_typed_when_it_is_closed_without_saving(TestRunner r)
    {
        const string Title = "Draft test", Typed = "print('kept')";
        string? saved = "print('old')";
        CodeEdit Edit() => WindowTests.Descendants(r).OfType<AcceptDialog>().Where(d => d.Title == Title && d.Visible).SelectMany(WindowTests.Descendants).OfType<CodeEdit>().Single();
        ScriptEditor.Open(r, Title, "", saved, t => saved = t);
        await r.Frames(2);
        var edit = Edit();
        edit.Text = Typed;
        ((AcceptDialog)edit.GetWindow()).Hide();
        await r.Frames(3);
        Check.Equal("print('old')", saved, "closing saves nothing");
        ScriptEditor.Open(r, Title, "", saved, t => saved = t);
        await r.Frames(2);
        Check.Equal(Typed, Edit().Text, "but what was typed is there when the window is opened again");
        Check.That(WindowTests.Descendants(Edit().GetWindow()).OfType<Label>().Any(l => l.Text == ScriptEditor.NotSavedBefore), "and the window says it was not saved");
        ((AcceptDialog)Edit().GetWindow()).Hide();
        ScriptEditor.Drafts.Clear();
        await r.Frames(2);
    }
}
