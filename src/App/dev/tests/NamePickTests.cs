using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class NamePickTests
{
    const string U = Layers.Umpire;
    const double Near = 1e-6, LeastPixels = 40;
    const float CloseView = 9;

    static List<Stroke> Names(Session s) => (s.View.Drawings.GetValueOrDefault(U) ?? new()).Where(st => st.Text != null).ToList();

    static Vec2 Spun(Vec2 p, Vec2 about, double clockwise)
    {
        double rad = -clockwise * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        var off = p - about;
        return new Vec2(about.X + off.X * cos - off.Z * sin, about.Z + off.X * sin + off.Z * cos);
    }

    static Vec2 Out(Vec2 p, Vec2 about, double by) => new(about.X + (p.X - about.X) * by, about.Z + (p.Z - about.Z) * by);

    static void Drag(LetterTool letters, Vec2 from, Vec2 to)
    {
        letters.Press(from, false);
        for (int i = 1; i <= 4; i++) letters.Motion(new Vec2(from.X + (to.X - from.X) * i / 4, from.Z + (to.Z - from.Z) * i / 4));
        letters.Release();
    }

    static Vec2 Spot(MapLettering.NameBox box, double across) => box.At(box.HalfWide * across, 0);

    [NodeTest]
    static async Task A_name_being_written_shows_its_box_and_a_click_on_a_laid_name_opens_it_whose_knob_turns_it_corner_sizes_it_and_body_moves_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(6, 4)), CloseView);
        await r.Frames(2);
        var letters = t.Table.Letters;
        var frame = t.Table.NamePick.Frame;
        t.Table.SetTool(Tool.Letter);
        letters.LetterKept = 4;
        letters.Press(new Vec2(3, 2), false);
        letters.Release();
        Check.That(letters.Box is { HalfWide: > 0, HalfTall: > 0 }, "an empty name already shows the box it will be written in");
        letters.Type("Uru");
        await r.Frames(3);
        var typed = letters.Box!.Value;
        Check.That(typed.Centre.X > 3 && typed.Holds(new Vec2(3.2, 2.5), 0), $"the box sits round the letters, left-aligned from the click: {typed}");
        var corners = frame.Corners(typed);
        Check.That((frame.Screen(corners[0]) - frame.Screen(corners[2])).Length() > LeastPixels, $"the name is big enough on screen to take its handles apart: {(frame.Screen(corners[0]) - frame.Screen(corners[2])).Length()} px");
        letters.Commit();
        await r.Frame();
        var laid = Names(t.S).Single();

        var box = t.Table.MapLettering.BoxOf(laid)!.Value;
        letters.Press(Spot(box, 0.6), false);
        letters.Release();
        Check.That(letters.Open && letters.Editing == laid.Id && letters.Typed == "Uru", "a single click on a name opens it in place for editing");
        Check.That(t.Table.MapLettering.Hidden == laid.Id, "the laid letters hide while it is in hand");

        var held = letters.Box!.Value;
        var knob = frame.Knob(held);
        Drag(letters, knob, Spun(knob, held.Centre, 90));
        await r.Frame();
        var turned = Names(t.S).Single();
        Check.That(turned.Id == laid.Id && Math.Abs((turned.Turn ?? 0) - 90) < 1, $"the round knob turns it a quarter clockwise and lays it at once under its own id: {turned.Turn}");
        Check.That(letters.Open, "it stays in hand to go on with");

        held = letters.Box!.Value;
        var corner = frame.Corners(held)[2];
        Drag(letters, corner, Out(corner, held.Centre, 1.5));
        await r.Frame();
        var sized = Names(t.S).Single();
        Check.That(Math.Abs(sized.Letter!.Value - 6) < 0.05 && Math.Abs(letters.LetterKept - 6) < 0.05, $"a corner dragged half as far again out makes the letters half as tall again, and the panel follows: {sized.Letter}");
        Check.That((t.Table.MapLettering.BoxOf(sized)!.Value.Centre - held.Centre).Length < 0.05, "it grows about its middle");

        held = letters.Box!.Value;
        var middle = held.Centre;
        Drag(letters, middle, middle + new Vec2(5, -1));
        await r.Frame();
        var moved = Names(t.S).Single();
        Check.That((moved.Points[0] - (sized.Points[0] + new Vec2(5, -1))).Length < 0.01 && moved.Id == laid.Id, $"its body drags it whole: {moved.Points[0]}");

        letters.Type("");
        letters.Commit();
        await r.Frame();
        Check.Equal(0, Names(t.S).Count, "clearing a name's letters and laying it rubs it out");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(1, Names(t.S).Count, "and undo brings it back");
    }

    static void KeyIn(TestRunner r, Key key, bool shift = false, char unicode = '\0') =>
        r.GetViewport().PushInput(new InputEventKey { Keycode = key, Unicode = unicode, ShiftPressed = shift, Pressed = true });

    [NodeTest]
    static async Task A_ctrl_drag_makes_a_text_box_whose_lines_wrap_to_its_width_shift_enter_breaks_a_line_and_a_side_handle_sets_its_width(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(5, 5)), CloseView);
        await r.Frames(2);
        var letters = t.Table.Letters;
        var frame = t.Table.NamePick.Frame;
        t.Table.SetTool(Tool.Letter);
        t.Table.Settings.NameAlign = Stroke.AlignLeft;
        letters.LetterKept = 0.5;
        letters.Press(new Vec2(2, 8), false, boxed: true);
        letters.Motion(new Vec2(5, 6));
        letters.Motion(new Vec2(8, 4));
        letters.Release();
        Check.That(letters.Open, "a Ctrl+drag opens a text box to type in");
        letters.Type("Uru");
        await r.Frames(3);
        var one = letters.Box!.Value;
        Check.That(Math.Abs(one.HalfWide * 2 - 6) < 0.01 && Math.Abs(one.Centre.X - one.HalfWide - 2) < 0.01 && Math.Abs(one.Centre.Z + one.HalfTall - 8) < 0.01,
            $"the box spans the drag's width from its top left corner: {one}");
        letters.Type("Pogue's men camp by the old mill below the ridge");
        await r.Frames(3);
        var wrapped = letters.Box!.Value;
        Check.That(wrapped.HalfTall > one.HalfTall * 1.5 && Math.Abs(wrapped.HalfWide - one.HalfWide) < 1e-6 && Math.Abs(wrapped.Centre.Z + wrapped.HalfTall - 8) < 0.01,
            $"a long text wraps onto lines below, the box keeping its width and top: {wrapped}");
        var below = t.Table.MapLettering.Caret!.Value.At;
        Check.That(below.Z < 8 - one.HalfTall * 2.5 && below.X < 8, $"the caret follows the last line: {below}");

        letters.Type("Uru");
        KeyIn(r, Godot.Key.Enter, shift: true);
        KeyIn(r, Godot.Key.Tab);
        KeyIn(r, Godot.Key.A, unicode: 'A');
        await r.Frames(3);
        Check.Equal("Uru\nA", letters.Typed ?? "", "Shift+Enter starts a new line, and Tab types nothing");
        Check.That(t.Table.MapLettering.Caret is { } caret && caret.At.Z < 8 - one.HalfTall * 2, "and the caret goes down to it");
        KeyIn(r, Godot.Key.Enter);
        await r.Frame();
        Check.That(!letters.Open, "Enter alone lays it");
        var laid = Names(t.S).Single();
        Check.That(laid is { Text: "Uru\nA", Wrap: { } wide } && Math.Abs(wide - 6) < 1e-6, $"the text box is laid with its lines and width: {laid.Text} {laid.Wrap}");
        var box = t.Table.MapLettering.BoxOf(laid)!.Value;
        Check.That(Math.Abs(box.HalfTall * 2 - (one.HalfTall * 2) * 2) < one.HalfTall * 0.5, $"two lines stand twice as tall as one: {box.HalfTall} vs {one.HalfTall}");

        letters.Press(Spot(box, 0), false);
        letters.Release();
        var held = letters.Box!.Value;
        var side = frame.Side(held, 1);
        Drag(letters, side, side + new Vec2(2, 0));
        await r.Frame();
        var widened = Names(t.S).Single();
        var now = t.Table.MapLettering.BoxOf(widened)!.Value;
        Check.That(widened.Id == laid.Id && Math.Abs(widened.Wrap!.Value - 8) < 0.01 && Math.Abs(now.Centre.X - now.HalfWide - 2) < 0.01,
            $"the right side handle widens it to the right, its left edge kept: {widened.Wrap} {now}");
        side = frame.Side(letters.Box!.Value, -1);
        Drag(letters, side, side + new Vec2(1, 0));
        await r.Frame();
        now = t.Table.MapLettering.BoxOf(Names(t.S).Single())!.Value;
        Check.That(Math.Abs(Names(t.S).Single().Wrap!.Value - 7) < 0.01 && Math.Abs(now.Centre.X + now.HalfWide - 10) < 0.01, $"the left one narrows it from the left: {now}");
        letters.Commit();
    }

    [NodeTest]
    static async Task Shift_keeps_a_dragged_path_straight_and_a_second_line_runs_below_the_path(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(3, 0)), CloseView);
        await r.Frames(2);
        var letters = t.Table.Letters;
        t.Table.SetTool(Tool.Letter);
        t.Table.Settings.NameAlign = Stroke.AlignCentre;
        letters.LetterKept = 0.5;
        letters.Press(new Vec2(0, 0), false);
        foreach (var p in new[] { new Vec2(1, 0.4), new Vec2(3, -0.3), new Vec2(6, 0.3) }) letters.Motion(p, straight: true);
        letters.Release();
        letters.Type("Ridge");
        letters.Commit();
        await r.Frame();
        var path = Names(t.S).Single();
        Check.That(path.Points.Count == 2 && Math.Abs(path.Points[1].Z) < 1e-6 && path.Points[1].X > 5.9, $"with Shift held the path runs straight, by 15 degrees: {string.Join(" ", path.Points)}");
        var single = t.Table.MapLettering.BoxOf(path)!.Value;

        letters.Edit(path);
        letters.Type("Ridge\nwood");
        await r.Frames(3);
        Check.That(t.Table.MapLettering.Caret is { } caret && caret.At.Z < -single.HalfTall, $"the second line runs below the path: {t.Table.MapLettering.Caret?.At}");
        letters.Commit();
        await r.Frame();
        var two = t.Table.MapLettering.BoxOf(Names(t.S).Single())!.Value;
        Check.That(Names(t.S).Single().Text == "Ridge\nwood" && two.HalfTall > single.HalfTall * 1.6, $"and the name's box holds both lines: {two.HalfTall} vs {single.HalfTall}");
    }

    [NodeTest]
    static async Task A_name_takes_the_panel_s_font_weight_and_slant_and_a_turned_name_reads_along_its_turn(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        var cfg = t.Table.Settings;
        await r.Frame();
        var letters = t.Table.Letters;
        t.Table.SetTool(Tool.Letter);
        letters.LetterKept = 3;
        letters.Press(new Vec2(0, 0), false);
        letters.Release();
        letters.Type("Sadowa");
        cfg.NameFont = Stroke.FontFraktur;
        cfg.NameBold = true;
        cfg.NameItalic = true;
        letters.Restyle();
        letters.Commit();
        await r.Frame();
        var name = Names(t.S).Single();
        Check.That(name is { Font: Stroke.FontFraktur, Bold: true, Italic: true }, "the name keeps the panel's font, bold and italic");
        cfg.NameFont = Stroke.FontSerif;
        cfg.NameBold = cfg.NameItalic = false;
        letters.Press(t.Table.MapLettering.BoxOf(name)!.Value.Centre, false);
        letters.Release();
        Check.That(cfg is { NameFont: Stroke.FontFraktur, NameBold: true, NameItalic: true }, "opening a name takes its font back into the panel");
        letters.Drop();
        var flat = t.Table.MapLettering.BoxOf(name)!.Value;
        var turned = NameEdits.Turned(name, flat.Centre, 90);
        var box = t.Table.MapLettering.BoxOf(turned)!.Value;
        Check.That(Math.Abs(box.Along.X) < Near && box.Along.Z < 0 && Math.Abs(box.HalfWide - flat.HalfWide) < 1e-3 && (box.Centre - flat.Centre).Length < 1e-3,
            $"turned a quarter clockwise it reads down the map, the same size, in the same place: {box}");
        t.S.Do(NameEdits.Relaid(t.S.View.DrawLayer(U), U, name, turned));
        await r.Frames(4);
        var quads = t.Table.MapLettering.Root.GetChildren().OfType<MeshInstance3D>().ToList();
        Check.That(quads.Count > 0 && quads.All(q => Math.Abs(q.Transform.Basis.X.X) < 0.01), "its letters lie turned on the map");
    }

    static InputEventMouseButton Button(HudTests.Rig h, Vec2 g, bool pressed, bool twice = false)
    {
        var at = h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(g));
        return new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, DoubleClick = twice, ButtonMask = pressed ? MouseButtonMask.Left : 0,
        };
    }

    static async Task Click(TestRunner r, HudTests.Rig h, Vec2 g, bool twice = false)
    {
        h.Table.Router.OnMouseButton(Button(h, g, true, twice));
        h.Table.Router.OnMouseButton(Button(h, g, false));
        await r.Frames(2);
    }

    static InputEventKey Key(Key key, bool shift = false) => new() { Keycode = key, Pressed = true, ShiftPressed = shift };

    static async Task<(HudTests.Rig h, Stroke name)> Named(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0);
        h.S.Do(new Draw { Layer = U, Stroke = new Stroke { Points = { new(2, 2) }, Text = "Uru-Anna", Width = 0, Letter = 3, Align = Stroke.AlignLeft } });
        h.Table.Rig.Frame(Coords.ToWorld(new Vec2(8, 4)), CloseView * 2);
        await r.Frames(3);
        return (h, Names(h.S).Single());
    }

    static Stroke Now(HudTests.Rig h, string id) => Names(h.S).Single(st => st.Id == id);

    [NodeTest]
    static async Task A_click_picks_a_name_whose_keys_turn_size_nudge_and_rub_it_out_and_a_double_click_writes_in_it(TestRunner r)
    {
        var (h, name) = await Named(r);
        var box = h.Table.MapLettering.BoxOf(name)!.Value;
        await Click(r, h, Spot(box, 0.5));
        Check.Equal(name.Id, h.Table.PickedName, "a click on a name picks it");
        var keys = h.Table.Keys;
        keys.OnKey(Key(Godot.Key.E));
        Check.That(Math.Abs((Now(h, name.Id).Turn ?? 0) - 15) < Near, "E turns it 15° clockwise, as it turns a piece");
        keys.OnKey(Key(Godot.Key.Q));
        keys.OnKey(Key(Godot.Key.Q));
        Check.That(Math.Abs((Now(h, name.Id).Turn ?? 0) - 345) < Near, "Q turns it back the other way");
        keys.OnKey(Key(Godot.Key.Equal));
        Check.That(Math.Abs(Now(h, name.Id).Letter!.Value - 3.3) < 1e-6, "= makes its letters a tenth taller");
        var was = h.Table.MapLettering.BoxOf(Now(h, name.Id))!.Value.Centre;
        keys.OnKey(Key(Godot.Key.Right, shift: true));
        var step = h.Table.Sheets.ScreenStep(Godot.Key.Right, 1);
        Check.That((h.Table.MapLettering.BoxOf(Now(h, name.Id))!.Value.Centre - (was + step)).Length < 1e-6, "Shift+Right nudges it a whole unit");
        Check.Equal(name.Id, h.Table.PickedName, "it stays picked through every change");

        box = h.Table.MapLettering.BoxOf(Now(h, name.Id))!.Value;
        await Click(r, h, box.Centre, twice: true);
        Check.That(h.Table.Tool == Tool.Letter && h.Table.Letters.Editing == name.Id, "a double-click on it writes in it with the name tool");
        h.Table.Letters.Drop();
        h.Table.SetTool(Tool.Select);
        await r.Frames(2);
        h.Table.PickName(name.Id);
        keys.OnKey(Key(Godot.Key.Delete));
        Check.That(Names(h.S).Count == 0 && h.Table.PickedName == null, "Delete rubs out a picked name");
        h.S.UndoLast();
        await r.Frames(2);
        await Click(r, h, new Vec2(-30, -30));
        Check.That(h.Table.PickedName == null, "a click off it lets it go");
    }

    [NodeTest]
    static async Task A_picked_name_is_dragged_by_its_body_turned_by_its_knob_and_sized_by_a_corner_with_the_select_tool(TestRunner r)
    {
        var (h, name) = await Named(r);
        h.Table.PickName(name.Id);
        await r.Frames(2);
        var pick = h.Table.NamePick;
        var box = pick.Box!.Value;
        Vector2 Screen(Vec2 p) => h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(p));
        void Gesture(Vec2 from, Vec2 to)
        {
            h.Table.Router.OnMouseButton(Button(h, from, true));
            for (int i = 1; i <= 4; i++)
            {
                var at = new Vec2(from.X + (to.X - from.X) * i / 4, from.Z + (to.Z - from.Z) * i / 4);
                h.Table.SelectTool.Motion(new InputEventMouseMotion { Position = Screen(at), ButtonMask = MouseButtonMask.Left }, at);
            }
            h.Table.Router.OnMouseButton(Button(h, to, false));
        }
        var knob = pick.Frame.Knob(box);
        Gesture(knob, Spun(knob, box.Centre, 90));
        await r.Frames(2);
        Check.That(Math.Abs((Now(h, name.Id).Turn ?? 0) - 90) < 1, $"the knob turns it: {Now(h, name.Id).Turn}");
        box = pick.Box!.Value;
        var corner = pick.Frame.Corners(box)[0];
        Gesture(corner, Out(corner, box.Centre, 2));
        await r.Frames(2);
        Check.That(Math.Abs(Now(h, name.Id).Letter!.Value - 6) < 0.05, $"a corner sizes it: {Now(h, name.Id).Letter}");
        box = pick.Box!.Value;
        var from = box.Centre;
        Gesture(from, from + new Vec2(-4, 3));
        await r.Frames(2);
        Check.That((pick.Box!.Value.Centre - (from + new Vec2(-4, 3))).Length < 0.01, "its body drags it");
        h.S.UndoLast();
        await r.Frames(2);
        Check.That((pick.Box!.Value.Centre - from).Length < 0.01 && h.Table.PickedName == name.Id, "one undo takes back one drag and the name stays picked");
    }

    [NodeTest]
    static async Task The_turn_and_resize_tool_turns_and_sizes_a_picked_name_and_its_window_takes_the_name_s_numbers(TestRunner r)
    {
        var (h, name) = await Named(r);
        h.Table.PickName(name.Id);
        h.Table.SetTool(Tool.Transform);
        await r.Frames(3);
        var gizmo = h.Table.Gizmo;
        var cam = h.Table.Rig.Cam;
        var centre = Coords.ToWorld(h.Table.NamePick.Box!.Value.Centre);
        var knob = gizmo.RotateKnobWorld();
        Check.Equal(Gizmo.Handle.Rotate, gizmo.At(cam.UnprojectPosition(knob)), "the ring's round knob is over a picked name");
        gizmo.Begin(Gizmo.Handle.Rotate, cam.UnprojectPosition(knob));
        var d = knob - centre;
        float quarter = Mathf.Atan2(d.Z, d.X) + Mathf.Pi / 2, reach = new Vector2(d.X, d.Z).Length();
        gizmo.Drag(cam.UnprojectPosition(centre + new Vector3(Mathf.Cos(quarter), 0, Mathf.Sin(quarter)) * reach), snap: true);
        gizmo.End();
        await r.Frames(2);
        Check.That(Math.Abs((Now(h, name.Id).Turn ?? 0) - 90) < Near, $"the ring turns the name a quarter clockwise, like a piece: {Now(h, name.Id).Turn}");
        var fields = WindowTests.Descendants(h.Hud.Numbers.Panel.Body).OfType<SpinBox>().ToList();
        Check.Equal(4, fields.Count, "the window takes X, Y, Turn and Size for a name");
        fields[3].Value = 5;
        await r.Frames(2);
        Check.That(Math.Abs(Now(h, name.Id).Letter!.Value - 5) < 1e-6, "Size sets its letters' height");
        fields = WindowTests.Descendants(h.Hud.Numbers.Panel.Body).OfType<SpinBox>().ToList();
        fields[2].Value = 45;
        await r.Frames(2);
        Check.That(Math.Abs((Now(h, name.Id).Turn ?? 0) - 45) < 1e-6, "Turn sets which way it runs");
        fields = WindowTests.Descendants(h.Hud.Numbers.Panel.Body).OfType<SpinBox>().ToList();
        fields[0].Value = 10;
        await r.Frames(2);
        Check.That(Math.Abs(h.Table.NamePick.Box!.Value.Centre.X - 10) < 1e-6, "X puts its middle there");
    }

    [NodeTest]
    static async Task Right_click_on_a_name_picks_it_and_offers_to_edit_or_remove_it(TestRunner r)
    {
        var (h, name) = await Named(r);
        var at = h.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(h.Table.MapLettering.BoxOf(name)!.Value.Centre));
        h.Hud.Menus.Show(at, null);
        await r.Frames(2);
        var card = WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().First(p => p.Visible);
        var labels = WindowTests.Descendants(card).OfType<Label>().Select(l => l.Text).ToList();
        Check.That(labels.Contains("Uru-Anna") && labels.Contains("Edit") && labels.Contains("Remove") && h.Table.PickedName == name.Id,
            "the card names it, offers Edit and Remove, and picks it: " + string.Join(" | ", labels));
        var remove = WindowTests.Descendants(card).OfType<Button>().First(b => WindowTests.Descendants(b).OfType<Label>().Any(l => l.Text == "Remove"));
        remove.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(0, Names(h.S).Count, "Remove rubs it out");
    }
}
