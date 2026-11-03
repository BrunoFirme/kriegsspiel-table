using System;
using System.IO;
using System.Linq;
using System.Threading;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using KTable.Server;
using Json = KTable.Core.Json;

namespace KTable.App;

public partial class Main
{
    internal sealed class Opening
    {
        public readonly CancellationTokenSource Cts = new();
        public CancellationToken Token => Cts.Token;
        public string Title = "";
        public volatile string Stage = "Reading";
        public volatile GameServer? Starting;
        public int Done, Total;
        public string? Held;
        public LoadingCard? Card;
        public LoadingCard.Progress Now() => Starting is { } s ? new(Stage, s.Scripts.LoadDone, s.Scripts.LoadTotal) : new(Stage, Done, Total);
    }

    Opening? _openingNow;
    Control? _loadSlot;
    CanvasLayer? _loadAlone;

    internal string? OpeningTitle => _openingNow?.Title;

    Opening BeginOpening(string title)
    {
        CancelOpening();
        var op = new Opening { Title = title };
        _openingNow = op;
        ShowCard(op);
        return op;
    }

    void ShowCard(Opening op)
    {
        DropCard(op);
        op.Card = new LoadingCard(op.Title, TitleStyle.Serif(), op.Now, CancelOpening);
        if (_loadSlot != null && IsInstanceValid(_loadSlot) && Title.Root != null) { _loadSlot.AddChild(op.Card); return; }
        _loadAlone = new CanvasLayer { Layer = 100 };
        var ground = new ColorRect { Color = Ui.Bar.Darkened(0.2f), MouseFilter = Control.MouseFilterEnum.Ignore };
        ground.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _loadAlone.AddChild(ground);
        var center = new CenterContainer { Theme = TitleStyle.LobbyTheme() };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _loadAlone.AddChild(center);
        center.AddChild(op.Card);
        AddChild(_loadAlone);
    }

    void DropCard(Opening op)
    {
        if (op.Card != null && IsInstanceValid(op.Card)) op.Card.QueueFree();
        op.Card = null;
        if (_loadAlone != null && IsInstanceValid(_loadAlone)) _loadAlone.QueueFree();
        _loadAlone = null;
    }

    internal void CancelOpening()
    {
        if (_openingNow is not { } op) return;
        _openingNow = null;
        op.Cts.Cancel();
        DropCard(op);
        GD.Print($"[open] {op.Title}: cancelled");
    }

    void EndOpening(Opening op)
    {
        if (op.Held != null) { InUse.Close(op.Held); op.Held = null; }
        DropCard(op);
        if (_openingNow == op) _openingNow = null;
    }

