using System.Text;
using KTable.Core.Protocol;

namespace KTable.Core;

public sealed class SaveLog : IDisposable
{
    public static string PathFor(string savePath) => savePath + ".log";
    public static string OldPathFor(string savePath) => savePath + ".log.old";

    public static string StampOf(string created, string scenarioId) =>
        "#" + (created + "|" + scenarioId).Replace('\n', ' ').Replace('\r', ' ');

    readonly string _save;
    readonly string _stamp;
    readonly Action<Action>? _disk;
    readonly MemoryStream _pending = new();
    FileStream? _out;
    public SaveLogGenerations Generations { get; }
    volatile bool _failed, _closed;
    public bool Working => !_failed && !_closed;
    public string? Failure { get; private set; }

    void Fail(string what, Exception e) { _failed = true; Failure ??= $"{what} ({e.Message})"; }
    public int Count { get; private set; }
    long _bytes;
    public long Bytes => Interlocked.Read(ref _bytes);

    public SaveLog(string savePath, string stamp, SaveLogGenerations? generations = null, Action<Action>? disk = null)
    {
        _save = savePath;
        _stamp = stamp;
        _disk = disk;
        Generations = generations ?? new SaveLogGenerations();
        OnDisk(() => Open(reuse: true));
    }

    void OnDisk(Action work) { if (_disk == null) work(); else _disk(work); }

    byte[] TakePending()
    {
        lock (_pending)
        {
            var b = _pending.ToArray();
            _pending.SetLength(0);
            return b;
        }
    }

