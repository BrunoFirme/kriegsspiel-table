using System.Diagnostics;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client;

public sealed class HostSaves
{
    readonly Func<ClientSession> _session;
    readonly Func<GameServer?> _server;
    readonly IScenarioFolders _folders;
    readonly SaveStore _store;
    readonly SaveLogGenerations _generations = new();
    readonly SaveWriter _writer;
    readonly SaveLogKeeper _logKeeper, _mirrorLog;
    readonly TurnSealer _mirrorSealer = new();
    readonly ScriptSaves _scripts;
    readonly ScenarioWriteBack _writeBack;
    internal TimeSpan CloseWait = TimeSpan.FromMinutes(2);
    internal Action<long>? BeforeLanding { get => _writer.BeforeLanding; set => _writer.BeforeLanding = value; }
    internal Action<long>? AfterLanding { get => _writer.AfterLanding; set => _writer.AfterLanding = value; }
    readonly SaveServices _app;
    ClientLog Log => _app.Log;
    Func<double> Clock => _app.Clock;

    public event Action<string?>? SaveProblem;
    public event Action<string>? SaveNote;
    public event Action<string>? SaveRecovered;
    public string? LastSaveProblem { get; private set; }
    public string? Warning => _saveWarned ? LastSaveProblem : null;
    public string? MirrorPath { get; private set; }
    public string? HeldFolder { get; set; }

    double _autosaveTimer;
    Checkpointer? _checkpoints;
    int _turnsPictured;
    double _turnEndedAt = -1;
    const double PictureWait = 2, PictureFreshFor = 120;
    bool _wholeWanted;
    bool _mirrorWholeWanted;
    long _mirrorSeq;
    GameState? _mirrorLive;
    ClientSession? _mirrored;
    (string ScenarioDir, string Path)? _pendingCurrent;
    double _saveFailingSince = -1;
    bool _saveWarned;

    public HostSaves(Func<ClientSession> session, Func<GameServer?> server, IScenarioFolders folders, SaveStore store, SaveServices app)
    {
        _session = session;
        _server = server;
        _folders = folders;
        _store = store;
        _app = app;
        _writer = new SaveWriter(store, folders, _generations);
        _logKeeper = new SaveLogKeeper(_generations, () => Log, n => SaveNote?.Invoke(n), _writer.Enqueue);
        _mirrorLog = new SaveLogKeeper(_generations, () => Log, n => SaveNote?.Invoke(n), _writer.Enqueue);
        _scripts = new ScriptSaves(server, () => _app.Dispatch, () => Log);
        _writeBack = new ScenarioWriteBack(session, server, folders, store, () => Log, n => SaveNote?.Invoke(n))
        {
            Quitting = () => _app.Quitting(), PostCopyDone = e => _app.PostWriteBackDone(e),
        };
    }

    GameServer? Server => _server();
    ClientSession Session => _session();

    public void BeginHosting(string savePath, int turnsNow)
    {
        if (Server is { } server && (server.SaveId.Length == 0 || _store.IdTakenElsewhere(savePath, server.SaveId)))
            server.SaveId = _store.IdOf(savePath) is { } id && !_store.IdTakenElsewhere(savePath, id) ? id : SaveFile.NewId(Repeatable.UtcNow);
        _checkpoints = new Checkpointer(_store, savePath, turnsNow, Repeatable.UtcNow);
        _turnsPictured = turnsNow;
        _pendingCurrent = _store.ScenarioOf(savePath) is { } scenarioDir ? (scenarioDir, savePath) : null;
        _wholeWanted = _pendingCurrent != null;
    }

    public (string ScenarioDir, string Path)? SaveInPlay => Server != null && Session.SavePath is { } path && _store.ScenarioOf(path) is { } dir ? (dir, path) : null;