    static string TitleOf(string what) => what.EndsWith(SaveStore.Ext, StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(what)
        : Json.PeekString(Path.Combine(PackageStore.DefaultRoot, "scenarios", what, "scenario.json"), "name") ?? what;

    internal async void StartHost(string what, string name, bool resume, string? password = null, Scenario? read = null, Opening? op = null)
    {
        op ??= BeginOpening(TitleOf(what));
        try { await OpenAndHost(what, name, resume, password, read, op); }
        catch (OperationCanceledException) {  }
        catch (Exception e) { if (_openingNow == op && IsInstanceValid(this) && !Assets.Quitting) Fail("Cannot open: " + e.Message); }
        finally { EndOpening(op); }
    }

    bool GivenUp(Opening op) => op.Token.IsCancellationRequested || !IsInstanceValid(this) || Assets.Quitting;

    sealed record Wanted(bool Kstable, string SavePath, string Dir, string ScenarioFile);

    sealed record Opened(GameServer Server, System.Collections.Generic.List<Outgoing> Started, long ReadMs, long OpenMs, long ScriptsMs, string? Report, string? ScenarioDir);

    async System.Threading.Tasks.Task OpenAndHost(string what, string name, bool resume, string? password, Scenario? read, Opening op)
    {
        var store = new PackageStore(PackageStore.DefaultRoot);
        var saves = SaveStore.Default;
        NewSession(store.Root);
        var pieceStore = _session.Store;
        _recoveryReport = null;
        if (Find(store, saves, what) is not { } wanted) return;
        if (_session.ScenarioDir is { } dirId)
        {
            var folder = InUse.Scenario(store.Root, dirId);
            if (InUse.TryOpen(folder, "is opening for the table: cancel it on the title screen first, then try again") is { } busy)
            {
                Fail($"The scenario {dirId} is {busy} right now: host it once that has finished.");
                return;
            }
            op.Held = folder;
        }
        bool fromSave = wanted.Kstable || (resume && saves.HasGame(wanted.Dir));
        string? newRun = null;
        if (!fromSave)
        {
            newRun = saves.NewRunId(wanted.Dir, Repeatable.UtcNow);
            wanted = wanted with { SavePath = saves.LivePath(wanted.Dir, newRun) };
        }
        var token = op.Token;

        var opened = await System.Threading.Tasks.Task.Run(() => Open(store, saves, pieceStore, what, wanted, fromSave, read, op, token), token);
        if (GivenUp(op)) return;
        var server = opened.Server;
        server.Scripts.LoadCancel = default;
        _recoveryReport = opened.Report;
        if (wanted.Kstable) _session.ScenarioDir = opened.ScenarioDir;
        Report(opened);
        if (server.State.Layer(KTable.Core.Model.Layers.Umpire).Count > 2000)
        {
            op.Starting = null;
            op.Stage = "Drawing table";
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (GivenUp(op)) return;
        }
        if (password != null && password.Trim() != server.Access.Password) { server.Access.Password = password.Trim(); server.Dirty = true; }
        _cfg.LastName = name;
        HostOpened(store, saves, server, name, wanted.SavePath, opened.Started, newRun != null ? (wanted.Dir, newRun) : null, op);
    }

    Wanted? Find(PackageStore store, SaveStore saves, string what)
    {
        bool kstable = what.EndsWith(SaveStore.Ext, StringComparison.OrdinalIgnoreCase) && (File.Exists(what) || saves.Checkpoints(what).Count > 0);
        if (kstable) return new Wanted(true, what, "", "");
        var all = store.ListScenarios();
        var sc = all.FirstOrDefault(s => string.Equals(s.id, what, StringComparison.OrdinalIgnoreCase));
        if (sc.path == null && _args.ContainsKey("host")) sc = all.FirstOrDefault(s => s.id.Contains(what) || what.Contains(s.id));
        var found = sc.path ?? (File.Exists(what) ? what : null);
        if (found == null) { Fail($"No scenario '{what}' under {store.Root}\\scenarios"); return null; }
        var dir = Path.GetFileName(Path.GetDirectoryName(found))!;
        _session.ScenarioDir = sc.path != null ? sc.id : null;
        return new Wanted(false, saves.LivePath(dir), dir, found);
    }

    static Opened Open(PackageStore store, SaveStore saves, PackageStore pieceStore, string what, Wanted wanted, bool fromSave, Scenario? read, Opening op, CancellationToken token)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long readMs = 0;
        string? report = null, scenarioDir = null;
        Recovery? recovery = null;
        GameServer server;
        if (wanted.Kstable)
        {
            server = SaveLoader.Open(saves, what, pieceStore, out recovery);
            token.ThrowIfCancellationRequested();
            var sid = server.State.ScenarioId;
            scenarioDir = store.ListScenarios().Where(s => { try { return Json.Load<Scenario>(s.path).Id == sid; } catch { return false; } }).Select(s => s.id).FirstOrDefault();
        }
        else if (fromSave) server = SaveLoader.Open(saves, wanted.SavePath, pieceStore, out recovery);
        else
        {
            var sc2 = read ?? Json.Load<Scenario>(wanted.ScenarioFile);
            readMs = clock.ElapsedMilliseconds;
            token.ThrowIfCancellationRequested();
            op.Stage = "Setting out pieces";
            server = GameServer.FromScenario(sc2, pieceStore);
        }
        token.ThrowIfCancellationRequested();
        long openMs = clock.ElapsedMilliseconds;
        op.Stage = "Starting scripts";
        op.Starting = server;
        server.Scripts.LoadCancel = token;
        var started = server.Scripts.Load();
        token.ThrowIfCancellationRequested();
        if (recovery != null) report = SaveReports.Describe(recovery);
        return new Opened(server, started, readMs, openMs, clock.ElapsedMilliseconds - openMs, report, scenarioDir);
    }

