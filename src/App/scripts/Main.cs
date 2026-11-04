using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using KTable.Server;
using Timer = Godot.Timer;

namespace KTable.App;

public partial class Main : Node
{
    Settings _cfg = null!;
    Dictionary<string, string> _args = new();
    Net _net = null!;
    Session _session = null!;
    IPlatform _platform = null!;
    bool _started;
    Window.FilesDroppedEventHandler? _onDrop;
    static bool _recovered;
    readonly Library _lib = Library.Load();
    TitleScreen? _title;
    IDevAids? _dev;
    Hud? _hud;
    AgentEndpoint _agent = null!;

    internal TitleScreen Title => _title ??= new TitleScreen(this, _cfg, _lib, _platform);
    IDevAids Dev => _dev ??= DevAids.For(this, _args);
    internal Net Net => _net;
    internal Session Session => _session;
    internal IPlatform Platform => _platform;

    public override void _ExitTree()
    {
        if (_onDrop != null) GetWindow().FilesDropped -= _onDrop;
        _onDrop = null;
        FileThumbs.Uninstall();
    }

    public override async void _Ready()
    {
        try { await _ReadyAsync(); }
        catch (Exception e) { GD.PushError(e.ToString()); }
    }

    async System.Threading.Tasks.Task _ReadyAsync()
    {
        Diagnostics.Install();
        Ui.InstallTheme();
        FileThumbs.Install();
        _cfg = Settings.Load();
        Ui.ApplyScale(GetTree().Root, _cfg);
        GuideLibrary.Keys = () => _cfg.Keys;
        GetTree().AutoAcceptQuit = false;
        static void UseAppIconForWindow()
        {
            var icon = Image.LoadFromFile(ProjectSettings.GlobalizePath("res://icon.png"));
            if (icon == null || icon.IsEmpty()) icon = ResourceLoader.Load<Texture2D>("res://icon.png")?.GetImage();
            if (icon != null && !icon.IsEmpty()) DisplayServer.SetIcon(icon);
        }
        BestEffort.Try(UseAppIconForWindow);
        var args = _args = ParseArgs(OS.GetCmdlineUserArgs());
        bool devRun = DisplayServer.GetName() == "headless" || args.Keys.Any(k => k is "shot" or "shot-loading" or "lock" || k.StartsWith("demo", StringComparison.Ordinal));
        Sfx.Install(this, _cfg, quiet: devRun);
        if (!devRun) Graphics.ApplyDisplay(_cfg);
        if (args.TryGetValue("packages", out var pk)) System.Environment.SetEnvironmentVariable("KTABLE_PACKAGES", pk);
        _net = new Net { Name = "Net" };
        AddChild(_net);
        _agent = new AgentEndpoint(_cfg)
        {
            TableSession = () => _hud != null && IsInstanceValid(_hud) && _hud.IsInsideTree() ? _session : null,
            TableHud = () => _hud != null && IsInstanceValid(_hud) && _hud.IsInsideTree() ? _hud : null,
            AtTitle = AgentAtTitle,
            OpenStudio = id => { if (!AgentAtTitle()) return "Close what is open first (close_studio or leave_game)."; OpenEditor(id); return null; },
            Host = (dir, resume) => { if (!AgentAtTitle()) return "Close what is open first (close_studio or leave_game)."; DoHost(dir, resume); return null; },
            CloseStudio = () => { if (_agent.Studio is not { } st || !IsInstanceValid(st) || st.Gone) return "The piece studio is not open."; st.AskLeave(); return null; },
            LeaveGame = AgentLeavesGame,
        };
        AddChild(_agent);
        if ((_cfg.AgentOn && !devRun) || args.ContainsKey("agent")) _agent.TurnOn();
        _platform = IPlatform.ForThisBuild();
        _platform.Start(this);
        var loading = Loading();
        AddChild(loading);
        ScenarioCards.ReadSoon(new PackageStore(PackageStore.DefaultRoot));
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Dev.LoadingDrawn();
        try { Start(args, pk); }
        finally { loading.QueueFree(); }
    }

    bool AgentAtTitle() => _title?.Root is { } r && IsInstanceValid(r) && r.IsInsideTree() && _openingNow == null;

