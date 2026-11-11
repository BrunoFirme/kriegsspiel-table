using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class TableDemo
{
    readonly Table T;

    public TableDemo(Table table) { T = table; }

    Session S => T.S;

    SignalAwaiter After(double seconds) => T.ToSignal(T.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    SignalAwaiter NextFrame() => T.ToSignal(T.GetTree(), SceneTree.SignalName.ProcessFrame);
    async Task Frames(int k)
    {
        for (int i = 0; i < k; i++) await NextFrame();
    }

    static async void Report(Task run)
    {
        try { await run; }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    static void Btn(Vector2 at, bool down) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
    static void Move(Vector2 from, Vector2 at) => Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = at - from });
    static Vec2 Lerp(Vec2 a, Vec2 b, double k) => new(a.X + (b.X - a.X) * k, a.Z + (b.Z - a.Z) * k);

    public void TypeWidget(int n, string text)
    {
        var list = S.View.Layer(Layers.Umpire);
        if (n < 0 || n >= list.Count) { GD.Print("[type] no such piece"); return; }
        var p = list[n];
        int idx = p.Widgets.FindIndex(w => w.Kind == "input") + 1;
        if (idx <= 0 || !T.Pieces.TryGet(Layers.Umpire, p.Id, out var node)) { GD.Print("[type] no input box on it"); return; }
        T.Names.BeginWidget(node, idx, p.Widgets[idx - 1]);
        foreach (var ch in text) T.Names.Key(new InputEventKey { Pressed = true, Unicode = ch });
        GD.Print($"[type] box {idx} now '{p.Widgets[idx - 1].Value}', face label '{p.Widgets[0].Label}'");
    }


    public void Ruler(Vec2 a, Vec2 b)
    {
        T.SetTool(Tool.Ruler);
        T.Ruler.A = a;
        T.Ruler.B = b;
    }

    public void Stretch(string arg) => Report(StretchAsync(arg));

    async Task StretchAsync(string arg)
    {
        const int Steps = 40;
        await After(5.0);
        var a = arg.Split(',');
        var ids = S.View.Layer(Layers.Umpire).Where(p => p.Def.Contains(a[0], StringComparison.Ordinal)).Select(p => p.Id).ToArray();
        GD.Print($"[stretch] {ids.Length} pieces of {a[0]}");
        T.SetTool(Tool.Formation);
        T.SetSelection(ids);
        T.Settings.Formation = "stretch";
        T.Forms.SetGap(T.Forms.Gap);
        var vp = T.GetViewport().GetVisibleRect().Size;
        bool s = a.Length > 1 && a[1] == "s", hold = a.Length > 2 && a[2] == "hold";
        Vector2 At(float t) => vp * (s ? new Vector2(0.4f + 0.5f * t, 0.5f - 0.17f * Mathf.Sin(Mathf.Tau * t)) : new Vector2(0.42f + 0.46f * t, 0.7f - 0.32f * Mathf.Sin(Mathf.Pi * t)));
        static void Right(Vector2 at, bool down) =>
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = down, Position = at, GlobalPosition = at, ButtonMask = down ? MouseButtonMask.Right : 0 });
        Input.ParseInputEvent(new InputEventMouseMotion { Position = At(0), GlobalPosition = At(0) });
        Right(At(0), true);
        await NextFrame();
        for (int i = 1; i <= Steps; i++)
        {
            Vector2 to = At(i / (float)Steps), from = At((i - 1) / (float)Steps);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Right });
            await NextFrame();
        }
        if (hold) { GD.Print("[stretch] holding the right button down, the ghost showing"); return; }
        Right(At(1), false);
        await After(2.0);
        foreach (var id in ids)
            if (S.View.Find(Layers.Umpire, id) is { } p) GD.Print($"[stretch] {id} at {p.Pos.X:0.00},{p.Pos.Z:0.00} turned {p.RotDeg:0.0}");
    }

    public void Curves() => Report(CurvesAsync());

    static void Click(Vector2 at, bool twice = false)
    {
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = twice, Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
    }

    async Task CurvesAsync()
    {
        await After(3.0);
        var vp = T.GetViewport().GetVisibleRect().Size;
        var runs = new (string Kind, string Ink, float See, Vector2[] Bends)[]
        {
            (Polyline.Area, "#2a6ebb", 1f, new[]
            {
                new Vector2(0.40f, 0.30f), new Vector2(0.52f, 0.26f), new Vector2(0.58f, 0.40f), new Vector2(0.50f, 0.50f), new Vector2(0.39f, 0.45f), new Vector2(0.40f, 0.30f),
            }),
            (Polyline.CurvedArrow, "#2a6ebb", 1f, new[] { new Vector2(0.62f, 0.90f), new Vector2(0.70f, 0.75f), new Vector2(0.64f, 0.58f), new Vector2(0.57f, 0.48f) }),
            (Polyline.Curve, "#c0392b", 1f, new[] { new Vector2(0.62f, 0.62f), new Vector2(0.70f, 0.55f), new Vector2(0.80f, 0.60f), new Vector2(0.88f, 0.50f) }),
        };
        T.SetTool(Tool.Shape);
        foreach (var r in runs)
        {
            T.ShapeKind = r.Kind;
            T.PenColor = Coords.ParseColor(r.Ink, Colors.Black);
            T.PenOpacity = r.See;
            T.PenPixels = 4f;
            for (int i = 0; i < r.Bends.Length; i++)
            {
                var at = r.Bends[i] * vp;
                Move(at, at);
                Click(at, twice: r.Kind != Polyline.Area && i == r.Bends.Length - 1);
                await NextFrame();
            }
            await After(0.3);
        }
        T.ShapeKind = Polyline.Curve;
        T.PenColor = Coords.ParseColor("#2e8b45", Colors.Black);
        foreach (var b in new[] { new Vector2(0.62f, 0.25f), new Vector2(0.75f, 0.32f) }) { Click(b * vp); await NextFrame(); }
        var hover = new Vector2(0.86f, 0.22f) * vp;
        Input.WarpMouse(hover);
        Move(hover, hover);
        await After(1.0);
        foreach (var (layer, strokes) in S.View.Drawings)
            foreach (var st in strokes)
                GD.Print($"[curves] {layer}: {st.Points.Count} points, ink {st.Color}, fill {st.Fill}");
    }

    public void Shapes() => Report(ShapesAsync());

    async Task ShapesAsync()
    {
        await After(3.0);
        var vp = T.GetViewport().GetVisibleRect().Size;
        var runs = new (string Kind, string Ink, Vector2 A, Vector2 B)[]
        {
            ("line",    "#1a1a1a", new Vector2(0.35f, 0.32f), new Vector2(0.47f, 0.46f)),
            ("rect",    "#c0392b", new Vector2(0.52f, 0.30f), new Vector2(0.66f, 0.46f)),
            ("ellipse", "#2a6ebb", new Vector2(0.35f, 0.58f), new Vector2(0.48f, 0.76f)),
            ("arrow",   "#2e8b45", new Vector2(0.52f, 0.76f), new Vector2(0.67f, 0.58f)),
        };
        T.SetTool(Tool.Shape);
        foreach (var r in runs)
        {
            T.ShapeKind = r.Kind;
            T.PenColor = Coords.ParseColor(r.Ink, Colors.Black);
            T.PenPixels = 4f;
            Vector2 a = r.A * vp, b = r.B * vp;
            Btn(a, true);
            await NextFrame();
            Move(a, (a + b) / 2);
            await NextFrame();
            Move((a + b) / 2, b);
            await NextFrame();
            Btn(b, false);
            await After(0.3);
        }
        await After(1.0);
        foreach (var (layer, strokes) in S.View.Drawings)
            foreach (var st in strokes)
                GD.Print($"[shapes] {layer}: {st.Points.Count} points, ink {st.Color}, width {st.Width.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}");
        int before = S.View.Drawings.Values.Sum(l => l.Count);
        T.SetTool(Tool.Erase);
        T.PenPixels = 10f;
        var mid = new Vector2(0.41f, 0.39f) * vp;
        Btn(mid, true);
        await NextFrame();
        Btn(mid, false);
        await After(0.6);
        int after = S.View.Drawings.Values.Sum(l => l.Count);
        GD.Print($"[shapes] rubbed the middle of the line: {before} drawings before, {after} after (the line comes back as its two ends)");
        var park = new Vector2(0.44f, 0.52f) * vp;
        Pointer.Drive(park);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = park, GlobalPosition = park });
        await NextFrame();
        GD.Print($"[shapes] the rubber reaches {T.RubRadius().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} units at this zoom");
    }

    public void State(int n, int to, bool stay = false) => Report(StateAsync(n, to, stay));

    async Task StateAsync(int n, int to, bool stay = false)
    {
        await After(4.0);
        var board = S.View.Layer(Layers.Umpire);
        if (board.Count <= n) { GD.Print("[state] no such piece"); return; }
        var id = board[n].Id;
        void Say(string step)
        {
            var p = S.View.Find(Layers.Umpire, id)!;
            var node = T.NodeOf(Layers.Umpire, id);
            GD.Print($"[state] {step}: state {p.State}, {p.Widgets.Count} widget(s) on the piece, drawn {node?.DrawnWidgets ?? -1} (drawn on state {p.DrawnOnState})");
        }
        Say("to begin with");
        S.Do(new SetState { Layer = Layers.Umpire, Id = id, State = to });
        await After(0.6);
        Say($"after going to state {to}");
        if (stay) return;
        S.Do(new SetState { Layer = Layers.Umpire, Id = id, State = 0 });
        await After(0.6);
        Say("and back");
    }

    public void AltTab(int n) => Report(AltTabAsync(n));

    async Task AltTabAsync(int n)
    {
        await After(3.0);
        var board = S.View.Layer(Layers.Umpire);
        if (board.Count <= n) { GD.Print("[alttab] no such piece"); return; }
        T.SetSelection(new[] { board[n].Id });
        T.Router.LeftDown = true;
        T.Hand.Start(board[n].Pos);
        await NextFrame();
        GD.Print($"[alttab] holding: {T.Holding}, the hand is down: {T.Router.LeftDown}");
        T.Notification((int)Node.NotificationApplicationFocusOut);
        await After(1.2);
        GD.Print($"[alttab] after the window lost focus: holding {T.Holding}, the hand is down: {T.Router.LeftDown}");
    }

    public void Lock(int n) => Report(LockAsync(n));

    async Task LockAsync(int n)
    {
        await After(3.0);
        var board = S.View.Layer(Layers.Umpire);
        if (board.Count <= n) { GD.Print("[lock] no such piece"); return; }
        var id = board[n].Id;
        Vec2 At() => S.View.Find(Layers.Umpire, id)!.Pos;
        async Task Drag()
        {
            T.SetSelection(new[] { id });
            var from = T.Rig.Cam.UnprojectPosition(T.NodeOf(Layers.Umpire, id)!.GlobalPosition);
            var to = from + new Vector2(160, 90);
            Btn(from, true);
            await NextFrame();
            for (int i = 1; i <= 6; i++)
            {
                var at = from.Lerp(to, i / 6f);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (to - from) / 6 });
                await NextFrame();
            }
            Btn(to, false);
            await After(1.2);
        }
        var start = At();
        S.Do(new SetLocked { Layer = Layers.Umpire, Id = id, Value = true });
        await After(0.4);
        await Drag();
        GD.Print($"[lock] locked, dragged: it moved {(At() - start).Length:0.00} units (it should not have)");
        S.Do(new SetLocked { Layer = Layers.Umpire, Id = id, Value = false });
        await After(0.4);
        await Drag();
        GD.Print($"[lock] unlocked, dragged: it moved {(At() - start).Length:0.00} units");
    }

    public void Attach() => Report(AttachAsync());

    async Task AttachAsync()
    {
        await After(3.0);
        var board = S.View.Layer(Layers.Umpire);
        if (board.Count < 2) { GD.Print("[attach] this needs two pieces on the table"); return; }
        var parent = board[0];
        var child = board[1];
        string Where() => $"{parent.Id} at {S.View.Find(Layers.Umpire, parent.Id)!.Pos.X:0.0},{S.View.Find(Layers.Umpire, parent.Id)!.Pos.Z:0.0} · {child.Id} at {S.View.Find(Layers.Umpire, child.Id)!.Pos.X:0.0},{S.View.Find(Layers.Umpire, child.Id)!.Pos.Z:0.0}";
        GD.Print($"[attach] before: {Where()}");
        S.Do(new KTable.Core.Protocol.Attach { Layer = Layers.Umpire, Parent = parent.Id, Child = child.Id });
        await After(0.5);
        var held = S.View.Find(Layers.Umpire, parent.Id)!;
        GD.Print($"[attach] attached: {parent.Id} now carries [{string.Join(",", held.Attached)}]");
        S.Do(new MovePiece { Layer = Layers.Umpire, Id = parent.Id, Pos = new Vec2(held.Pos.X + 20, held.Pos.Z + 10), RotDeg = held.RotDeg });
        await After(0.5);
        GD.Print($"[attach] after a move of 20,10: {Where()}");
    }

    public void Boards() => Report(BoardsAsync());

    async Task BoardsAsync()
    {
        await After(3.0);
        void Say(string step) => GD.Print($"[boards] {step}: layers [{string.Join(", ", S.View.Boards.Select(b => $"{b.Id} \"{b.Name}\""))}] · hidden [{string.Join(",", T.HiddenBoards)}] · drawings shown {T.DrawingCount()} of {S.View.Drawings.Values.Sum(l => l.Count)}");
        Say("to begin with");
        var first = T.DrawBoard();
        S.Do(new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = new() { new Vec2(-4, -2), new Vec2(4, -2) }, Color = "#c0392b", Width = 0.1, Board = first } });
        S.Do(new AddBoard { Name = "Plan" });
        await After(0.6);
        var second = S.View.Boards[^1].Id;
        T.Board = second;
        S.Do(new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = new() { new Vec2(-4, 2), new Vec2(4, 2) }, Color = "#2a6ebb", Width = 0.1, Board = second } });
        await After(0.6);
        Say("a line on each of two layers");
        T.ShowBoard(first, false);
        T.RefreshDrawings();
        await After(0.4);
        Say("the first layer hidden");
        GD.Print("[boards] nothing was sent for the hiding: it is a setting of this screen, not a command.");

        T.ShowBoard(first, true);
        T.RefreshDrawings();
        T.SetTool(Tool.Erase);
        async Task Click(Vec2 at, string what)
        {
            var sc = T.Rig.Cam.UnprojectPosition(Coords.ToWorld(at, 0f));
            foreach (bool down in new[] { true, false })
            {
                Btn(sc, down);
                await NextFrame();
            }
            await After(0.4);
            GD.Print($"[boards] rubbed at {what}: {S.View.Drawings.Values.Sum(l => l.Count)} drawings left");
        }
        await Click(new Vec2(0, -2), "the line on the layer NOT in hand");
        await Click(new Vec2(0, 2), "the line on the layer in hand");
        T.SetTool(Tool.Select);

        S.Do(new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = new() { new Vec2(-4, 0), new Vec2(4, 0) }, Color = "#2e8b45", Width = 0.1, Board = second } });
        S.Do(new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = new() { new Vec2(0, -4), new Vec2(0, 4) }, Color = "#2e8b45", Width = 0.1, Board = second } });
        await After(0.6);
        GD.Print($"[boards] two lines on \"Plan\": the game holds {S.View.Drawings.Values.Sum(l => l.Count)}, the table draws {T.DrawnSurfaces()}");
        S.Do(new RemoveBoard { Id = second });
        await After(0.6);
        GD.Print($"[boards] \"Plan\" removed: the game holds {S.View.Drawings.Values.Sum(l => l.Count)}, the table draws {T.DrawnSurfaces()}; layers left [{string.Join(", ", S.View.Boards.Select(b => b.Name))}]");
    }

    public void Undo() => Report(UndoAsync());

    async Task UndoAsync()
    {
        await After(3.0);
        var p = S.View.Layer(Layers.Umpire).FirstOrDefault();
        if (p == null) { GD.Print("[undo] nothing on the board"); return; }
        string Where() => $"{S.View.Find(Layers.Umpire, p.Id)!.Pos.X:0.0},{S.View.Find(Layers.Umpire, p.Id)!.Pos.Z:0.0}";
        GD.Print($"[undo] {p.Id} starts at {Where()}");
        foreach (var x in new[] { 10.0, 20.0, 30.0 })
        {
            S.Do(new MovePiece { Layer = Layers.Umpire, Id = p.Id, Pos = new Vec2(x, 0), RotDeg = p.RotDeg });
            await After(0.4);
        }
        GD.Print($"[undo] after three moves: {Where()} · {S.MyUndoable.Count} on the undo stack");
        for (int i = 1; i <= 4; i++)
        {
            S.UndoLast();
            await After(0.5);
            GD.Print($"[undo] press {i}: {Where()} · {S.MyUndoable.Count} left on the stack");
        }
    }

    public void Select(int n) => Report(SelectAsync(n));

    async Task SelectAsync(int n)
    {
        await After(3.0);
        var board = S.View.Layer(Layers.Umpire).Take(Math.Max(2, n)).Select(p => p.Id).ToList();
        if (board.Count < 2) { GD.Print("[select] this needs two pieces on the table"); return; }
        void Say(string step)
        {
            var sel = string.Join(",", T.Selected);
            var lit = string.Join(",", T.Pieces.Nodes.Where(x => x.Masked).Select(x => x.Piece.Id));
            GD.Print($"[select] {step}: the table says [{sel}] · drawn as selected [{lit}]");
        }
        async Task Drag(Vector2 a, Vector2 b)
        {
            Btn(a, true);
            await Frames(2);
            Move(a, (a + b) / 2);
            await Frames(2);
            Move((a + b) / 2, b);
            await Frames(2);
            Btn(b, false);
            await Frames(3);
        }
        if (T.Rig.TopDown) T.Rig.ToggleTopDown();
        T.Rig.Yaw = Mathf.Pi;
        T.Rig.Pitch = -1.0f;
        T.FrameAll();
        await Frames(1);
        var nodes = board.Select(id => T.NodeOf(Layers.Umpire, id)!).ToList();
        var pts = nodes.Select(x => T.Rig.Cam.UnprojectPosition(x.GlobalPosition)).ToList();
        var vp = T.GetViewport().GetVisibleRect().Size;
        var box0 = new Vector2(pts.Min(q => q.X) - 60, pts.Min(q => q.Y) - 60);
        var box1 = new Vector2(pts.Max(q => q.X) + 60, pts.Max(q => q.Y) + 60);
        await Drag(box0, box1);
        Say($"boxed {board.Count}");
        var empty = new Vector2(vp.X * 0.5f, vp.Y * 0.88f);
        await Drag(empty, empty);
        Say("clicked the empty table");
        await Drag(pts[0], pts[0]);
        Say($"clicked {board[0]} alone");
        await Drag(box0, box1);
        Say($"boxed {board.Count} again");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await Frames(3);
        Say("pressed Esc");
        await Drag(box0, box1);
        await Drag(pts[0], pts[0]);
        Say($"boxed them, then clicked {board[0]}");
        await Drag(box0, box1);
        T.SetTool(Tool.Sighting);
        await Frames(3);
        Say($"sighting started (draft {(T.Draft == null ? "none" : "on " + T.Draft.ObserverId)})");
        T.SetTool(Tool.Select);
        await Frames(3);
        Say("back to the select tool");
        pts = nodes.Select(x => T.Rig.Cam.UnprojectPosition(x.GlobalPosition)).ToList();
        await Drag(pts[0], pts[0]);
        Say($"clicked {board[0]} with the draft still up");
        T.CancelSighting();
        await Frames(3);
        await Drag(pts[1], pts[1]);
        Say($"sighting cancelled, clicked {board[1]}");
    }

    public void Physics(Vec2 at, string earthwork, string block, string marker) => Report(PhysicsAsync(at, earthwork, block, marker));

    async Task PhysicsAsync(Vec2 at, string earthwork, string block, string marker)
    {
        string Id() => S.View.NewId();
        string ew = Id(), b1 = Id(), b2 = Id(), mk = Id();
        S.Do(new Batch { Items = new List<Command>
        {
            new SpawnPiece { Layer = Layers.Umpire, Def = earthwork, Pos = at, RotDeg = 180, Id = ew },
            new SpawnPiece { Layer = Layers.Umpire, Def = block, Pos = at + new Vec2(-3, 3), RotDeg = 180, Id = b1 },
            new SpawnPiece { Layer = Layers.Umpire, Def = block, Pos = at + new Vec2(0, 3), RotDeg = 180, Id = b2 },
            new SpawnPiece { Layer = Layers.Umpire, Def = marker, Pos = at + new Vec2(3, 3), RotDeg = 180, Id = mk },
        } });
        await After(1.5);
        string Where(string id) => S.View.Find(Layers.Umpire, id) is { } p ? $"{id} at {p.Pos.X:0.00},{p.Pos.Z:0.00} y {p.Y:0.000}" : $"{id} gone";
        void Say(string step) => GD.Print($"[phys] {step}: " + string.Join(" | ", new[] { ew, b1, b2, mk }.Select(Where)));
        async Task Carry(string id, Vec2 to)
        {
            T.SetSelection(new[] { id });
            var from = S.View.Find(Layers.Umpire, id)!.Pos;
            T.Hand.Start(from);
            GD.Print($"[phys] lifted {id}: the hand holds {T.Hand.Count} ({string.Join(", ", T.Hand.HeldIds)})");
            for (int i = 1; i <= 15; i++) { T.Hand.Move(new Vec2(from.X + (to.X - from.X) * i / 15.0, from.Z + (to.Z - from.Z) * i / 15.0)); await NextFrame(); }
            await After(0.3);
            T.Hand.Drop();
            await After(0.6);
        }
        Say("spawned");
        var ewNode = T.NodeOf(Layers.Umpire, ew)!;
        var probe = Enumerable.Range(-40, 81).Select(k => at + new Vec2(0, k * 0.025)).Select(q => (q, h: ewNode.SurfaceAt(Coords.ToWorld(q)) ?? 0)).OrderByDescending(x => x.h).First();
        GD.Print($"[phys] the earthwork's top is {probe.h:0.000} at {probe.q.X:0.00},{probe.q.Z:0.00}");
        await Carry(b1, probe.q);
        Say("block 1 put on the parapet");
        await Carry(b2, probe.q + new Vec2(1.25, 0));
        Say("block 2 let go with its edge over block 1");
        await Carry(mk, probe.q);
        Say("the marker put on block 1");
        await Carry(ew, at + new Vec2(0, -2.5));
        Say("the earthwork lifted and carried");
        var many = new List<Command>();
        var manyIds = new List<string>();
        for (int i = 0; i < 400; i++)
        {
            var id = Id();
            manyIds.Add(id);
            many.Add(new SpawnPiece { Layer = Layers.Umpire, Def = block, Pos = at + new Vec2(-16 + (i % 20) * 1.7, 8 + (i / 20) * 0.9), RotDeg = 180, Id = id });
        }
        S.Do(new Batch { Items = many });
        await After(2.0);
        T.SetSelection(manyIds);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var grabAt = at + new Vec2(0, 8 + 9);
        T.Hand.Start(grabAt);
        GD.Print($"[phys] lifted {T.Hand.Count} in {clock.ElapsedMilliseconds} ms");
        for (int i = 1; i <= 10; i++) { T.Hand.Move(new Vec2(grabAt.X, grabAt.Z - 17 * i / 10.0)); await NextFrame(); }
        await After(0.3);
        T.Hand.Drop();
        await After(1.0);
        var ys = manyIds.Select(id => S.View.Find(Layers.Umpire, id)?.Y ?? -1).ToList();
        GD.Print($"[phys] the formation down: {ys.Count(y => y > 0.01)} of {ys.Count} resting on something, highest at {ys.Max():0.000}");
    }

    public void Hold(List<string> ids, Vec2 to, int rows, Vec2? pasteAt) => Report(HoldAsync(ids, to, rows, pasteAt));

    async Task HoldAsync(List<string> ids, Vec2 to, int rows, Vec2? pasteAt)
    {
        T.SetSelection(ids);
        var nodes = T.SelectedNodes.ToList();
        var grab = new Vec2(nodes.Average(n => n.Piece.Pos.X), nodes.Average(n => n.Piece.Pos.Z));
        T.Hand.Start(grab);
        for (int i = 1; i <= 20; i++) { T.Hand.Move(new Vec2(grab.X + (to.X - grab.X) * i / 20.0, grab.Z + (to.Z - grab.Z) * i / 20.0)); await NextFrame(); }
        T.FormRows(rows);
        T.Hand.Rotate(30);
        await After(0.5);
        T.Hand.Drop();
        await After(2.5);
        if (pasteAt is { } pa) { T.CopySelection(); T.Paste(pa); }
    }

    public void Stack() => Report(StackAsync());

    async Task StackAsync()
    {
        List<string> Census() => S.View.Layer(Layers.Umpire).Select(p => $"{p.Id}={p.Def}@{p.Pos.X:0.0},{p.Pos.Z:0.0}").ToList();
        await After(3.0);
        var before = Census();
        GD.Print($"[stack] before: {before.Count} pieces - " + string.Join(" | ", before));
        var board = S.View.Layer(Layers.Umpire).ToList();
        if (board.Count < 2) { GD.Print("[stack] need two pieces"); return; }
        var a = board[0];
        var b = board[1];
        T.SetSelection(new[] { a.Id });
        T.Hand.Start(a.Pos);
        for (int i = 1; i <= 18; i++)
        {
            double k = i / 18.0;
            T.Hand.Move(new Vec2(a.Pos.X + (b.Pos.X - a.Pos.X) * k, a.Pos.Z + (b.Pos.Z - a.Pos.Z) * k));
            await Frames(1);
        }
        await After(0.3);
        T.Hand.Drop();
        await After(2.5);
        var after = Census();
        GD.Print($"[stack] after: {after.Count} pieces - " + string.Join(" | ", after));
        var ids = before.Select(x => x.Split('=')[0]).ToHashSet();
        foreach (var n in after.Where(x => !ids.Contains(x.Split('=')[0]))) GD.Print("[stack] NEW PIECE " + n);
    }

    public void Grab(string id) => Report(GrabAsync(id));

    async Task GrabAsync(string id)
    {
        if (T.NodeOf(Layers.Umpire, id) is not { } n) { GD.Print("[grab] no such piece"); return; }
        Vec2 At() => Coords.FromWorld(n.Position);
        string Show(Vec2 p) => $"{p.X:0.00},{p.Z:0.00}";

        T.SetSelection(new[] { id });
        var start = n.Piece.Pos;
        var carryTo = new Vec2(start.X + 14, start.Z + 10);
        T.Hand.Start(start);
        for (int i = 1; i <= 20; i++) { T.Hand.Move(Lerp(start, carryTo, i / 20.0)); await Frames(1); }
        GD.Print($"[grab] start {Show(start)} · carried to {Show(At())} (cursor {Show(carryTo)}) at height {n.Position.Y:0.00}");

        T.FlipPieces(new[] { n });
        await Frames(4);
        GD.Print($"[grab] four frames into the turn: at {Show(At())}, {(At() - carryTo).Length:0.000} from the cursor, {(At() - start).Length:0.000} from where it was picked up");
        await After(0.5);
        GD.Print($"[grab] turn over: at {Show(At())}, face down {n.Piece.FaceDown}, still in hand {n.Held}");

        T.Hand.Drop();
        await Frames(2);
        var caught = At();
        T.Hand.Start(caught);
        var thenTo = new Vec2(carryTo.X - 9, carryTo.Z + 6);
        for (int i = 1; i <= 15; i++) { T.Hand.Move(Lerp(caught, thenTo, i / 15.0)); await Frames(1); }
        GD.Print($"[grab] caught out of the drop at {Show(caught)} and carried to {Show(At())} (cursor {Show(thenTo)}) at height {n.Position.Y:0.00}");
        await After(0.6);
        GD.Print($"[grab] held still for 0.6 s: at {Show(At())}, {(At() - thenTo).Length:0.000} from the cursor (a drop still running would have moved it)");
        T.Hand.Drop();
        await After(1.5);
        GD.Print($"[grab] dropped: node at {Show(At())}, the table says {Show(n.Piece.Pos)}, y {n.Piece.Y:0.00}, face down {n.Piece.FaceDown}");
    }

    public void Push(int which, Vec2 to, bool steady = false) => Report(PushAsync(which, to, steady));

    async Task PushAsync(int which, Vec2 to, bool steady = false)
    {
        var board = S.View.Layer(Layers.Umpire).ToList();
        if (board.Count <= which) { GD.Print("[push] no such piece"); return; }
        var nodes = board.Select(p => T.NodeOf(Layers.Umpire, p.Id)).Where(n => n != null).Select(n => n!).ToList();
        string Drawn_(PieceNode n) { var p = Coords.FromWorld(n.Position); return $"{n.Piece.Id}@{p.X:0.00},{p.Z:0.00}"; }
        string Sent(PieceNode n) => $"{n.Piece.Id}@{n.Piece.Pos.X:0.00},{n.Piece.Pos.Z:0.00}";
        var start = nodes.ToDictionary(n => n, n => n.Piece.Pos);
        var carrier = T.NodeOf(Layers.Umpire, board[which].Id)!;
        int Shifted() => nodes.Count(n => GodotObject.IsInstanceValid(n) && n != carrier && (Coords.FromWorld(n.Position) - start[n]).Length > 0.05);
        GD.Print($"[push] the table before ({nodes.Count} pieces): " + string.Join(" | ", nodes.Take(12).Select(Sent)));
        var from = carrier.Piece.Pos;
        T.SetSelection(new[] { carrier.Piece.Id });
        var idle = System.Diagnostics.Stopwatch.StartNew();
        await Frames(150);
        GD.Print($"[push] 150 frames with nothing in hand: {idle.Elapsed.TotalMilliseconds / 150:0.00} ms a frame");
        T.Hand.Start(from);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 1; i <= 150; i++)
        {
            T.Hand.Move(Lerp(from, to, i / 150.0));
            await Frames(1);
            if (i == 75) GD.Print($"[push] half way, still in hand ({Shifted()} shoved so far): " + string.Join(" | ", nodes.Where(GodotObject.IsInstanceValid).Take(12).Select(Drawn_)));
        }
        GD.Print($"[push] {board.Count} pieces on the table; the 150-frame carry took {watch.ElapsedMilliseconds} ms, {watch.Elapsed.TotalMilliseconds / 150:0.00} ms a frame");
        GD.Print($"[push] end of the carry, still in hand ({Shifted()} shoved): " + string.Join(" | ", nodes.Where(GodotObject.IsInstanceValid).Take(12).Select(Drawn_)));
        if (steady) await After(0.3);
        T.Hand.Drop();
        await After(2.5);
        GD.Print($"[push] the table after: " + string.Join(" | ", nodes.Where(GodotObject.IsInstanceValid).Take(12).Select(Sent)));
        var went = nodes.Where(n => GodotObject.IsInstanceValid(n) && n != carrier && (n.Piece.Pos - start[n]).Length > 0.05).ToList();
        GD.Print($"[push] committed as moved: {went.Count} - " + string.Join(", ", went.Select(n => $"{n.Piece.Id} by {(n.Piece.Pos - start[n]).Length:0.00}")));
    }

    public void Throw(List<string> ids, Vec2 from, Vec2 to, int frames) => Report(ThrowAsync(ids, from, to, frames));

    async Task ThrowAsync(List<string> ids, Vec2 from, Vec2 to, int frames)
    {
        T.SetSelection(ids);
        T.Hand.Start(from);
        for (int i = 1; i <= frames; i++)
        {
            T.Hand.Move(new Vec2(from.X + (to.X - from.X) * i / frames, from.Z + (to.Z - from.Z) * i / frames));
            await NextFrame();
        }
        GD.Print($"[throw] release velocity {T.Hand.ThrowVelocity().Length():0.0} u/s");
        T.Hand.Drop();
    }
}
