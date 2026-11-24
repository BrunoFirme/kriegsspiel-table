using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class GizmoPanelTests
{
    const string U = Layers.Umpire;
    const double Near = 1e-6;

    static List<SpinBox> Fields(HudTests.Rig h) => WindowTests.Descendants(h.Hud.Numbers.Panel.Body).OfType<SpinBox>().ToList();

    static PieceInstance Piece(HudTests.Rig h, string id) => h.S.View.Find(U, id)!;

    internal static List<string> Names(HudTests.Rig h) => Fields(h).Select(f => ((Label)f.GetParent().GetChild(f.GetIndex() - 1)).Text).ToList();

    static async Task Landed(TestRunner r, HudTests.Rig h)
    {
        h.Table.Poses.Flush();
        await r.Frames(3);
    }

    [NodeTest]
    static async Task The_turn_and_resize_window_takes_exact_numbers_for_where_the_selection_stands_its_turn_and_its_size(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SetTool(Tool.Transform);
        await r.Frames(2);
        Check.That(h.Hud.Numbers.Panel.Visible, "the window opens with the tool");
        var f = Fields(h);
        Check.Equal("X, Y, Z, Turn, Tilt X, Tilt Y, Size, Size X, Size Y, Size Z", string.Join(", ", Names(h)), "where it stands, its turn, its size, and each side's size named by its axis");
        Check.That(f.Skip(7).All(side => !side.TooltipText.Contains("width", StringComparison.OrdinalIgnoreCase) && !side.TooltipText.Contains("depth", StringComparison.OrdinalIgnoreCase)),
            "no side is called a width or a depth beside the axes");
        Check.That(Math.Abs(f[0].Value - 1) < Near && Math.Abs(f[1].Value) < Near, $"it shows where the piece stands: {f[0].Value}, {f[1].Value}");
        f[0].Value = 3.25;
        f[1].Value = -2.5;
        f[3].Value = 37.5;
        f[6].Value = 1.4;
        await Landed(r, h);
        var one = Piece(h, "p1");
        Check.That(Math.Abs(one.Pos.X - 3.25) < Near && Math.Abs(one.Pos.Z + 2.5) < Near, $"typed X and Z put the piece there: {one.Pos}");
        Check.That(Math.Abs(one.RotDeg - 37.5) < Near && Math.Abs(one.Scale - 1.4) < Near, $"typed Turn and Size set them: {one.RotDeg}, {one.Scale}");
        Check.Equal(0.0, Piece(h, "p2").RotDeg, "a piece that is not selected is left alone");
    }

    [NodeTest]
    static async Task Width_and_depth_are_typed_apart_and_the_window_says_how_big_the_piece_now_is(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SetTool(Tool.Transform);
        await r.Frames(2);
        var was = h.Table.Pieces.NodeOf(U, "p1")!.Footprint;
        int undos = h.S.MyUndoable.Count;
        Check.That(Mathf.Abs((float)Fields(h)[7].Value - was.X) < 1e-4f && Mathf.Abs((float)Fields(h)[8].Value - was.Y) < 1e-4f,
            $"Size X and Size Y say how big the piece is, in table units: {Fields(h)[7].Value} × {Fields(h)[8].Value} for {was}");
        Fields(h)[7].Value = was.X * 3;
        await r.Frames(4);
        var wide = h.Table.Pieces.NodeOf(U, "p1")!.Footprint;
        Check.That(Mathf.Abs(wide.X - was.X * 3) < 1e-4f && Mathf.Abs(wide.Y - was.Y) < 1e-4f, $"a width three times as big makes it three times as wide and no deeper: {was} → {wide}");
        Check.That(Math.Abs(Piece(h, "p1").WideBy - 3) < 1e-4, "the table keeps the width");
        Fields(h)[8].Value = was.Y / 2;
        await r.Frames(4);
        var both = h.Table.Pieces.NodeOf(U, "p1")!.Footprint;
        Check.That(Mathf.Abs(both.X - was.X * 3) < 1e-4f && Mathf.Abs(both.Y - was.Y / 2) < 1e-4f, $"half the depth halves it and keeps the width: {both}");
        Check.That(Mathf.Abs((float)Fields(h)[7].Value - both.X) < 1e-4f && Mathf.Abs((float)Fields(h)[8].Value - both.Y) < 1e-4f, "and the fields say the new size");
        var measured = WindowTests.Descendants(h.Hud.Numbers.Panel.Body).OfType<Label>().Where(l => l.Visible && l.TooltipText.Contains("measure", StringComparison.Ordinal)).ToList();
        Check.Equal(0, measured.Count, "on a map with no scale no second size is said under them");
        Check.Equal(undos + 2, h.S.MyUndoable.Count, "each is one thing to undo");
        h.S.UndoLast();
        h.S.UndoLast();
        await r.Frames(4);
        var back = h.Table.Pieces.NodeOf(U, "p1")!.Footprint;
        Check.That(Mathf.Abs(back.X - was.X) < 1e-4f && Mathf.Abs(back.Y - was.Y) < 1e-4f, $"two undos give it its own shape back: {back}");
        Check.That(Mathf.Abs((float)Fields(h)[7].Value - was.X) < 1e-4f && Mathf.Abs((float)Fields(h)[8].Value - was.Y) < 1e-4f, "and the fields follow");
        h.Table.SetTool(Tool.Select);
    }

    [NodeTest]
    static async Task Size_Z_sets_how_tall_a_piece_stands_alone_within_a_tenth_and_ten_times_its_own_height(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SetTool(Tool.Transform);
        await r.Frames(2);
        var node = h.Table.Pieces.NodeOf(U, "p1")!;
        var (was, own) = (node.Footprint, node.Thickness);
        int undos = h.S.MyUndoable.Count;
        var tall = Fields(h)[9];
        Check.That(Math.Abs(tall.Value - own) < 1e-4 && Math.Abs(tall.MinValue - own * PieceInstance.LeastStretch) < 1e-6 && Math.Abs(tall.MaxValue - own * PieceInstance.MostStretch) < 1e-6,
            $"Size Z says how tall the piece stands and how far it goes: {tall.Value} in {tall.MinValue} to {tall.MaxValue}");
        tall.Value = own * 6;
        await r.Frames(4);
        node = h.Table.Pieces.NodeOf(U, "p1")!;
        Check.That(Mathf.Abs(node.Thickness - own * 6) < 1e-4f && node.Footprint.IsEqualApprox(was), $"six times the height makes it six times as tall and no wider or deeper: {node.Thickness}, {node.Footprint}");
        var piece = Piece(h, "p1");
        Check.That(Math.Abs(piece.TallBy - 6) < 1e-4 && piece.Scale == 1 && piece.Wide == null && piece.Deep == null, "the table keeps the height and nothing else changed");
        Check.That(Math.Abs(Fields(h)[9].Value - own * 6) < 1e-4 && Math.Abs(Fields(h)[7].Value - was.X) < 1e-4, "and the fields say so");
        Fields(h)[9].Value = own * 50;
        await r.Frames(4);
        Check.That(Mathf.Abs(h.Table.Pieces.NodeOf(U, "p1")!.Thickness - own * 10) < 1e-4f, "a height past the field's most stops there, where the field said it would");
        Fields(h)[7].Value = was.X * 2;
        await r.Frames(4);
        Check.That(Mathf.Abs(h.Table.Pieces.NodeOf(U, "p1")!.Thickness - own * 10) < 1e-4f, "made wider, it stands as tall as before");
        Check.Equal(undos + 3, h.S.MyUndoable.Count, "each is one thing to undo");
        h.Table.SetTool(Tool.Select);
    }

    [NodeTest]
    static async Task With_several_pieces_the_window_moves_their_middle_and_it_follows_what_the_keys_do(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1", "p2" });
        h.Table.SetTool(Tool.Transform);
        await r.Frames(2);
        var f = Fields(h);
        Check.That(Math.Abs(f[0].Value - 1.5) < Near, $"X is the middle of the two: {f[0].Value}");
        f[0].Value = 10;
        await Landed(r, h);
        Check.That(Math.Abs(Piece(h, "p1").Pos.X - 9.5) < Near && Math.Abs(Piece(h, "p2").Pos.X - 10.5) < Near, "both move, each keeping its place beside the other");
        h.Table.RotateSmooth(15);
        await r.Frames(3);
        Check.That(Math.Abs(Fields(h)[3].Value - 15) < Near, $"turned with the key, the window shows the new turn: {Fields(h)[3].Value}");
        await Landed(r, h);
        h.Table.Selection.Set(Array.Empty<string>());
        await r.Frames(3);
        Check.Equal(0, Fields(h).Count, "with nothing selected there is nothing to type into");
        h.Table.SetTool(Tool.Select);
        await r.Frames(2);
        Check.That(!h.Hud.Numbers.Panel.Visible, "and the window goes with the tool");
    }

    [NodeTest]
    static async Task A_tilted_piece_is_stood_level_by_typing_its_tilts_and_each_is_one_undo(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.S.Do(new KTable.Core.Protocol.TiltPiece { Layer = U, Id = "p1", X = 35, Z = -12.5 });
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SetTool(Tool.Transform);
        await r.Frames(2);
        var names = Names(h);
        SpinBox Named(string name) => Fields(h)[names.IndexOf(name)];
        Check.That(Math.Abs(Named("Tilt X").Value - 35) < Near && Math.Abs(Named("Tilt Y").Value + 12.5) < Near, $"the window shows how the piece leans: {Named("Tilt X").Value}, {Named("Tilt Y").Value}");
        int undos = h.S.MyUndoable.Count;
        Named("Tilt X").Value = 0;
        await r.Frames(2);
        Named("Tilt Y").Value = 0;
        await r.Frames(2);
        Check.That(!Piece(h, "p1").HasTilt, $"typed level it stands level: {Piece(h, "p1").TiltX}, {Piece(h, "p1").TiltZ}");
        Check.Equal(undos + 2, h.S.MyUndoable.Count, "each tilt typed is one thing to undo");
        Named("Tilt Y").Value = 20;
        await r.Frames(2);
        Check.That(Math.Abs(Piece(h, "p1").TiltZ - 20) < Near && Piece(h, "p1").TiltX == 0, "a level piece leans as typed");
        h.S.UndoLast();
        await r.Frames(2);
        Check.That(!Piece(h, "p1").HasTilt && Math.Abs(Named("Tilt Y").Value) < Near, "undone it stands level again and the window follows");
        Check.Equal(0.0, Piece(h, "p2").TiltX, "a piece that is not selected is left alone");
        h.Table.SetTool(Tool.Select);
    }
}
