using Godot;
using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;
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
        t.Table.Sighting.UndoWall();
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
    static async Task First_person_puts_the_eye_on_the_piece_and_a_direction_held_turns_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p2" });
        Check.That(t.Table.Sighting.Begin(10), "the tool begins on the picked piece");
        var rig = t.Table.Rig;
        Check.That(rig.TopDown && rig.Eye == null, "2D first: the map from above");
        t.Table.Sighting.SetFirstPerson(true);
        await r.Frame();
        var node = t.Node("p2");
        Check.That(rig.Eye is { } eye && Mathf.Abs(eye.X - node.GlobalPosition.X) < 0.01f && Mathf.Abs(eye.Z - node.GlobalPosition.Z) < 0.01f && eye.Y > node.GlobalPosition.Y + node.Thickness,
            "the eye stands over the piece, above its top");
        Check.That(!rig.TopDown && rig.Cam.Projection == Camera3D.ProjectionType.Perspective && Mathf.IsEqualApprox(rig.Cam.Fov, CameraRig.NormalLens), "a perspective view through the normal lens");
        Check.That(rig.Cam.GlobalPosition.IsEqualApprox(rig.Eye!.Value), "the camera is at the eye");
        t.Table.Sighting.SetWide(true);
        Check.That(Mathf.IsEqualApprox(rig.Cam.Fov, CameraRig.WideLens), "the wide lens widens the view");
        t.Table.Sighting.Face("E");
        Check.Equal("E", t.Table.Draft!.Facing, "east is held");
        Check.That(Mathf.IsEqualApprox(Mathf.Wrap(rig.YawGoal, -Mathf.Pi, Mathf.Pi), Mathf.Pi / 2), "the view turns to look along +X");
        t.Table.Sighting.Face("N");
        Check.That(Mathf.IsEqualApprox(Mathf.Abs(Mathf.Wrap(rig.YawGoal, -Mathf.Pi, Mathf.Pi)), Mathf.Pi), "north looks along -Z");
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
    static async Task Hidden_text_leaves_the_text_layer_out_of_the_view_and_the_picture(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        var labels = t.Node("p1").FindChildren("*", nameof(Label3D), true, false).OfType<Label3D>().ToList();
        Check.That(labels.Count > 0 && labels.All(l => (l.Layers & Table.TextLayer) != 0 && (l.Layers & Table.WorldLayer) == 0), "a piece's text is on the text layer and nowhere else");
        t.Node("p1").HideFromPicture(true);
        Check.That(labels.All(l => (l.Layers & Table.TextLayer) == 0 && (l.Layers & Table.OutOfPictureLayer) != 0), "hidden from the picture, the text goes with the piece");
        t.Node("p1").HideFromPicture(false);
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
}