    public void OnDisk<T>(Func<T> work, Action<T?, string?> then)
    {
        _writer.Job(work).ContinueWith(t =>
        {
            var why = t.Status == TaskStatus.RanToCompletion ? null : t.Exception?.InnerException?.Message ?? "it stopped";
            var result = why == null ? t.Result : default;
            _app.Post(() => then(result, why));
        }, TaskScheduler.Default);
    }

    public void SaveAs(string folder, string name, bool replace, Action<string?, string?> then)
    {
        if (Server is not { } server || SaveInPlay is not var (scenarioDir, onTable)) { then(null, "this table is not one of a scenario's games"); return; }
        _scripts.All();
        if (_app.PictureSince(Clock() - PictureFreshFor) is { } picture) server.Picture = picture;
        byte[] bytes;
        var now = Repeatable.UtcNow;
        try
        {
            var copy = server.ToSave(copy: false);
            copy.Id = SaveFile.NewId(now);
            bytes = Json.BigBytes(copy);
        }
        catch (Exception e) { then(null, e.Message); return; }
        OnDisk(() => _store.SaveInto(scenarioDir, folder, name, Json.Packed(bytes), replace, now, onTable), then);
    }

    public void Load(LoadFrom from, Action<string?> then)
    {
        if (Server is not { } server || SaveInPlay is not var (scenarioDir, path)) { then("this table is not one of a scenario's games"); return; }
        if (from.File is { } file && from.TurnIndex == null && from.Start == null && SaveStore.SamePath(file, path)) { then(null); return; }
        TurnRecord? record = null;
        List<TurnRecord> before = new();
        if (from.File == null && from.TurnIndex is int ti)
        {
            if (ti < 0 || ti >= server.Turns.Count) { then("no such turn"); return; }
            record = server.Turns[ti];
            before = server.Turns.Take(ti).ToList();
        }
        _scripts.All();
        byte[] bytes;
        try { bytes = server.SaveBytes(); }
        catch (Exception e) { then("the game on the table could not be written first: " + e.Message); return; }
        var now = Repeatable.UtcNow;
        var packs = server.Store;
        _writer.Queue(new SaveRequest { Path = path, Bytes = bytes, Now = now, SettlesLog = false }).ContinueWith(t =>
        {
            Loaded? loaded = null;
            string? why;
            try
            {
                why = t.Status != TaskStatus.RanToCompletion ? t.Exception?.InnerException?.Message ?? "the write stopped" : t.Result.Problem;
                if (why == null) loaded = Read(scenarioDir, path, from, record, before, packs, now);
            }
            catch (Exception e) { why = e.Message; }
            _app.Post(() =>
            {
                if (Server != server) { then("the table was left"); return; }
                if (loaded == null)
                {
                    server.Dirty = true;
                    then(why ?? "nothing could be read");
                    return;
                }
                try
                {
                    Switch(server, loaded);
                    if (loaded.Report is { } report) SaveRecovered?.Invoke($"{Path.GetFileNameWithoutExtension(loaded.Path)}: {report}");
                    then(null);
                }
                catch (Exception e) { Log.Error("loading a save failed: " + e); then(e.Message); }
            });
        }, TaskScheduler.Default);
    }

    sealed record Loaded(string Path, GameState State, List<TurnRecord> Turns, byte[]? Picture, string Name, string? Created, string? Id, string? Report = null);

