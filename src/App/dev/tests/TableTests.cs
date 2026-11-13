using Godot;
using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Client;
using KTable.Client.Studio;

namespace KTable.App;

static class TableTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task Every_piece_on_the_board_is_drawn_by_a_node_of_its_own(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        Check.Equal(3, t.Table.Pieces.Count, "piece nodes");
        Check.That(new[] { "p1", "p2", "p3" }.All(id => t.Node(id).Piece.Id == id), "each node draws its own piece");
    }

    [NodeTest]
    static async Task A_move_reaches_the_piece_s_node_and_undo_takes_it_back(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(5, 2) });
        await r.Frame();
        Check.Equal(new Vec2(5, 2), t.Node("p1").Piece.Pos, "moved");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(new Vec2(1, 0), t.Node("p1").Piece.Pos, "taken back");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task Ctrl_y_puts_back_what_ctrl_z_took_back(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        void Press(Key key) => t.Table.Keys.OnKey(new InputEventKey { Keycode = key, CtrlPressed = true, Pressed = true });
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(5, 2) });
        await r.Frame();
        Press(Key.Z);
        await r.Frame();
        Check.Equal(new Vec2(1, 0), t.Node("p1").Piece.Pos, "taken back");
        Press(Key.Y);
        await r.Frame();
        Check.Equal(new Vec2(5, 2), t.Node("p1").Piece.Pos, "put back");
        Press(Key.Z);
        await r.Frame();
        Check.Equal(new Vec2(1, 0), t.Node("p1").Piece.Pos, "taken back again");
        Press(Key.Y);
        Press(Key.Y);
        await r.Frame();
        Check.Equal(new Vec2(5, 2), t.Node("p1").Piece.Pos, "a second Ctrl+Y has nothing left");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task Ctrl_z_and_ctrl_y_reach_past_a_pasted_or_cut_piece_whose_script_draws_a_button(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        void Press(Key key) => t.Table.Keys.OnKey(new InputEventKey { Keycode = key, CtrlPressed = true, Pressed = true });
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(5, 2) });
        t.S.Do(new SetScript { Layer = U, Id = "p1", Text = "function on_load() self.button({ label = 'go', x = 0, z = 0, on_click = 'go' }) end\nfunction go() end" });
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        Press(Key.C);
        t.Table.Clip.Paste(new Vec2(9, 9));
        await r.Frame();
        Check.Equal(4, t.S.View.Layers[U].Count, "pasted");
        Press(Key.Z);
        await r.Frame();
        Check.Equal(3, t.S.View.Layers[U].Count, "Ctrl+Z takes the pasted piece back");
        Press(Key.Y);
        await r.Frame();
        Check.Equal(4, t.S.View.Layers[U].Count, "Ctrl+Y puts it back");
        Press(Key.Z);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        Press(Key.X);
        await r.Frame();
        Check.That(t.S.View.Find(U, "p1") == null, "cut");
        Press(Key.Z);
        await r.Frame();
        Check.That(t.S.View.Find(U, "p1") != null, "Ctrl+Z puts the cut piece back");
        Press(Key.Z);
        await r.Frame();
        Check.That(t.S.View.Find(U, "p1")!.Script == null, "the next Ctrl+Z takes back the script");
        Press(Key.Z);
        await r.Frame();
        Check.Equal(new Vec2(2, 0), t.Node("p2").Piece.Pos, "and the one before it the move");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task Holding_ctrl_z_or_ctrl_y_keeps_rolling_through_the_steps(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        void Hold(Key key) => t.Table.Router.Handle(new InputEventKey { Keycode = key, CtrlPressed = true, Pressed = true, Echo = true });
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(5, 2) });
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(7, 4) });
        await r.Frame();
        Hold(Key.Z);
        Hold(Key.Z);
        await r.Frame();
        Check.Equal(new Vec2(1, 0), t.Node("p1").Piece.Pos, "held down, Ctrl+Z keeps taking steps back");
        Hold(Key.Y);
        Hold(Key.Y);
        await r.Frame();
        Check.Equal(new Vec2(7, 4), t.Node("p1").Piece.Pos, "and Ctrl+Y keeps putting them back");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task Selecting_marks_the_nodes_and_tells_whoever_listens(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        int told = 0;
        t.Table.SelectionChanged += () => told++;
        t.Table.Selection.Set(new[] { "p1", "p3" });
        Check.Equal("p1,p3", string.Join(",", t.Table.Selection.Nodes.Select(n => n.Piece.Id).OrderBy(x => x)), "selected");
        t.Table.Selection.Toggle(t.Node("p1"));
        Check.Equal("p3", string.Join(",", t.Table.Selection.Nodes.Select(n => n.Piece.Id)), "after taking p1 out");
        t.Table.Selection.Set(System.Array.Empty<string>());
        Check.That(!t.Table.Selection.Nodes.Any(), "nothing selected");
        Check.Equal(3, told, "times told");
    }

    [NodeTest]
    static async Task A_locked_piece_can_be_picked_out_but_not_moved(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.S.Do(new SetLocked { Layer = U, Id = "p2", Value = true });
        await r.Frame();
        t.Table.Selection.SelectAllEditable();
        Check.Equal(2, t.Table.Selection.Nodes.Count(), "picked out");
        Check.Equal("p1", string.Join(",", t.Table.Selection.Movable.Select(n => n.Piece.Id)), "movable");
    }

    [NodeTest]
    static async Task A_locked_piece_is_left_out_of_a_selection_box_and_only_the_menu_deletes_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.S.Do(new SetLocked { Layer = U, Id = "p2", Value = true });
        await r.Frame();
        Vector2 Screen(Vec2 at) => t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(at));
        Vector2 from = Screen(new Vec2(-0.5, -2)), to = Screen(new Vec2(4.5, 2));
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
        await r.Frame();
        Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left });
        await r.Frame();
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
        await r.Frames(2);
        Check.Equal("p1,p3", string.Join(",", t.Table.Selection.Ids.OrderBy(x => x)), "the box takes the free pieces and leaves the locked one");

        void Key(Godot.Key key, bool ctrl = false) => t.Table.Keys.OnKey(new InputEventKey { Keycode = key, Pressed = true, CtrlPressed = ctrl });
        t.Table.Selection.Set(new[] { "p1", "p2" });
        Key(Godot.Key.X, ctrl: true);
        await r.Frame();
        Check.Equal(1, t.Table.Clipped, "Ctrl+X cuts only the free piece");
        Check.That(t.S.View.Find(U, "p1") == null && t.S.View.Find(U, "p2") != null, "and the locked one stays on the table");

        t.Table.Selection.Set(new[] { "p2", "p3" });
        Key(Godot.Key.Delete);
        await r.Frame();
        Check.That(t.S.View.Find(U, "p3") == null && t.S.View.Find(U, "p2") != null, "the Delete key leaves the locked piece");

        t.Table.Selection.Set(new[] { "p2" });
        t.Table.DeleteSelection();
        await r.Frame();
        Check.That(t.S.View.Find(U, "p2") == null, "the menu's Delete removes it");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task Copy_and_paste_makes_new_pieces_round_the_place_and_picks_them_out(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1", "p3" });
        t.Table.Clip.Copy();
        t.Table.Clip.Paste(new Vec2(10, 10));
        await r.Frame();
        Check.Equal(5, t.Table.Pieces.Count, "pieces after the paste");
        var pasted = t.S.View.Layers[U].Where(p => !p.Id.StartsWith('p')).OrderBy(p => p.Pos.X).ToList();
        Check.Equal(new Vec2(9, 10), pasted[0].Pos, "first copy, as far left of the place as p1 was of the pair's middle");
        Check.Equal(new Vec2(11, 10), pasted[1].Pos, "second copy");
        t.Table.Clip.SelectArrived();
        Check.Equal(string.Join(",", pasted.Select(p => p.Id).OrderBy(x => x)), string.Join(",", t.Table.Selection.Ids.OrderBy(x => x)), "the copies picked out");
    }

    [NodeTest]
    static async Task A_copy_waits_for_another_scenario_and_brings_the_pack_it_needs_with_it(TestRunner r)
    {
        var from = OfflineTable.Make(r, pieces: 0, packs: new[] { "std.dice" }, prepare: StandardPacks.Ensure);
        await r.Frame();
        from.S.Do(new SpawnPiece { Layer = U, Def = "std.dice.d6", Id = "d1", Pos = new Vec2(0, 0) });
        await r.Frames(3);
        from.Table.Selection.Set(new[] { "d1" });
        from.Table.Clip.Copy();
        var into = OfflineTable.Make(r, pieces: 1, prepare: StandardPacks.Ensure);
        into.Table.Clip.BringPack = id => into.S.Do(new AddPack { Id = id });
        await r.Frame();
        Check.That(!into.S.View.Packs.Contains("std.dice"), "the other scenario has no dice pack of its own");
        Check.Equal(1, into.Table.Clip.Count, "pieces on the clipboard");
        into.Table.Clip.Paste(new Vec2(4, 4));
        await r.Frames(3);
        Check.That(into.S.View.Packs.Contains("std.dice"), "the pack came in with the paste");
        var pasted = into.S.View.Layers[U].Where(p => p.Def == "std.dice.d6").ToList();
        Check.Equal(1, pasted.Count, "dice pasted");
        Check.Equal(new Vec2(4, 4), pasted[0].Pos, "where the paste asked for");
        Check.That(into.Errors.Count == 0, "no command refused: " + string.Join("; ", into.Errors));
    }

    [NodeTest]
    static async Task A_converted_module_s_pieces_bring_their_pack_and_are_still_drawn_after_the_game_reopens(TestRunner r)
    {
        const string Pack = "tts.sleepwalkers-balkan", Kind = "sleepwalkers.1-kd";
        static void Install(KTable.Core.PackageStore store)
        {
            System.IO.Directory.CreateDirectory(store.PackageDir(Pack));
            KTable.Core.Json.Save(System.IO.Path.Combine(store.PackageDir(Pack), "pack.json"), new PackManifest { Id = Pack, Name = "Sleepwalkers-Balkan", Pieces = { new PieceDef { Id = Kind, Name = "1.KD" } } });
        }
        var from = OfflineTable.Make(r, pieces: 0, packs: new[] { Pack }, prepare: Install);
        await r.Frame();
        from.S.Do(new SpawnPiece { Layer = U, Def = Kind, Id = "k1", Pos = new Vec2(0, 0) });
        await r.Frames(3);
        from.Table.Selection.Set(new[] { "k1" });
        from.Table.Clip.Copy();
        var into = OfflineTable.Make(r, pieces: 1, prepare: Install);
        into.Table.Clip.BringPack = id => into.S.Do(new AddPack { Id = id });
        await r.Frame();
        into.Table.Clip.Paste(new Vec2(4, 4));
        await r.Frames(3);
        Check.That(into.S.View.Packs.Contains(Pack), "the module's pack came in with the paste though its kinds are not named after it");
        var saved = KTable.Core.Json.Clone(into.Server.State);
        var reopened = OfflineTable.Make(r, pieces: 0, prepare: Install, arrange: g => { g.Packs = saved.Packs; g.Layers = saved.Layers; });
        await r.Frames(3);
        var pasted = reopened.S.View.Layers[U].Single(p => p.Def == Kind);
        Check.That(reopened.Table.Pieces.NodeOf(U, pasted.Id) is { Visible: true }, "the pasted piece is drawn after the game reopens");
        Check.That(reopened.Errors.Count == 0 && into.Errors.Count == 0, "no command refused: " + string.Join("; ", into.Errors.Concat(reopened.Errors)));
    }

    [NodeTest]
    static async Task A_press_counts_on_a_piece_where_its_shape_is_as_it_lies_or_as_it_would_lie(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        var n = t.Node("p1");
        double hw = n.Footprint.X / 2, hh = n.Footprint.Y / 2;
        var corner = new Vec2(1 + hw * 0.95, hh * 0.95);
        Check.That(n.Contains(new Vec2(1, 0)) && n.Contains(corner), "the middle and a corner of the block");
        Check.That(!n.Contains(new Vec2(1 + hw * 1.1, 0)), "just past its side");
        Check.That(!n.ContainsAt(corner, new Vec2(1, 0), 45), "the corner, with the block turned an eighth");
        Check.That(n.ContainsAt(new Vec2(5, 5), new Vec2(5, 5), 0), "where a drop would put it");
    }

    [NodeTest]
    static async Task A_piece_with_another_on_it_is_picked_where_the_eye_sees_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        var low = t.Node("p1");
        double thick = low.Thickness, w = low.Footprint.X;
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(1 + w / 2, 0), Y = thick });
        await r.Frames(3);
        var cam = t.Table.Rig.Cam;
        PieceNode? PickAt(Vec2 at, double y)
        {
            var screen = cam.UnprojectPosition(Coords.ToWorld(at, (float)y));
            return t.Table.Ground(screen) is { } g ? t.Table.Pieces.Pick(g, screen: screen) : null;
        }
        Check.Equal("p1", PickAt(new Vec2(1 - w * 0.4, 0), thick)?.Piece.Id, "the uncovered half of the lower block");
        Check.Equal("p1", PickAt(new Vec2(1 + w * 0.1, -low.Footprint.Y / 2), thick * 0.5)?.Piece.Id, "the lower block's side, under the one on it");
        Check.Equal("p2", PickAt(new Vec2(1 + w * 0.9, 0), thick * 2)?.Piece.Id, "the block on top");
    }

    [NodeTest]
    static async Task A_piece_redrawn_after_its_kind_changes_still_takes_a_click(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        var before = t.Node("p1");
        t.Table.RebuildPiecesOf(before.Piece.Def);
        await r.Frames(2);
        var after = t.Node("p1");
        Check.That(after != before, "a new node draws it");
        var screen = t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(after.Piece.Pos, after.Thickness));
        Check.Equal("p1", (t.Table.Ground(screen) is { } g ? t.Table.Pieces.Pick(g, screen: screen) : null)?.Piece.Id, "a click on it");
        Check.Equal("p1", t.Table.Pieces.Pick(after.Piece.Pos)?.Piece.Id, "a pick at its middle");
    }

    [NodeTest]
    static async Task A_pinned_ruler_line_stays_through_every_tool_until_its_pin_is_clicked_again(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        var ruler = t.Table.Ruler;
        void Measure(Vec2 to)
        {
            t.Table.SetTool(Tool.Ruler);
            ruler.A = new Vec2(0, 0);
            ruler.B = to;
        }
        Measure(new Vec2(4, 0));
        await r.Frames(2);
        var pins = t.Table.Labels.GetChildren().OfType<Button>().ToList();
        Check.That(pins.Count == 1 && pins[0].Visible, "a pin beside the reading");
        pins[0].EmitSignal(BaseButton.SignalName.Pressed);
        Check.That(ruler.Pinned.Count == 1 && ruler.A == null, "pinned, the line leaves the ruler for the table");
        Check.Equal("0,0,4,0", string.Join(",", t.Table.Settings.RulerPins[t.S.View.ScenarioId].Single()), "kept on this PC for this table");
        Measure(new Vec2(0, 3));
        t.Table.SetTool(Tool.Transform);
        t.Table.SetTool(Tool.Ruler);
        t.Table.SetTool(Tool.Select);
        await r.Frames(2);
        Check.That(ruler.Pinned.Count == 1 && ruler.A == null, "other tools and the ruler again leave the pinned line alone and take the loose one");
        Check.That(t.Table.Labels.TextOf("ruler-pinned-0") != null, "its reading stays");
        pins = t.Table.Labels.GetChildren().OfType<Button>().ToList();
        Check.That(pins[0].Visible, "and its pin");
        pins[0].EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(ruler.Pinned.Count == 0 && t.Table.Labels.TextOf("ruler-pinned-0") == null && pins.All(p => !p.Visible), "its pin clicked again, it goes");
        Check.That(!t.Table.Settings.RulerPins.ContainsKey(t.S.View.ScenarioId), "and is forgotten");
    }

    [NodeTest]
    static async Task A_deleted_piece_s_node_goes_and_comes_back_with_undo(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.S.Do(new DeletePiece { Layer = U, Id = "p2" });
        await r.Frame();
        Check.That(t.Table.Pieces.NodeOf(U, "p2") == null, "node gone");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(new Vec2(2, 0), t.Node("p2").Piece.Pos, "back where it was");
    }

    [NodeTest]
    static async Task Turning_and_resizing_the_selection_waits_then_goes_out_as_one_change_each(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.S.Do(new SetLocked { Layer = U, Id = "p3", Value = true });
        t.Table.Selection.Set(new[] { "p1", "p2", "p3" });
        int sent = t.S.MyUndoable.Count;
        t.Table.Poses.RotateSmooth(30);
        t.Table.Poses.RotateSmooth(-400);
        t.Table.Poses.ScaleSmooth(2);
        Check.Equal(sent, t.S.MyUndoable.Count, "nothing sent while the wheel turns");
        Check.Equal(0.0, t.S.View.Find(U, "p1")!.RotDeg, "the game not changed yet");
        t.Table.Poses.Flush();
        await r.Frame();
        foreach (var id in new[] { "p1", "p2" })
        {
            var p = t.S.View.Find(U, id)!;
            Check.Equal(350.0, p.RotDeg, id + " turned");
            Check.Equal(2.0, p.Scale, id + " resized");
        }
        Check.Equal(0.0, t.S.View.Find(U, "p3")!.RotDeg, "the locked piece stays as it was");
        Check.Equal(sent + 1, t.S.MyUndoable.Count, "one change to take back");
    }

    [NodeTest]
    static async Task A_wall_traced_in_the_pov_tool_leaves_out_what_is_behind_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        Check.That(t.Table.Sighting.Begin(10), "the tool begins on the picked piece");
        var d = t.Table.Draft!;
        Check.Equal(new Vec2(1, 0), d.Center, "centred on its piece");
        Check.Equal("p1,p2,p3", string.Join(",", d.Included.OrderBy(x => x)), "all in sight before any wall");
        t.Table.Sighting.Press(new Vec2(1.5, -3));
        for (int i = -2; i <= 3; i++) t.Table.Sighting.Move(new Vec2(1.5, i));
        t.Table.Sighting.Release();
        Check.Equal("p1", string.Join(",", d.Included.OrderBy(x => x)), "what is behind the wall left out");
        t.Table.Sighting.UndoStroke();
        Check.Equal(3, t.Table.Draft!.Included.Count, "the wall taken back");
        t.Table.Sighting.Cancel();
        Check.That(t.Table.Draft == null, "nothing left when the tool is put down");
    }

    [NodeTest]
    static async Task A_piece_the_wall_cuts_through_stays_in_the_pov_picture(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        Check.That(t.Table.Sighting.Begin(10), "the tool begins on the picked piece");
        t.Table.Sighting.Press(new Vec2(2.2, -3));
        for (int i = -2; i <= 3; i++) t.Table.Sighting.Move(new Vec2(2.2, i));
        t.Table.Sighting.Release();
        Check.Equal("p1,p2", string.Join(",", t.Table.Draft!.Included.OrderBy(x => x)), "the piece whose centre is behind the wall but whose edge is in sight is kept, the one wholly behind it is not");
        t.Table.Sighting.Cancel();
    }

    [NodeTest]
    static async Task An_outline_drawn_by_hand_in_the_pov_tool_is_the_picture_with_no_line_traced(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        Check.That(t.Table.Sighting.Begin(10), "the tool begins on the picked piece");
        t.Table.Sighting.Press(new Vec2(1.5, -3));
        for (int i = -2; i <= 3; i++) t.Table.Sighting.Move(new Vec2(1.5, i));
        t.Table.Sighting.Release();
        t.Table.SetSightingFormat(SightingFormat.Drawn);
        var d = t.Table.Draft!;
        Check.That(d is { Drawn: true, Square: true, Edged: false } && t.Table.Sighting.StrokesDrawn == 0, "drawn starts on the whole square, the traced wall set aside");
        Check.Equal(3, d.Included.Count, "all in the square before anything is drawn");
        Vec2[] loop = { new(0.5, -0.5), new(1.5, -0.5), new(2.5, -0.5), new(2.5, 0.5), new(1.5, 0.5), new(0.5, 0.5) };
        t.Table.Sighting.Press(loop[0]);
        foreach (var p in loop.Skip(1)) t.Table.Sighting.Move(p);
        t.Table.Sighting.Release();
        Check.That(d.Edged, "the stroke closes into the outline");
        Check.Equal("p1,p2", string.Join(",", d.Included.OrderBy(x => x)), "only what the outline takes in is in the picture");
        t.Table.Sighting.Press(new Vec2(-1, -1));
        t.Table.Sighting.Move(new Vec2(-0.5, -1));
        t.Table.Sighting.Release();
        Check.Equal(1, t.Table.Sighting.StrokesDrawn, "a click draws no outline");
        t.Table.Sighting.UndoStroke();
        Check.Equal(3, d.Included.Count, "undo goes back to the whole square");
        t.Table.Sighting.RedoStroke();
        Check.Equal(2, d.Included.Count, "redo puts the outline back");
        t.Table.SetSightingFormat(SightingFormat.Round);
        Check.Equal("p1", string.Join(",", d.Included.OrderBy(x => x)), "back to round, the traced wall cuts again");
        t.Table.SetSightingFormat(SightingFormat.Drawn);
        Check.Equal(2, d.Included.Count, "and back to drawn, the outline is still there");
        t.Table.Sighting.Cancel();
    }

    [NodeTest]
    static async Task The_reach_is_outlined_round_or_square_before_a_line_is_traced(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        Check.That(t.Table.Draft is { Square: false, Walls.Count: 0 }, "a round reach with nothing traced yet");
        Check.That(t.Table.Sighting.OutlineParts > 0, "the round reach is drawn as a line");
        t.Table.Draft!.Format = SightingFormat.Square;
        t.Table.Draft.Recompute(t.Table);
        t.Table.ShowDraft();
        await r.Frame();
        Check.That(t.Table.Sighting.OutlineParts > 0, "so is the square one");
        t.Table.Sighting.Cancel();
    }

    [NodeTest]
    static async Task First_person_puts_the_eye_on_the_piece_and_a_direction_held_turns_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        var node = t.Node("p2");
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = node.Piece.Pos, RotDeg = 90 });
        await r.Frame();
        t.Table.Selection.Set(new[] { "p2" });
        t.Table.SetTool(Tool.Sighting);
        Check.That(t.Table.Draft != null, "the tool begins on the picked piece");
        var rig = t.Table.Rig;
        Check.That(rig.TopDown && rig.Eye == null, "2D first: the map from above");
        rig.Yaw = 0;
        t.Table.Sighting.SetFirstPerson(true);
        await r.Frame();
        var front = new Vec2(-1, 0);
        var edge = node.Piece.Pos + new Vec2(front.X * node.Footprint.Y / 2, front.Z * node.Footprint.Y / 2);
        var eyeOnMap = Coords.FromWorld(rig.Eye ?? Vector3.Zero);
        Check.That(rig.Eye is { } eye && Mathf.Abs(eyeOnMap.X - edge.X) < 0.01 && Mathf.Abs(eyeOnMap.Z - edge.Z) < 0.01 && eye.Y > node.GlobalPosition.Y + node.Thickness,
            "the eye stands on the piece's front edge, above its top");
        var look = Coords.FromWorld(-rig.Cam.GlobalBasis.Z);
        Check.That(look.X * front.X + look.Z * front.Z > 0.95, "it looks out of the piece's front");
        Check.That(!rig.TopDown && rig.Cam.Projection == Camera3D.ProjectionType.Perspective && Mathf.IsEqualApprox(rig.Cam.Fov, (float)SightingDraft.NormalLens), "a perspective view through the normal lens");
        Check.That(rig.Cam.GlobalPosition.IsEqualApprox(rig.Eye!.Value), "the camera is at the eye");
        t.Table.Sighting.SetLens(SightingDraft.WholeCircleLens);
        Check.That(Mathf.IsEqualApprox(rig.Cam.Fov, (float)SightingDraft.WholeCircleLens), "a wider lens widens the view");
        t.Table.Sighting.Face("E");
        Check.Equal("E", t.Table.Draft!.Facing, "east is held");
        Check.That(Mathf.IsEqualApprox(Mathf.Wrap(rig.YawGoal, -Mathf.Pi, Mathf.Pi), Mathf.Pi / 2), "the view turns to look along +X");
        t.Table.Sighting.Face("N");
        Check.That(Mathf.IsEqualApprox(Mathf.Abs(Mathf.Wrap(rig.YawGoal, -Mathf.Pi, Mathf.Pi)), Mathf.Pi), "north looks along -Z");
        t.Table.Sighting.SetEyeHeight(0);
        await r.Frame();
        Check.That(rig.Eye is { } onTheGround && Mathf.Abs(onTheGround.Y - node.GlobalPosition.Y) < 0.01f, "the eye slider takes the view down to the ground");
        t.Table.Sighting.SetEyeHeight(t.Table.SightingEyeHighest * 2);
        await r.Frame();
        Check.That(rig.Eye is { } high && Mathf.Abs(high.Y - node.GlobalPosition.Y - (float)t.Table.SightingEyeHighest) < 0.01f, "it goes no higher than its own ceiling");
        rig.Orbit(new Vector2(40, 0));
        t.Table.Sighting.Unlock();
        Check.That(t.Table.Draft!.Facing == null, "dragging lets the direction go");
        float yawBefore = rig.Yaw;
        rig.PanWorld(new Vector3(5, 0, 5));
        rig.Zoom(0.5f);
        Check.That(rig.Cam.GlobalPosition.IsEqualApprox(rig.Eye!.Value) && rig.Yaw == yawBefore, "panning and zooming do not move the eye");
        t.Table.Sighting.SetFirstPerson(false);
        await r.Frame();
        Check.That(rig.Eye == null && rig.TopDown && Mathf.IsEqualApprox(rig.Cam.Fov, CameraRig.TableFov), "back to the map from above, through the table's own lens");
        t.Table.Sighting.SetFirstPerson(true);
        t.Table.Sighting.Cancel();
        Check.That(rig.Eye == null && t.Table.Draft == null, "putting the tool down leaves the eye");
    }

    [NodeTest]
    static async Task A_text_line_prints_in_its_own_colour_with_an_edge_that_stands_out_from_it(TestRunner r)
    {
        var inked = new PieceDef { Id = "inked", Name = "Inked block" };
        inked.TextLines.Add(new TextLineDef { Name = "Unit", Default = "I/12", Color = "#1d1d1bff", Place = new PrintPlace { Side = PrintPlace.Front, X = 0.1, Y = 0.1, W = 0.8, H = 0.3 } });
        inked.TextLines.Add(new TextLineDef { Name = "Note", Default = "reserve", Place = new PrintPlace { Side = PrintPlace.Front, X = 0.1, Y = 0.6, W = 0.8, H = 0.3 } });
        var t = OfflineTable.Make(r, pieces: 1, prepare: store => store.RegisterDef(new PackManifest { Id = "inked", Name = "Inked" }, inked),
            arrange: g => g.Layers[Layers.Umpire][0].Def = "inked");
        await r.Frame();
        var labels = t.Node("p1").FindChildren("*", nameof(Label3D), true, false).OfType<Label3D>().ToList();
        var dark = labels.FirstOrDefault(l => l.Text == "I/12") ?? throw new TestFailure("the coloured line prints");
        var plain = labels.FirstOrDefault(l => l.Text == "reserve") ?? throw new TestFailure("the plain line prints");
        Check.That(dark.Modulate.IsEqualApprox(Hex.To("#1d1d1bff", Colors.White)), "a text line prints in its own colour");
        Check.That(dark.OutlineModulate.Luminance > 0.5f, "dark ink gets a light edge");
        Check.That(plain.Modulate.IsEqualApprox(FaceInk.TextColor) && plain.OutlineModulate.Luminance < 0.5f, "a line with no colour prints light with a dark edge, as before");
    }

    [NodeTest]
    static async Task Hidden_text_leaves_the_text_layer_out_of_the_view_and_the_picture(TestRunner r)
    {
        var named = new PieceDef { Id = "named", Name = "Named block" };
        named.TextLines.Add(new TextLineDef { Name = "Name", Default = "1st", Place = new PrintPlace { Side = PrintPlace.Front, X = 0.1, Y = 0.1, W = 0.8, H = 0.3 } });
        var t = OfflineTable.Make(r, pieces: 2, prepare: store => store.RegisterDef(new PackManifest { Id = "named", Name = "Named" }, named),
            arrange: g => g.Layers[Layers.Umpire][0].Def = "named");
        await r.Frame();
        var labels = t.Node("p1").FindChildren("*", nameof(Label3D), true, false).OfType<Label3D>().ToList();
        Check.That(labels.Count > 0 && labels.All(l => (l.Layers & Table.TextLayer) != 0 && (l.Layers & Table.WorldLayer) == 0), "a piece's text is on the text layer and nowhere else");
        t.Table.Sighting.LeaveOut(t.Node("p1"), true);
        Check.That(labels.All(l => (l.Layers & Table.TextLayer) == 0 && (l.Layers & Table.OutOfPictureLayer) != 0), "hidden from the picture, the text goes with the piece");
        t.Table.Sighting.LeaveOut(t.Node("p1"), false);
        Check.That(labels.All(l => (l.Layers & Table.TextLayer) != 0 && (l.Layers & Table.WorldLayer) == 0), "and comes back to the text layer only");
        var cam = t.Table.Rig.Cam;
        Check.That((cam.CullMask & Table.TextLayer) != 0, "the table shows text");
        t.Table.Selection.Set(new[] { "p1" });
        Check.That(t.Table.Sighting.Begin(10), "the tool begins");
        t.Table.Sighting.SetHideText(true);
        Check.That(t.Table.Draft!.HideText && (cam.CullMask & Table.TextLayer) == 0, "with text hidden the view drops the text layer");
        t.Table.Sighting.SetHideText(false);
        Check.That((cam.CullMask & Table.TextLayer) != 0, "shown again");
        t.Table.Sighting.SetHideText(true);
        t.Table.Sighting.Cancel();
        Check.That((cam.CullMask & Table.TextLayer) != 0, "putting the tool down shows the text again");
    }

    [NodeTest]
    static async Task Stripping_a_face_document_drops_its_text_and_unit_plate_lettering(TestRunner r)
    {
        await r.Frame();
        var doc = new ArtDoc();
        doc.Layers.Add(new ArtLayer { Kind = "paint" });
        doc.Layers.Add(new ArtLayer { Kind = "text", Text = "Guard" });
        doc.Layers.Add(new ArtLayer { Kind = "block", Block = new BlockSpec { Label = "1st Foot", Symbol = "text", SymbolText = "X" } });
        Check.That(MutedFaces.Wordy(doc), "a document with words");
        var bare = MutedFaces.Strip(doc);
        Check.That(bare.Layers.Count == 2 && bare.Layers.All(l => l.Kind != "text"), "the text layer is gone");
        var plate = bare.Layers.Single(l => l.Block != null).Block!;
        Check.That(plate.Label.Length == 0 && plate.Symbol == "none", "the plate keeps its shape but not its words");
        Check.That(!MutedFaces.Wordy(bare), "nothing left to hide");
        Check.That(doc.Layers.Count == 3 && doc.Layers[2].Block!.Label == "1st Foot", "the original is untouched");
        Check.That(!MutedFaces.Wordy(new ArtDoc { Layers = { new ArtLayer { Kind = "text", Text = "hidden", Visible = false } } }), "hidden text is not a word to strip");
    }

    [NodeTest]
    static async Task Another_tool_frees_the_camera_from_the_eye_and_the_pov_tool_takes_it_back(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        t.Table.Sighting.SetFirstPerson(true);
        await r.Frame();
        var rig = t.Table.Rig;
        Check.That(rig.Eye != null, "looking from the piece");
        t.Table.SetTool(Tool.Ruler);
        await r.Frame();
        Check.That(rig.Eye == null && t.Table.Draft is { FirstPerson: true }, "the ruler frees the camera and keeps the draft");
        float before = rig.ZoomGoal;
        var at = rig.Target;
        rig.Zoom(0.5f);
        rig.PanWorld(new Vector3(3, 0, 0));
        Check.That(rig.ZoomGoal < before && rig.Target.X > at.X + 2.9f, "the wheel zooms and the keys pan again");
        await r.Frame();
        Check.That(rig.Eye == null, "and nothing drags the view back while the ruler is out");
        t.Table.SetTool(Tool.Sighting);
        await r.Frame();
        Check.That(rig.Eye != null && t.Table.Draft is { FirstPerson: true }, "back in the POV capture tool the eye is on the piece again");
        t.Table.Sighting.SetFirstPerson(false);
        await r.Frame();
        Check.That(rig.TopDown && rig.Eye == null, "2D frames from above");
        t.Table.SetTool(Tool.Ruler);
        rig.PanWorld(new Vector3(5, 0, 5));
        var moved = rig.Target;
        await r.Frame();
        Check.That(rig.Target.X == moved.X && rig.Target.Z == moved.Z, "in 2D too, another tool leaves the view where it is put");
        t.Table.Sighting.Cancel();
    }

    [NodeTest]
    static async Task The_flat_view_says_so_because_a_panorama_cannot_be_drawn_without_perspective(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        var rig = t.Table.Rig;
        Check.Equal(rig.Cam.Projection == Camera3D.ProjectionType.Orthogonal, rig.Flat, "the flag and the camera agree from the start");
        if (!rig.TopDown) rig.ToggleTopDown();
        await r.Frame();
        Check.That(rig.Flat && rig.Cam.Projection == Camera3D.ProjectionType.Orthogonal, "the map from above is drawn flat");
        rig.ToggleTopDown();
        await r.Frame();
        Check.That(!rig.Flat && rig.Cam.Projection == Camera3D.ProjectionType.Perspective, "the 3D table is not");
        int said = 0;
        rig.FlatChanged += () => said++;
        rig.ToggleTopDown();
        rig.ToggleTopDown();
        Check.Equal(2, said, "each change of projection is told once");
        t.Table.Selection.Set(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        t.Table.Sighting.SetFirstPerson(true);
        await r.Frame();
        Check.That(!rig.Flat && rig.Cam.Projection == Camera3D.ProjectionType.Perspective, "first person looks out through a lens, however the view stood before");
        t.Table.Sighting.Cancel();
        await r.Frame();
        Check.Equal(rig.Cam.Projection == Camera3D.ProjectionType.Orthogonal, rig.Flat, "putting the tool down leaves the two in step");
    }

    [NodeTest]
    static async Task Ctrl_and_a_number_keeps_a_view_of_the_scenario_unless_pieces_are_picked_and_the_number_goes_back_to_it_unless_a_piece_takes_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2,
            prepare: store => store.RegisterDef(new PackManifest { Id = "looks", Name = "Looks" },
                new PieceDef { Id = "flagged", Name = "Flagged", States = new() { new() { Name = "Formed" }, new() { Name = "Routed" } } }),
            arrange: g =>
            {
                g.ScenarioId = "views-" + System.Guid.NewGuid().ToString("N");
                g.Layers[Layers.Umpire].Add(new PieceInstance { Id = "f1", Def = "flagged", Pos = new Vec2(-4, 0) });
            });
        await r.Frame();
        var rig = t.Table.Rig;
        void Press(Key key, bool ctrl) => t.Table.Keys.OnKey(new InputEventKey { Keycode = key, Pressed = true, ShiftPressed = !ctrl, CtrlPressed = ctrl });
        var kept = new CameraRig.View(new Vector3(12, 0, -7), 40, 0.6f, -0.9f, false);
        rig.Show(kept);
        Press(Key.Key3, ctrl: true);
        rig.Show(new CameraRig.View(new Vector3(-30, 0, 25), 90, 2.1f, -1.2f, true));
        await r.Frame();
        Press(Key.Key3, ctrl: false);
        await r.Frame();
        Check.That(!rig.TopDown && rig.Target.DistanceTo(kept.Target) < 0.01f && Mathf.IsEqualApprox(rig.Distance, kept.Distance) && Mathf.IsEqualApprox(rig.Yaw, kept.Yaw),
            "Shift+3 goes back to the view Ctrl+3 kept");
        Check.Equal("3", string.Join(",", t.Table.Cam.Views.Keys), "views kept");
        t.Table.Selection.Set(new[] { "p1", "p2" });
        Press(Key.Key4, ctrl: true);
        Check.Equal("3", string.Join(",", t.Table.Cam.Views.Keys), "views kept after Ctrl+4 with two pieces picked, which forms rows");
        t.Table.Selection.Set(System.Array.Empty<string>());
        Check.That(!t.Table.Cam.GoToView(4), "an empty slot goes nowhere");
        rig.Show(new CameraRig.View(new Vector3(-30, 0, 25), 90, 2.1f, -1.2f, true));
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Key3, Pressed = true });
        await r.Frame();
        Check.That(rig.TopDown, "3 alone leaves the view alone even when no piece takes the number");
        rig.Show(new CameraRig.View(new Vector3(-30, 0, 25), 90, 2.1f, -1.2f, true));
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Kp3, Pressed = true });
        await r.Frame();
        Check.That(rig.TopDown, "the keypad's 3 leaves the view alone");
        t.Table.Selection.Set(new[] { "f1" });
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Key2, Pressed = true });
        await r.Frame();
        Check.Equal(1, t.S.View.Layer(Layers.Umpire).First(p => p.Id == "f1").State, "2 sets the picked piece's state");
        Check.That(rig.TopDown, "and leaves the view alone");
        t.Table.Selection.Set(System.Array.Empty<string>());
        t.Table.Cam.ForgetView(3);
        Check.Equal(0, t.Table.Cam.Views.Count, "views kept after forgetting it");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
