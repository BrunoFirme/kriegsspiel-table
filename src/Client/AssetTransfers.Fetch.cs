using System.Collections.Concurrent;
using KTable.Core;
using KTable.Core.Protocol;

namespace KTable.Client;

public sealed partial class AssetTransfers
{
    const int UrgentAtOnce = 4;
    const int BackgroundAtOnce = 2;
    const int TriesPerFile = 2;

    sealed class Fetch(string package, string path, string abs, bool urgent)
    {
        public string Package { get; } = package;
        public string Path { get; } = path;
        public string Abs { get; } = abs;
        public string Part => Abs + ".part";
        public bool Urgent { get; set; } = urgent;
        public int Tries { get; set; }
        public FileStream? File { get; set; }
        public long Total { get; set; } = -1;
        public long Received { get; set; }
    }

    readonly List<Fetch> _waiting = new();
    readonly Dictionary<string, Fetch> _asked = new();
    readonly Dictionary<string, Fetch> _copying = new();
    readonly ConcurrentQueue<(Fetch Fetch, bool Copied)> _copied = new();

    internal int Asked => _asked.Count;
    internal int Waiting => _waiting.Count;

    public void QueueFetch(string pkg, string rel) => Want(pkg, rel, urgent: true);

    public void Prefetch(string pkg, string rel) => Want(pkg, rel, urgent: false);

    void Want(string pkg, string rel, bool urgent)
    {
        if (PackagePaths.Inside(Session.Store.Root, pkg, rel) == null)
        {
            Log.Warning($"the host named a file outside this PC's game files ({pkg}/{rel}): it is not fetched");
            FileMissing(pkg, rel);
            return;
        }
        var abs = Abs(pkg, rel);
        if (_copying.ContainsKey(abs)) return;
        if (_asked.TryGetValue(abs, out var asked))
        {
            if (urgent && !asked.Urgent) { asked.Urgent = true; Send(1, new NeedAsset { Package = pkg, Path = rel, Urgent = true }); }
            return;
        }
        if (_waiting.Find(f => f.Abs == abs) is { } waiting)
        {
            waiting.Urgent |= urgent;
            PumpFetches();
            return;
        }
        var fetch = new Fetch(pkg, rel, abs, urgent);
        if (Local?.Candidate(pkg, rel) is { } copy) { CopyIn(fetch, copy); return; }
        _waiting.Add(fetch);
        PumpFetches();
    }

    void CopyIn(Fetch fetch, string from)
    {
        _copying[fetch.Abs] = fetch;
        var local = Local!;
        Task.Run(() =>
        {
            var copied = false;
            try { copied = local.CopyTo(from, fetch.Abs); }
            finally { _copied.Enqueue((fetch, copied)); }
        });
    }

    void TakeLocalCopies()
    {
        while (_copied.TryDequeue(out var done))
        {
            if (!_copying.Remove(done.Fetch.Abs, out var fetch) || fetch != done.Fetch) continue;
            if (done.Copied) FileArrived(fetch.Package, fetch.Path);
            else _waiting.Add(fetch);
        }
        PumpFetches();
    }

    void PumpFetches()
    {
        while (NextToAsk() is { } f) Ask(f);
    }

    Fetch? NextToAsk()
    {
        var urgent = _asked.Values.Count(f => f.Urgent);
        var next = urgent < UrgentAtOnce ? _waiting.Find(f => f.Urgent) : null;
        if (next == null && _asked.Count - urgent < BackgroundAtOnce) next = _waiting.Find(f => !f.Urgent);
        if (next != null) _waiting.Remove(next);
        return next;
    }

    void Ask(Fetch f)
    {
        f.Tries++;
        f.Total = -1;
        f.Received = 0;
        _asked[f.Abs] = f;
        Send(1, new NeedAsset { Package = f.Package, Path = f.Path, Urgent = f.Urgent });
    }

    public void ReceiveChunk(AssetChunk c)
    {
        if (c.Data.Length > 0) Send(1, new ChunkAck { Package = c.Package, Path = c.Path, Offset = c.Offset });
        if (!_asked.TryGetValue(Abs(c.Package, c.Path), out var f)) return;
        if (c.Total < 0) { GiveUp(f, $"host has no {c.Package}/{c.Path}"); return; }
        try
        {
            if (f.File == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(f.Abs)!);
                f.File = new FileStream(f.Part, FileMode.Create);
            }
            f.Total = c.Total;
            f.File.Seek(c.Offset, SeekOrigin.Begin);
            f.File.Write(c.Data);
            f.Received += c.Data.Length;
            if (f.Received >= f.Total) Finish(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            GiveUp(f, $"{c.Package}/{c.Path} could not be written here ({e.Message})");
        }
    }

    void Finish(Fetch f)
    {
        f.File!.Dispose();
        f.File = null;
        if (FileHashes.IsHashNamed(f.Abs) && FileHashes.Sha256(f.Part) != Path.GetFileNameWithoutExtension(f.Abs))
        {
            BestEffort.DeleteFile(f.Part);
            if (f.Tries < TriesPerFile) { Ask(f); return; }
            GiveUp(f, $"{f.Package}/{f.Path} arrived damaged {f.Tries} times");
            return;
        }
        Json.Land(f.Part, f.Abs);
        _asked.Remove(f.Abs);
        FileArrived(f.Package, f.Path);
        PumpFetches();
    }

    void GiveUp(Fetch f, string why)
    {
        Log.Warning(why);
        Close(f);
        _asked.Remove(f.Abs);
        FileMissing(f.Package, f.Path);
        PumpFetches();
    }

    static void Close(Fetch f)
    {
        f.File?.Dispose();
        f.File = null;
        BestEffort.DeleteFile(f.Part);
    }

    void CloseFetches()
    {
        foreach (var f in _asked.Values) Close(f);
        _asked.Clear();
        _waiting.Clear();
        _copying.Clear();
    }
}
