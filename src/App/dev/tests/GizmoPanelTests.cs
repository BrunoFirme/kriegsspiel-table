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
        Check.Equal(4, f.Count, "X, Z, Turn and Size");
        Check.That(Math.Abs(f[0].Value - 1) < Near && Math.Abs(f[1].Value) < Near, $"it shows where the piece stands: {f[0].Value}, {f[1].Value}");
        f[0].Value = 3.25;
        f[1].Value = -2.5;
        f[2].Value = 37.5;
        f[3].Value = 1.4;
        await Landed(r, h);
        var one = Piece(h, "p1");
        Check.That(Math.Abs(one.Pos.X - 3.25) < Near && Math.Abs(one.Pos.Z + 2.5) < Near, $"typed X and Z put the piece there: {one.Pos}");
        Check.That(Math.Abs(one.RotDeg - 37.5) < Near && Math.Abs(one.Scale - 1.4) < Near, $"typed Turn and Size set them: {one.RotDeg}, {one.Scale}");
        Check.Equal(0.0, Piece(h, "p2").RotDeg, "a piece that is not selected is left alone");
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
        Check.That(Math.Abs(Fields(h)[2].Value - 15) < Near, $"turned with the key, the window shows the new turn: {Fields(h)[2].Value}");
        await Landed(r, h);
        h.Table.Selection.Set(Array.Empty<string>());
        await r.Frames(3);
        Check.Equal(0, Fields(h).Count, "with nothing selected there is nothing to type into");
        h.Table.SetTool(Tool.Select);
        await r.Frames(2);
        Check.That(!h.Hud.Numbers.Panel.Visible, "and the window goes with the tool");
    }
}
