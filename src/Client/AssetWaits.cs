namespace KTable.Client;

public sealed class AssetWaits<TTexture>
{
    public Action<string, string>? RequestRemote;
    public Action<string, string>? RequestInBackground;

    readonly Dictionary<string, List<(Action ready, Action? missing)>> _waitingFiles = new();
    readonly HashSet<string> _missing = new();
    readonly Dictionary<string, List<Action<TTexture>>> _waitingTex = new();
    readonly List<string> _jobs = new();

    public int PendingTextures => _waitingTex.Count;
    public int PendingFiles => _waitingFiles.Count;
    public bool Busy => _waitingTex.Count > 0 || _waitingFiles.Count > 0 || _jobs.Count > 0;
    public bool Shows => _waitingTex.Count > _quiet.Count || _waitingFiles.Count > 0 || _jobs.Count > 0;
    readonly HashSet<string> _quiet = new();
    public IReadOnlyList<string> Jobs => _jobs;
    public void BeginJob(string name) { lock (_jobs) { _jobs.Add(name); JobProgress = -1; } }
    public void EndJob(string name) { lock (_jobs) { _jobs.Remove(name); JobProgress = -1; } }
    public double JobProgress { get; set; } = -1;
    public int BatchDone { get; private set; }
    public int BatchTotal { get; private set; }
    void Started()
    {
        if (!Shows && BatchDone >= BatchTotal) { BatchDone = 0; BatchTotal = 0; } BatchTotal++;
    }
    void Finished() { BatchDone++; }

    public void EnsureFile(string package, string rel, string abs, Action onReady, Action? onMissing = null)
    {
        if (File.Exists(abs)) { onReady(); return; }
        if (RequestRemote == null || _missing.Contains(abs))
        {
            _missing.Add(abs);
            onMissing?.Invoke();
            return;
        }
        if (_waitingFiles.TryGetValue(abs, out var l))
        {
            l.Add((onReady, onMissing));
            return;
        }
        Started();
        _waitingFiles[abs] = new() { (onReady, onMissing) };
        RequestRemote(package, rel);
    }

    public void Prefetch(string package, string rel, string abs)
    {
        if (RequestInBackground == null || _missing.Contains(abs) || _waitingFiles.ContainsKey(abs) || File.Exists(abs)) return;
        RequestInBackground(package, rel);
    }

    public bool KnownMissing(string abs) => _missing.Contains(abs) && !File.Exists(abs);

    public void FileArrived(string abs)
    {
        _missing.Remove(abs);
        if (_waitingFiles.Remove(abs, out var l)) { Finished(); foreach (var a in l) a.ready(); }
    }

    public void FileMissing(string abs)
    {
        _missing.Add(abs);
        if (_waitingFiles.Remove(abs, out var l)) { Finished(); foreach (var a in l) a.missing?.Invoke(); }
    }

    public bool StartTexture(string key, Action<TTexture>? onReady, bool quiet = false)
    {
        if (_waitingTex.TryGetValue(key, out var waiting))
        {
            if (onReady != null) waiting.Add(onReady);
            if (!quiet && _quiet.Remove(key)) Started();
            return false;
        }
        if (quiet) _quiet.Add(key);
        else Started();
        _waitingTex[key] = onReady != null ? new() { onReady } : new();
        return true;
    }

    public List<Action<TTexture>> TextureDone(string key)
    {
        if (_waitingTex.Remove(key, out var cbs)) { if (!_quiet.Remove(key)) Finished(); }
        else cbs = new();
        return cbs;
    }
}
