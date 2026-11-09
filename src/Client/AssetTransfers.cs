using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client;

public sealed partial class AssetTransfers
{
    public const int ChunkSize = 16 * 1024;
    const long MaxUserPackBytes = 300L * 1024 * 1024;

    readonly Func<ClientSession> _session;
    readonly Func<GameServer?> _server;
    public Action<long, Message> Send = (_, _) => { };
    public Action<string, string> FileArrived = (_, _) => { };
    public Action<string, string> FileMissing = (_, _) => { };
    public Func<double> Clock = () => 0;
    public ClientLog Log = new();
    public LocalCopies? Local { get; set; }

    public AssetTransfers(Func<ClientSession> session, Func<GameServer?> server) { _session = session; _server = server; }

    GameServer? Server => _server();
    ClientSession Session => _session();

    string Abs(string package, string rel) => Path.Combine(Session.Store.Root, package, rel);

    static string Key(string package, string path, long offset) => $"{package}|{path}|{offset}";

    public void Reset()
    {
        CloseFetches();
        CloseStreams();
        _uploadQueue.Clear();
        _uploadPacer = null;
        _uploadedBy.Clear();
        foreach (var key in _uploads.Keys.ToList()) DropUpload(key);
    }

    public void PeerLeft(long peerId)
    {
        ForgetStreamsTo(peerId);
        foreach (var key in _uploads.Keys.Where(k => k.StartsWith(peerId + "|", StringComparison.Ordinal)).ToList()) DropUpload(key);
    }

    public void Pump()
    {
        TakeLocalCopies();
        PumpStreams();
        PumpUploads();
    }

    public void OnAck(long from, ChunkAck ack)
    {
        if (Server != null) StreamAcked(from, ack);
        else UploadAcked(ack);
    }

    void DropUpload(string key)
    {
        if (!_uploads.Remove(key, out var fs)) return;
        var part = fs.Name;
        try { fs.Dispose(); File.Delete(part); }
        catch (Exception e) { Log.Warning($"an unfinished upload could not be cleared away ({part}): {e.Message}"); }
    }

    readonly Queue<UploadAsset> _uploadQueue = new();
    Pacer? _uploadPacer;
    Pacer UploadPacer => _uploadPacer ??= new Pacer(() => Clock(), ChunkSize);

    public void Upload(string pkg, string rel)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Session.Store.PackageDir(pkg), rel));
        for (long off = 0; off < bytes.Length || off == 0; off += ChunkSize)
        {
            var len = (int)Math.Min(ChunkSize, bytes.Length - off);
            _uploadQueue.Enqueue(new UploadAsset { Package = pkg, Path = rel, Offset = off, Total = bytes.Length, Data = bytes.AsSpan((int)off, len).ToArray() });
            if (bytes.Length == 0) break;
        }
        PumpUploads();
    }

    void PumpUploads()
    {
        while (_uploadQueue.Count > 0 && UploadPacer.CanSend())
        {
            var u = _uploadQueue.Dequeue();
            UploadPacer.Sent(Key(u.Package, u.Path, u.Offset), u.Data.Length);
            Send(1, u);
        }
    }

    void UploadAcked(ChunkAck ack)
    {
        UploadPacer.Acked(Key(ack.Package, ack.Path, ack.Offset));
        PumpUploads();
    }

    internal int UploadsWaiting => _uploadQueue.Count;
    public bool UploadsDone => _uploadQueue.Count == 0 && (_uploadPacer?.InFlight ?? 0) == 0;

    readonly Dictionary<string, FileStream> _uploads = new();
    internal int OpenUploads => _uploads.Count;

    public void ReceiveUpload(long from, UploadAsset u)
    {
        var peer = Server!.Peer(from);
        if (peer == null || !Visibility.IsUmpire(peer.Role) || !UploadAsset.PathAllowed(u.Package, u.Path) || u.Total <= 0 || u.Total > UploadAsset.MaxFileBytes) return;
        var dir = Session.Store.PackageDir(u.Package);
        var dst = Path.Combine(dir, u.Path.Replace('/', Path.DirectorySeparatorChar));
        var key = $"{from}|{u.Package}|{u.Path}";
        Send(from, new ChunkAck { Package = u.Package, Path = u.Path, Offset = u.Offset });
        if (!_uploads.TryGetValue(key, out var fs))
        {
            if (u.Offset != 0) return;
            if (UploadRefused(from, u, dir) is { } refused)
            {
                Send(from, new ErrorMessage { Text = refused });
                return;
            }
            _uploadedBy[from] = _uploadedBy.GetValueOrDefault(from) + u.Total;
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            _uploads[key] = fs = new FileStream(dst + ".part" + from, FileMode.Create);
        }
        if (fs.Position != u.Offset) { DropUpload(key); return; }
        fs.Write(u.Data);
        if (fs.Position < u.Total) return;
        var tmp = fs.Name;
        fs.Dispose();
        _uploads.Remove(key);
        try
        {
            if (u.Path == "pack.json") CheckUploadedPack(peer, u.Package, dir, tmp);
            else if (FileHashes.Sha256(tmp) != Path.GetFileNameWithoutExtension(dst)) throw new InvalidDataException("hash mismatch");
            File.Move(tmp, dst, true);
            if (u.Package == UploadAsset.ReliefPackage) FileArrived(u.Package, u.Path);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            BestEffort.DeleteFile(tmp);
            Send(from, new ErrorMessage { Text = $"upload of {u.Package}/{u.Path} refused: {e.Message}" });
        }
    }

    public const long MaxUploadPerPeer = 2L * 1024 * 1024 * 1024;
    readonly Dictionary<long, long> _uploadedBy = new();

    string? UploadRefused(long from, UploadAsset u, string dir)
    {
        if (_uploadedBy.GetValueOrDefault(from) + u.Total > MaxUploadPerPeer) return $"upload of {u.Package}/{u.Path} refused: more than {MaxUploadPerPeer >> 30} GB sent to this host at one sitting";
        if (u.Package != UploadAsset.ReliefPackage && Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*",
            SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) + u.Total > MaxUserPackBytes)
            return $"pack {u.Package} is too large for this host";
        try { ContentLimits.CheckFreeSpace(Session.Store.Root, u.Total); }
        catch (IOException e) { return $"upload of {u.Package}/{u.Path} refused: {e.Message}"; }
        return null;
    }

    static void CheckUploadedPack(PeerInfo peer, string package, string dir, string tmp)
    {
        var pack = Json.Load<PackManifest>(tmp);
        if (pack.Id != package) throw new InvalidDataException("pack id does not match");
        if (pack.Assets.Values.Any(a => !UploadAsset.PathAllowed(package, a.File.Replace(Path.DirectorySeparatorChar, '/')))) throw new InvalidDataException("pack lists files it may not");
        var existing = Path.Combine(dir, "pack.json");
        if (!File.Exists(existing) || FileHashes.Sha256(existing) == FileHashes.Sha256(tmp)) return;
        var owner = Json.Load<PackManifest>(existing).Source.Authors.FirstOrDefault() ?? "";
        if (owner.Length == 0) throw new InvalidDataException($"{package} is already on this host and names no author, so it stays as it is");
        if (!string.Equals(owner, peer.Name, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{package} belongs to {owner}");
    }
}