    Loaded Read(string scenarioDir, string onTable, LoadFrom from, TurnRecord? record, List<TurnRecord> before, PackageStore? packs, DateTime now)
    {
        if (from.File is { } file && from.TurnIndex == null && from.Start == null)
        {
            var g = SaveLoader.Open(_store, file, packs, out var recovery);
            var report = SaveReports.Of(g, recovery);
            g.SaveTo(file);
            return new Loaded(file, g.State, g.Turns.ToList(), PictureOf(file), from.Name, g.Created, g.SaveId, report);
        }
        string source = onTable;
        if (from.File is { } branchFrom && from.TurnIndex is int ti)
        {
            var g = SaveLoader.Load(branchFrom, packs);
            if (ti < 0 || ti >= g.Turns.Count) throw new InvalidDataException("that save has no such turn");
            record = g.Turns[ti];
            before = g.Turns.Take(ti).ToList();
            source = branchFrom;
        }
        GameState state;
        List<TurnRecord> turns;
        byte[]? picture = null;
        string name;
        if (record != null)
        {
            state = record.Unpack();
            turns = before;
            picture = record.Picture;
            name = from.SaveName ?? SaveNames.From(Path.GetFileNameWithoutExtension(source), SaveNames.Turn(record.Turn, record.GameTime));
        }
        else if (from.Start is { } start)
        {
            (state, turns) = start();
            name = from.SaveName ?? _store.NextGameName(scenarioDir);
        }
        else throw new ArgumentException("nothing to load");
        var folder = from.PlaceIn ?? (record != null ? Path.GetDirectoryName(source) : null);
        var target = _store.NewSavePath(scenarioDir, name, folder);
        return new Loaded(target, state, turns, picture, from.Name, Repeatable.UtcNow.ToString("o"), SaveFile.NewId(now));
    }

    static byte[]? PictureOf(string file)
    {
        try { return Json.ReadSaveHeadline(file)?.Preview.Picture; }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { return null; }
    }

    public void MakeSave(LoadFrom from, Action<string?, string?> then)
    {
        if (Server is not { } server || SaveInPlay is not var (scenarioDir, onTable)) { then(null, "this table is not one of a scenario's games"); return; }
        var (access, packs, now) = (server.Access.Clone(), server.Store, Repeatable.UtcNow);
        OnDisk(() =>
        {
            var l = Read(scenarioDir, onTable, from, null, new(), packs, now);
            var save = new SaveFile
            {
                Id = l.Id ?? SaveFile.NewId(now), ScenarioId = l.State.ScenarioId, Created = l.Created ?? now.ToString("o"), Saved = now.ToString("o"),
                Preview = SavePreview.Of(l.State, l.Picture), Access = access, Snapshot = l.State,
            }.WithTurns(l.Turns);
            Json.SaveBig(l.Path, save);
            return l.Path;
        }, then);
    }

    void Switch(GameServer server, Loaded l)
    {
        _logKeeper.Close(server);
        Session.SavePath = l.Path;
        server.Picture = l.Picture;
        _app.Dispatch(server.LoadSave(l.State, l.Turns, l.Name, l.Created, l.Id ?? ""));
        BeginHosting(l.Path, server.Turns.Count);
        if (_logKeeper.Ensure(server, l.Path)) _writeBack.Hosted();
        SaveNow();
    }

    void KeepPictures(GameServer server)
    {
        int count = server.Turns.Count;
        if (count < _turnsPictured) _turnsPictured = count;
        if (count == _turnsPictured) { _turnEndedAt = -1; return; }
        if (_turnEndedAt < 0)
        {
            _turnEndedAt = Clock();
            _app.RequestPicture();
            return;
        }
        var picture = _app.PictureSince(_turnEndedAt);
        if (picture == null && Clock() - _turnEndedAt < PictureWait) return;
        _turnEndedAt = -1;
        _turnsPictured = count;
        if (picture == null) return;
        server.Picture = picture;
        if (server.Turns[^1].Picture == null)
        {
            server.Turns[^1].Picture = picture;
            server.Dirty = true;
        }
    }

    public void CloseLog()
    {
        _logKeeper.Close(Server);
        _mirrorLog.Close(null);
        if (!_writer.WaitForLog(TimeSpan.FromSeconds(10))) Log.Error("the log beside the save was still being written ten seconds after the table was left");
    }

