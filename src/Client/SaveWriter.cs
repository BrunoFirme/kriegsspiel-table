using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

sealed record SetupToKeep(PackageStore Store, string Dir, TurnRecord First, string Id, string Name);

sealed record SaveRequest
{
    public required string Path { get; init; }
    public byte[]? Bytes { get; init; }
    public (string File, Scenario Scenario)? Folder { get; init; }
    public SetupToKeep? Setup { get; init; }
    public bool Mirror { get; init; }
    public bool Changed { get; init; }
    public bool Closing { get; init; }
    public long LogGeneration { get; init; }
    public bool LiveLogIdle { get; init; }
    public bool SettlesLog { get; init; } = true;
    public int Turns { get; init; }
    public int LastTurn { get; init; }
    public int ClockTurn { get; init; }
    public DateTime Now { get; init; }
    public Checkpointer? Checkpoints { get; init; }
    public (string ScenarioDir, string Run)? MakesCurrent { get; init; }
}

sealed record SaveOutcome(SaveRequest Request, bool Overtaken, string? Problem, string? FolderProblem, Checkpoint? Kept, bool SetupKept, bool MadeCurrent = false);

sealed class SaveWriter
{
    readonly SaveStore _store;
    readonly IScenarioFolders _folders;
    readonly SaveLogGenerations _generations;
    readonly SaveLandings _landings = new();
    long _writes;
    Task<SaveOutcome>? _writing;
    Task? _lateClose;
    Task? _queued;
    Task? _logWork;
    Task? _job;

    public SaveWriter(SaveStore store, IScenarioFolders folders, SaveLogGenerations generations)
    {
        _store = store;
        _folders = folders;
        _generations = generations;
    }

    internal Action<long>? BeforeLanding { get; set; }
    internal Action<long>? AfterLanding { get; set; }

    public bool Busy => _writing is { IsCompleted: false } || _queued is { IsCompleted: false };

    public string? Writing { get; private set; }

    public Task[] Running() => new Task?[] { _writing, _lateClose, _queued, _logWork, _job }.Where(t => t is { IsCompleted: false }).Select(t => t!).ToArray();

    Task? Tail()
    {
        var open = new Task?[] { _writing, _queued, _logWork, _job }.Where(t => t is { IsCompleted: false }).Select(t => t!).ToArray();
        return open.Length switch { 0 => null, 1 => open[0], _ => Task.WhenAll(open) };
    }

    public void Start(SaveRequest r)
    {
        long n = ++_writes;
        Writing = r.Path;
        var before = Tail();
        _writing = before == null ? Task.Run(() => Run(r, n)) : before.ContinueWith(_ => Run(r, n), TaskScheduler.Default);
    }

    public (SaveOutcome? before, SaveOutcome? written) WriteAndWait(SaveRequest r, TimeSpan wait)
    {
        long n = ++_writes;
        var before = Tail();
        var run = before == null ? Task.Run(() => Run(r, n)) : before.ContinueWith(_ => Run(r, n), TaskScheduler.Default);
        bool done = false;
        try { done = run.Wait(wait); } catch (AggregateException) { done = true; }
        var earlier = TakeEnded();
        if (done) return (earlier, OutcomeOf(run, r));
        _lateClose = _lateClose is { IsCompleted: false } late ? Task.WhenAll(late, run) : run;
        return (earlier, null);
    }

    public Task<SaveOutcome> Queue(SaveRequest r)
    {
        long n = ++_writes;
        var before = Tail();
        var run = before == null ? Task.Run(() => Run(r, n)) : before.ContinueWith(_ => Run(r, n), TaskScheduler.Default);
        _queued = run;
        return run;
    }

    public Task<T> Job<T>(Func<T> work)
    {
        var before = Tail();
        var run = before == null ? Task.Run(work) : before.ContinueWith(_ => work(), TaskScheduler.Default);
        _job = run;
        return run;
    }

    public void Enqueue(Action work)
    {
        var before = _logWork;
        _logWork = before is not { IsCompleted: false } ? Task.Run(work) : before.ContinueWith(_ => work(), TaskScheduler.Default);
    }

    public bool WaitForLog(TimeSpan wait)
    {
        if (_logWork is not { IsCompleted: false } log) return true;
        try { return log.Wait(wait); }
        catch (AggregateException) { return true; }
    }

    public SaveOutcome? TakeEnded()
    {
        if (_writing is not { IsCompleted: true } w) return null;
        _writing = null;
        var path = Writing!;
        Writing = null;
        return OutcomeOf(w, null, path);
    }

    public bool WaitFor(TimeSpan wait)
    {
        if (Tail() is not { } all) return true;
        try { return all.Wait(wait); }
        catch (AggregateException) { return true; }
    }

    static SaveOutcome OutcomeOf(Task<SaveOutcome> t, SaveRequest? r, string? path = null) =>
        t.Status == TaskStatus.RanToCompletion ? t.Result
            : new SaveOutcome(r ?? new SaveRequest { Path = path ?? "" }, false, $"the game could not be written: {t.Exception?.InnerException?.Message ?? "the write stopped"}", null, null, false);

    SaveOutcome Run(SaveRequest r, long number)
    {
        bool overtaken = false, setupKept = false, madeCurrent = false;
        string? problem = null, folderProblem = null;
        Checkpoint? kept = null;
        if (r.Bytes != null)
        {
            try { overtaken = !Land(r.Path, r.Bytes, number); if (!overtaken) AfterLanding?.Invoke(number); }
            catch (Exception e) { problem = (r.Mirror ? "this PC's copy of the game" : "the game") + $" could not be written to {r.Path}: {e.Message}"; }
            if (problem == null && !overtaken)
                try
                {
                    if (r.SettlesLog) _generations.Settled(r.Path, r.LogGeneration, r.LiveLogIdle);
                    if (r.Changed) kept = r.Checkpoints?.AfterSave(r.Turns, r.LastTurn, r.ClockTurn, r.Now);
                    if (r.Closing) kept = r.Checkpoints?.OnClose(r.ClockTurn, r.Now) ?? kept;
                    if (r.MakesCurrent is var (scenarioDir, run)) { _store.SetCurrent(scenarioDir, run); madeCurrent = true; }
                }
                catch (Exception e) { problem = $"the game is written, but no copy of it could be kept in {_store.CheckpointDir(r.Path)}: {e.Message}"; }
        }
        if (r.Folder is { } f)
            try { Land(f.File, Json.SerializeBytes(f.Scenario), number); }
            catch (Exception e) { folderProblem = "the scenario folder could not be updated: " + e.Message; }
        if (r.Setup is { } s)
            try { _folders.KeepSetup(s.Store, s.Dir, s.First, s.Id, s.Name); setupKept = true; }
            catch (Exception e) { folderProblem ??= "the scenario's setup (what Start over brings back) could not be kept: " + e.Message; }
        return new SaveOutcome(r, overtaken, problem, folderProblem, kept, setupKept, madeCurrent);
    }

    bool Land(string path, byte[] bytes, long number)
    {
        var tmp = Json.WriteTemp(path, bytes, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            BeforeLanding?.Invoke(number);
            if (_landings.TryLand(path, number, () => Json.Land(tmp, Path.GetFullPath(path)))) return true;
            Json.Discard(tmp);
            return false;
        }
        catch { Json.Discard(tmp); throw; }
    }
}
