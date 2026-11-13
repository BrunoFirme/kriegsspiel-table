using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class GizmoArrowTests
{
    const string U = Layers.Umpire;

    static async Task<(HudTests.Rig T, Gizmo G, PieceNode Node)> OnePiece(TestRunner r)
    {
        var t = await HudTests.Host(r, pieces: 1);
        t.Table.Selection.Set(new[] { "p1" });
        t.Table.SetTool(Tool.Transform);
        await r.Seconds(0.6);
        var g = t.Table.Gizmo;
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        return (t, g, t.Table.Pieces.NodeOf(U, "p1") ?? throw new TestFailure("the piece is on the table"));
    }

    static Vector2 Middle(HudTests.Rig t, Gizmo g, Gizmo.Handle arrow)
    {
        var (from, _, tip) = g.ArrowWorld(arrow);
        return t.Table.Rig.Cam.UnprojectPosition((from + tip) / 2);
    }

    [ScreenTest]
    static async Task The_rings_X_and_Y_arrows_move_the_selection_along_that_side_of_the_map_alone(TestRunner r)
    {
        var (t, g, _) = await OnePiece(r);
        var cam = t.Table.Rig.Cam;
        var was = t.S.View.Find(U, "p1")!.Pos;
        var press = Middle(t, g, Gizmo.Handle.AlongX);
        Check.Equal(Gizmo.Handle.AlongX, g.At(press), "the X arrow is where it is drawn");
        var grip = t.Table.Rig.GroundAt(press)!.Value;
        g.Begin(Gizmo.Handle.AlongX, press);
        g.Drag(cam.UnprojectPosition(grip + new Vector3(3, 0, 1.5f)), snap: false);
        g.End();
        t.Table.Poses.Flush();
        await r.Frames(3);
        var across = t.S.View.Find(U, "p1")!.Pos;
        Check.That(Math.Abs(across.X - was.X - 3) < 0.1 && Math.Abs(across.Z - was.Z) < 1e-6, $"dragged aslant, the piece went 3 along X and nowhere along Y: {was} to {across}");
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        press = Middle(t, g, Gizmo.Handle.AlongY);
        Check.Equal(Gizmo.Handle.AlongY, g.At(press), "the Y arrow is where it is drawn");
        grip = t.Table.Rig.GroundAt(press)!.Value;
        g.Begin(Gizmo.Handle.AlongY, press);
        g.Drag(cam.UnprojectPosition(grip + new Vector3(2, 0, -2.4f)), snap: true);
        g.End();
        t.Table.Poses.Flush();
        await r.Frames(3);
        var up = t.S.View.Find(U, "p1")!.Pos;
        Check.That(Math.Abs(up.Z - across.Z - 2) < 1e-6 && Math.Abs(up.X - across.X) < 1e-6, $"with Shift the Y arrow moves it whole units up the map and not across: {across} to {up}");
        t.Table.SetTool(Tool.Select);
    }

    [ScreenTest]
    static async Task The_upright_arrow_lifts_a_piece_into_flight_and_never_puts_it_under_the_ground_it_stands_on(TestRunner r)
    {
        var (t, g, node) = await OnePiece(r);
        var cam = t.Table.Rig.Cam;
        Check.That(g.Arrows(sheet: false).Contains(Gizmo.Handle.Up), "a piece that can fly has the upright arrow");
        float foot = node.Position.Y;
        var (from, _, tip) = g.ArrowWorld(Gizmo.Handle.Up);
        var mid = (from + tip) / 2;
        var press = cam.UnprojectPosition(mid);
        Check.Equal(Gizmo.Handle.Up, g.At(press), "the upright arrow is where it is drawn");
        g.Begin(Gizmo.Handle.Up, press);
        g.Drag(cam.UnprojectPosition(mid + Vector3.Up * 4), snap: true);
        Check.That(node.Position.Y > foot + 3, $"the piece rises with the drag: {foot} to {node.Position.Y}");
        g.End();
        await r.Frames(3);
        var piece = t.S.View.Find(U, "p1")!;
        Check.That(piece.Flies && Math.Abs(piece.Altitude - 4) < 0.6 && Math.Abs(piece.Altitude * 2 - Math.Round(piece.Altitude * 2)) < 1e-6,
            $"let go, it flies at the height it was lifted to, a half unit with Shift: {piece.Altitude}");
        int steps = t.S.MyUndoable.Count;
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        (from, _, tip) = g.ArrowWorld(Gizmo.Handle.Up);
        mid = (from + tip) / 2;
        press = cam.UnprojectPosition(mid);
        g.Begin(Gizmo.Handle.Up, press);
        float lowest = float.MaxValue;
        foreach (float down in new[] { 2f, 6f, 30f, 400f })
        {
            g.Drag(press + new Vector2(0, down), snap: false);
            lowest = Math.Min(lowest, node.Position.Y);
        }
        Check.That(lowest >= foot - 1e-3f, $"dragged far down it stops on the ground and never goes under it: {lowest} against {foot}");
        g.End();
        await r.Frames(3);
        piece = t.S.View.Find(U, "p1")!;
        Check.That(!piece.Flies && piece.Aloft == 0 && t.S.MyUndoable.Count == steps + 1, "let go on the ground it has landed, in one undo step");
        t.Table.Rig.ToggleTopDown();
        await r.Seconds(0.6);
        Check.That(!g.Arrows(sheet: false).Contains(Gizmo.Handle.Up), "seen from straight above there is no upright arrow to pull");
        t.Table.Rig.ToggleTopDown();
        t.Table.SetTool(Tool.Select);
    }

    [ScreenTest]
    static async Task A_picked_sheet_has_the_X_and_Y_arrows_and_no_upright_one(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0, sheets: 1);
        h.S.Do(new LockSheet { Id = "s0", Locked = false });
        h.Table.FrameAll();
        await r.Seconds(0.6);
        h.Table.SelectSheet("s0");
        h.Table.SetTool(Tool.Transform);
        await r.Frames(3);
        var g = h.Table.Gizmo;
        var cam = h.Table.Rig.Cam;
        h.Table.Sheets.TryGet("s0", out var node);
        g.Measure(new List<PieceNode>(), node);
        Check.That(g.Arrows(sheet: true).SequenceEqual(new[] { Gizmo.Handle.AlongX, Gizmo.Handle.AlongY }), "a sheet has the X and Y arrows only");
        var (from, _, tip) = g.ArrowWorld(Gizmo.Handle.AlongX);
        var press = cam.UnprojectPosition((from + tip) / 2);
        Check.Equal(Gizmo.Handle.AlongX, g.At(press), "its X arrow is where it is drawn");
        var grip = h.Table.Rig.GroundAt(press)!.Value;
        var was = h.S.View.Sheets[0].Pos;
        g.Begin(Gizmo.Handle.AlongX, press);
        g.Drag(cam.UnprojectPosition(grip + new Vector3(4, 0, 2)), snap: true);
        g.End();
        await r.Frames(3);
        var now = h.S.View.Sheets[0].Pos;
        Check.That(Math.Abs(now.X - was.X - 4) < 1e-6 && Math.Abs(now.Z - was.Z) < 1e-6, $"the X arrow slides the sheet along X alone: {was} to {now}");
        h.Table.SetTool(Tool.Select);
    }

    [NodeTest]
    static async Task The_numbers_window_takes_Z_how_high_a_piece_flies_with_0_standing_it_on_the_ground(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SetTool(Tool.Transform);
        await r.Frames(3);
        SpinBox Z() => WindowTests.Descendants(h.Hud.Numbers.Panel.Body).OfType<SpinBox>().ElementAt(2);
        Check.That(Z().TooltipText.Contains("flies", StringComparison.Ordinal) && Z().Value == 0, "Z stands third, after X and Y, and says 0 for a piece on the ground");
        Z().Value = 5;
        await r.Frames(3);
        var piece = h.S.View.Find(U, "p1")!;
        Check.That(piece.Flies && Math.Abs(piece.Altitude - 5) < 1e-6, "a typed height sets it flying that high");
        Z().Value = 0.3;
        await r.Frames(3);
        Check.That(Math.Abs(h.S.View.Find(U, "p1")!.Aloft - PieceInstance.LeastAltitude) < 1e-6, "a height under the least a piece flies at becomes the least");
        Z().Value = 0;
        await r.Frames(3);
        Check.That(!h.S.View.Find(U, "p1")!.Flies, "and 0 lands it");
        h.Table.SetTool(Tool.Select);
    }
}