    public void Stop()
    {
        var copy = _writeBack.Stop();
        Unmirror();
        MirrorPath = null;
        _checkpoints = null;
        if (HeldFolder is { } held)
        {
            HeldFolder = null;
            var running = copy != null ? _writer.Running().Append(copy).ToArray() : _writer.Running();
            if (running.Length == 0) _folders.Release(held);
            else Task.WhenAll(running).ContinueWith(_ => _folders.Release(held), TaskScheduler.Default);
        }
    }

    public void TickMirror(double delta)
    {
        _mirrorLog.Tick(delta);
        _autosaveTimer += delta;
        if (_autosaveTimer < (_mirrorLog.Working ? 1.0 : Math.Max(1.0, _lastSaveCost * 20))) return;
        _autosaveTimer = 0;
        if (!ReferenceEquals(Session.Live, _mirrorLive)) _mirrorWholeWanted = true;
        if (Session.Dirty && (_mirrorWholeWanted || _mirrorLog.WholeSaveDue(closing: false, Session.Live.Clock.Turn))) SaveMirror(closing: false);
    }

    void Mirrored(Command c)
    {
        if (c is LoadSave || c.Seq != _mirrorSeq + 1) _mirrorWholeWanted = true;
        if (c is not LoadSave) _mirrorLog.Append(c);
        _mirrorSeq = c.Seq;
    }

    void Unmirror()
    {
        if (_mirrored != null) _mirrored.Applied -= Mirrored;
        _mirrored = null;
        _mirrorLog.Close(null);
    }

    void FoldMirrorLog(string path)
    {
        if (!File.Exists(SaveLog.PathFor(path)) && !File.Exists(SaveLog.OldPathFor(path))) return;
        try
        {
            SaveLoader.Open(_store, path, null, out _).SaveTo(path);
            SaveLog.Clear(path);
        }
        catch (Exception e) when (SaveStore.IsFileTrouble(e) || e is SaveTooNewException or SaveHeldException)
        {
            var kept = SaveLog.SetAside(path, "unfolded", Repeatable.UtcNow);
            Log.Tell($"The changes logged beside this PC's earlier copy of this game could not be added to it ({e.Message}): they are kept as {string.Join(", ", kept.Select(Path.GetFileName))}.");
        }
    }

    public void Tick(double delta)
    {
        if (Server == null || Session.SavePath == null) return;
        if (_logKeeper.Ensure(Server, Session.SavePath)) _writeBack.Hosted();
        KeepPictures(Server);
        _logKeeper.Tick(delta);
        _autosaveTimer += delta;
        if (_autosaveTimer >= (_logKeeper.Working ? 1.0 : Math.Max(1.0, _lastSaveCost * 20)))
        {
            _autosaveTimer = 0;
            _scripts.Round();
            if (Server.Dirty && (_wholeWanted || _logKeeper.WholeSaveDue(closing: false, Server.State.Clock.Turn))) SaveNow();
        }
        else _scripts.Backlog();
    }

    public void StartMirror(JoinResult jr)
    {
        try
        {
            WaitForWrite();
            var saves = _store;
            var path = saves.MirrorPath(Session.Live.ScenarioId.Length > 0 ? Session.Live.ScenarioId : Session.Live.Name);
            Unmirror();
            FoldMirrorLog(path);
            if (File.Exists(path))
            {
                SaveFile? old = null;
                try { old = Json.Load<SaveFile>(path); }
                catch (Exception e) { Log.Tell($"This PC's earlier copy of this game ({Path.GetFileName(path)}) could not be read ({e.Message}): it is kept among the copies of its turns, and the copy starts again from the table as it is now."); }
                if (old == null || old.Created != jr.Created) saves.Checkpoint(path, CheckpointKind.StartOver, old?.Snapshot.Clock.Turn ?? 0, Repeatable.UtcNow);
                else if (Session.MirrorTurns.Count == 0)
                {
                    old.BindTurns();
                    foreach (var r in old.Turns) if (r.Turn < Session.Live.Clock.Turn) Session.MirrorTurns.Add(r);
                }
            }
            MirrorPath = path;
            _checkpoints = new Checkpointer(saves, path, Session.MirrorTurns.Count, Repeatable.UtcNow);
            _mirrored = Session;
            _mirrored.Applied += Mirrored;
            _mirrorWholeWanted = true;
            Session.Dirty = true;
            SaveMirror(closing: false);
        }
        catch (Exception e)
        {
            Log.Error("mirror: " + e.Message);
            Unmirror();
            MirrorPath = null;
        }
    }