    string? AgentLeavesGame()
    {
        if (_hud == null || !IsInstanceValid(_hud) || !_hud.IsInsideTree()) return "No game is open.";
        _net.SaveNow(closing: true);
        if (_net.LastSaveProblem is { } problem) return "The game could not be saved, so it was not left: " + problem;
        GetTree().CreateTimer(0.5).Timeout += () => { _cfg.Save(); _net.Shutdown(); GetTree().ReloadCurrentScene(); };
        return null;
    }

    static Control Loading()
    {
        var c = new ColorRect { Color = new Color(0.06f, 0.065f, 0.07f), MouseFilter = Control.MouseFilterEnum.Stop };
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var title = new Label { Text = "Kriegsspiel Table", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Modulate = new Color(0.92f, 0.9f, 0.84f, 0.8f) };
        title.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Georgia", "Cambria", "Book Antiqua", "Times New Roman", "Noto Serif" } });
        title.AddThemeFontSizeOverride("font_size", 34);
        title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        c.AddChild(title);
        return c;
    }

    void Start(Dictionary<string, string> args, string? pk)
    {
        GetWindow().FilesDropped += _onDrop = files =>
        {
            if (Title.Root == null) return;
            foreach (var f in files.Where(f => f.EndsWith(".ktpack", StringComparison.OrdinalIgnoreCase))) InstallFile(f);
        };

        Dev.Starting();
        if (args.TryGetValue("port", out var port)) _cfg.Port = int.Parse(port);
        Dev.WatchForShot();
        if (!_recovered) { _recovered = true; RecoverCutOffInstalls(); }
        try
        {
            var saves = SaveStore.Default;
            if (pk == null) foreach (var line in saves.Migrate(Path.Combine(DataPaths.LegacyDocuments, "saves"), saves.HistoryDir)) GD.Print("[saves] moved " + line);
            foreach (var line in saves.MigrateToRuns(DateTime.UtcNow)) GD.Print("[saves] " + line);
            FillRunsSoon(saves);
            var gone = saves.EmptyTrash(DateTime.UtcNow);
            if (gone > 0) GD.Print($"[saves] emptied {gone} trash entr{(gone == 1 ? "y" : "ies")} older than {SaveStore.TrashDays} days");
        }
        catch (Exception e) { GD.PushError("save folders: " + e.Message); }
        StandardPacks.Ensure(new PackageStore(PackageStore.DefaultRoot));
        if (Dev.TakesOver()) return;
        if (args.TryGetValue("host", out var what)) { StartHost(what, args.GetValueOrDefault("name", "Umpire"), resume: args.ContainsKey("resume"), password: args.GetValueOrDefault("password")); return; }
        if (args.TryGetValue("join", out var addr)) { StartJoin(addr, _cfg.Port, args.GetValueOrDefault("name", "Guest"), args.GetValueOrDefault("password", ""), args.GetValueOrDefault("packages")); return; }
        BuildLobby();
    }

    void RecoverCutOffInstalls()
    {
        try
        {
            var root = PackageStore.DefaultRoot;
            var recovered = InstallJournal.RecoverAll(root, SaveStore.ForRoot(root), Repeatable.UtcNow);
            foreach (var u in recovered.Where(u => u.Any || u.Notes.Count > 0)) GD.Print("[install] undone: " + u.Details());
            var undone = recovered.Where(u => u.Any).ToList();
            var said = undone.Select(u => u.Describe()).ToList();
            if (said.Count > 0) _pendingNotice = said.Count == 1 ? said[0] : $"{said.Count} imports or installs were cut off half way when the app last closed and have been undone: {string.Join(" ", said)}";
        }
        catch (Exception e) { GD.PushError("undoing a cut-off install: " + e.Message); }
    }

    internal void QuitApp()
    {
        Assets.Quiesce();
        Diagnostics.Closing();
        GetTree().Quit();
    }

