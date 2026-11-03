using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class ReleaseDemo
{
    readonly Table T;
    readonly Func<Vector2>? _paletteItem;
    readonly Action? _openPieces;
    Vector2 _vp;
    string _sheetId = "", _pieceId = "";
    Vector2 _gui, _outside;

    public ReleaseDemo(Table table, Func<Vector2>? paletteItem, Action? openPieces)
    {
        T = table; _paletteItem = paletteItem; _openPieces = openPieces;
    }

    Session S => T.S;

    sealed record Gesture(string Key, string Name, MouseButton Button, Func<bool> Setup, Func<Vector2> From, Vector2 By);

    public async Task Run(string spec)
    {
        await T.ToSignal(T.GetTree().CreateTimer(4.0), SceneTreeTimer.SignalName.Timeout);
        var parts = spec.Split(':');
        var only = parts[0] is "" or "true" or "all" ? null : parts[0].Split(',').ToHashSet();
        var wheres = parts.Length > 1 ? parts[1].Split(',') : new[] { "panel", "chord", "outside", "popup", "focus", "appfocus" };
        _vp = T.GetViewport().GetVisibleRect().Size;

        var sheetId = S.View.Sheets.FirstOrDefault()?.Id;
        var pieceId = S.View.Layer(Layers.Umpire).FirstOrDefault()?.Id;
        if (sheetId == null || pieceId == null) { GD.Print("[release] this needs a map sheet and a piece on the table (see --new-blank with --demo-sheets)"); return; }
        _sheetId = sheetId; _pieceId = pieceId;
        if (S.View.Sheets.First(s => s.Id == sheetId).Locked) S.Do(new LockSheet { Id = sheetId, Locked = false });
        View();
        await Frames(3);
        await FindPanel();
        _outside = new Vector2(-40, _vp.Y * 0.5f);
        GD.Print($"[release] window at {DisplayServer.WindowGetPosition()}, {_vp.X:0}x{_vp.Y:0}; it has the focus: {T.GetWindow().HasFocus()}");

        foreach (var g in Gestures())
        {
            if (only != null && !only.Contains(g.Key)) continue;
            foreach (var where in wheres) await Try(g, where);
        }
        View();
        GD.Print("[release] done");
    }

    async Task Frames(int k) { for (int i = 0; i < k; i++) await T.ToSignal(T.GetTree(), SceneTree.SignalName.ProcessFrame); }
    async Task Wait(double s) => await T.ToSignal(T.GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
    static MouseButtonMask MaskOf(MouseButton b) => (MouseButtonMask)(1 << ((int)b - 1));
    static void Btn(MouseButton b, bool down, Vector2 at) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = down, Position = at, GlobalPosition = at, ButtonMask = down ? MaskOf(b) : 0 });
    static void Move(Vector2 from, Vector2 to, MouseButtonMask held) { Pointer.Drive(to); Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = held }); }

    void View(Vector3? at = null, float zoom = 1)
    {
        if (T.Rig.TopDown) T.Rig.ToggleTopDown();
        T.Rig.Yaw = Mathf.Pi; T.Rig.Pitch = -1.0f;
        T.FrameAll();
        if (at is { } p) T.Rig.Frame(p, 24);
        else if (zoom != 1) { T.Rig.Distance *= zoom; T.Rig.Apply(); }
    }

    Vec2 G(Vector2 screen) => T.Ground(screen) ?? new Vec2(0, 0);
    Draw Line(Vector2 a, Vector2 b) => new() { Layer = Layers.Umpire, Stroke = new Stroke { Points = new() { G(a), G(b) }, Color = "#2a6ebb", Width = 0.3, Board = T.DrawBoard() } };

    Command RubberLines() => new Batch { Items = new List<Command>
    {
        Line(_vp * new Vector2(0.50f, 0.56f), _vp * new Vector2(0.50f, 0.76f)),
        Line(_vp * new Vector2(0.40f, 0.42f), _vp * new Vector2(0.40f, 0.58f)),
    } };

    async Task FindPanel()
    {
        _gui = new Vector2(24, _vp.Y * 0.32f);
        foreach (var c in new[] { new Vector2(24, _vp.Y * 0.32f), new Vector2(_vp.X * 0.5f, 14), new Vector2(24, _vp.Y * 0.6f) })
        {
            Move(c, c, 0); await Frames(3);
            if (T.GetViewport().GuiGetHoveredControl() is { } hc && hc.MouseFilter != Control.MouseFilterEnum.Ignore) { _gui = c; GD.Print($"[release] let go over a panel: {hc.GetType().Name} '{hc.Name}' at {c.X:0},{c.Y:0}"); break; }
        }
    }

    Vector2 PieceAt() => T.Rig.Cam.UnprojectPosition(T.NodeOf(Layers.Umpire, _pieceId)!.GlobalPosition);
    Vector3 PieceWorld() => T.NodeOf(Layers.Umpire, _pieceId)!.GlobalPosition;
    Vector2 Knob(bool turn)
    {
        var nodes = T.SelectedNodes.ToList();
        var gs = nodes.Count == 0 ? T.Gizmo.Sheet : null;
        if (gs != null || nodes.Count > 0) T.Gizmo.Measure(nodes, gs);
        return T.Rig.Cam.UnprojectPosition((turn ? T.Gizmo.RotateKnobWorld() : T.Gizmo.ScaleKnobWorld()) + Vector3.Up * 0.4f);
    }
    Vector2 CropKnob() { var r = T.CropRect; return T.Rig.Cam.UnprojectPosition(T.Crop.World(r.X + r.W, r.Y + r.H / 2)); }
    bool Pick(Tool t) { T.SetTool(t); return T.Tool == t; }

    List<Gesture> Gestures()
    {
        var vp = _vp;
        var gestures = new List<Gesture>
        {
            new("knob", "ring knob turning a piece", MouseButton.Left, () => { View(PieceWorld()); T.SetSelection(new[] { _pieceId }); return Pick(Tool.Transform); }, () => Knob(true), new Vector2(90, 50)),
            new("size", "ring knob resizing a piece", MouseButton.Left, () => { View(PieceWorld()); T.SetSelection(new[] { _pieceId }); return Pick(Tool.Transform); }, () => Knob(false), new Vector2(30, 24)),
            new("sheet", "map sheet dragged", MouseButton.Left, () => { T.SelectSheet(_sheetId); return Pick(Tool.Select); }, () => vp * new Vector2(0.62f, 0.40f), new Vector2(70, 40)),
            new("sheetknob", "ring knob turning a map sheet", MouseButton.Left, () => { View(null, 1.9f); T.SelectSheet(_sheetId); return Pick(Tool.Transform); }, () => Knob(true), new Vector2(90, 30)),
            new("crop", "crop knob on a map sheet", MouseButton.Left, () => { T.BeginCrop(_sheetId); return T.Tool == Tool.Crop; }, CropKnob, new Vector2(-60, 0)),
            new("carry", "piece carried", MouseButton.Left, () => { View(PieceWorld()); return Pick(Tool.Select); }, PieceAt, new Vector2(80, 40)),
            new("box", "box select", MouseButton.Left, () => Pick(Tool.Select), () => vp * new Vector2(0.40f, 0.30f), new Vector2(140, 90)),
            new("ruler", "ruler", MouseButton.Left, () => Pick(Tool.Ruler), () => vp * new Vector2(0.40f, 0.45f), new Vector2(160, 30)),
            new("line", "line drawn", MouseButton.Left, () => Pick(Tool.Draw), () => vp * new Vector2(0.40f, 0.35f), new Vector2(140, 50)),
            new("shape", "shape drawn", MouseButton.Left, () => { T.ShapeKind = "rect"; return Pick(Tool.Shape); }, () => vp * new Vector2(0.58f, 0.30f), new Vector2(120, 70)),
            new("rubber", "rubber", MouseButton.Left, () => { S.Do(RubberLines()); return Pick(Tool.Erase); }, () => vp * new Vector2(0.33f, 0.50f), new Vector2(120, 0)),
            new("pan", "view dragged with the middle button", MouseButton.Middle, () => true, () => vp * 0.5f, new Vector2(120, 0)),
            new("orbit", "view turned with the right button", MouseButton.Right, () => true, () => vp * 0.5f, new Vector2(120, 0)),
        };
        if (_paletteItem != null)
            gestures.Add(new("palette", "piece dragged out of the Pieces drawer", MouseButton.Left, () => { _openPieces?.Invoke(); return Pick(Tool.Select); }, _paletteItem, Vector2.Zero));
        return gestures;
    }

    string Live()
    {
        var on = new List<string>();
        if (T.Router.LeftDown) on.Add("left button down");
        if (T.Gizmo.Dragging) on.Add($"{T.Gizmo.Held.ToString().ToLowerInvariant()} knob");
        if (T.SelectTool.Dragging) on.Add("dragging");
        if (T.SelectTool.BoxSelecting) on.Add("box");
        if (T.Holding) on.Add($"{T.Hand.Count} in hand");
        if (T.Router.Panning) on.Add("panning");
        if (T.Router.Orbiting) on.Add("orbiting");
        if (T.Pen.Stroking) on.Add("line");
        if (T.Pen.Shaping) on.Add("shape");
        if (T.Crop.Dragging) on.Add("crop knob");
        if (T.Palette.Dragging) on.Add("drawer piece");
        return on.Count == 0 ? "nothing" : string.Join(", ", on);
    }

    string Probe()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var sn in T.Sheets.Nodes) sb.Append($"{sn.Position}{sn.Rotation}{sn.Scale};");
        foreach (var n in T.Pieces.Nodes) sb.Append($"{n.Position}{n.Rotation}{n.Scale};");
        sb.Append($"{T.Rig.Target}{T.Rig.Yaw}{T.Rig.Pitch}{T.Rig.Distance};{T.Ruler.B};{T.Pen.ShapeEnd};{T.Pen.StrokePoints};{T.CropRect.X},{T.CropRect.W};");
        sb.Append(S.View.Drawings.Values.Sum(l => l.Sum(st => st.Points.Count)));
        return sb.ToString();
    }

    static string Named(Command c) => c switch
    {
        Batch bt => $"Batch[{string.Join(",", bt.Items.Select(Named))}]",
        MovePiece mp => FormattableString.Invariant($"MovePiece to ({mp.Pos.X:0.0}, {mp.Pos.Z:0.0})"),
        MoveSheet ms => FormattableString.Invariant($"MoveSheet to ({ms.Pos.X:0.0}, {ms.Pos.Z:0.0})"),
        SpawnPiece sp => FormattableString.Invariant($"SpawnPiece at ({sp.Pos.X:0.0}, {sp.Pos.Z:0.0})"),
        _ => c.GetType().Name,
    };

    async Task Try(Gesture g, string where)
    {
        T.SetTool(Tool.Select); T.SetSelection(Array.Empty<string>()); T.SelectSheet(null);
        View();
        await Frames(2);
        if (!g.Setup()) { GD.Print($"[release] {g.Name}: could not be set up"); return; }
        await Frames(3);
        var a = g.From();
        var to = g.By != Vector2.Zero ? a + g.By : _vp * new Vector2(0.55f, 0.5f);
        var sent = new List<string>();
        void Heard(Command c) => sent.Add(Named(c));
        S.Applied += Heard;
        try
        {
            Move(a, a, 0); await Frames(2);
            Btn(g.Button, true, a); await Frames(2);
            var p = a;
            for (int i = 1; i <= 6; i++) { var q = a.Lerp(to, i / 6f); Move(p, q, MaskOf(g.Button)); p = q; await Frames(2); }
            await Wait(0.15);
            var began = Live();
            var (how, thief, at) = await LetGo(where, g.Button, p);
            await Report(g, where, sent, began, how, thief, at);
        }
        finally { S.Applied -= Heard; }
        if (T.Tool == Tool.Crop) T.CancelCrop();
    }

    async Task<(string how, Window? thief, Vector2 p)> LetGo(string where, MouseButton button, Vector2 p)
    {
        string how;
        Window? thief = null;
        switch (where)
        {
            case "panel":
                Move(p, _gui, MaskOf(button)); p = _gui; await Wait(0.15);
                Btn(button, false, _gui);
                how = "let go over a panel";
                break;
            case "chord":
            {
                var other = button == MouseButton.Middle ? MouseButton.Right : MouseButton.Middle;
                Move(p, _gui, MaskOf(button)); p = _gui; await Wait(0.15);
                Btn(other, true, _gui); await Frames(2);
                Btn(button, false, _gui); await Frames(2);
                Btn(other, false, _gui);
                how = $"let go over a panel with the {other.ToString().ToLowerInvariant()} button pressed there";
                break;
            }
            case "outside":
                Move(p, _outside, MaskOf(button)); p = _outside; await Wait(0.15);
                Btn(button, false, _outside);
                how = "let go outside the window";
                break;
            case "popup":
            {
                var pop = new PopupPanel();
                pop.AddChild(new Label { Text = "Someone is knocking at the door" });
                T.AddChild(pop);
                pop.Popup(new Rect2I((int)(_vp.X * 0.6f), (int)(_vp.Y * 0.12f), 320, 120));
                await Frames(3);
                var at = new Vector2(pop.Position.X + pop.Size.X / 2f, pop.Position.Y + pop.Size.Y / 2f);
                Move(p, at, MaskOf(button)); p = at; await Wait(0.15);
                Btn(button, false, at);
                await Frames(3);
                pop.QueueFree();
                how = "let go over a popup";
                break;
            }
            case "appfocus":
                T.GetTree().Notification((int)MainLoop.NotificationApplicationFocusOut);
                T.GetTree().Root.PropagateNotification((int)Node.NotificationWMWindowFocusOut);
                await Wait(0.3);
                how = "the program lost focus";
                break;
            default:
                if (T.GetWindow().HasFocus())
                {
                    thief = new Window { Visible = false, Title = "focus thief", Size = new Vector2I(320, 90) };
                    thief.ForceNative = true;
                    thief.Position = DisplayServer.WindowGetPosition() + new Vector2I(80, 80);
                    T.AddChild(thief);
                    thief.Show();
                    thief.GrabFocus();
                    await Wait(0.5);
                    how = T.GetWindow().HasFocus() ? "another window opened but the focus stayed" : "the window lost focus";
                }
                else
                {
                    T.GetTree().Root.PropagateNotification((int)Node.NotificationWMWindowFocusOut);
                    await Wait(0.3);
                    how = "the window was told it lost focus";
                }
                break;
        }
        return (how, thief, p);
    }

    async Task Report(Gesture g, string where, List<string> sent, string began, string how, Window? thief, Vector2 p)
    {
        await Wait(0.6);
        var holds = Live();
        var s1 = Probe();
        var during = string.Join(" ", sent);
        var c0 = _vp * new Vector2(0.42f, 0.62f); var c1 = _vp * new Vector2(0.58f, 0.70f);
        Move(p, c0, 0); p = c0; await Frames(2);
        for (int i = 1; i <= 8; i++) { var q = c0.Lerp(c1, i / 8f); Move(p, q, 0); p = q; await Frames(2); }
        await Frames(2);
        var follows = Probe() != s1;
        var still = Live();
        if (thief != null) { thief.QueueFree(); await Frames(2); T.GetWindow().GrabFocus(); await Wait(0.3); }
        else if (where is "focus" or "appfocus")
        {
            if (where == "appfocus") T.GetTree().Notification((int)MainLoop.NotificationApplicationFocusIn);
            T.GetTree().Root.PropagateNotification((int)Node.NotificationWMWindowFocusIn);
        }
        sent.Clear();
        Btn(g.Button, false, c1);
        await Wait(0.4);
        var next = string.Join(" ", sent);
        GD.Print($"[release] {g.Name} · {how} (in hand: {began}): then holds {holds}; pointer moved on: follows {(follows ? "YES" : "no")}, holds {still}; sent {(during.Length > 0 ? during : "nothing")}; the next click sent {(next.Length > 0 ? next : "nothing")}");
    }
}