    void Report(Opened opened)
    {
        var server = opened.Server;
        GD.Print($"[open] {server.State.Layer(KTable.Core.Model.Layers.Umpire).Count} pieces ready in {opened.OpenMs} ms" + (opened.ReadMs > 0 ? $" ({opened.ReadMs} ms of it reading the scenario file)" : ""));
        if (opened.ScriptsMs > 100) GD.Print($"[scripts] {server.Scripts.Loaded} script(s) started in {opened.ScriptsMs} ms");
        if (_recoveryReport != null) GD.Print("[saves] " + _recoveryReport);
        if (server.Loaded.ReplayedFromLog > 0 || server.Loaded.LogTorn)
        {
            var carried = $"The game was carried forward by {server.Loaded.ReplayedFromLog} change(s) played after the last whole save was written"
                + (server.Loaded.LogTorn ? ", and the last of them was cut short by the way the app ended and could not be read." : ".");
            GD.Print("[saves] " + carried);
            if (server.Loaded.LogTorn) _recoveryReport = _recoveryReport == null ? carried : _recoveryReport + " " + carried;
        }
        if (server.Loaded.KeptAside is { } keptAside)
        {
            var kept = SaveReports.Describe(keptAside);
            GD.Print("[saves] " + kept);
            _recoveryReport = _recoveryReport == null ? kept : _recoveryReport + " " + kept;
        }
        if (server.MissingPacks.Count > 0)
        {
            var miss = $"This game names {(server.MissingPacks.Count == 1 ? "a pack" : server.MissingPacks.Count + " packs")} not installed here: {string.Join(", ", server.MissingPacks)}. Its pieces are kept but not shown; install it under Packs and Pieces and they come back.";
            GD.Print("[packs] " + miss);
            _recoveryReport = _recoveryReport == null ? miss : _recoveryReport + " " + miss;
        }
    }

    void HostOpened(PackageStore store, SaveStore saves, GameServer server, string name, string savePath, System.Collections.Generic.List<Outgoing> started, (string Dir, string Run)? newRun, Opening op)
    {
        if (op.Held != null) { _net.HeldFolder = op.Held; op.Held = null; }
        else if (_session.ScenarioDir is { } held)
        {
            var folder = InUse.Scenario(store.Root, held);
            if (InUse.TryOpen(folder, "is on the table now, and the table writes itself back into its folder: leave the table first, then try again from the title screen") is { } busy)
            {
                Fail($"The scenario {held} is {busy} right now: host it once that has finished.");
                return;
            }
            _net.HeldFolder = folder;
        }
        if (_net.Host(server, _cfg.Port, name, savePath, started) != Error.Ok)
        {
            var why = _net.Status;
            _net.Shutdown();
            Fail(why);
            return;
        }
        if (newRun is var (runDir, run))
            try { GD.Print("[saves] this play is saved in " + saves.MakeRun(runDir, run, saves.NextRunName(runDir), null, Repeatable.UtcNow)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.PushWarning("the folder for this play's saves could not be made yet: " + e.Message); }
        GD.Print(server.Access.Password.Length > 0 ? "[host] the game has a password" : "[host] open door: no password; everyone who knocks still waits for an umpire");
    }
}
