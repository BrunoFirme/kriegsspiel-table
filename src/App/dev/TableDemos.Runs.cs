using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class TableDemos
{
    async System.Threading.Tasks.Task DemoWheel(Table table)
    {
        await Wait(3.0);
        var board = Session.View.Layer(KTable.Core.Model.Layers.Umpire);
        if (board.Count == 0) { GD.Print("[wheel] nothing on the board"); return; }
        var p = board[0];
        table.SetSelection(new[] { p.Id });
        var vp = _main.GetViewport().GetVisibleRect().Size;
        async System.Threading.Tasks.Task Notch(Vector2 at, string where)
        {
            await Frames(2);
            double d0 = table.Rig.Distance, r0 = p.RotDeg;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = at, GlobalPosition = at, Factor = 1 });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false, Position = at, GlobalPosition = at, Factor = 1 });
            await Wait(0.8);
            GD.Print($"[wheel] {where}: camera {d0:0.000} -> {table.Rig.Distance:0.000}, piece {r0:0.00} -> {p.RotDeg:0.00} deg");
        }
        await Notch(vp / 2, "pointer ON the piece ");
        await Notch(new Vector2(vp.X * 0.85f, vp.Y * 0.25f), "pointer OFF the piece");
    }

    void PlaceCamera(Table table)
    {
        try { PlaceCameraCore(table); }
        catch (Exception e) { GD.Print("[cam] not yet: " + e.Message); }
    }

    void PlaceCameraCore(Table table)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (_args.TryGetValue("cam", out var cam))
        {
            var parts = cam.Split(',');
            KTable.Core.Model.Vec2 at;
            if (parts[0].StartsWith("piece:")) at = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[int.Parse(parts[0][6..])].Pos;
            else at = new KTable.Core.Model.Vec2(double.Parse(parts[0], inv), double.Parse(parts[1], inv));
            table.Rig.Frame(Coords.ToWorld(at), float.Parse(parts[^1], inv));
        }
        if (_args.TryGetValue("pitch", out var pitchDeg))
        {
            table.Rig.Pitch = Mathf.Clamp(-Mathf.DegToRad(float.Parse(pitchDeg, inv)), -1.55f, -0.12f);
            table.Rig.Apply();
        }
        if (_args.TryGetValue("yaw", out var yawDeg))
        {
            table.Rig.Yaw = Mathf.Pi - Mathf.DegToRad(float.Parse(yawDeg, inv));
            table.Rig.Apply();
        }
        if (_args.TryGetValue("demo-north", out var dno))
            Tree.CreateTimer(double.Parse(dno, inv)).Timeout += () => table.Rig.FaceNorth();
    }

    async System.Threading.Tasks.Task DemoTransform(Table t)
    {
        await _main.ToSignal(Tree.CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
        var p = t.SelectedNodes.FirstOrDefault()?.Piece;
        if (p == null) { GD.Print("[transform] nothing selected"); return; }
        GD.Print($"[transform] before rot={p.RotDeg:0.#} scale={p.Scale:0.##}");
        for (int i = 0; i < 6; i++) { t.RotateSmooth(5); t.ScaleSmooth(1.07); await _main.ToSignal(Tree.CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout); }
        await _main.ToSignal(Tree.CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        var after = Net.Server!.State.Find(KTable.Core.Model.Layers.Umpire, p.Id)!;
        GD.Print($"[transform] server after rot={after.RotDeg:0.#} scale={after.Scale:0.##} log={Net.Server.Log.Count}");
    }

    async System.Threading.Tasks.Task Demo(Table t, Hud hud)
    {
        await _main.ToSignal(Tree.CreateTimer(double.Parse(_args.GetValueOrDefault("demo-delay", "3"), System.Globalization.CultureInfo.InvariantCulture)), SceneTreeTimer.SignalName.Timeout);
        var board = Session.View.Layer(KTable.Core.Model.Layers.Umpire);
        if (board.Count == 0) { GD.Print("[demo] nothing on the umpire board yet (try a longer --demo-delay)"); return; }
        var obs = board[int.Parse(_args.GetValueOrDefault("demo-piece", "0"))];
        Session.Do(new KTable.Core.Protocol.RollDice { Count = 2, Sides = 6, Label = "demo" });
        t.SetSelection(new[] { obs.Id });
        t.SetTool(Tool.Sighting);
        if (t.Draft == null) { GD.Print("[demo] the sighting tool opened no draft"); return; }
        t.Draft.Radius = double.Parse(_args.GetValueOrDefault("demo-radius", "18"), System.Globalization.CultureInfo.InvariantCulture);
        var o = obs.Pos;
        t.Draft.Walls.Add(new() { o + new KTable.Core.Model.Vec2(-14, 4), o + new KTable.Core.Model.Vec2(14, 4) });
        t.Draft.Note = "demo sighting";
        t.Draft.Recompute(t);
        t.ShowDraft();
        t.RefreshSightingPanel();
        var seen = t.Draft.Shape();
        int behind = board.Count(p => (p.Pos - o).Length <= t.Draft.Radius && p.Pos.Z > o.Z + 4 && !seen.Contains(p.Pos));
        int infront = board.Count(p => (p.Pos - o).Length <= t.Draft.Radius && p.Pos.Z < o.Z + 4 && seen.Contains(p.Pos));
        GD.Print($"[demo] wall traced: the lit ground is {t.Draft.Polygon.Count} points; {behind} pieces behind the wall are out of it, {infront} in front are in it; {t.Draft.Included.Count} in the picture");
        await _main.ToSignal(Tree.CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        await hud.CaptureSighting();
        await _main.ToSignal(Tree.CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        var vp = _main.GetViewport().GetVisibleRect().Size;
        await hud.CaptureRect(new Rect2(vp * 0.3f, vp * 0.4f));
        GD.Print("[demo] done");
    }

    static void Mouse(MouseButton b, bool pressed, Vector2 at) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = b, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? (MouseButtonMask)(1 << ((int)b - 1)) : 0 });
    static void Move(Vector2 from, Vector2 to, MouseButtonMask held) =>
        Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = held });
    static void KeyTap(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }
    async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame); }

    async System.Threading.Tasks.Task DemoMapPerf(Table table, string spec)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var parts = spec.Split(',');
        double secs = double.Parse(parts[0], inv), speed = parts.Length > 1 ? double.Parse(parts[1], inv) : 0;
        await Wait(2.0);
        Session.Assets.Uploaded = 0;
        double t = 0, tick = 0; int frames = 0; double worst = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (t < secs)
        {
            double before = sw.Elapsed.TotalSeconds;
            await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            double dt = sw.Elapsed.TotalSeconds - before;
            t += dt; tick += dt; frames++; worst = Math.Max(worst, dt);
            if (speed > 0) table.Rig.PanWorld(new Vector3((float)(speed * dt), 0, 0));
            if (tick >= 1.0)
            {
                GD.Print($"[mapperf] t={t:0}s fps={frames / tick:0} worst={worst * 1000:0}ms dist={table.Rig.Distance:0} {table.SheetStats()} tex={Session.Assets.TexCount} waiting={Session.Assets.WaitingCount} uploaded={Session.Assets.Uploaded}");
                tick = 0; frames = 0; worst = 0; Session.Assets.Uploaded = 0;
            }
        }
        GD.Print("[mapperf] done");
    }
    async System.Threading.Tasks.Task Wait(double s) => await _main.ToSignal(Tree.CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    async System.Threading.Tasks.Task DemoPointers(Table table, Hud hud)
    {
        table.S.OnAccess(new GameAccess { Approved = { new Approved { Name = "Pogue", Role = Role.Umpire }, new Approved { Name = "Boggs", Role = Role.Umpire }, new Approved { Name = "Lead umpire", Role = Role.LeadUmpire } } });
        var people = new KTable.Core.Protocol.PeerList
        {
            Peers = [table.S.Me, new() { PeerId = 2, Name = "Pogue", Role = Role.Umpire }, new() { PeerId = 3, Name = "Boggs", Role = Role.Umpire }, new() { PeerId = 4, Name = "Lead umpire", Role = Role.LeadUmpire }, new() { PeerId = 5, Name = "Watcher", Role = Role.Observer }],
            Waiting = [new() { PeerId = 6, Name = "Newcomer" }],
        };
        table.S.Do(new KTable.Core.Protocol.SetZone { Zone = new Zone { Name = "West", Owner = "Pogue", Polygon = { new(-8, -3), new(-1, -3), new(-1, 6), new(-8, 6) } } });
        var carried = table.S.View.Layer(Layers.Umpire).Where(p => table.Pieces.NodeOf(Layers.Umpire, p.Id) is { Footprint.X: > 1 }).OrderBy(p => (p.Pos - new Vec2(2, -2)).Length).FirstOrDefault()?.Id;
        for (var frame = 0; frame < 900; frame++)
        {
            var t = frame / 60.0;
            table.ShowRemoteCursor(2, "Pogue", new Vec2(-3 + 2.5 * Math.Cos(t * 2.4), 1 + 2.5 * Math.Sin(t * 2.4)));
            var boggs = new Vec2(2 + 3 * Math.Sin(t * 1.3), -2);
            table.ShowRemoteCursor(3, "Boggs", boggs);
            if (carried != null)
                table.CarryLayoutArrived(3, [new KTable.Server.CarriedPiece(Layers.Umpire, carried, 0, 0, 0, 0)], new KTable.Client.Hand.HandAt((float)boggs.X, (float)boggs.Z, Carry.LiftHeight), t);
            table.ShowRemoteCursor(4, "Lead umpire", new Vec2(4, 3));
            if (frame % 30 == 0) table.S.OnPeers(people);
            if (frame == 420) hud.Top.People?.Open();
            await Frames(1);
        }
    }

    async System.Threading.Tasks.Task DemoCamera(Table table)
    {
        await Wait(2.0);
        var c = _main.GetViewport().GetVisibleRect().Size / 2;
        Pointer.Drive(c);
        var t0 = table.Rig.Target;
        Mouse(MouseButton.Middle, true, c);
        var p = c;
        for (int i = 0; i < 8; i++) { var q = p + new Vector2(28, 0); Move(p, q, MouseButtonMask.Middle); p = q; await Frames(1); }
        Mouse(MouseButton.Middle, false, p);
        await Frames(1);
        var t1 = table.Rig.Target;
        await Wait(0.8);
        var t2 = table.Rig.Target;
        GD.Print($"[camera] pan: dragged {(t1 - t0).Length():0.0} u, glided on {(t2 - t1).Length():0.0} u more after release (gliding now: {table.Rig.Gliding})");
        var y0 = table.Rig.Yaw;
        Mouse(MouseButton.Right, true, p);
        for (int i = 0; i < 8; i++) { var q = p + new Vector2(20, 0); Move(p, q, MouseButtonMask.Right); p = q; await Frames(1); }
        Mouse(MouseButton.Right, false, p);
        await Frames(1);
        var y1 = table.Rig.Yaw;
        await Wait(0.8);
        var y2 = table.Rig.Yaw;
        GD.Print($"[camera] orbit: dragged {Mathf.RadToDeg(y1 - y0):0.0}°, glided on {Mathf.RadToDeg(y2 - y1):0.0}° more after release");
        var t3 = table.Rig.Target;
        Mouse(MouseButton.Middle, true, p);
        for (int i = 0; i < 4; i++) { var q = p + new Vector2(28, 0); Move(p, q, MouseButtonMask.Middle); p = q; await Frames(1); }
        await Wait(0.3);
        Mouse(MouseButton.Middle, false, p);
        await Frames(1);
        var t4 = table.Rig.Target;
        await Wait(0.5);
        GD.Print($"[camera] pan then pause: dragged {(t4 - t3).Length():0.0} u, glided {(table.Rig.Target - t4).Length():0.0} u (expected 0)");
    }

    async System.Threading.Tasks.Task DemoScript(Table table, string spec)
    {
        int n = int.Parse(spec);
        await Wait(2.0);
        var piece = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[n];
        const string script = """
            function on_load(s) draw() end
            function draw()
              local sz = self.size
              self.clear_widgets()
              self.button{ x = 0, z = -sz.h * 0.3, label = "Ammo " .. (self.counters.Ammo or 0), font = sz.h * 0.22, text_color = "white" }
              self.button{ x = sz.w * 0.32, z = sz.h * 0.25, w = sz.w * 0.22, h = sz.h * 0.3, label = "+", font = sz.h * 0.22, color = "#DBD500", text_color = "black", on_click = "up", tooltip = "Ammo up" }
              self.input{ x = -sz.w * 0.16, z = sz.h * 0.25, w = sz.w * 0.6, h = sz.h * 0.3, value = self.text.Name or "", placeholder = "Name", font = sz.h * 0.18, color = "#EEEEEE", text_color = "black", on_change = "rename" }
              self.button{ face = "back", x = 0, z = 0, w = sz.w * 0.28, h = sz.h * 0.45, label = tostring(self.counters.Ammo or 0), font = sz.h * 0.32, color = "black", text_color = "white", on_click = "up", tooltip = "Ammo" }
            end
            function up(who, alt, i) self.set_counter("Ammo", (self.counters.Ammo or 0) + (alt and -1 or 1)) draw() end
            function rename(who, v) self.set_text("Name", v) end
            """;
        Session.Do(new KTable.Core.Protocol.SetScript { Layer = KTable.Core.Model.Layers.Umpire, Id = piece.Id, Text = script });
        await Wait(1.0);
        GD.Print($"[script] piece {piece.Id}: {piece.Widgets.Count} widgets: {string.Join(" | ", piece.Widgets.Select(w => $"{w.Kind} '{(w.Kind == "input" ? w.Value : w.Label)}' at {w.X:0.00},{w.Z:0.00}"))}; Ammo {piece.Counters.GetValueOrDefault("Ammo")}");
        Session.Do(new KTable.Core.Protocol.ClickWidget { Layer = KTable.Core.Model.Layers.Umpire, Id = piece.Id, Index = 2 });
        await Wait(0.5);
        GD.Print($"[script] after the click: Ammo {piece.Counters.GetValueOrDefault("Ammo")}; label '{piece.Widgets[0].Label}'; log tail: {string.Join(", ", Net.Server!.Log.TakeLast(3).Select(c => c.GetType().Name))}");
    }

    async System.Threading.Tasks.Task DemoStateKey(Table table, string spec)
    {
        var a = spec.Split(',').Select(int.Parse).ToArray();
        await Wait(2.0);
        var piece = Session.View.Layer(KTable.Core.Model.Layers.Umpire)[a[0]];
        var node = table.NodeOf(KTable.Core.Model.Layers.Umpire, piece.Id)!;
        var key = a[1] == 10 ? Key.Key0 : Key.Key0 + a[1];
        var def = Session.Def(piece.Def);
        GD.Print($"[statekey] piece {piece.Id} '{def?.Name}' has {def?.States.Count} states; state now {piece.State}");
        table.SetSelection(Array.Empty<string>());
        var at = table.Rig.Cam.UnprojectPosition(node.GlobalPosition);
        Pointer.Drive(at); Move(at, at, 0);
        await Wait(0.3);
        KeyTap(key);
        await Wait(0.3);
        GD.Print($"[statekey] hovered + key {a[1]}: state {piece.State}");
        var away = new Vector2(_main.GetViewport().GetVisibleRect().Size.X - 40, _main.GetViewport().GetVisibleRect().Size.Y - 40);
        Pointer.Drive(away); Move(at, away, 0);
        table.SetSelection(new[] { piece.Id });
        await Wait(0.3);
        KeyTap(Key.Key1);
        await Wait(0.3);
        GD.Print($"[statekey] selected + key 1: state {piece.State}");
    }

    static IEnumerable<KTable.Core.Protocol.Command> Flat(KTable.Core.Protocol.Command c) => c is KTable.Core.Protocol.Batch b ? b.Items.SelectMany(Flat) : new[] { c };

    async System.Threading.Tasks.Task DemoHotkey(Table table, string spec)
    {
        var a = spec.Split(',').Select(int.Parse).ToArray();
        await Wait(3.0);
        var layer = Session.View.Layer(KTable.Core.Model.Layers.Umpire);
        var piece = layer[a[0]];
        var node = table.NodeOf(KTable.Core.Model.Layers.Umpire, piece.Id)!;
        var def = Session.Def(piece.Def);
        GD.Print($"[hotkey] piece {piece.Id} '{def?.Name}' tags [{string.Join(",", piece.Tags ?? new())}] memo {piece.Memo}; {def?.States.Count} states, state {piece.State}; hotkeys: {string.Join(" | ", Session.View.Hotkeys.Select(h => h.Label + "=" + table.Settings.Keys.Label("script:" + h.Label)))}; table menu: {string.Join(" | ", Session.View.MenuItems.Select(m => m.Label))}");
        table.SetSelection(Array.Empty<string>());
        var at = table.Rig.Cam.UnprojectPosition(node.GlobalPosition);
        Pointer.Drive(at); Move(at, at, 0);
        await Wait(0.5);
        static IEnumerable<KTable.Core.Model.UiNode> All(KTable.Core.Model.UiNode n) { yield return n; foreach (var c in n.Children) foreach (var x in All(c)) yield return x; }
        foreach (var k in a.Skip(1))
        {
            KeyTap(k == 10 ? Key.Key0 : Key.Key0 + k);
            await Wait(2.0);
            var ribbon = Session.View.Ui.SelectMany(All).Where(n => n.Tag == "Text" && n.Attributes.GetValueOrDefault("text", "").Length > 0).Select(n => n.Attributes["text"]).ToList();
            var now = layer.FirstOrDefault(p => p.Id == piece.Id);
            GD.Print($"[hotkey] key {k}: piece {(now == null ? "gone" : $"state {now.State}, memo {now.Memo}")}; {layer.Count} pieces; ribbon: {string.Join(" / ", ribbon)}; log tail: {string.Join(", ", Net.Server!.Log.TakeLast(4).Select(c => c.GetType().Name))}; reaction: {string.Join(" | ", Flat(Net.Server!.Log.LastOrDefault(e => Flat(e).Any(x => x is KTable.Core.Protocol.NumberTyped or KTable.Core.Protocol.Hotkey)) ?? Net.Server!.Log[^1]).Select(c => c is KTable.Core.Protocol.Say sy ? "Say " + sy.Text : c.GetType().Name))}");
        }
    }

    async System.Threading.Tasks.Task DemoPalette(Table table, Hud hud)
    {
        await Wait(2.0);
        new HudDemo(hud).Run("pieces");
        await Wait(1.0);
        int before = Session.View.Layer(KTable.Core.Model.Layers.Umpire).Count;
        var from = hud.Pieces.ItemCentre(0);
        var to = _main.GetViewport().GetVisibleRect().Size / 2;
        Pointer.Drive(from); Move(from, from, 0);
        await Frames(2);
        Mouse(MouseButton.Left, true, from);
        var p = from;
        for (int i = 1; i <= 10; i++) { var q = from.Lerp(to, i / 10f); Move(p, q, MouseButtonMask.Left); p = q; await Frames(2); }
        Pointer.Drive(to);
        await Frames(2);
        GD.Print($"[palette] dragging={table.PaletteDragging} tool={table.Tool}");
        Mouse(MouseButton.Left, false, to);
        await Wait(0.5);
        var board = Session.View.Layer(KTable.Core.Model.Layers.Umpire);
        var last = board.Count > before ? board[^1] : null;
        var ground = table.Rig.GroundAt(to) is { } w ? Coords.FromWorld(w) : (Vec2?)null;
        GD.Print($"[palette] pieces {before} -> {board.Count}; placed {last?.Def} at {last?.Pos.X:0.0},{last?.Pos.Z:0.0} (pointer over {ground?.X:0.0},{ground?.Z:0.0}); tool now {table.Tool}");
    }

    async System.Threading.Tasks.Task DemoRestore(Table table, string spec)
    {
        await Wait(1.5);
        var U = KTable.Core.Model.Layers.Umpire;
        var p0 = Session.View.Layer(U)[int.Parse(spec)];
        var x0 = p0.Pos.X;
        Session.Do(new KTable.Core.Protocol.AdvanceClock());
        await Wait(0.5);
        Session.Do(new KTable.Core.Protocol.MovePiece { Layer = U, Id = p0.Id, Pos = p0.Pos + new Vec2(12, 0), RotDeg = p0.RotDeg });
        await Wait(0.5);
        Session.Do(new KTable.Core.Protocol.AdvanceClock());
        await Wait(1.5);
        GD.Print($"[restore] before: clock turn {Session.Live.Clock.Turn}, turns {Session.Turns.Count}, piece x {Session.Live.Find(U, p0.Id)!.Pos.X:0.#}");
        string? why = "not answered";
        Net.Saves.Load(new KTable.Client.LoadFrom("Turn 1", TurnIndex: 1), w => why = w);
        await Wait(3);
        GD.Print($"[restore] load: {why ?? "done"}; saved in {Session.SavePath}");
        var cps = SaveStore.Default.Checkpoints(Session.SavePath!);
        GD.Print($"[restore] after: clock turn {Session.Live.Clock.Turn}, turns {Session.Turns.Count}, piece x {Session.Live.Find(U, p0.Id)!.Pos.X:0.#} (was {x0:0.#}), node x {Coords.FromWorld(table.NodeOf(U, p0.Id)!.Position).X:0.#}; checkpoints: {string.Join(", ", cps.Select(c => c.Kind + "-" + c.Turn))}");
    }
}