    void Open(bool reuse)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_save) ?? ".");
            var path = PathFor(_save);
            bool keep = reuse && File.Exists(path) && HeaderOf(path) == _stamp;
            bool midLine = keep && EndsMidLine(path);
            _out = new FileStream(path, keep ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 1 << 16);
            if (midLine) { _out.WriteByte((byte)'\n'); _out.Flush(true); }
            if (!keep)
            {
                _out.Write(Encoding.UTF8.GetBytes(_stamp));
                _out.WriteByte((byte)'\n');
                _out.Flush(true);
            }
            Interlocked.Add(ref _bytes, _out.Length);
        }
        catch (Exception e) { _out = null; Fail("it could not be opened", e); }
    }

    public void Append(Command c)
    {
        if (!Working) return;
        try
        {
            var line = Json.BigBytes(c);
            lock (_pending) { _pending.Write(line); _pending.WriteByte((byte)'\n'); }
            Count++;
            Interlocked.Add(ref _bytes, line.Length + 1);
        }
        catch (Exception e) { Fail("a change could not be added to it", e); }
    }

    public void Flush(bool toDisk = true)
    {
        if (!Working) return;
        var lines = TakePending();
        OnDisk(() =>
        {
            if (_failed || _out == null) return;
            try { _out.Write(lines); _out.Flush(toDisk); }
            catch (Exception e) { Fail("it could not be written to the disk", e); }
        });
    }

    public void Rotate()
    {
        if (!Working) return;
        var lines = TakePending();
        Count = 0;
        Interlocked.Exchange(ref _bytes, 0);
        Generations.CountSetAside(_save);
        OnDisk(() =>
        {
            if (_failed) return;
            try
            {
                if (_out != null) { _out.Write(lines); _out.Flush(true); _out.Dispose(); }
                _out = null;
                var live = PathFor(_save);
                var old = OldPathFor(_save);
                if (File.Exists(live))
                {
                    if (!File.Exists(old)) File.Move(live, old);
                    else { AppendInto(old, live, _stamp); File.Delete(live); }
                }
                Open(reuse: false);
            }
            catch (Exception e) { Fail("it could not be set aside for a whole save", e); }
        });
    }

    static void AppendInto(string oldPath, string livePath, string stamp)
    {
        var bytes = ReadAll(livePath);
        int body = Array.IndexOf(bytes, (byte)'\n') + 1;
        using var fs = new FileStream(oldPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        var head = new byte[(int)Math.Min(4096, fs.Length)];
        int n = fs.Read(head, 0, head.Length);
        int nl = Array.IndexOf(head, (byte)'\n', 0, n);
        if (nl < 0 || Encoding.UTF8.GetString(head, 0, nl) != stamp)
        {
            fs.SetLength(0);
            fs.Write(Encoding.UTF8.GetBytes(stamp));
            fs.WriteByte((byte)'\n');
        }
        else if (EndsMidLine(fs)) fs.WriteByte((byte)'\n');
        fs.Seek(0, SeekOrigin.End);
        if (body > 0 && body < bytes.Length) fs.Write(bytes, body, bytes.Length - body);
        if (bytes.Length > 0 && bytes[^1] != (byte)'\n') fs.WriteByte((byte)'\n');
        fs.Flush(true);
    }

    static bool EndsMidLine(FileStream fs)
    {
        if (fs.Length == 0) return false;
        fs.Seek(-1, SeekOrigin.End);
        bool mid = fs.ReadByte() != '\n';
        fs.Seek(0, SeekOrigin.End);
        return mid;
    }

    static bool EndsMidLine(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return EndsMidLine(fs);
    }

    public static string? Clear(string savePath)
    {
        string? problem = null;
        foreach (var path in new[] { PathFor(savePath), OldPathFor(savePath) })
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { File.Delete(path); break; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (attempt < 2) { Thread.Sleep(40 * (attempt + 1)); continue; }
                    problem ??= $"{Path.GetFileName(path)} could not be removed ({e.Message})";
                }
            }
        return problem;
    }

    public static List<Command> FollowingOn(IEnumerable<Command> logged, long lastSeq, out int skipped)
    {
        skipped = 0;
        var next = new List<Command>();
        foreach (var c in logged)
        {
            if (c.Seq <= lastSeq) continue;
            if (c.Seq != lastSeq + 1) { skipped++; continue; }
            next.Add(c);
            lastSeq = c.Seq;
        }
        return next;
    }

    public static List<Command> Read(string savePath, string stamp, out bool torn, out string? unreadable)
    {
        torn = false;
        unreadable = null;
        var all = new List<Command>();
        foreach (var path in new[] { OldPathFor(savePath), PathFor(savePath) })
        {
            if (!File.Exists(path)) continue;
            byte[] bytes;
            try { bytes = ReadRetried(path); }
            catch (FileNotFoundException) { continue; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                torn = false;
                unreadable = $"{Path.GetFileName(path)}: {e.Message}";
                return new List<Command>();
            }
            int from = 0;
            bool first = true, bad = false;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != (byte)'\n') continue;
                int len = i - from;
                if (first)
                {
                    first = false;
                    if (Encoding.UTF8.GetString(bytes, from, len) != stamp) goto nextFile;
                }
                else if (len > 0)
                {
                    Command? c = null;
                    try { c = Json.Deserialize<Command>(bytes.AsSpan(from, len)); }
                    catch { c = null; }
                    bad = c == null;
                    if (c != null)
                    {
                        while (all.Count > 0 && all[^1].Seq >= c.Seq) all.RemoveAt(all.Count - 1);
                        all.Add(c);
                    }
                }
                from = i + 1;
            }
            torn = bad || from < bytes.Length;
            nextFile: ;
        }
        return all;
    }

    static byte[] ReadRetried(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return ReadAll(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && e is not FileNotFoundException && attempt < 4)
            {
                Thread.Sleep(250 * (attempt + 1));
            }
        }
    }

    public static List<string> SetAside(string savePath, string reason, DateTime nowUtc)
    {
        var stamp = nowUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var moved = new List<(string from, string to)>();
        try
        {
            foreach (var from in new[] { PathFor(savePath), OldPathFor(savePath) })
            {
                if (!File.Exists(from)) continue;
                var to = $"{from}.{reason}-{stamp}";
                for (int n = 2; File.Exists(to); n++) to = $"{from}.{reason}-{stamp}-{n}";
                File.Move(from, to, false);
                moved.Add((from, to));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            foreach (var (from, to) in moved)
                try { File.Move(to, from, false); }
                catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                {
                    throw new IOException($"{e.Message}; and {Path.GetFileName(to)}, already moved, could not go back ({back.Message})", e);
                }
            throw;
        }
        return moved.Select(m => m.to).ToList();
    }

    static byte[] ReadAll(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[fs.Length];
        int got = 0;
        while (got < bytes.Length)
        {
            int n = fs.Read(bytes, got, bytes.Length - got);
            if (n <= 0) break;
            got += n;
        }
        return got == bytes.Length ? bytes : bytes[..got];
    }

    static string? HeaderOf(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[Math.Min(4096, (int)Math.Min(int.MaxValue, fs.Length))];
            int n = fs.Read(buf, 0, buf.Length);
            int nl = Array.IndexOf(buf, (byte)'\n', 0, n);
            return nl < 0 ? null : Encoding.UTF8.GetString(buf, 0, nl);
        }
        catch { return null; }
    }

    public void Dispose()
    {
        Flush();
        _closed = true;
        OnDisk(() =>
        {
            BestEffort.Try(() => _out?.Dispose());
            _out = null;
        });
    }
}

public sealed class SaveLogGenerations
{
    readonly Dictionary<string, long> _counts = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();

    static string KeyOf(string savePath)
    {
        try { return Path.GetFullPath(savePath); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return savePath; }
    }

    public long Of(string savePath)
    {
        lock (_gate) return _counts.GetValueOrDefault(KeyOf(savePath));
    }

    internal void CountSetAside(string savePath)
    {
        lock (_gate)
        {
            var key = KeyOf(savePath);
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
        }
    }

    public void Settled(string savePath, long generation, bool liveToo = false)
    {
        lock (_gate)
        {
            if (_counts.GetValueOrDefault(KeyOf(savePath)) != generation) return;
            BestEffort.DeleteFile(SaveLog.OldPathFor(savePath));
            if (liveToo) BestEffort.DeleteFile(SaveLog.PathFor(savePath));
        }
    }
}