    static Dictionary<string, string> ParseArgs(string[] a)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < a.Length; i++)
            if (a[i].StartsWith("--")) d[a[i][2..]] = i + 1 < a.Length && !a[i + 1].StartsWith("--") ? a[++i] : "true";
        return d;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest) return;
        if (GetNodeOrNull<PieceStudio>("Studio") is { } studio && IsInstanceValid(studio)) { _ = CloseFromStudio(studio); return; }
        _net.SaveNow(closing: true);
        _cfg.Save();
        if (_net.LastSaveProblem is { } problem && !_quitAnyway) { AskBeforeQuitting(problem); return; }
        QuitApp();
    }

    bool _quitAnyway, _studioClosing;

    async System.Threading.Tasks.Task CloseFromStudio(PieceStudio studio)
    {
        if (_studioClosing) return;
        _studioClosing = true;
        string? problem;
        try { problem = await studio.FlushAllAsync(); }
        finally { _studioClosing = false; }
        _cfg.Save();
        if (problem != null && !_quitAnyway) { AskBeforeQuitting(problem, "Your work in the piece studio"); return; }
        QuitApp();
    }

    void AskBeforeQuitting(string problem, string what = "The game")
    {
        var layer = new CanvasLayer { Layer = 128 };
        var root = new Control { Theme = Ui.Theme(), MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(root);
        AddChild(layer);
        var dlg = Ui.Confirm(root, "Not saved", "Quit anyway", () => { _quitAnyway = true; QuitApp(); },
            $"{what} could not be saved: {problem}\n\nIf you quit now, what changed since the last save is lost. Stay, and closing the window again tries the save again.", 420);
        dlg.CancelButtonText = "Stay";
        dlg.Canceled += () => layer.QueueFree();
    }

    bool _netWired;

    Session NewSession(string packagesRoot)
    {
        var assets = new Assets(new PackageStore(packagesRoot));
        _session = new Session(assets);
        Diagnostics.Watch(_session);
        _net.Init(_session);
        Dev.SessionMade(_session);
        if (_netWired) return _session;
        _netWired = true;
        _net.Joined += EnterTable;
        _net.AtTheDoor += () => { if (_door is { } op) op.Stage = "At the door"; };
        _net.TurnedAway += why =>
        {
            if (_door is not { } op) return;
            _door = null;
            EndOpening(op);
            _net.Shutdown();
            Fail(why);
        };
        _net.Dropped += why =>
        {
            _pendingNotice = "You left the table: " + why;
            _net.SaveNow(closing: true);
            _net.Shutdown();
            GetTree().ReloadCurrentScene();
        };
        return _session;
    }

    string? _recoveryReport;

    Opening? _door;

    internal void StartJoin(string address, int port, string name, string password, string? packages)
    {
        var op = BeginOpening($"{address}:{port}");
        op.Stage = "Connecting";
        _door = op;
        op.Token.Register(() => Callable.From(() => LeaveTheDoor(op)).CallDeferred());
        if (Title.Root != null) Title.Status.Text = "";
        var root = packages ?? Path.Combine(DataPaths.Local, "cache", "packages");
        NewSession(root);
        _cfg.LastName = name; _cfg.LastAddress = address;
        if (_net.Join(address, port, name, password) == Error.Ok) return;
        _door = null;
        EndOpening(op);
        Fail(_net.Status);
    }

    void LeaveTheDoor(Opening op)
    {
        if (_door != op) return;
        _door = null;
        if (_net.Server != null) return;
        _net.Shutdown();
        if (_started) GetTree().ReloadCurrentScene();
    }

    static string? _pendingNotice;

    internal static string? TakePendingNotice()
    {
        var notice = _pendingNotice;
        _pendingNotice = null;
        return notice;
    }

    void EnterTable()
    {
        if (_started) { return; }
        _started = true;
        Title.Close();
        if (!_session.IsHost)
            foreach (var p in _session.View.Packs)
            {
                var pj = Path.Combine(_session.Store.Root, p, "pack.json");
                BestEffort.DeleteFile(pj);
                _session.Store.Forget(p);
            }
        var pending = _session.View.Packs.Where(p => !File.Exists(Path.Combine(_session.Store.Root, p, "pack.json"))).ToList();
        if (_session.IsHost && pending.Count > 0)
        {
            GD.Print("[packs] not installed here, their pieces will not be drawn: " + string.Join(", ", pending));
            pending.Clear();
        }
        var door = _door;
        if (pending.Count == 0) { InFromTheDoor(door); BuildTable(); return; }
        int left = pending.Count;
        bool built = false;
        void Ready()
        {
            if (built || _door != door) return;
            built = true;
            InFromTheDoor(door);
            _session.SetView(_session.View);
            BuildTable();
        }
        void Arrived() { left--; if (door != null) door.Done = door.Total - left; if (left == 0) Ready(); }
        if (door != null)
        {
            door.Title = _session.View.Name is { Length: > 0 } scenario ? scenario : door.Title;
            door.Stage = "Piece lists";
            door.Total = pending.Count;
            ShowCard(door);
        }
        foreach (var p in pending) _session.Assets.EnsureFile(p, "pack.json", Arrived, Arrived);
        GetTree().CreateTimer(20).Timeout += () =>
        {
            if (built) return;
            GD.Print($"[packs] gave up waiting for {left} manifest(s); those pieces will not be drawn");
            Ready();
        };
    }

    void InFromTheDoor(Opening? door)
    {
        if (door == null || _door != door) return;
        _door = null;
        EndOpening(door);
    }

    void BuildTable()
    {
        var table = NewTable();
        FollowPackUpdates(table);
        if (PieceNode.DebugDice) _session.Error += e => GD.Print("[session] error: " + e);
        var hud = NewHud(table);
        SayWhereTheGameIsKept(hud);
        Dev.TableBuilt(table, hud);
        if (_openSavesOnArrival) { _openSavesOnArrival = false; Callable.From(() => { if (IsInstanceValid(hud)) SavesWindow.Open(hud.Ctx); }).CallDeferred(); }
    }

    Table NewTable()
    {
        var table = new Table { Name = "Table" };
        AddChild(table);
        table.Bind(_session, _cfg);
        table.PointerMoved += p => _net.SendPointer(p);
        table.CarryLayoutShared += _net.ShareCarryLayout;
        table.CarryPlaceShared += _net.ShareCarryPlace;
        table.FlightShared += _net.ShareFlight;
        _net.CarryLayoutReceived += (carrier, pieces, at, t) => { if (IsInstanceValid(table)) table.CarryLayoutArrived(carrier, pieces, at, t); };
        _net.CarryPlaceReceived += (carrier, at, t) => { if (IsInstanceValid(table)) table.CarryPlaceArrived(carrier, at, t); };
        _net.FlightReceived += (carrier, t, samples) => { if (IsInstanceValid(table)) table.FlightArrived(carrier, t, samples); };
        _net.PointerReceived += (id, name, p) => table.ShowRemoteCursor(id, name, p);
        _session.PeersChanged += () =>
        {
            foreach (var id in table.CursorPeers().Where(id => _session.Peers.All(p => p.PeerId != id)).ToList()) table.ForgetCursor(id);
        };
        return table;
    }

    void FollowPackUpdates(Table table)
    {
        _session.PackAdded += (id, hash) =>
        {
            var store = _session.Store;
            var pj = Path.Combine(store.PackageDir(id), "pack.json");
            if (!_session.IsHost && UserPacks.Hash(store, id) != hash) BestEffort.DeleteFile(pj);
            store.Forget(id);
            _session.Assets.EnsureFile(id, "pack.json", () => { store.Forget(id); store.Pack(id); table.Rebuild(); _hud?.RefreshPacks(); });
        };
    }

    bool _openSavesOnArrival;
    static System.Threading.Tasks.Task? _filling;

    internal static void FillRunsSoon(SaveStore saves)
    {
        if (_filling is { IsCompleted: false }) return;
        _filling = System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var (dir, run) in saves.RunsToFill())
            {
                if (Assets.Quitting) return;
                try
                {
                    var n = saves.FillRun(dir, run);
                    if (n > 0) GD.Print($"[saves] {n} turn save(s) written into {run.Folder}");
                }
                catch (Exception e) { GD.PushWarning($"[saves] the turn saves of {run.Folder} could not be written yet: {e.Message}"); }
            }
        });
    }

    internal void HostAndOpenSaves(string scenarioDir)
    {
        _openSavesOnArrival = true;
        DoHost(scenarioDir, resume: true);
    }

    Hud NewHud(Table table)
    {
        var hud = new Hud { Name = "Hud" };
        _hud = hud;
        AddChild(hud);
        hud.Bind(_session, table, _net, _cfg);
        _session.RoleChanged += () =>
        {
            _hud?.QueueFree();
            var h2 = new Hud { Name = "Hud" };
            _hud = h2;
            AddChild(h2);
            h2.Bind(_session, table, _net, _cfg);
            h2.Log($"You are now {Fmt.Role(_session.Me.Role)}.");
            ShowWindowTitle();
        };
        ShowWindowTitle();
        return hud;
    }

    void ShowWindowTitle() => GetWindow().Title = $"Kriegsspiel Table — {_session.View.Name} — {Fmt.RoleCaps(_session.Me.Role)}";

    void SayWhereTheGameIsKept(Hud hud)
    {
        if (_session.IsHost)
        {
            var runFolder = _session.ScenarioDir != null && SaveStore.Default.RunOf(_session.SavePath!) is var (runDir, run) ? SaveStore.Default.RunFolderOf(runDir, run) : null;
            hud.Log(runFolder != null
                ? $"Hosting · saved as you play, every ended turn kept in {LogPanel.FolderLink(runFolder, System.IO.Path.GetFileName(runFolder))} · Saves… {_cfg.Keys.Label("saves")}"
                : $"Hosting · saved as you play ({LogPanel.FolderLink(System.IO.Path.GetDirectoryName(_session.SavePath) ?? "", "open the folder")})");
            if (_recoveryReport != null) hud.Log($"[color=#ffd75e]{Fmt.Bb(_recoveryReport)}[/color]");
        }
        else if (_net.MirrorPath != null)
            hud.Log($"This PC keeps its own copy of the game as you play ({LogPanel.FolderLink(System.IO.Path.GetDirectoryName(_net.MirrorPath) ?? "", "open the folder")}). If the host's PC fails, Host a game → Mirrored games continues it from here with the same password, and the people already let in come straight back.");
    }

    public void OpenEditor(string packId)
    {
        CancelOpening();
        var store = new PackageStore(PackageStore.DefaultRoot);
        var held = InUse.Pack(store.Root, packId);
        if (InUse.TryOpen(held, "is open in the piece studio: press Done there first, then try again") is { } busy)
        {
            if (Title.Root == null) BuildLobby();
            Title.Status.Text = $"The pack {packId} is {busy} right now: open it in the piece studio once that has finished.";
            return;
        }
        Title.Close();
        var pack = store.Pack(packId);
        if (pack == null) { InUse.Close(held); Fail($"No pack '{packId}'"); return; }
        var s = new Session(new Assets(store));
        var g = new GameState { Name = pack.Name, Packs = store.ListPackIds().ToList() };
        g.Layers[KTable.Core.Model.Layers.Umpire] = new();
        var server = new GameServer(g, store);
        void Deliver(List<Outgoing> outs)
        {
            foreach (var o in outs)
                switch (o.Message)
                {
                    case KTable.Core.Protocol.CommandMessage cm: s.OnCommand(cm.Command); break;
                    case KTable.Core.Protocol.JoinResult jr: s.OnJoined(jr); break;
                    case KTable.Core.Protocol.ErrorMessage em: GD.PushWarning("editor: " + em.Text); break;
                    case KTable.Core.Protocol.ScriptConsole or KTable.Core.Protocol.ScriptList: s.OnScriptMessage(o.Message); break;
                }
            if (server.ConsoleOut() is { Count: > 0 } said) Deliver(said);
        }
        s.Send = c => Deliver(server.Handle(1, c));
        s.Request = m => Deliver(server.Request(1, m));
        s.SetName("Editor");
        Deliver(server.Join(1, new KTable.Core.Protocol.JoinRequest { Name = "Editor" }));
        var table = new Table { Name = "Table" };
        AddChild(table);
        table.Bind(s, _cfg);
        var tick = new Timer { WaitTime = 0.05, Autostart = true };
        tick.Timeout += () =>
        {
            try { Deliver(server.Scripts.Tick(Time.GetTicksMsec() / 1000.0)); }
            catch (Exception e) { GD.PushError("[scripts] tick failed: " + e); }
        };
        table.AddChild(tick);
        table.SetStandIn(true, _cfg.TableShade);
        s.Art.Look.ShowFaceBoxes = true;
        var ed = new PieceStudio { Name = "Studio", ScriptsChanged = () => Deliver(server.Scripts.Load()) };
        AddChild(ed);
        _agent.Studio = ed;
        ed.Bind(s, table, store, packId, _cfg, () =>
        {
            if (_agent.Studio == ed) _agent.Studio = null;
            InUse.Close(held);
            table.QueueFree(); ed.QueueFree();
            GetWindow().Title = "Kriegsspiel Table";
            BuildLobby(); Title.Select(2); Title.Packs.Show();
        });
        GetWindow().Title = $"Kriegsspiel Table — piece studio — {pack.Name}";
        Dev.StudioOpened(ed);
    }

    void Fail(string msg)
    {
        GD.PushError(msg);
        if (Title.Root == null) BuildLobby();
        Title.Status.Text = msg;
    }
}
