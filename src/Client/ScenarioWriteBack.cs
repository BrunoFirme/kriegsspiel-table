using System.Diagnostics;
using KTable.Core;
using KTable.Core.Model;
using KTable.Server;

namespace KTable.Client;

sealed class ScenarioWriteBack
{
    readonly Func<ClientSession> _session;
    readonly Func<GameServer?> _server;
    readonly IScenarioFolders _folders;
    readonly SaveStore _store;
    readonly Func<ClientLog> _log;
    readonly Action<string> _note;
    readonly Stopwatch _since = new();
    TurnRecord? _setupKept;
    Task? _copy;
    CancellationTokenSource? _copyStop;

    public ScenarioWriteBack(Func<ClientSession> session, Func<GameServer?> server, IScenarioFolders folders, SaveStore store, Func<ClientLog> log, Action<string> note)
    {
        _session = session;
        _server = server;
        _folders = folders;
        _store = store;
        _log = log;
        _note = note;
    }

    public Func<bool> Quitting { get; set; } = () => false;
    public Action<string> PostCopyDone { get; set; } = _ => { };

    public void Hosted() => _since.Restart();

    public Task? Copy => _copy is { IsCompleted: false } c ? c : null;

    public string? Prepare(bool closing, out (string File, Scenario Scenario)? folder, out SetupToKeep? setup)
    {
        folder = null;
        setup = null;
        var server = _server()!;
        var session = _session();
        bool due = closing || _since.ElapsedMilliseconds > 20000 || _since.ElapsedMilliseconds == 0;
        if (session.ScenarioDir is not { } dir || !due) return null;
        _since.Restart();
        if (_copy is { IsCompleted: false }) return null;
        if (_folders.SheetsPending(session.Store, server.State, dir))
        {
            var snapshot = server.State.DeepClone();
            var store = session.Store;
            var name = server.State.Name;
            _note("Copying the map's tiles into the scenario folder; a big map takes a while.");
            var stop = (_copyStop = new CancellationTokenSource()).Token;
            _copy = Task.Run(() => _folders.SaveTable(store, snapshot, name, dir, stop));
            _copy.ContinueWith(t => { if (!Quitting() && !stop.IsCancellationRequested) PostCopyDone(t.IsFaulted ? t.Exception?.InnerException?.Message ?? "failed" : ""); }, TaskScheduler.Default);
            return null;
        }
        try
        {
            var (file, sc) = _folders.PrepareTable(session.Store, server.State, server.State.Name, dir);
            folder = (file, sc);
            if (server.Turns.Count > 0 && !ReferenceEquals(server.Turns[0], _setupKept) && sc.Id.StartsWith("local.", StringComparison.Ordinal)
                && session.SavePath != null && string.Equals(_store.ScenarioOf(session.SavePath), dir, StringComparison.OrdinalIgnoreCase))
                setup = new SetupToKeep(session.Store, dir, server.Turns[0], sc.Id, sc.Name);
            return null;
        }
        catch (Exception e) { _log().Error("scenario write-back failed: " + e.Message); return "the scenario folder could not be updated: " + e.Message; }
    }

    public void SetupKept(SetupToKeep s)
    {
        _setupKept = s.First;
        _log().Print($"[saves] the scenario's setup is kept: the table as turn {s.First.Turn} ended");
    }

    public void CopyDone(string error, Action saveNow)
    {
        _copy = null;
        if (error.Length > 0)
        {
            _log().Error("scenario write-back failed: " + error);
            _note("The map's tiles could not be copied into the scenario folder: " + error);
            return;
        }
        _note("The map's tiles are in the scenario folder now.");
        if (_server() == null || _session().ScenarioDir == null) return;
        _since.Reset();
        saveNow();
    }

    public Task? Stop()
    {
        var copy = _copy;
        _copy = null;
        if (copy is not { IsCompleted: false }) return null;
        _copyStop?.Cancel();
        bool stopped;
        try { stopped = copy.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { stopped = true; }
        if (stopped) return null;
        _log().Error("the map's tile copy had not stopped ten seconds after the table was left; it stops in the background, and the scenario folder stays held until it has");
        return copy;
    }
}
