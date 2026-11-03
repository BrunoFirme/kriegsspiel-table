using KTable.Core.Protocol;

namespace KTable.Client;

public sealed partial class AssetTransfers
{
    sealed class Outbound(string package, string path, string abs, long total, long next, bool urgent) : IDisposable
    {
        FileStream? _file;
        public string Package { get; } = package;
        public string Path { get; } = path;
        public long Total { get; } = total;
        public long Next { get; set; } = next;
        public bool Urgent { get; set; } = urgent;
        public bool Done => Next >= Total;

        public byte[] ReadNext()
        {
            _file ??= new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var want = (int)Math.Min(ChunkSize, Total - Next);
            var data = new byte[want];
            _file.Seek(Next, SeekOrigin.Begin);
            _file.ReadExactly(data);
            return data;
        }

        public void Dispose() => _file?.Dispose();
    }

    sealed class Receiver(Pacer pacer)
    {
        public Pacer Pacer { get; } = pacer;
        public List<Outbound> Files { get; } = new();
        public Outbound? Next => Files.FirstOrDefault(f => f.Urgent) ?? Files.FirstOrDefault();
    }

    readonly Dictionary<long, Receiver> _receivers = new();

    internal int Pending(long peer) => _receivers.TryGetValue(peer, out var r) ? r.Files.Count : 0;

    static AssetChunk Missing(string package, string path) => new() { Package = package, Path = path, Total = -1 };

    public void Serve(long to, NeedAsset na)
    {
        if (Server?.Peer(to) == null || Shared(na.Package, na.Path) is not { } abs) { Send(to, Missing(na.Package, na.Path)); return; }
        if (!_receivers.TryGetValue(to, out var r)) _receivers[to] = r = new Receiver(new Pacer(() => Clock(), ChunkSize));
        if (r.Files.FirstOrDefault(f => f.Package == na.Package && f.Path == na.Path) is { } already) { already.Urgent |= na.Urgent; return; }
        var total = new FileInfo(abs).Length;
        if (total == 0) { Send(to, new AssetChunk { Package = na.Package, Path = na.Path, Total = 0 }); return; }
        r.Files.Add(new Outbound(na.Package, na.Path, abs, total, Math.Clamp(na.Offset, 0, total - 1), na.Urgent));
        PumpStreams();
    }

    string? Shared(string package, string path)
    {
        var root = Path.GetFullPath(Session.Store.Root);
        var abs = Path.GetFullPath(Path.Combine(root, package, path));
        var shared = UploadAsset.IsRelief(package, path.Replace('\\', '/')) || Server!.State.Packs.Concat(Server.State.Sheets.Select(s => s.Package)).Distinct()
            .Any(p => abs.StartsWith(Path.GetFullPath(Path.Combine(root, p)) + Path.DirectorySeparatorChar));
        return abs.StartsWith(root) && shared && File.Exists(abs) ? abs : null;
    }

    void PumpStreams()
    {
        for (var sent = true; sent;)
        {
            sent = false;
            foreach (var (peer, r) in _receivers)
                if (r.Next is { } f && r.Pacer.CanSend()) { SendNext(peer, r, f); sent = true; }
        }
    }

    void SendNext(long peer, Receiver r, Outbound f)
    {
        byte[] data;
        try { data = f.ReadNext(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"{f.Package}/{f.Path} could not be read for a player ({e.Message})");
            Finish(r, f);
            Send(peer, Missing(f.Package, f.Path));
            return;
        }
        var piece = new AssetChunk { Package = f.Package, Path = f.Path, Offset = f.Next, Total = f.Total, Data = data };
        r.Pacer.Sent(Key(f.Package, f.Path, f.Next), data.Length);
        f.Next += data.Length;
        if (f.Done) Finish(r, f);
        Send(peer, piece);
    }

    static void Finish(Receiver r, Outbound f)
    {
        r.Files.Remove(f);
        f.Dispose();
    }

    void StreamAcked(long from, ChunkAck ack)
    {
        if (!_receivers.TryGetValue(from, out var r)) return;
        r.Pacer.Acked(Key(ack.Package, ack.Path, ack.Offset));
        PumpStreams();
    }

    void ForgetStreamsTo(long peer)
    {
        if (!_receivers.Remove(peer, out var r)) return;
        foreach (var f in r.Files) f.Dispose();
    }

    void CloseStreams()
    {
        foreach (var peer in _receivers.Keys.ToList()) ForgetStreamsTo(peer);
    }
}