    public void StopMirror()
    {
        SaveMirror(closing: true);
        Unmirror();
        MirrorPath = null;
        _checkpoints = null;
    }

    void SaveMirror(bool closing)
    {
        if (MirrorPath == null) return;
        if (!closing && _writer.Busy) { StillWriting(); return; }
        var clock = Stopwatch.StartNew();
        var r = new SaveRequest
        {
            Path = MirrorPath, Mirror = true, Changed = Session.Dirty, Closing = closing, Now = Repeatable.UtcNow, Checkpoints = _checkpoints,
            Turns = Session.MirrorTurns.Count, LastTurn = Session.MirrorTurns.Count > 0 ? Session.MirrorTurns[^1].Turn : 0, ClockTurn = Session.Live.Clock.Turn,
        };
        var live = Session.Live;
        _mirrorLog.Ensure(MirrorPath, Session.Created, live.ScenarioId, live.Clock.Turn);
        var (generation, liveLogIdle) = _mirrorLog.SetAside(MirrorPath, live.Clock.Turn);
        r = r with { LogGeneration = generation, LiveLogIdle = liveLogIdle };
        try
        {
            _mirrorSealer.Seal(Session.MirrorTurns);
            r = r with { Bytes = Json.BigBytes(Session.ToMirrorSave(_app.MirrorOf())) };
            _mirrorLog.TakenWhole(r.Bytes!.Length);
            Session.Dirty = false;
            _mirrorWholeWanted = false;
            _mirrorLive = live;
            _mirrorSeq = live.LastSeq;
        }
        catch (Exception e)
        {
            _mirrorWholeWanted = true;
            Log.Error("mirror save failed: " + e.Message);
            ReportSave($"this PC's copy of the game could not be written to {MirrorPath}: {e.Message}", closing);
            return;
        }
        Write(r);
        _lastSaveCost = clock.Elapsed.TotalSeconds;
    }

    public void SaveNow(bool closing = false)
    {
        if (Server == null) { SaveMirror(closing); return; }
        if (Session.SavePath == null) return;
        if (!closing && _writer.Busy) { StillWriting(); return; }
        if (closing) _scripts.All();
        var clock = Stopwatch.StartNew();
        var r = new SaveRequest
        {
            Path = Session.SavePath, Changed = Server.Dirty, Closing = closing, Now = Repeatable.UtcNow, Checkpoints = _checkpoints,
            Turns = Server.Turns.Count, LastTurn = Server.Turns.Count > 0 ? Server.Turns[^1].Turn : 0, ClockTurn = Server.State.Clock.Turn,
            MakesCurrent = _pendingCurrent,
        };
        _wholeWanted = false;
        string? problem = _writeBack.Prepare(closing, out var folder, out var setup);
        var (generation, liveLogIdle) = _logKeeper.SetAside(r.Path, Server.State.Clock.Turn);
        r = r with { Folder = folder, Setup = setup, LogGeneration = generation, LiveLogIdle = liveLogIdle };
        try { r = r with { Bytes = Server.SaveBytes() }; _logKeeper.TakenWhole(r.Bytes!.Length); }
        catch (Exception e) { problem = $"the game could not be written to {r.Path}: {e.Message}"; Log.Error("save failed: " + e.Message); }
        if (r.Bytes != null || r.Folder != null) Write(r);
        _lastSaveCost = clock.Elapsed.TotalSeconds;
        if (clock.ElapsedMilliseconds > 30)
            Log.Print($"[save] {clock.ElapsedMilliseconds} ms on the main thread ({Server.State.Layer(Layers.Umpire).Count} pieces{(r.Folder != null ? ", the scenario folder with it" : "")})");
        if (problem != null) ReportSave(problem, closing);
    }

