using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class HudDemo : HudPart
{
    readonly Hud Hud;
    public HudDemo(Hud hud) : base(hud.Ctx) { Hud = hud; }

    public void Run(string what)
    {
        var centre = Hud.GetViewport().GetVisibleRect().Size / 2;
        if (Pages(what) || CompassDrag(what) || Hearts(what) || Scripts(what) || View(what) || PovPictures(what) || PovTool(what) || PovFormat(what) || DrawingPanels(what) || PanelMoves(what) || Hands(what) || PieceMoves(what) || Lettering(what) || Dialogs(what)) return;
        if (what == "dice") Hud.OpenPanel("dice");
        else if (what == "dice:roll")
        {
            Hud.OpenPanel("dice");
            var buttons = Hud.Dice.Panel.FindChildren("*", "Button", true, false).OfType<Button>().ToList();
            foreach (var text in new[] { "d6", "d6", "d6", "d10", "Roll" }) buttons.FirstOrDefault(b => b.Text == text)?.EmitSignal(BaseButton.SignalName.Pressed);
        }
        else if (what == "menu" && !T.SelectedNodes.Any() && S.View.Layer(KTable.Core.Model.Layers.Umpire).FirstOrDefault() is { } first)
        {
            T.SetSelection(new List<string> { first.Id });
            Hud.Menus.Show(centre, T.SelectedNodes.FirstOrDefault());
        }
        else Hud.Menus.Show(centre, what == "menu" ? T.SelectedNodes.FirstOrDefault() : null);
    }

    async void SavesDemo(bool stopAtDialog, bool stopAtTrash)
    {
        async System.Threading.Tasks.Task Wait(double s) => await Hud.ToSignal(Hud.GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
        if (N.Saves.RunInPlay is not var (dir, run)) { GD.Print("[saves] this table is not one of a scenario's games"); return; }
        var store = KTable.Core.SaveStore.Default;
        for (int i = 0; i < 2; i++) { S.Do(new AdvanceClock()); await Wait(3); }
        var root = store.SavesRoot(dir);
        var group = store.MakeFolder(dir, root, "Second group");
        string? saved = null;
        N.Saves.SaveAs(root, "Before the attack", true, (p, why) => saved = p ?? why);
        await Wait(2);
        GD.Print($"[saves] run {run} in {store.RunFolderOf(dir, run)}; saved {saved}; folder {group}");
        foreach (var f in System.IO.Directory.GetFiles(root, "*" + KTable.Core.SaveStore.Ext, System.IO.SearchOption.AllDirectories)) GD.Print("[saves]   " + System.IO.Path.GetRelativePath(root, f));
        SavesWindow.Open(Ctx);
        await Wait(1);
        var w = Hud.GetTree().Root.FindChildren("*", "Window", true, false).OfType<SavesWindow>().FirstOrDefault();
        if (w == null) { GD.Print("[saves] the window did not open"); return; }
        GD.Print("[saves] showing " + System.IO.Path.GetFileName(w.ShownFolder));
        if (w.TreeRowOf("Second group") is not { } row) { GD.Print("[saves] no Second group row"); return; }
        var at = row.GetCenter();
        foreach (var down in new[] { true, false })
            w.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at, ButtonMask = down ? MouseButtonMask.Left : 0 });
        await Wait(1);
        GD.Print("[saves] after a click on Second group, showing " + System.IO.Path.GetFileName(w.ShownFolder));
        async System.Threading.Tasks.Task Click(Control c)
        {
            var at = (Vector2)c.GetWindow().Position + c.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            await Wait(0.1);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
            await Wait(0.1);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        }
        Button ButtonOf(Node under, string text) => under.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == text);
        await Click(ButtonOf(w, "New folder"));
        await Wait(1);
        foreach (var ch in "Group B") { Input.ParseInputEvent(new InputEventKey { Pressed = true, Unicode = ch }); Input.ParseInputEvent(new InputEventKey { Pressed = false, Unicode = ch }); await Wait(0.05); }
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = Key.Enter, PhysicalKeycode = Key.Enter });
        Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = Key.Enter, PhysicalKeycode = Key.Enter });
        await Wait(1.5);
        GD.Print("[saves] folders in Second group: " + string.Join(", ", System.IO.Directory.GetDirectories(group).Select(System.IO.Path.GetFileName)));
        await Click(ButtonOf(w, "New play…"));
        await Wait(1);
        if (w.FindChildren("*", "ConfirmationDialog", true, false).OfType<ConfirmationDialog>().LastOrDefault() is { } dlg)
        {
            GD.Print("[saves] new play offers: " + string.Join(" | ", dlg.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>().SelectMany(o => Enumerable.Range(0, o.ItemCount).Select(o.GetItemText))));
            if (stopAtDialog) return;
            await Click(dlg.GetOkButton());
        }
        await Wait(2);
        GD.Print("[saves] plays: " + string.Join(", ", store.Runs(dir).Select(r => System.IO.Path.GetRelativePath(root, r.Folder) + (System.IO.File.Exists(store.LivePath(dir, r.Id)) ? " (latest)" : ""))) + "; showing " + System.IO.Path.GetFileName(w.ShownFolder));
        if (saved == null || !System.IO.File.Exists(saved)) return;
        store.TrashSaves(dir, saved, System.DateTime.UtcNow);
        w.QueueFree();
        await Wait(0.5);
        SavesWindow.Open(Ctx);
        await Wait(1);
        w = Hud.GetTree().Root.FindChildren("*", "Window", true, false).OfType<SavesWindow>().First();
        if (w.TreeRowOf("Trash · 1") is not { } trashRow) { GD.Print("[saves] no Trash row"); return; }
        await ClickAt(w, trashRow.GetCenter());
        await Wait(1);
        var tile = w.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text == "Before the attack")?.GetParent()?.GetParent() as Control;
        GD.Print("[saves] in the trash: " + (tile != null ? "Before the attack" : "nothing"));
        if (tile == null || stopAtTrash) return;
        await ClickAt(w, tile.GetGlobalRect().GetCenter());
        await Wait(0.5);
        await Click(ButtonOf(w, "Restore"));
        await Wait(1.5);
        GD.Print("[saves] restored: " + System.IO.File.Exists(saved));
    }

    async System.Threading.Tasks.Task ClickAt(Window w, Vector2 local, bool ctrl = false)
    {
        var at = (Vector2)w.Position + local;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        await Hud.ToSignal(Hud.GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left, CtrlPressed = ctrl });
        await Hud.ToSignal(Hud.GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at, CtrlPressed = ctrl });
    }

    async System.Threading.Tasks.Task Press(Key key, bool ctrl = false)
    {
        Input.ParseInputEvent(new InputEventKey { Pressed = true, Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl });
        Input.ParseInputEvent(new InputEventKey { Pressed = false, Keycode = key, PhysicalKeycode = key, CtrlPressed = ctrl });
        await Hud.ToSignal(Hud.GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
    }

    async void SavesManyDemo()
    {
        async System.Threading.Tasks.Task Wait(double s) => await Hud.ToSignal(Hud.GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
        if (N.Saves.RunInPlay is not var (dir, run)) return;
        var store = KTable.Core.SaveStore.Default;
        var folder = store.EnsureRunFolder(dir, run, System.DateTime.UtcNow);
        foreach (var name in new[] { "Pick A", "Pick B", "Keep C" }) { N.Saves.SaveAs(folder, name, true, (_, _) => { }); await Wait(1); }
        SavesWindow.Open(Ctx);
        await Wait(1.5);
        var w = Hud.GetTree().Root.FindChildren("*", "Window", true, false).OfType<SavesWindow>().First();
        Control? TileOf(string name) => w.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text == name)?.GetParent()?.GetParent() as Control;
        await ClickAt(w, TileOf("Pick A")!.GetGlobalRect().GetCenter());
        await ClickAt(w, TileOf("Pick B")!.GetGlobalRect().GetCenter(), ctrl: true);
        await Press(Godot.Key.Delete);
        if (w.FindChildren("*", "ConfirmationDialog", true, false).OfType<ConfirmationDialog>().LastOrDefault() is { } dlg)
        {
            GD.Print("[many] asked: " + dlg.Title);
            await ClickAt(w, dlg.GetOkButton().GetGlobalRect().GetCenter() + (Vector2)dlg.Position - (Vector2)w.Position);
        }
        await Wait(1.5);
        GD.Print("[many] left in the play: " + string.Join(", ", System.IO.Directory.GetFiles(folder, "*.kstable").Select(System.IO.Path.GetFileNameWithoutExtension)));
        GD.Print("[many] in the trash: " + store.SavesTrash(dir).Count);
        if (w.TreeRowOf("Trash · 2") is not { } trashRow) { GD.Print("[many] no Trash · 2 row"); return; }
        await ClickAt(w, trashRow.GetCenter());
        await Wait(1);
        await Press(Godot.Key.A, ctrl: true);
        var restore = w.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text.StartsWith("Restore"));
        GD.Print("[many] restore button: " + restore.Text);
        await ClickAt(w, restore.GetGlobalRect().GetCenter());
        await Wait(1.5);
        GD.Print("[many] back in the play: " + string.Join(", ", System.IO.Directory.GetFiles(folder, "*.kstable").Select(System.IO.Path.GetFileNameWithoutExtension).Order()));
    }

    bool Pages(string what)
    {
        if (what is "pieces" or "map" or "layers" or "board") { Hud.Drawer.ShowPage(what); return true; }
        if (what == "resetpanels") { Hud.PutPanelsBack(); return true; }
        if (what == "pieceindex") { Hud.Drawer.ShowPage("pieces"); Hud.Pieces.ShowIndex(); return true; }
        if (what == "pen") { T.SetTool(Tool.Draw); return true; }
        if (what == "agentpill" && AgentEndpoint.Current is { } agent)
        {
            agent.Log("made the kind Fusiliers (2nd Brigade)");
            agent.Log("placed 6 Fusiliers in line at 120, 340");
            agent.Log("drew an arrow on the map");
            Hud.GetTree().CreateTimer(0.5).Timeout += () =>
                Hud.GetTree().Root.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault(l => l.Text == "Agent")?.GetParent()?.GetParent<Button>()?.EmitSignal(BaseButton.SignalName.Pressed);
            return true;
        }
        if (what == "newplay")
        {
            SavesWindow.Open(Ctx);
            Hud.GetTree().CreateTimer(1).Timeout += () =>
            {
                var w = Hud.GetTree().Root.FindChildren("*", "Window", true, false).OfType<SavesWindow>().FirstOrDefault();
                w?.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "New play…")?.EmitSignal(BaseButton.SignalName.Pressed);
            };
            return true;
        }
        if (what == "mapsheet")
        {
            Hud.Drawer.ShowPage("map");
            if (S.View.Sheets.FirstOrDefault() is { } first) { T.SelectSheet(first.Id); Hud.Map.Rebuild(); }
            Hud.GetTree().CreateTimer(1).Timeout += () => GD.Print($"[drawer] set {Cfg.LeftWidth:0}, drawn {Hud.Drawer.Panel.Size.X:0}; least width of each page: "
                + string.Join(", ", Hud.Drawer.Pages.Where(p => p.Value.page != null).Select(p => $"{p.Key} {p.Value.page!.GetCombinedMinimumSize().X:0}")));
            return true;
        }
        if (what == "sheet") { Hud.Drawer.ShowPage("map"); if (S.View.Sheets.Count > 0) { T.SelectSheet(S.View.Sheets[0].Id); Hud.Map.Rebuild(); } return true; }
        if (what == "sheetresize")
        {
            Hud.Drawer.ShowPage("map");
            if (S.View.Sheets.Count > 0) { var sh = S.View.Sheets[0]; S.Do(new MoveSheet { Id = sh.Id, Pos = sh.Pos, RotDeg = sh.RotDeg, Z = sh.Z, Size = new Size2(sh.Size.W * 1.5, sh.Size.H * 1.5) }); T.SelectSheet(sh.Id); Hud.Map.Rebuild(); }
            return true;
        }
        if (what == "sheetring")
        {
            Hud.Drawer.ShowPage("map");
            if (S.View.Sheets.Count > 0) { var id = S.View.Sheets[0].Id; S.Do(new LockSheet { Id = id, Locked = false }); T.SelectSheet(id); Hud.Map.Rebuild(); T.SetTool(Tool.Transform); }
            return true;
        }
        if (what is "crop" or "cropapply")
        {
            Hud.Drawer.ShowPage("map");
            if (S.View.Sheets.Count > 0)
            {
                var id = S.View.Sheets[0].Id; T.SelectSheet(id); T.BeginCrop(id); T.SetCropRect(new FaceBox { X = 0.08, Y = 0.1, W = 0.78, H = 0.72 });
                if (what == "cropapply") T.ApplyCrop();
            }
            return true;
        }
        return false;
    }

    bool CompassDrag(string what)
    {
        if (what is "compassdrag" or "compassdrag:stay")
        {
            NorthCompass.InDemo = true;
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.4), Timer.SignalName.Timeout);
                var g = Hud.Compass.GetGlobalRect();
                var from = g.GetCenter() + new Vector2(0, 18);
                var to = new Vector2(Hud.GetViewport().GetVisibleRect().Size.X - Hud.Gap - g.Size.X / 2 - 6, 420);
                GD.Print($"[compass] at {Hud.Compass.GetGlobalRect().Position.X:0},{Hud.Compass.GetGlobalRect().Position.Y:0}; tab grabbed at {from.X:0},{from.Y:0}");
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
                for (int i = 1; i <= 10; i++)
                {
                    var at = from.Lerp(to, i / 10f);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (to - from) / 10, ButtonMask = MouseButtonMask.Left });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.2), Timer.SignalName.Timeout);
                var r = Hud.Compass.GetGlobalRect();
                GD.Print($"[compass] dropped at {r.Position.X:0},{r.Position.Y:0} (right edge {r.End.X:0} of {Hud.GetViewport().GetVisibleRect().Size.X:0}); kept {(Cfg.CompassPos is { } cp ? $"{cp[0]:0},{cp[1]:0}" : "none")}");
                if (what.EndsWith(":stay")) return;
                var tab = r.GetCenter();
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = tab, GlobalPosition = tab });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false, Position = tab, GlobalPosition = tab });
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.2), Timer.SignalName.Timeout);
                GD.Print($"[compass] after a right-click: {Hud.Compass.GetGlobalRect().Position.X:0},{Hud.Compass.GetGlobalRect().Position.Y:0}; kept {(Cfg.CompassPos == null ? "none" : "a place")}");
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool Hearts(string what)
    {
        if (what.StartsWith("hearts:"))
        {
            var parts = what[7..].Split(':');
            var names = parts[0].Split(';', StringSplitOptions.RemoveEmptyEntries);
            Cfg.Favourites = S.Store.ListPackIds().SelectMany(id => S.Store.Pack(id)?.Pieces ?? new()).Where(d => names.Contains(d.Name)).Select(d => d.Id).Distinct().ToList();
            Cfg.FavouritesOnly = parts.Length > 1 && parts[1] == "only";
            Hud.Pieces.Hearts.SetPressedNoSignal(Cfg.FavouritesOnly);
            Hud.Drawer.ShowPage("pieces");
            Hud.Pieces.RebuildPacks();
            Hud.Pieces.PackPick.Selected = 0;
            Hud.Pieces.RebuildList();
            GD.Print($"[hearts] {Cfg.Favourites.Count} hearted; filter {(Cfg.FavouritesOnly ? "on" : "off")}; the list shows {Hud.Pieces.List.ItemCount}: {string.Join(", ", Enumerable.Range(0, Math.Min(5, Hud.Pieces.List.ItemCount)).Select(i => Hud.Pieces.List.GetItemText(i).Split('\n')[0]))}");
            return true;
        }
        return false;
    }

    static void Complete(ScriptWindow w, string typed)
    {
        var code = w.Code;
        code.GrabFocus();
        code.SetCaretLine(code.GetLineCount() - 1);
        code.SetCaretColumn(code.GetLine(code.GetLineCount() - 1).Length);
        code.InsertTextAtCaret("\n" + typed);
        code.RequestCodeCompletion(true);
        Callable.From(() =>
        {
            var offered = code.GetCodeCompletionOptions().Select(o => o["display_text"].AsString()).Take(12);
            GD.Print($"[scripts] after '{typed}' completion offers: {string.Join(", ", offered)}");
            var e = ScriptAssist.EntryAt(code, code.GetCaretLine(), Math.Max(0, code.GetCaretColumn() - 1));
            GD.Print($"[scripts] the word at the caret: {(e == null ? "none" : ScriptAssist.Describe(e).Replace("\n", " / "))}");
        }).CallDeferred();
    }

    bool Scripts(string what)
    {
        if (what.StartsWith("scripts"))
        {
            var parts = what.Split(':');
            var board = S.View.Layer(Layers.Umpire);
            string? at = parts.Length > 1 && int.TryParse(parts[1], out var n) && n >= 1 && n <= board.Count ? board[n - 1].Id : null;
            Hud.ShowScriptWindow(at ?? "");
            Hud.GetTree().CreateTimer(1.0).Timeout += () =>
            {
                var w = Hud.Root.GetChildren().OfType<ScriptWindow>().FirstOrDefault();
                GD.Print($"[scripts] window open: {w != null}; the host says {S.Scripts.Count} script(s): {string.Join(", ", S.Scripts.Select(i => $"{(i.Id.Length == 0 ? "global" : i.Id)}={i.Status}"))}");
                if (S.IsLead) S.Request(new RunLua { Target = at ?? "", Code = at != null ? "self.pos" : "game.turn" });
                if (w != null && parts.Contains("complete")) Complete(w, "self.se");
            };
            Hud.GetTree().CreateTimer(2.0).Timeout += () => GD.Print("[scripts] console: " + string.Join(" | ", S.ConsoleLines.TakeLast(6).Select(l => $"{l.Kind} {l.Source}: {l.Text}")));
            return true;
        }
        if (what is "briefing" or "briefingpage")
        {
            const string Sample = "# 18:00, 18 June 1815\nThe Prussian IV Corps holds **Plancenoit**; the Young Guard is counter-attacking.\n\n## French\n- The Middle Guard forms in the valley below La Haye Sainte.\n\n## Allied\n- Wellington's centre is thin behind La Haye Sainte.";
            if (S.IsLead && S.View.Briefing == null) S.Do(new KTable.Core.Protocol.SetBriefing { Text = Sample });
            if (what == "briefing") BriefingWindow.Open(Hud.Root, S);
            else { DialogPages.LastPage["scenario"] = "Briefing"; ScenarioDialog.Open(Hud.Ctx); }
            return true;
        }
        if (what == "console")
        {
            Hud.OpenPanel("console");
            if (S.IsLead)
            {
                S.Request(new RunLua { Code = "print('hello from the console', game.turn)" });
                S.Request(new RunLua { Code = "board.pieces()[1].pos" });
                S.Request(new RunLua { Code = "nothing.here()" });
            }
            Hud.GetTree().CreateTimer(1.0).Timeout += () => GD.Print("[console] " + string.Join(" | ", S.ConsoleLines.TakeLast(8).Select(l => $"{l.Kind} {l.Source}: {l.Text}")));
            return true;
        }
        if (what == "check")
        {
            MainMenu.CheckScenarioScripts(Ctx);
            Hud.GetTree().CreateTimer(12).Timeout += () =>
            {
                var w = Hud.Root.GetChildren().OfType<Window>().LastOrDefault(x => x.Title == "Check this scenario's scripts");
                var text = w?.FindChildren("*", "RichTextLabel", true, false).OfType<RichTextLabel>().FirstOrDefault()?.GetParsedText() ?? "(no window)";
                var state = w?.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text).FirstOrDefault(t => t.StartsWith("Done") || t.StartsWith("Running") || t.StartsWith("Finished") || t.StartsWith("Stopped"));
                GD.Print($"[check] {state}: {text.Replace("\n", " | ")}");
            };
            return true;
        }
        if (what.StartsWith("reference"))
        {
            var name = what.Contains(':') ? what[(what.IndexOf(':') + 1)..] : "self.set_state";
            GuideReader.ShowEntry(Hud.Root, KTable.Server.Scripting.ApiDoc.All.FirstOrDefault(e => e.Qualified == name));
            return true;
        }
        return false;
    }

    bool View(string what)
    {
        if (what == "cam")
        {
            var at = KTable.App.Coords.FromWorld(T.Rig.Target);
            GD.Print($"[cam] the view is on {at.X:0.0},{at.Z:0.0} at {T.Rig.Distance:0.0} out, {(T.Rig.TopDown ? "flat" : "tilted")}");
            return true;
        }
        if (what == "view")
        {
            void Say(string step) => GD.Print($"[view] {step}: the view is {(T.Rig.TopDown ? "flat" : "three-dimensional")} and the button is {(Hud.Strip.Btn2d!.ButtonPressed ? "lit" : "dark")}");
            Say("to begin with");
            Hud.Strip.Btn2d!.EmitSignal(Button.SignalName.Pressed);
            Say("after pressing it");
            Hud.Strip.Btn2d!.EmitSignal(Button.SignalName.Pressed);
            Say("after pressing it again");
            T.Rig.ToggleTopDown();
            Say("after something else flattened the view");
            return true;
        }
        if (what == "bar" || what.StartsWith("bar:"))
        {
            string arg = what.Contains(':') ? what.Split(':')[1] : "";
            if (arg == "off") Cfg.TopLine = false;
            else if (arg.StartsWith("strip")) { Cfg.StripSize = float.Parse(arg[5..], System.Globalization.CultureInfo.InvariantCulture); Hud.Strip.Rebuild(); }
            else if (arg.Length > 0) Cfg.TopLineWidth = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) / 100f;
            Callable.From((Action)(async () =>
            {
                Hud.Relayout();
                Hud.Top.SyncTopLine();
                Say("A roll of 6 and 3 · 9");
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.3), Timer.SignalName.Timeout);
                var mb = Hud.Top.MenuButton!.GetGlobalRect();
                var sp = Hud.Strip.Panel.GetGlobalRect();
                GD.Print($"[bar] menu {mb.Position.X:0}..{mb.End.X:0} (w {mb.Size.X:0}), min {Hud.Top.MenuButton.GetCombinedMinimumSize().X:0}");
                GD.Print($"[bar] sidebar {sp.Position.X:0}..{sp.End.X:0} (w {sp.Size.X:0}), strip size {Cfg.StripSize}");
                foreach (var (k, b) in Hud.Strip.Buttons)
                {
                    var r = b.GetGlobalRect();
                    GD.Print($"[bar] first button ({k}) {r.Position.X:0}..{r.End.X:0} (w {r.Size.X:0})");
                    break;
                }
                var sd = Hud.Top.Said!.GetGlobalRect();
                var st = Hud.Top.People!.Button.GetGlobalRect();
                GD.Print($"[bar] the top line is {(Cfg.TopLine ? "on" : "off")} at {Cfg.TopLineWidth * 100:0}%: {sd.Position.X:0}..{sd.End.X:0} (w {sd.Size.X:0}), free room ends at {st.Position.X:0}");
                GD.Print($"[bar] it says: \"{Hud.Top.Said.Text}\" · the status reads \"{Hud.Top.People!.StatusText}\" ({st.Position.X:0}..{st.End.X:0})");
            })).CallDeferred();
            return true;
        }
        if (what.StartsWith("guides"))
        {
            GuideReader.Spec(Hud.Root, what.Contains(':') ? what[(what.IndexOf(':') + 1)..] : "");
            foreach (var d in GuideLibrary.Docs) GD.Print($"[guides] {d.File}: {GuideLibrary.Title(d.File)} · on disk: {GuideLibrary.OnDisk(d.File) ?? "no"}");
            return true;
        }
        if (what == "esc")
        {
            Callable.From((Action)(async () =>
            {
                var board = S.View.Layer(KTable.Core.Model.Layers.Umpire);
                if (board.Count > 0) T.SetSelection(new[] { board[0].Id });
                T.SetTool(Tool.Range);
                void Say(string step) => GD.Print($"[esc] {step}: the tool is {T.Tool}, {T.Selected.Count} piece(s) in hand, the menu is {(Hud.Top.MenuButton!.GetPopup().Visible ? "open" : "shut")}");
                async System.Threading.Tasks.Task Press()
                {
                    Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    await Hud.ToSignal(Hud.GetTree().CreateTimer(0.25), Timer.SignalName.Timeout);
                }
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.3), Timer.SignalName.Timeout);
                Say("with the ranging tool out and a piece picked");
                await Press(); Say("after one press");
                await Press(); Say("after a second press");
                await Hud.ToSignal(Hud.GetTree().CreateTimer(1.5), Timer.SignalName.Timeout);
                await Press(); Say("after a third press");
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool PovPictures(string what)
    {
        if (what.StartsWith("povshot"))
        {
            var pb = S.View.Layer(KTable.Core.Model.Layers.Umpire);
            if (pb.Count == 0) { GD.Print("[povshot] no pieces"); return true; }
            T.SetSelection(new[] { pb[0].Id });
            T.SetTool(Tool.Sighting);
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.8), Timer.SignalName.Timeout);
                if (T.Draft is not { } d) { GD.Print("[povshot] no draft"); return; }
                var vp = Hud.GetViewport().GetVisibleRect().Size;
                bool down = what.Contains("v");
                var a = down ? new Vector2(vp.X * 0.58f, vp.Y * 0.18f) : new Vector2(vp.X * 0.25f, vp.Y * 0.42f);
                var b = down ? new Vector2(vp.X * 0.58f, vp.Y * 0.82f) : new Vector2(vp.X * 0.88f, vp.Y * 0.42f);
                if (what.Contains("edge") && pb.Count > 1 && Hud.GetViewport().GetCamera3D() is { } cam)
                {
                    var at = cam.UnprojectPosition(Coords.ToWorld(pb[^1].Pos));
                    (a, b) = (new Vector2(at.X, vp.Y * 0.1f), new Vector2(at.X, vp.Y * 0.9f));
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a });
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                for (int i = 1; i <= 10; i++)
                {
                    var at = a.Lerp(b, i / 10f);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (b - a) / 10f });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.6), Timer.SignalName.Timeout);
                if (what.EndsWith(":all")) d.Included = S.View.Layer(KTable.Core.Model.Layers.Umpire).Select(x => x.Id).ToHashSet();
                GD.Print($"[povshot] {d.Walls.Count} wall(s), the lit ground {d.Polygon.Count} points, {d.Included.Count} pieces in the picture of {S.View.Layer(KTable.Core.Model.Layers.Umpire).Count}");
                var img = await T.RenderSighting(d, 900);
                if (img == null) { GD.Print("[povshot] no picture"); return; }
                var path = ProjectSettings.GlobalizePath("user://povshot.png");
                GD.Print($"[povshot] {img.GetWidth()}×{img.GetHeight()} written to {path}: {img.SavePng(path)}");
            })).CallDeferred();
            return true;
        }
        if (what.StartsWith("povclip"))
        {
            bool square = what.EndsWith(":square");
            var board = S.View.Layer(KTable.Core.Model.Layers.Umpire);
            if (board.Count == 0) { GD.Print("[povclip] no pieces"); return true; }
            T.SetSelection(new[] { board[0].Id });
            T.SetTool(Tool.Sighting);
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.8), Timer.SignalName.Timeout);
                if (T.Draft is not { } d) { GD.Print("[povclip] no draft"); return; }
                if (square) { d.Square = true; d.Recompute(T); }
                var mid = T.Rig.Cam.UnprojectPosition(KTable.App.Coords.ToWorld(d.Center, 0));
                var edge = T.Rig.Cam.UnprojectPosition(KTable.App.Coords.ToWorld(new KTable.Core.Model.Vec2(d.Center.X + d.Radius, d.Center.Z), 0));
                float px = Math.Max(20f, Math.Abs(edge.X - mid.X));
                var a = mid + new Vector2(-px * 2.2f, -px * 0.35f);
                var b = mid + new Vector2(px * 2.2f, px * 0.35f);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a });
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                for (int i = 1; i <= 24; i++)
                {
                    var at = a.Lerp(b, i / 24f);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (b - a) / 24f });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.6), Timer.SignalName.Timeout);
                double worst = 0; int pts = 0;
                foreach (var w in d.Walls)
                    foreach (var q in w)
                    {
                        pts++;
                        double far = square
                            ? Math.Max(Math.Abs(q.X - d.Center.X), Math.Abs(q.Z - d.Center.Z)) / d.Radius
                            : (q - d.Center).Length / d.Radius;
                        worst = Math.Max(worst, far);
                    }
                GD.Print($"[povclip] {(square ? "square" : "round")} picture of {d.Radius:0.0} u · the hand went {px * 2.2f / Math.Max(1f, px) * 100:0}% of the reach past the middle on each side");
                GD.Print($"[povclip] {d.Walls.Count} wall(s), {pts} point(s); the farthest is {worst * 100:0.0}% of the way out");
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool PovTool(string what)
    {
        if (what == "pov" || what.StartsWith("pov:"))
        {
            int n = what.Contains(':') ? int.Parse(what.Split(':')[1]) : 0;
            var board = S.View.Layer(KTable.Core.Model.Layers.Umpire);
            if (board.Count <= n) { GD.Print("[pov] no such piece"); return true; }
            T.SetSelection(new[] { board[n].Id });
            T.SetTool(Tool.Sighting);
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.6), Timer.SignalName.Timeout);
                var p = Hud.Panels["sighting"];
                var cam = KTable.App.Coords.FromWorld(T.Rig.Target);
                var at = board[n].Pos;
                GD.Print($"[pov] piece at {at.X:0.0},{at.Z:0.0}; the view is on {cam.X:0.0},{cam.Z:0.0}");
                GD.Print($"[pov] panel at {p.Position.X:0},{p.Position.Y:0} size {p.Size.X:0}×{p.Size.Y:0}; the top bar ends at {Hud.Top.Panel.Size.Y:0}, the window is {Hud.GetViewport().GetVisibleRect().Size.Y:0} tall");
                GD.Print($"[pov] in the picture: {T.Draft?.Included.Count}; wearing the selection ring: {T.RingedCount()} of {board.Count}");
                if (board.Count > n + 1)
                {
                    T.SetSelection(new[] { board[n + 1].Id });
                    await Hud.ToSignal(Hud.GetTree().CreateTimer(0.5), Timer.SignalName.Timeout);
                    var c2 = KTable.App.Coords.FromWorld(T.Rig.Target);
                    GD.Print($"[pov] picked the next piece: the sighting is now from {T.Draft?.ObserverId}, the view on {c2.X:0.0},{c2.Z:0.0} (that piece is at {board[n + 1].Pos.X:0.0},{board[n + 1].Pos.Z:0.0})");
                }
                T.CancelSighting();
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.4), Timer.SignalName.Timeout);
                GD.Print($"[pov] the shot cancelled: {T.RingedCount()} of {board.Count} wearing the ring, {T.Selected.Count} selected");
            })).CallDeferred();
            return true;
        }
        if (what.StartsWith("povwall"))
        {
            int pn = what.Contains(':') ? int.Parse(what.Split(':')[1]) : 0;
            var board = S.View.Layer(KTable.Core.Model.Layers.Umpire);
            if (board.Count <= pn) { GD.Print("[povwall] no such piece"); return true; }
            T.SetSelection(new[] { board[pn].Id });
            T.SetTool(Tool.Sighting);
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.5), Timer.SignalName.Timeout);
                if (T.Draft == null) { GD.Print("[povwall] no draft"); return; }
                int before = T.Draft.Included.Count;
                var vp = Hud.GetViewport().GetVisibleRect().Size;
                var a = new Vector2(vp.X * 0.52f, vp.Y * 0.40f);
                var b = new Vector2(vp.X * 0.80f, vp.Y * 0.40f);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a });
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                int steps = what.Contains("long") ? 90 : 6;
                for (int i = 1; i <= steps; i++)
                {
                    var at = a.Lerp(b, i / (float)steps) + new Vector2(0, what.Contains("wobble") ? (i % 2 == 0 ? 3 : -3) : 0);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (b - a) / steps });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.6), Timer.SignalName.Timeout);
                var d = T.Draft!;
                GD.Print($"[povwall] {d.Walls.Count} wall(s) traced, {(d.Walls.Count > 0 ? d.Walls[0].Count : 0)} points; the lit ground is {d.Polygon.Count} points");
                GD.Print($"[povwall] in the picture: {before} before the wall, {d.Included.Count} after");
                var radii = d.Polygon.Select(q => (q - d.Center).Length).ToList();
                int full = radii.Count(x => x > d.Radius - 0.01);
                GD.Print($"[povwall] of {radii.Count} rays, {full} reach the radius; the shortest is {radii.Min():0.00} of {d.Radius:0.00}; the shadow is {(radii.Count - full) * 360.0 / radii.Count:0} degrees of the circle");
                var p2 = Hud.Panels["sighting"];
                GD.Print($"[povwall] the panel is {p2.Size.X:0}×{p2.Size.Y:0} at {p2.Position.X:0},{p2.Position.Y:0}");
                GD.Print($"[povwall] the view is {(T.Rig.TopDown ? "flat" : "three-dimensional")} and the 3D button is {(Hud.Strip.Btn2d!.ButtonPressed ? "lit" : "dark")}");
                var home = KTable.App.Coords.FromWorld(T.Rig.Target);
                T.Rig.Centre(new Vector3((float)home.X + 60f, 0, (float)home.Z + 60f));
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                var back = KTable.App.Coords.FromWorld(T.Rig.Target);
                GD.Print($"[povwall] the view was shoved 60 units away; a frame later it is on {back.X:0.0},{back.Z:0.0} (the piece is at {board[pn].Pos.X:0.0},{board[pn].Pos.Z:0.0})");
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool PovFormat(string what)
    {
        if (what.StartsWith("povfmt"))
        {
            int pn = what.Contains(':') ? int.Parse(what.Split(':')[1]) : 0;
            var board = S.View.Layer(KTable.Core.Model.Layers.Umpire);
            if (board.Count <= pn) { GD.Print("[povfmt] no such piece"); return true; }
            T.SetSelection(new[] { board[pn].Id });
            T.SetTool(Tool.Sighting);
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.4), Timer.SignalName.Timeout);
                var d = T.Draft;
                if (d == null) { GD.Print("[povfmt] no draft"); return; }
                foreach (var square in new[] { false, true })
                {
                    d.Square = square;
                    d.Recompute(T); T.ShowDraft();
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    var sh = d.Shape();
                    var corner = new KTable.Core.Model.Vec2(d.Center.X + d.Radius * 0.98, d.Center.Z + d.Radius * 0.98);
                    GD.Print($"[povfmt] {(square ? "square" : "round")}: the shape is a {sh.Kind} of {sh.Polygon.Count} points, radius {sh.Radius:0.##}; {d.Included.Count} pieces in it; the far corner of the box is {(sh.Contains(corner) ? "in" : "out")}");
                }
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool DrawingPanels(string what)
    {
        if (what == "layerlast")
        {
            var b0 = S.View.Boards[0].Id;
            S.Do(new KTable.Core.Protocol.RenameBoard { Id = b0, Name = "Plan" });
            S.Do(new KTable.Core.Protocol.Draw { Layer = KTable.Core.Model.Layers.Umpire, Stroke = new KTable.Core.Model.Stroke { Points = { new(0, 0), new(6, 6) }, Color = "#101010", Width = 0.4 } });
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.4), Timer.SignalName.Timeout);
                void Say(string step) => GD.Print($"[layer] {step}: {S.View.Boards.Count} layer(s), the first is \"{S.View.Boards[0].Name}\" ({S.View.Boards[0].Id}), {S.View.Drawings.Values.Sum(l => l.Count)} drawing(s) on the table, the pen writes on {T.DrawBoard()}");
                Say("a line drawn on the only layer, renamed");
                S.Do(new KTable.Core.Protocol.RemoveBoard { Id = b0 });
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.4), Timer.SignalName.Timeout);
                Say("after pressing its ✕");
                Hud.Boards.Rebuild();
            })).CallDeferred();
            return true;
        }
        if (what == "snap")
        {
            Hud.Drawer.ShowPage(null);
            Hud.OpenPanel("paint");
            Hud.OpenPanel("boards");
            Callable.From((Action)(async () =>
            {
                var a = Hud.Panels["paint"]; var b = Hud.Panels["boards"];
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                float sidebar = Hud.Gap + Cfg.StripSize + 10 + Hud.Gap;
                float top = (Hud.Top.Panel?.Size.Y ?? 28) + Hud.Gap;
                async System.Threading.Tasks.Task DragTo(FloatPanel p, Vector2 want)
                {
                    var from = p.GlobalPosition + new Vector2(20, 8);
                    var to = from + (want - p.Position);
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                await DragTo(a, new Vector2(sidebar + 9, top + 46));
                GD.Print($"[snap] the sidebar line is x={sidebar:0}; drawing panel let go at x={sidebar + 9:0} sits at {a.Position.X:0},{a.Position.Y:0} ({a.Size.X:0}×{a.Size.Y:0})");
                await DragTo(b, new Vector2(a.Position.X + 11, a.Position.Y + a.Size.Y + 10));
                GD.Print($"[snap] the drawing panel ends at y={a.Position.Y + a.Size.Y:0}; layers let go 10 px under it sits at {b.Position.X:0},{b.Position.Y:0}");
                await DragTo(b, new Vector2(b.Position.X + 40, b.Position.Y + 40));
                GD.Print($"[snap] dragged 40 px off: {b.Position.X:0},{b.Position.Y:0}");
            })).CallDeferred();
            return true;
        }
        if (what.StartsWith("penlock"))
        {
            Hud.OpenPanel("paint");
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.4), Timer.SignalName.Timeout);
                bool Badge() => Hud.Strip.Buttons.TryGetValue("paint", out var b)
                    && b.GetChildren().OfType<ToolIcon>().Any(i => i.Locked);
                void Say(string step) => GD.Print($"[penlock] {step}: the tool is {T.Tool}, the pen is {(T.DrawingLocked ? "locked" : "free")}, the button {(Badge() ? "wears the padlock" : "does not")}");
                T.SetTool(Tool.Draw);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Say("pen in hand, the layer shown");
                T.HiddenBoards.Add(T.DrawBoard());
                T.RefreshDrawings();
                Hud.Paint.Rebuild(); Hud.Strip.SyncPenLock();
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Say("the layer hidden");
                T.SetTool(Tool.Draw);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Say("after trying to take the pen again");
                if (what.EndsWith(":stay")) return;
                T.HiddenBoards.Clear();
                T.RefreshDrawings();
                Hud.Paint.Rebuild(); Hud.Strip.SyncPenLock();
                T.SetTool(Tool.Draw);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Say("the layer shown again");
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool PanelMoves(string what)
    {
        if (what.StartsWith("panelmem:"))
        {
            var key = what[9..];
            Hud.OpenPanel(key);
            Callable.From((Action)(async () =>
            {
                var p = Hud.Panels[key];
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                var from = p.GlobalPosition + new Vector2(20, 8);
                var to = from + new Vector2(300, 150);
                void Btn(Vector2 at, bool down) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
                void Move(Vector2 a, Vector2 b) => Input.ParseInputEvent(new InputEventMouseMotion { Position = b, GlobalPosition = b, Relative = b - a });
                Btn(from, true);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Move(from, to);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Btn(to + new Vector2(400, 260), false);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"[panelmem] {key} dragged to {p.Position.X:0},{p.Position.Y:0}; settings {(Cfg.PanelPos.TryGetValue(key, out var a1) ? $"{a1[0]:0},{a1[1]:0}" : "nothing")}");
                Hud.ClosePanel(key);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Hud.OpenPanel(key);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"[panelmem] closed and opened again at {p.Position.X:0},{p.Position.Y:0}, size {p.Size.X:0}×{p.Size.Y:0}");
            })).CallDeferred();
            return true;
        }
        if (what.StartsWith("panel:"))
        {
            var key = what[6..];
            Hud.OpenPanel(key);
            Callable.From((Action)(async () =>
            {
                var p = Hud.Panels[key];
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                var from = p.GlobalPosition + new Vector2(20, 8);
                var to = from + new Vector2(260, 180);
                GD.Print($"[panel] {key} opened at {p.Position.X:0},{p.Position.Y:0} size {p.Size.X:0}×{p.Size.Y:0}");
                if (key == "pieces")
                {
                    GD.Print($"[panel] the list asks for {Hud.Pieces.List.GetCombinedMinimumSize().Y:0}, the body for {p.Body.GetCombinedMinimumSize().Y:0}, the panel's own floor is {p.CustomMinimumSize.Y:0}");
                    GD.Print($"[panel] sizes: list {Hud.Pieces.List.Size.Y:0}, its host {Hud.Pieces.List.GetParent<Control>().Size.Y:0}, the body {p.Body.Size.Y:0}, the panel {p.Size.Y:0}; the panel's combined minimum is {p.GetCombinedMinimumSize().Y:0}");
                }
                void Btn(Vector2 at, bool down) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
                void Move(Vector2 a, Vector2 b) => Input.ParseInputEvent(new InputEventMouseMotion { Position = b, GlobalPosition = b, Relative = b - a });
                Btn(from, true);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Move(from, (from + to) / 2);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Move((from + to) / 2, to);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                Btn(to, false);
                await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                GD.Print($"[panel] dragged to {p.Position.X:0},{p.Position.Y:0}; settings hold {(Cfg.PanelPos.TryGetValue(key, out var at) ? $"{at[0]:0},{at[1]:0}" : "nothing")}");
                async System.Threading.Tasks.Task Pull(float dx, float dy, string what2)
                {
                    var grip = p.GlobalPosition + p.Size - new Vector2(4, 4);
                    var end = grip + new Vector2(dx, dy);
                    Btn(grip, true);
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Move(grip, (grip + end) / 2);
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Move((grip + end) / 2, end);
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    Btn(end, false);
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    GD.Print($"[panel] {what2}: {p.Size.X:0}×{p.Size.Y:0}; settings hold {(Cfg.PanelSize.TryGetValue(key, out var wh) ? $"{wh[0]:0}×{wh[1]:0}" : "nothing")}");
                }
                await Pull(-90, -60, "pulled the corner in");
                await Pull(40, 30, "pushed it back out");
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool PieceMoves(string what)
    {
        if (what.StartsWith("facebutton"))
        {
            var parts = what.Split(':');
            int idx = int.Parse(parts[1]), want = int.Parse(parts[2]);
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(1.0), Timer.SignalName.Timeout);
                var id = S.View.Layer(KTable.Core.Model.Layers.Umpire)[idx].Id;
                int Count() => S.View.Layer(KTable.Core.Model.Layers.Umpire).Count;
                Vector2? Find()
                {
                    var n = T.NodeOf(KTable.Core.Model.Layers.Umpire, id)!;
                    var c = T.Rig.Cam.UnprojectPosition(n.GlobalPosition);
                    for (int r = 0; r < 160; r += 2)
                        for (int a = 0; a < 32; a++)
                        {
                            var s = c + new Vector2(Mathf.Cos(a * Mathf.Tau / 32), Mathf.Sin(a * Mathf.Tau / 32)) * r;
                            if (n.FacePoint(s) is { } f && n.WidgetAt(f)?.index == want) return s;
                        }
                    return null;
                }
                async System.Threading.Tasks.Task Frames(int k) { for (int i = 0; i < k; i++) await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame); }
                void Button(Vector2 at, bool down) => Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
                void Motion(Vector2 at) { Pointer.Drive(at); Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left }); }
                if (Find() is not { } at) { GD.Print($"[facebutton] widget {want} not found on {id}"); return; }
                int before = Count();
                Motion(at); await Frames(2); Button(at, true);
                for (int i = 1; i <= 20; i++) { Motion(at + new Vector2(8 * i, 4 * i)); await Frames(1); }
                Button(at + new Vector2(160, 80), false);
                await Hud.ToSignal(Hud.GetTree().CreateTimer(1.0), Timer.SignalName.Timeout);
                GD.Print($"[facebutton] dragged from widget {want}: pieces {before} -> {Count()}");
                if (Find() is not { } at2) { GD.Print($"[facebutton] widget {want} not found after the drag"); return; }
                before = Count();
                Motion(at2); await Frames(2); Button(at2, true); await Frames(2); Button(at2, false);
                await Hud.ToSignal(Hud.GetTree().CreateTimer(1.0), Timer.SignalName.Timeout);
                GD.Print($"[facebutton] clicked widget {want}: pieces {before} -> {Count()}");
            })).CallDeferred();
            return true;
        }
        if (what.StartsWith("piecedrag"))
        {
            var parts = what.Split(':');
            int idx = parts.Length > 1 ? int.Parse(parts[1]) : 0;
            var prefix = parts.Length > 2 ? string.Join(":", parts[2..]) : null;
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(1.0), Timer.SignalName.Timeout);
                var p = S.View.Layer(KTable.Core.Model.Layers.Umpire)[idx];
                var was = p.Pos;
                var from = T.Rig.Cam.UnprojectPosition(Coords.ToWorld(p.Pos, 0.1f));
                var to = from + new Vector2(260, 140);
                Pointer.Drive(from);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from });
                for (int f = 0; f < 4; f++) await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                if (prefix != null) Hud.GetViewport().GetTexture().GetImage().SavePng($"{prefix}_h.png");
                GD.Print($"[piecedrag] pointer at {Pointer.In(Hud.GetViewport())} (sent {from}); under it {T.HoveredNode?.Piece.Id ?? "nothing"}");
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
                const int steps = 40;
                for (int i = 1; i <= steps; i++)
                {
                    var at = from.Lerp(to, i / (float)steps);
                    Pointer.Drive(at);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (to - from) / steps, ButtonMask = MouseButtonMask.Left });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (prefix != null && (i == steps / 2 || i == steps)) Hud.GetViewport().GetTexture().GetImage().SavePng($"{prefix}_{(i == steps ? "b" : "a")}.png");
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.8), Timer.SignalName.Timeout);
                if (prefix != null) Hud.GetViewport().GetTexture().GetImage().SavePng($"{prefix}_c.png");
                var now = S.View.Find(KTable.Core.Model.Layers.Umpire, p.Id)!;
                GD.Print($"[piecedrag] {p.Id} from {was.X:0.0},{was.Z:0.0} to {now.Pos.X:0.0},{now.Pos.Z:0.0}; pieces {S.View.Layer(KTable.Core.Model.Layers.Umpire).Count}");
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool Hands(string what)
    {
        if (what.StartsWith("diceroll"))
        {
            var want = what.Contains(':') ? what.Split(':')[1].Split(',') : new[] { "6" };
            Hud.OpenPanel("dice");
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.3), Timer.SignalName.Timeout);
                var buttons = new List<Button>();
                void Walk(Node n) { if (n is Button bb) buttons.Add(bb); foreach (var ch in n.GetChildren()) Walk(ch); }
                Walk(Hud.Dice.Panel);
                foreach (var w in want)
                {
                    var b = buttons.FirstOrDefault(x => x.Text == "d" + w);
                    if (b == null) { GD.Print($"[dice] the dice panel has no d{w}"); return; }
                    b.EmitSignal(BaseButton.SignalName.Pressed);
                }
                int before = Hud.LogPanel.Text.GetParsedText().Split('\n').Length;
                buttons.First(x => x.Text == "Roll").EmitSignal(BaseButton.SignalName.Pressed);
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.8), Timer.SignalName.Timeout);
                var lines = Hud.LogPanel.Text.GetParsedText().Split('\n').Where(l => l.Trim().Length > 0).ToList();
                GD.Print($"[dice] rolled {string.Join(" + ", want.Select(w => "d" + w))}; the transcript gained {lines.Count - before + 1} line(s)");
                foreach (var l in lines.TakeLast(2)) GD.Print("[dice] log: " + l.Trim());
                GD.Print("[dice] the top line says: " + Hud.Top.Said!.Text);
            })).CallDeferred();
            return true;
        }
        if (what.StartsWith("capdrag"))
        {
            T.SetTool(Tool.Capture);
            Callable.From((Action)(async () =>
            {
                await Hud.ToSignal(Hud.GetTree().CreateTimer(0.3), Timer.SignalName.Timeout);
                var vp = Hud.GetViewport().GetVisibleRect().Size;
                var a = new Vector2(vp.X * 0.45f, vp.Y * 0.25f);
                var b = new Vector2(vp.X * 0.85f, vp.Y * 0.70f);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a, GlobalPosition = a });
                for (int i = 1; i <= 8; i++)
                {
                    var at = a.Lerp(b, i / 8f);
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = (b - a) / 8 });
                    await Hud.ToSignal(Hud.GetTree(), SceneTree.SignalName.ProcessFrame);
                }
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b, GlobalPosition = b });
                await Hud.ToSignal(Hud.GetTree().CreateTimer(2.5), Timer.SignalName.Timeout);
                var r = T.LastCaptureRect;
                GD.Print($"[capdrag] dragged {a.X:0},{a.Y:0} to {b.X:0},{b.Y:0}; the tool took {(r == null ? "nothing" : $"{r.Value.Size.X:0}×{r.Value.Size.Y:0} at {r.Value.Position.X:0},{r.Value.Position.Y:0}")}");
                GD.Print($"[capdrag] folder: {Cfg.CaptureFolder}");
                GD.Print("[capdrag] the top line says: " + Hud.Top.Said!.Text);
                GD.Print($"[capdrag] the tool afterwards is {T.Tool}");
            })).CallDeferred();
            return true;
        }
        return false;
    }

    bool Lettering(string what)
    {
        if (what.StartsWith("chunkflip"))
        {
            float radius = what.Length > 10 && float.TryParse(what[10..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rr) ? rr : 15f;
            Hud.GetTree().CreateTimer(4).Timeout += () =>
            {
                var mid = Coords.FromWorld(T.Rig.Target);
                var ids = S.View.Layer(Layers.Umpire).Where(p => (p.Pos - mid).Length <= radius).Select(p => p.Id).ToArray();
                T.SetSelection(ids);
                T.FlipSelection();
                GD.Print($"[chunkflip] {ids.Length} picked out and turned over");
            };
            what = "letters";
        }
        if (what == "letters")
        {
            for (int i = 1; i <= 6; i++)
            {
                int at = i * 10;
                Hud.GetTree().CreateTimer(at).Timeout += () => GD.Print($"[letters] {at} s: {T.LetteredCount} lettered, {T.LetteredWeight} text meshes; {T.FindChildren("*", "Label3D", true, false).Count} Label3D in the table");
            }
            return true;
        }
        if (what.StartsWith("reach:") &&float.TryParse(what[6..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var k))
        {
            var eye = T.Rig.Cam.GlobalPosition;
            string Near() => S.View.Layer(Layers.Umpire).Select(p => T.NodeOf(Layers.Umpire, p.Id)).OfType<PieceNode>().OrderBy(n => n.Position.DistanceTo(eye)).FirstOrDefault() is { } n0 ? $"nearest {n0.Position.DistanceTo(eye):0.#} away, reach {n0.LetteringRange():0.#}" : "no piece";
            GD.Print($"[reach] before: {T.LetteredCount} lettered at {T.S.Art.Look.LetteringReach:0}×; {Near()}");
            T.S.Art.Look.LetteringReach = Math.Clamp(k, Settings.ReachMin, Settings.ReachOff);
            T.RefreshLettering();
            Hud.GetTree().CreateTimer(1).Timeout += () => GD.Print($"[reach] after: {T.LetteredCount} lettered at {T.S.Art.Look.LetteringReach:0}×; {Near()}");
            return true;
        }
        return false;
    }

    bool Dialogs(string what)
    {
        if (what == "props") { if (T.SelectedNodes.FirstOrDefault() is { } pn) PropertiesDialog.Open(Ctx, pn); return true; }
        if (what.StartsWith("mapturn:"))
        {
            var press = what["mapturn:".Length..];
            if (S.View.Sheets.OrderBy(s => s.Z).FirstOrDefault() is not { } first) return true;
            T.SelectSheet(first.Id);
            Hud.Drawer.ShowPage("map");
            Hud.Map.Rebuild();
            Hud.GetTree().CreateTimer(1.0).Timeout += () =>
            {
                var b = Hud.GetTree().Root.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(x => x.Text == press && x.IsVisibleInTree());
                GD.Print(b == null ? $"[mapturn] no {press} button" : $"[mapturn] pressed {press} with {first.Name} selected");
                b?.EmitSignal(BaseButton.SignalName.Pressed);
            };
            return true;
        }
        if (what == "props:strip")
        {
            if (S.View.Layer(KTable.Core.Model.Layers.Umpire).FirstOrDefault() is not { } first) return true;
            T.SetSelection(new List<string> { first.Id });
            if (T.SelectedNodes.FirstOrDefault() is { } pn) PropertiesDialog.Open(Ctx, pn);
            bool before = Cfg.LeftOpen;
            Hud.GetTree().CreateTimer(1.0).Timeout += () =>
            {
                var map = Hud.Strip.Panel.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.TooltipText.StartsWith("Map"));
                if (map == null) { GD.Print("[props] no Map button"); return; }
                var at = map.GetGlobalRect().GetCenter();
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
                Hud.GetTree().CreateTimer(0.5).Timeout += () => GD.Print($"[props] properties open, left panel was {(before ? "open" : "shut")}, after clicking Map it is {(Cfg.LeftOpen ? "open" : "shut")}");
            };
            return true;
        }
        if (what == "nameedit") { new TableDemo(T).NameEdit("Hussars"); return true; }
        if (what == "mainmenu") { Hud.FindChildren("*", "MenuButton", true, false).OfType<MenuButton>().FirstOrDefault()?.ShowPopup(); return true; }
        if (what == "clock") { ClockDialog.Open(Ctx); return true; }
        if (what == "settings") { ((IHudShell)Hud).ShowSettings(); return true; }
        if (what == "history") { Hud.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "History")?.EmitSignal(BaseButton.SignalName.Pressed); return true; }
        if (what == "connection") { Hud.Top.People!.Open(); return true; }
        if (what == "savecopy") { ((IHudShell)Hud).ShowSaveCopy(); return true; }
        if (what is "saves" or "saves:dialog" or "saves:trash") { SavesDemo(what == "saves:dialog", what == "saves:trash"); return true; }
        if (what == "saves:many") { SavesManyDemo(); return true; }
        if (what.StartsWith("open:")) { Hud.OpenPanel(what["open:".Length..]); return true; }
        if (what.StartsWith("widget:"))
        {
            var handler = what["widget:".Length..];
            var piece = S.View.Layer(Layers.Umpire).FirstOrDefault(p => p.Widgets.Any(w => w.OnClick?.Contains(handler) == true));
            GD.Print($"[widget] pressing {handler} on {piece?.Label ?? piece?.Id ?? "nothing"}");
            if (piece != null) S.Do(new ClickWidget { Layer = Layers.Umpire, Id = piece.Id, Index = piece.Widgets.FindIndex(w => w.OnClick?.Contains(handler) == true) + 1 });
            Hud.GetTree().CreateTimer(2.0).Timeout += () => GD.Print("[widget] console: " + string.Join(" | ", S.ConsoleLines.TakeLast(6).Select(l => $"{l.Kind} {l.Source}: {l.Text}")));
            return true;
        }
        if (what.StartsWith("pictures:"))
        {
            var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFiles, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter }, Size = new Vector2I(900, 600) };
            FileThumbs.Show(fd);
            Hud.AddChild(fd);
            fd.CurrentDir = what["pictures:".Length..];
            fd.PopupCentered();
            return true;
        }
        if (what.Split(':') is [var dialog and ("settings" or "scenario"), ..] parts)
        {
            if (dialog == "settings") ((IHudShell)Hud).ShowSettings(); else ScenarioDialog.Open(Ctx);
            if (parts.Length > 1)
                Hud.GetTree().CreateTimer(0.5).Timeout += () =>
                {
                    if (Hud.GetTree().Root.FindChildren("*", "AcceptDialog", true, false).OfType<AcceptDialog>().LastOrDefault() is not { } dlg) return;
                    var tab = dlg.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.ToggleMode && string.Equals(b.Text, parts[1], StringComparison.OrdinalIgnoreCase));
                    tab?.EmitSignal(BaseButton.SignalName.Pressed);
                };
            return true;
        }
        if (what == "shortcuts") { GuideReader.Show(Hud.Root, KTable.Client.Docs.KeysPage.File); return true; }
        if (what == "keys" || what == "invite") { InviteDialog.Open(Ctx); return true; }
        if (what == "sighting") { T.SetTool(Tool.Sighting); return true; }
        if (what == "gizmo") { T.SetTool(Tool.Transform); return true; }
        if (what == "leave") { LeaveDialog.Open(Ctx); return true; }
        return false;
    }
}
