using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;
using Timer = Godot.Timer;

namespace KTable.App;

sealed class DevFlags : IDevAids
{
    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    readonly Main _main;
    readonly Dictionary<string, string> _args;
    static int _reloads;

    public DevFlags(Main main, Dictionary<string, string> args) { _main = main; _args = args; }

    SceneTree Tree => _main.GetTree();

    public void LoadingDrawn()
    {
        if (_args.TryGetValue("shot-loading", out var shotLoading)) _main.GetViewport().GetTexture().GetImage().SavePng(shotLoading);
    }

    public void Starting()
    {
        if (_args.ContainsKey("debug-dice")) { PieceNode.DebugDice = true; }
        if (_args.ContainsKey("check-overflow"))
        {
            var t = new Timer { WaitTime = 1.0, Autostart = true };
            t.Timeout += () => OverflowCheck.Report(Tree.Root);
            _main.AddChild(t);
        }
        if (_args.TryGetValue("demo-click", out var dck))
        {
            var a = dck.Split(',').Select(x => float.Parse(x, Inv)).ToArray();
            Tree.CreateTimer(1.5).Timeout += () =>
            {
                var at = new Vector2(a[0], a[1]);
                Pointer.Drive(at);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
            };
        }
        if (_args.TryGetValue("demo-scroll", out var dsc))
        {
            var a = dsc.Split(',').Select(x => float.Parse(x, Inv)).ToArray();
            Tree.CreateTimer(2.5).Timeout += () =>
            {
                var at = new Vector2(a[0], a[1]);
                int ticks = a.Length > 2 ? (int)a[2] : 5;
                Pointer.Drive(at);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                for (int i = 0; i < ticks; i++)
                {
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = at, GlobalPosition = at, Factor = 1 });
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = false, Position = at, GlobalPosition = at, Factor = 1 });
                }
                var hovered = _main.GetViewport().GuiGetHoveredControl();
                GD.Print($"[scroll] {ticks} ticks at {at}; hovered={hovered?.GetType().Name} '{hovered?.Name}' filter={hovered?.MouseFilter}");
            };
        }
    }

    public void WatchForShot()
    {
        if (!_args.TryGetValue("shot", out var shot)) return;
        if (_args.TryGetValue("shot-every", out var every)) _ = Shots(shot, double.Parse(every, Inv));
        else _ = Shot(shot, double.Parse(_args.GetValueOrDefault("shot-delay", "6"), Inv), _args.ContainsKey("quit"));
    }

    async System.Threading.Tasks.Task Shots(string path, double every)
    {
        var stem = System.IO.Path.ChangeExtension(path, null);
        for (int n = 1; ; n++)
        {
            await _main.ToSignal(Tree.CreateTimer(every), SceneTreeTimer.SignalName.Timeout);
            if (!GodotObject.IsInstanceValid(_main) || !_main.IsInsideTree()) return;
            var file = $"{stem}-{n:000}.png";
            _main.GetViewport().GetTexture().GetImage().SavePng(file);
            GD.Print("[shot] " + file);
        }
    }

    async System.Threading.Tasks.Task Shot(string path, double delay, bool quit)
    {
        await _main.ToSignal(Tree.CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        if (!GodotObject.IsInstanceValid(_main) || !_main.IsInsideTree()) return;
        _main.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print("[shot] " + path);
        if (quit)
        {
            var qc = System.Diagnostics.Stopwatch.StartNew();
            _main.Net.SaveNow(closing: true);
            GD.Print($"[close] the last save took {qc.ElapsedMilliseconds} ms");
            _main.QuitApp();
            GD.Print($"[close] quit asked for at {qc.ElapsedMilliseconds} ms; what follows is the engine letting go");
        }
    }

    public bool TakesOver()
    {
        if (_args.ContainsKey("bundle-selftest"))
        {
            BundleSelfTest.Run();
            Tree.Quit();
            return true;
        }
        if (_args.ContainsKey("workshop-selftest")) { WorkshopSelfTest(); return true; }
        if (_args.ContainsKey("demo-converter"))
        {
            int n = 0;
            GD.Print("[converter] found at " + (ConverterTool.Find() ?? "(missing)"));
            ConverterTool.Run(new[] { "list-modules" }, l => { if (l.StartsWith("{")) n++; }, code => { GD.Print($"[converter] list-modules exit {code}, {n} entries"); Tree.Quit(); });
            return true;
        }
        if (_args.TryGetValue("new-blank", out var blankName))
        {
            var dir = ScenarioFiles.CreateBlank(new PackageStore(PackageStore.DefaultRoot), blankName, "1812-07-22T06:00", 15);
            _main.StartHost(dir, "Umpire", resume: false);
            return true;
        }
        if (_args.TryGetValue("edit-pack", out var ep))
        {
            var st = new PackageStore(PackageStore.DefaultRoot);
            if (ep == "new") ep = PackEdit.CreatePack(st, "Test pieces", "Umpire");
            _main.OpenEditor(ep);
            return true;
        }
        if (_args.ContainsKey("demo-leave"))
        {
            if (_reloads++ == 0) Tree.CreateTimer(3).Timeout += () =>
            {
                GD.Print("[leave] leaving the table");
                _main.Net.SaveNow(closing: true);
                Tree.ReloadCurrentScene();
            };
            else Tree.CreateTimer(2.5).Timeout += () => GD.Print($"[leave] after the reload: status='{_main.Net.Status}' hosting={_main.Net.Server != null} table={_main.GetNodeOrNull("Table") != null}");
        }
        if (_args.TryGetValue("start-over", out var over))
        {
            _main.BuildLobby();
            var st = new PackageStore(PackageStore.DefaultRoot);
            var (setup, keep, back) = Main.StartOverPlan(st, over);
            GD.Print("[start over] " + back);
            _main.StartOver(st, over, setup, keep, m => GD.Print("[start over] " + m));
            return true;
        }
        if (_args.TryGetValue("demo-host-card", out var card))
        {
            _main.BuildLobby();
            Tree.CreateTimer(1).Timeout += () => _main.DoHost(card, _args.ContainsKey("resume"));
            if (_args.TryGetValue("demo-cancel-after", out var ca) && double.TryParse(ca, Inv, out var cs))
                Tree.CreateTimer(cs).Timeout += () => { GD.Print($"[demo] cancel, opening={_main.OpeningTitle ?? "none"}"); _main.CancelOpening(); };
            if (_args.TryGetValue("demo-switch", out var sw) && sw.Split('@') is [var other, var at] && double.TryParse(at, Inv, out var ss))
                Tree.CreateTimer(ss).Timeout += () => { GD.Print($"[demo] host {other} while opening={_main.OpeningTitle ?? "none"}"); _main.DoHost(other, false); };
            return true;
        }
        return false;
    }

    void WorkshopSelfTest()
    {
        var store = new PackageStore(PackageStore.DefaultRoot);
        var scratch = new PackageStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ktable-ws-selftest"));
        foreach (var (kind, id) in new[] { ("pack", "example.miranda-de-azan-1812.extras"), ("scenario", "ferrara") })
        {
            var dir = WorkshopContent.Stage(store, kind, id);
            var files = System.IO.Directory.GetFiles(dir, "*", System.IO.SearchOption.AllDirectories).Length;
            var meta = WorkshopContent.Install(scratch, dir);
            var preview = WorkshopContent.Preview(store, kind, id);
            GD.Print($"[workshop] {kind} {id}: staged {files} files, installed as {meta.Kind}:{meta.Id} ('{meta.Name}', packs: {string.Join(",", meta.Packs)}), preview {new System.IO.FileInfo(preview).Length / 1024} KB");
        }
        var a1 = store.Pack("example.miranda-de-azan-1812.extras")!;
        var b1 = scratch.Pack("example.miranda-de-azan-1812.extras")!;
        GD.Print($"[workshop] pack round trip identical: {Json.Serialize(a1) == Json.Serialize(b1)}");
        foreach (var line in _main.Platform.SelfTest()) GD.Print(line);
        Tree.Quit();
    }

    public void SessionMade(Session session)
    {
        if (_args.TryGetValue("admit-all", out var aa))
            session.Knocked += k =>
                _main.Net.Door(new KTable.Core.Protocol.Admit { PeerId = k.PeerId, Role = aa.ToLowerInvariant() switch { "lead" => Role.LeadUmpire, "umpire" => Role.Umpire, _ => Role.Observer } });
    }

    async void OpenAgentPage(string[] buttons)
    {
        DialogPages.LastPage["app-settings"] = "AI agent";
        AppSettings.OpenWindow(_main, Settings.Load());
        foreach (var b in buttons)
        {
            await _main.ToSignal(Tree.CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
            Press(_main.GetTree().Root, b);
        }
    }

    static bool Press(Node n, string text)
    {
        if (n is Button b && b.Text == text && b.IsVisibleInTree()) { b.EmitSignal(BaseButton.SignalName.Pressed); return true; }
        foreach (var c in n.GetChildren(true)) if (Press(c, text)) return true;
        return false;
    }

    public void TitleShown(TitleScreen title)
    {
        if (_args.Remove("shelf", out var folder))
        {
            bool packs = _args.GetValueOrDefault("page") == "packs";
            title.Shelf.Go(packs, folder);
            if (!packs) { Callable.From(_main.RebuildLobby).CallDeferred(); return; }
        }
        if (title.HasWorkshop && _args.GetValueOrDefault("page") == "workshop") { title.Select(3); title.Workshop.Show(); }
        if (_args.GetValueOrDefault("page") == "packs") { title.Select(2); title.Packs.Show(); }
        if (_args.GetValueOrDefault("page") == "join") title.Select(1);
        if (_args.GetValueOrDefault("page") == "settings") title.SettingsPage.Show();
        if (_args.GetValueOrDefault("page") == "report") title.Report.Show();
        if (_args.GetValueOrDefault("page") is "agentpage" or "agent" or "agenttech" or "agentkey")
            OpenAgentPage(_args["page"] switch { "agentpage" => System.Array.Empty<string>(), "agent" => new[] { AgentConnect.Title }, "agenttech" => new[] { "Technical settings…" }, _ => new[] { "Technical settings…", "New key" } });
        if (_args.GetValueOrDefault("page") == "trash") { title.Select(0); title.Trash.Show(packs: false); }
        if (_args.GetValueOrDefault("page") == "packtrash") { title.Select(2); title.Trash.Show(packs: true); }
        if (_args.GetValueOrDefault("page") is { } pg && pg.StartsWith("makers"))
        {
            title.Select(2);
            title.Packs.Show();
            GuideReader.Spec(_main, pg.Contains(':') ? pg[(pg.IndexOf(':') + 1)..] : "makers.md");
        }
        if (_args.TryGetValue("demo-picks", out var dp)) _ = PickRows(int.Parse(dp, Inv));
        if (_args.TryGetValue("demo-import", out var di))
        {
            title.Select(2);
            title.Packs.Show();
            var dlg = new ImportDialog(PackageStore.DefaultRoot, (sc, pk) => title.Status.Text = $"Imported {sc} / {pk}");
            Ui.Open(_main, dlg);
            if (di == "browse")
            {
                const double ListLoads = 4;
                Tree.CreateTimer(ListLoads).Timeout += () => dlg.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "Browse…")?.EmitSignal(BaseButton.SignalName.Pressed);
                return;
            }
            dlg.Prefill(di);
            if (_args.TryGetValue("demo-stop-on", out var stopOn)) dlg.StopOn = stopOn;
            Callable.From(dlg.StartImport).CallDeferred();
        }
    }

    public void TableBuilt(Table table, Hud hud) => new TableDemos(_main, _args, table, hud).Run();

    async System.Threading.Tasks.Task PickRows(int n)
    {
        await _main.ToSignal(Tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        if (TitlePicks.Latest is not { } picks) { GD.Print("[picks] no list on this page"); return; }
        foreach (var box in picks.Boxes.Take(n))
        {
            var at = box.GetGlobalRect().Position + new Vector2(24, 12);
            Pointer.Drive(at);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, CtrlPressed = true });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at, CtrlPressed = true });
            await _main.ToSignal(Tree.CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        }
        GD.Print($"[picks] {picks.Count} picked of {picks.Boxes.Count}; bar shown: {picks.Bar.Visible}");
    }

    public void StudioOpened(PieceStudio studio)
    {
        if (_args.TryGetValue("demo-studio", out var steps)) Tree.CreateTimer(1.5).Timeout += () => new StudioDemo(studio).Run(steps);
    }
}
