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
        var (from, tip) = g.Grip(arrow);
        return t.Table.Rig.Cam.UnprojectPosition((from + tip) / 2);
    }

    [ScreenTest]
    static async Task The_arrows_are_one_shape_whose_outline_lies_under_every_arrow_and_their_joint(TestRunner r)
    {
        var (t, _, _) = await OnePiece(r);
        t.Table.Overlay.Redraw();
        var mesh = t.Table.Overlay.Mesh;
        var surfaces = Enumerable.Range(0, mesh.GetSurfaceCount()).Select(i => mesh.SurfaceGetMaterial(i)).OfType<StandardMaterial3D>()
            .Where(m => m.Transparency == BaseMaterial3D.TransparencyEnum.Alpha).ToList();
        var casings = surfaces.Where(m => m.AlbedoColor.IsEqualApprox(SelectionAura.Casing)).ToList();
        var inks = surfaces.Except(casings).ToList();
        int arrows = t.Table.Gizmo.Arrows(false).Count();
        Check.Equal(arrows + 1, casings.Count, "an outline for each arrow and one for the joint");
        Check.Equal(arrows + 1, inks.Count, "a fill for each arrow and one for the joint");
        Check.That(casings.Max(m => m.RenderPriority) < inks.Min(m => m.RenderPriority), "every outline lies under every fill, so no outline crosses another arrow");
    }

    [ScreenTest]
    static async Task A_locked_piece_stays_put_under_the_keys_and_moves_by_the_ring_and_its_window(TestRunner r)
    {
        var (t, g, _) = await OnePiece(r);
        t.S.Do(new SetLocked { Layer = U, Id = "p1", Value = true });
        await r.Frames(3);
        var was = t.S.View.Find(U, "p1")!.Pos;
        t.Table.Poses.RotateSmooth(15);
        t.Table.Poses.Flush();
        await r.Frames(3);
        Check.That(Math.Abs(t.S.View.Find(U, "p1")!.RotDeg) < 1e-6, "the turn keys leave a locked piece as it lies");
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        var press = Middle(t, g, Gizmo.Handle.AlongX);
        var grip = t.Table.Rig.GroundAt(press)!.Value;
        g.Begin(Gizmo.Handle.AlongX, press);
        g.Drag(t.Table.Rig.Cam.UnprojectPosition(grip + new Vector3(3, 0, 0)), snap: false);
        g.End();
        t.Table.Poses.Flush();
        await r.Frames(3);
        var moved = t.S.View.Find(U, "p1")!;
        Check.That(Math.Abs(moved.Pos.X - was.X - 3) < 0.1 && moved.Locked, $"the ring's arrow moves it, still locked: {was} to {moved.Pos}");
        t.Table.MoveSelectionTo(new Vec2(-4, 2));
        t.Table.Poses.Flush();
        await r.Frames(3);
        Check.That((t.S.View.Find(U, "p1")!.Pos - new Vec2(-4, 2)).Length < 1e-6, "and so do the window's numbers");
        t.Table.SetTool(Tool.Select);
        Check.That(t.Table.SelectionPose() == null, "with another tool it is pinned again");
    }

    [ScreenTest]
    static async Task The_rings_round_knob_turns_freely_by_whole_degrees_with_Ctrl_and_by_fifteen_with_Shift(TestRunner r)
    {
        var (t, g, _) = await OnePiece(r);
        var cam = t.Table.Rig.Cam;
        var middle = Coords.ToWorld(t.S.View.Find(U, "p1")!.Pos);
        async Task<double> Turned(bool snap, bool fine)
        {
            t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = t.S.View.Find(U, "p1")!.Pos, RotDeg = 0 });
            await r.Frames(3);
            g.Measure(t.Table.Selection.Nodes.ToList(), null);
            var knob = g.RotateKnobWorld();
            var from = knob - middle;
            g.Begin(Gizmo.Handle.Rotate, cam.UnprojectPosition(knob));
            g.Drag(cam.UnprojectPosition(middle + from.Rotated(Vector3.Up, Mathf.DegToRad(-22.4f))), snap, fine);
            g.End();
            t.Table.Poses.Flush();
            await r.Frames(3);
            double turned = t.S.View.Find(U, "p1")!.RotDeg;
            return turned > 180 ? 360 - turned : turned;
        }
        double free = await Turned(false, false), whole = await Turned(false, true), stepped = await Turned(true, false);
        Check.That(Math.Abs(free - 22.4) < 1.5 && Math.Abs(free - Math.Round(free)) > 0.01, $"dragged free, the piece turns as far as the knob went: {free}");
        Check.That(Math.Abs(whole - Math.Round(free)) < 1e-6, $"with Ctrl it stops on whole degrees: {whole}");
        Check.That(Math.Abs(stepped - 15) < 1e-6 || Math.Abs(stepped - 30) < 1e-6, $"with Shift on steps of 15: {stepped}");
        t.Table.SetTool(Tool.Select);
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
        Check.That(g.Arrows(flat: false).Contains(Gizmo.Handle.Up), "a piece that can fly has the upright arrow");
        float foot = node.Position.Y;
        var (from, tip) = g.Grip(Gizmo.Handle.Up);
        var mid = (from + tip) / 2;
        var press = cam.UnprojectPosition(mid);
        Check.Equal(Gizmo.Handle.Up, g.At(press), "the upright arrow is where it is drawn");
        var feet = new[] { Gizmo.Handle.AlongX, Gizmo.Handle.AlongY, Gizmo.Handle.Up }.Select(a => g.ArrowWorld(a).From).ToList();
        Check.That(feet.All(f => f.IsEqualApprox(feet[0])), "all three arrows start from the one place, the middle of the ring");
        Check.That(new[] { "X", "Y", "Z" }.All(l => t.Table.Labels.TextOf("ring-arrow-" + l) == l), "and each is lettered X, Y or Z");
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
        (from, tip) = g.Grip(Gizmo.Handle.Up);
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
        Check.That(!g.Arrows(flat: false).Contains(Gizmo.Handle.Up), "seen from straight above there is no upright arrow to pull");
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
        Check.That(g.Arrows(flat: true).SequenceEqual(new[] { Gizmo.Handle.AlongX, Gizmo.Handle.AlongY }), "a sheet has the X and Y arrows only");
        var (from, tip) = g.Grip(Gizmo.Handle.AlongX);
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
        Check.That(Math.Abs(h.S.View.Find(U, "p1")!.Aloft - 0.3) < 1e-6, "a low height holds as typed");
        Z().Value = 0.03;
        await r.Frames(3);
        Check.That(Math.Abs(h.S.View.Find(U, "p1")!.Aloft - PieceInstance.LeastAltitude) < 1e-6, "a height under the least a piece flies at becomes the least");
        Z().Value = 0;
        await r.Frames(3);
        Check.That(!h.S.View.Find(U, "p1")!.Flies, "and 0 lands it");
        h.Table.SetTool(Tool.Select);
    }

    [ScreenTest]
    static async Task Seen_from_low_down_each_arrow_keeps_its_length_on_screen_and_is_picked_where_it_is_drawn(TestRunner r)
    {
        const float LowPitch = 0.18f, Size = 6, Yaw = 0.6f, ShortestPx = 40;
        var (t, g, node) = await OnePiece(r);
        t.Table.Rig.Frame(node.Position, Size);
        t.Table.Rig.Pitch = -LowPitch;
        t.Table.Rig.Yaw = Mathf.Pi - Yaw;
        t.Table.Rig.Apply();
        await r.Frames(3);
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        foreach (var arrow in g.Arrows(flat: false))
        {
            var (from, tip) = g.OnScreen(arrow) ?? throw new TestFailure($"the {arrow} arrow shows from low down");
            Check.That((tip - from).Length() >= ShortestPx - 0.5f, $"the {arrow} arrow is drawn at least {ShortestPx} px long: {(tip - from).Length():0.#}");
            Check.Equal(arrow, g.At(from.Lerp(tip, 0.8f)), $"the {arrow} arrow is picked where it is drawn");
        }
        t.Table.SetTool(Tool.Select);
    }

    static readonly Gizmo.Handle[] EverySide = { Gizmo.Handle.SizeX, Gizmo.Handle.SizeY, Gizmo.Handle.SizeZ };
    const float CloseFrame = 6, KnobSlack = 0.3f, Turned = 35;

    static async Task<(HudTests.Rig T, Gizmo G)> OneCloseUp(TestRunner r, int pieces = 1)
    {
        var t = await HudTests.Host(r, pieces: pieces);
        t.Table.Selection.Set(new[] { "p1" });
        t.Table.SetTool(Tool.Transform);
        t.Table.Rig.Frame(Coords.ToWorld(t.S.View.Find(U, "p1")!.Pos), CloseFrame);
        await r.Seconds(0.6);
        var g = t.Table.Gizmo;
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        return (t, g);
    }

    static async Task HoldSizeKey(TestRunner r, bool down)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Gizmo.SizeKey, PhysicalKeycode = Gizmo.SizeKey, Pressed = down });
        Input.FlushBufferedEvents();
        await r.Frames(2);
    }

    static PieceNode Shown(HudTests.Rig t, string id = "p1") => t.Table.Pieces.NodeOf(U, id) ?? throw new TestFailure($"{id} is on the table");

    static Vector3 Sizes(HudTests.Rig t) => new(Shown(t).Footprint.X, Shown(t).Thickness, Shown(t).Footprint.Y);

    static Vector2 GripMiddle(HudTests.Rig t, Gizmo g, Gizmo.Handle arrow)
    {
        var (from, tip) = g.Grip(arrow);
        return (t.Table.Rig.Screen(from) + t.Table.Rig.Screen(tip)) / 2;
    }

    static async Task Pull(TestRunner r, HudTests.Rig t, Gizmo g, Gizmo.Handle side, float by, bool snap)
    {
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        var (from, tip) = g.Grip(side);
        var away = side == Gizmo.Handle.SizeZ ? Vector3.Up : ((tip - from) with { Y = 0 }).Normalized();
        g.Begin(side, t.Table.Rig.Screen(tip));
        g.Drag(t.Table.Rig.Screen(tip + away * by), snap);
        await r.Frames(2);
    }

    [ScreenTest]
    static async Task With_Alt_a_lone_pieces_arrows_turn_to_its_own_sides_and_each_pulls_that_size_alone(TestRunner r)
    {
        var (t, g) = await OneCloseUp(r);
        var place = t.S.View.Find(U, "p1")!.Pos;
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = place, RotDeg = Turned });
        await r.Frames(4);
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        Check.That(!g.Sizing && !g.Arrows(flat: false).Any(a => EverySide.Contains(a)), "without Alt the arrows move the piece");
        try
        {
            await HoldSizeKey(r, true);
            g.Measure(t.Table.Selection.Nodes.ToList(), null);
            Check.That(g.Sizing && g.Arrows(flat: false).SequenceEqual(EverySide), "with Alt held one piece's arrows are X, Y and Z sizes");
            foreach (var side in new[] { Gizmo.Handle.SizeX, Gizmo.Handle.SizeY })
            {
                var (from, tip) = g.Grip(side);
                var own = (side == Gizmo.Handle.SizeX ? Shown(t).Transform.Basis.X : Shown(t).Transform.Basis.Z) with { Y = 0 };
                float along = Mathf.Abs(((tip - from) with { Y = 0 }).Normalized().Dot(own.Normalized()));
                Check.That(along > 0.99f, $"the {side} arrow lies along the piece's own side, not the table's: {along:0.###}");
            }
            foreach (var side in EverySide)
            {
                g.Measure(t.Table.Selection.Nodes.ToList(), null);
                Check.Equal(side, g.At(GripMiddle(t, g, side)), $"the {side} arrow is picked where it is drawn");
                var was = Sizes(t);
                int steps = t.S.MyUndoable.Count;
                await Pull(r, t, g, side, 1, snap: true);
                var live = Shown(t).Scale;
                Check.That(live != Vector3.One && t.S.MyUndoable.Count == steps, $"the piece is shown stretching while the {side} arrow is held, and nothing is sent yet: {live}");
                string said = t.Hud.Top.Said!.Text;
                Check.That(said.StartsWith("Size " + side.ToString()[^1], StringComparison.Ordinal) && said.Contains(" u", StringComparison.Ordinal) && said.Contains("Shift snaps to 0.1", StringComparison.Ordinal),
                    $"the top line says which size changes and what it is: {said}");
                g.End();
                await r.Frames(4);
                var now = Sizes(t);
                int axis = side == Gizmo.Handle.SizeX ? 0 : side == Gizmo.Handle.SizeZ ? 1 : 2;
                float grown = side == Gizmo.Handle.SizeZ ? 1 : 2;
                for (int i = 0; i < 3; i++)
                    Check.That(Mathf.Abs(now[i] - was[i] - (i == axis ? grown : 0)) < (i == axis ? KnobSlack : 1e-4f), $"the {side} arrow changes that size and no other: {was} to {now}");
                Check.That(Math.Abs(now[axis] * 10 - Math.Round(now[axis] * 10)) < 1e-3, $"with Shift the size stops on tenths of a unit: {now[axis]}");
                var piece = t.S.View.Find(U, "p1")!;
                Check.That(piece.Scale == 1 && (piece.Pos - place).Length < 1e-6 && Math.Abs(piece.RotDeg - Turned) < 1e-6, "its Size, its place and its turn stay as they were");
                Check.Equal(steps + 1, t.S.MyUndoable.Count, "let go, it is one thing to undo");
            }
        }
        finally
        {
            await HoldSizeKey(r, false);
        }
        g.Measure(t.Table.Selection.Nodes.ToList(), null);
        Check.That(!g.Sizing && g.Arrows(flat: false).First() == Gizmo.Handle.AlongX, "Alt let go, the arrows move again");
        var stretched = t.S.View.Find(U, "p1")!;
        Check.That(stretched.WideBy > 2 && stretched.DeepBy > 2 && stretched.TallBy > 2, $"the table keeps all three: {stretched.WideBy}, {stretched.DeepBy}, {stretched.TallBy}");
        t.S.UndoLast();
        await r.Frames(4);
        Check.That(t.S.View.Find(U, "p1")!.TallBy == 1 && Mathf.Abs(Shown(t).Thickness - 0.2f) < 1e-4f, "an undo gives it its own height back");
        t.Table.SetTool(Tool.Select);
    }

    [ScreenTest]
    static async Task A_size_pulled_past_the_limit_stops_at_ten_times_the_pieces_own_size(TestRunner r)
    {
        var (t, g) = await OneCloseUp(r);
        await Pull(r, t, g, Gizmo.Handle.SizeZ, 3, snap: false);
        g.End();
        await r.Frames(4);
        Check.That(Math.Abs(t.S.View.Find(U, "p1")!.TallBy - PieceInstance.MostStretch) < 1e-6 && Mathf.Abs(Shown(t).Thickness - 2) < 1e-3f, $"pulled far up it stands ten times as tall: {Shown(t).Thickness}");
        await Pull(r, t, g, Gizmo.Handle.SizeZ, -3, snap: false);
        g.End();
        await r.Frames(4);
        Check.That(Math.Abs(t.S.View.Find(U, "p1")!.TallBy - PieceInstance.LeastStretch) < 1e-6, "pushed far down, a tenth as tall");
        t.Table.SetTool(Tool.Select);
    }

    [ScreenTest]
    static async Task Sizing_the_height_raises_what_stands_on_the_piece_and_one_undo_puts_both_back(TestRunner r)
    {
        const double OnTop = 0.17;
        var (t, g) = await OneCloseUp(r, pieces: 2);
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(1, 0), Stack = 1, Y = OnTop });
        await r.Frames(5);
        int steps = t.S.MyUndoable.Count;
        await Pull(r, t, g, Gizmo.Handle.SizeZ, 1, snap: true);
        g.End();
        await r.Frames(5);
        float tall = Shown(t).Thickness;
        var rider = t.S.View.Find(U, "p2")!;
        Check.That(tall > 0.9f && Math.Abs(rider.Y - OnTop - (tall - 0.2f)) < 0.02, $"the piece on top rose as far as the top did: {tall} tall, the rider at {rider.Y}");
        Check.That(Mathf.Abs(Shown(t, "p2").Position.Y - Shown(t).TopY) < 0.1f, $"and it is drawn standing on the new top: {Shown(t, "p2").Position.Y} on {Shown(t).TopY}");
        Check.Equal(steps + 1, t.S.MyUndoable.Count, "both are one thing to undo");
        t.S.UndoLast();
        await r.Frames(5);
        Check.That(t.S.View.Find(U, "p1")!.TallBy == 1 && Math.Abs(t.S.View.Find(U, "p2")!.Y - OnTop) < 1e-6, "undone, the piece is its own height and the rider back down on it");
        t.Table.SetTool(Tool.Select);
    }

    [ScreenTest]
    static async Task Several_pieces_a_sheet_and_a_view_from_straight_above_keep_the_size_arrows_they_can_use(TestRunner r)
    {
        var (t, g) = await OneCloseUp(r, pieces: 2);
        try
        {
            await HoldSizeKey(r, true);
            t.Table.Selection.Set(new[] { "p1", "p2" });
            await r.Frames(3);
            g.Measure(t.Table.Selection.Nodes.ToList(), null);
            Check.That(!g.Sizing && !g.Arrows(flat: false).Any(a => EverySide.Contains(a)), "with several pieces Alt leaves the arrows moving them");
            t.Table.Selection.Set(new[] { "p1" });
            t.Table.Rig.ToggleTopDown();
            await r.Seconds(0.6);
            g.Measure(t.Table.Selection.Nodes.ToList(), null);
            Check.That(g.Arrows(flat: false).SequenceEqual(new[] { Gizmo.Handle.SizeX, Gizmo.Handle.SizeY }), "seen from straight above there is no height arrow, like the upright arrow, and the two sides stay");
            t.Table.Rig.ToggleTopDown();
            await r.Seconds(0.6);
            t.Table.Selection.Set(Array.Empty<string>());
            t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "s9", Size = new Size2(4, 3) } });
            t.S.Do(new LockSheet { Id = "s9", Locked = false });
            await r.Frames(3);
            t.Table.SelectSheet("s9");
            await r.Frames(3);
            Check.That(!g.Arrows(flat: true).Any(a => EverySide.Contains(a)), "a picked sheet has no size arrow");
        }
        finally
        {
            await HoldSizeKey(r, false);
        }
        t.Table.SetTool(Tool.Select);
    }
}