    double _lastSaveCost;

    internal bool WaitForDisk() => _writer.WaitFor(TimeSpan.FromSeconds(10));

    internal bool WaitForLog() => _writer.WaitForLog(TimeSpan.FromSeconds(10));

    public string SaveLogState => _logKeeper.State;

    double _writingSince;

    void Write(SaveRequest r)
    {
        if (!r.Closing)
        {
            Harvest();
            _writingSince = Clock();
            _writer.Start(r);
            return;
        }
        var (before, closed) = _writer.WriteAndWait(r, CloseWait);
        if (before != null) Finish(before);
        if (closed != null) Finish(closed);
        else
        {
            Log.Error($"the closing save of {r.Path} was still being written after {CloseWait.TotalSeconds:0} seconds; the table closes without waiting for it");
            ReportSave("the game was still being written when the table closed", closing: true);
        }
    }

    void StillWriting()
    {
        var running = Clock() - _writingSince;
        if (running >= 10) ReportSave($"the last save of {_writer.Writing} has not finished after {running:0} seconds", closing: false);
    }

    public void Harvest()
    {
        if (_writer.TakeEnded() is { } o) Finish(o);
    }

    void WaitForWrite()
    {
        if (!_writer.WaitFor(TimeSpan.FromMinutes(2))) Log.Error($"a save of {_writer.Writing} was still being written after two minutes; carrying on without waiting for it");
        Harvest();
    }

    void Finish(SaveOutcome o)
    {
        var r = o.Request;
        if (o.Overtaken) Log.Print($"[saves] a write of {r.Path} finished after a newer one had landed there, and was dropped");
        if (o.Problem != null)
        {
            Log.Error("save failed: " + o.Problem);
            if (r.Mirror)
            {
                if (SaveStore.SamePath(MirrorPath, r.Path))
                {
                    Session.Dirty = true;
                    _mirrorWholeWanted = true;
                }
            }
            else if (Server != null && SaveStore.SamePath(Session.SavePath, r.Path)) Server.Dirty = true;
        }
        if (o.FolderProblem != null) Log.Error("scenario write-back failed: " + o.FolderProblem);
        if (o.SetupKept) _writeBack.SetupKept(r.Setup!);
        if (o.MadeCurrent && _pendingCurrent == r.MakesCurrent) _pendingCurrent = null;
        if (o.Problem != null && !r.Mirror && SaveStore.SamePath(Session.SavePath, r.Path)) _wholeWanted = _pendingCurrent != null;
        Note(o.Kept);
        ReportSave(o.Problem ?? o.FolderProblem, r.Closing);
    }

    public void WriteBackDone(string error) => _writeBack.CopyDone(error, () => SaveNow());

    void Note(Checkpoint? cp) { if (cp != null) SaveNote?.Invoke($"A copy of the game was kept ({cp.Describe()}): {Path.GetFileName(cp.Path)}"); }

    void ReportSave(string? problem, bool closing)
    {
        LastSaveProblem = problem;
        var t = Clock();
        if (problem == null)
        {
            if (_saveWarned) SaveProblem?.Invoke(null);
            _saveFailingSince = -1;
            _saveWarned = false;
            return;
        }
        if (_saveFailingSince < 0) _saveFailingSince = t;
        if (!_saveWarned && (closing || t - _saveFailingSince >= 10)) { _saveWarned = true; SaveProblem?.Invoke(problem); }
    }
}

public sealed record LoadFrom(string Name, string? File = null, int? TurnIndex = null,
    Func<(GameState State, List<TurnRecord> Turns)>? Start = null, string? SaveName = null, string? PlaceIn = null);
