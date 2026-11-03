using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Client.Hand;
using KTable.Core;
using Json = KTable.Core.Json;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.App;

public partial class Net : Node
{
    public const int ChunkSize = AssetTransfers.ChunkSize;
    public GameServer? Server { get; private set; }
    public Session Session { get; private set; } = null!;
    public string Status { get; private set; } = "";
    public event Action<string>? StatusChanged;
    public event Action? Joined;
    public event Action? AtTheDoor;
    public event Action<string>? TurnedAway;
    public event Action<string?>? SaveProblem { add => _saves.SaveProblem += value; remove => _saves.SaveProblem -= value; }
    public event Action<string>? SaveNote { add => _saves.SaveNote += value; remove => _saves.SaveNote -= value; }
    public string? LastSaveProblem => _saves.LastSaveProblem;
    public string? MirrorPath => _saves.MirrorPath;
    public string? HeldFolder { get => _saves.HeldFolder; set => _saves.HeldFolder = value; }
    internal HostSaves Saves => _saves;
    internal Action? RequestPicture;
    internal Func<double, byte[]?>? PictureSince;
    string _address = "";

    ENetMultiplayerPeer? _peer;
    MultiplayerApi.PeerConnectedEventHandler? _onPeerIn;
    MultiplayerApi.PeerDisconnectedEventHandler? _onPeerOut;
    Action? _onConnected;
    Action? _onFailed;
    Action? _onServerGone;
    string _name = "", _password = "";
    string? _token;
    int _port;
    public event Action<string>? Dropped;
    bool _dropped;

    void Drop(string why)
    {
        if (_dropped) return;
        _dropped = true;
        Dropped?.Invoke(why);
    }
    readonly HostSaves _saves;
    readonly AssetTransfers _transfers;

    public static string AppVersion => ProjectSettings.GetSetting("application/config/version", "").AsString();

    void SetStatus(string s) { Status = s; StatusChanged?.Invoke(s); GD.Print("[net] " + s); }

    public Net()
    {
        var log = new ClientLog { Print = m => GD.Print(m), Warning = m => GD.PushWarning(m), Error = m => GD.PushError(m), Tell = Problems.Tell };
        _saves = new HostSaves(() => Session, () => Server, new ScenarioFolders(), SaveStore.Default, new SaveServices
        {
            Dispatch = Dispatch, Clock = () => Time.GetTicksMsec() / 1000.0, Quitting = () => Assets.Quitting, Log = log,
            PostWriteBackDone = error => CallDeferred(nameof(WriteBackDone), error), Post = a => Callable.From(a).CallDeferred(),
            RequestPicture = () => RequestPicture?.Invoke(), PictureSince = t => PictureSince?.Invoke(t),
            MirrorOf = () => $"{Session.Peers.FirstOrDefault(p => p.PeerId == 1)?.Name ?? "the host"} at {_address}",
        });
        _transfers = new AssetTransfers(() => Session, () => Server)
        {
            Send = SendRaw, FileArrived = (pkg, rel) => Session.Assets.FileArrived(pkg, rel), FileMissing = (pkg, rel) => Session.Assets.FileMissing(pkg, rel), Clock = () => Time.GetTicksMsec() / 1000.0, Log = log,
        };
    }

    public void Init(Session s)
    {
        Session = s;
        Session.Send = SendCommand;
        Session.Request = m => { if (Server != null) Dispatch(Server.Request(1, m)); else SendRaw(1, m); };
        Session.Desynced += AskForTheTable;
    }

    double _askedForTable = -10;

    void AskForTheTable()
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _askedForTable < 3) return;
        _askedForTable = now;
        if (Server != null) Callable.From(() => { if (Server != null) Dispatch(Server.Resync(1)); }).CallDeferred();
        else if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer) SendRaw(1, new GetSnapshot());
    }

    public Error Host(GameServer server, int port, string name, string savePath, List<Outgoing>? started = null)
    {
        Server = server;
        server.AppVersion = AppVersion;
        server.Scripts.FetchPicture = new WebPictures(Session.Store.Root).Fetch;
        if (Session.Store.RepointLostTiles(server.State.Sheets) is var moved and > 0) GD.Print($"[sheets] {moved} map sheet(s) found their tiles in another folder");
        KTable.Core.WireTap.HostState = () => Server?.State;
        Session.SavePath = savePath;
        _saves.BeginHosting(savePath, server.Turns.Count);
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateServer(port, 48);
        if (err != Error.Ok) { SetStatus($"cannot listen on {port}: {err}"); return err; }
        if (Multiplayer is SceneMultiplayer sm) sm.ServerRelay = false;
        Multiplayer.MultiplayerPeer = _peer;
        Multiplayer.PeerConnected += _onPeerIn = id => SetStatus($"peer {id} connected");
        Multiplayer.PeerDisconnected += _onPeerOut = id => { _transfers.PeerLeft(id); if (Server != null) Dispatch(Server.Leave(id)); SetStatus($"peer {id} left"); };
        Session.SetName(name);
        if (started != null) Dispatch(started);
        else
        {
            var loadClock = System.Diagnostics.Stopwatch.StartNew();
            Dispatch(Server.Scripts.Load());
            if (loadClock.ElapsedMilliseconds > 100) GD.Print($"[scripts] {Server.Scripts.Loaded} script(s) started in {loadClock.ElapsedMilliseconds} ms");
        }
        Dispatch(Server.Join(1, new JoinRequest { Name = name }));
        SetStatus($"hosting on port {port}");
        _cfgPort = port;
        TryUpnp(port);
        return Error.Ok;
    }

    public string UpnpStatus { get; private set; } = "not tried";
    public string? ExternalAddress { get; private set; }

    const int UpnpLeaseSeconds = 4 * 3600;
    static readonly TimeSpan UpnpRenewEvery = TimeSpan.FromHours(1);
    readonly object _upnpGate = new();
    Upnp? _upnp;
    int _upnpPort, _upnpSitting;
    System.Threading.Timer? _upnpRenew;

    void TryUpnp(int port)
    {
        UpnpStatus = "trying";
        if (KTable.Core.WireTap.Listener != null) { CallDeferred(nameof(UpnpDone), "no gateway", ""); return; }
        int sitting;
        lock (_upnpGate) sitting = _upnpSitting;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var u = new Upnp();
                if (u.Discover(2000) != 0 || u.GetGateway() == null || !u.GetGateway().IsValidGateway()) { if (!Assets.Quitting) CallDeferred(nameof(UpnpDone), "no gateway", ""); return; }
                var ip = u.QueryExternalAddress();
                var r = MapPort(u, port);
                if (r == 0)
                    lock (_upnpGate)
                    {
                        if (sitting != _upnpSitting) { ReleasePortOrLetLeaseRunOut(u, port); return; }
                        _upnp = u; _upnpPort = port;
                        _upnpRenew = new System.Threading.Timer(_ => RenewUpnp(sitting), null, UpnpRenewEvery, UpnpRenewEvery);
                    }
                TellUpnp(r == 0 ? "ok" : "failed", ip ?? "");
            }
            catch { TellUpnp("failed", ""); }
        });
    }

    void TellUpnp(string result, string ip)
    {
        if (Assets.Quitting || !IsInstanceValid(this)) return;
        BestEffort.Try(() => CallDeferred(nameof(UpnpDone), result, ip));
    }

    static void ReleasePortOrLetLeaseRunOut(Upnp u, int port) => BestEffort.Try(() => u.DeletePortMapping(port, "UDP"));

    static int MapPort(Upnp u, int port)
    {
        var r = u.AddPortMapping(port, port, "KriegsspielTable", "UDP", UpnpLeaseSeconds);
        return r == 0 ? 0 : u.AddPortMapping(port, port, "KriegsspielTable", "UDP");
    }

    void RenewUpnp(int sitting)
    {
        Upnp? u; int port;
        lock (_upnpGate) { if (sitting != _upnpSitting || _upnp == null) return; u = _upnp; port = _upnpPort; }
        BestEffort.Try(() => MapPort(u, port));
        bool ended;
        lock (_upnpGate) ended = sitting != _upnpSitting;
        if (ended) ReleasePortOrLetLeaseRunOut(u, port);
    }

    void CloseUpnp()
    {
        Upnp? u; int port;
        lock (_upnpGate)
        {
            _upnpSitting++;
            _upnpRenew?.Dispose(); _upnpRenew = null;
            u = _upnp; port = _upnpPort; _upnp = null;
        }
        if (u == null) return;
        var close = System.Threading.Tasks.Task.Run(() => ReleasePortOrLetLeaseRunOut(u, port));
        if (!close.Wait(TimeSpan.FromSeconds(2))) GD.Print($"[net] the router had not closed port {port} after two seconds; its lease ends it within a few hours");
    }

    void UpnpDone(string status, string ip)
    {
        UpnpStatus = status;
        if (ip.Length > 0) ExternalAddress = ip;
        if (status == "ok") SetStatus($"hosting on {ip}:{_cfgPort} · port open");
        else SetStatus($"hosting on port {_cfgPort} · LAN only, see Invite");
    }
    int _cfgPort;

    public static List<string> LanAddresses() =>
        IP.GetLocalAddresses().Select(a => a.ToString()).Where(a => a.StartsWith("192.168.") || a.StartsWith("10.") || System.Text.RegularExpressions.Regex.IsMatch(a, @"^172\.(1[6-9]|2\d|3[01])\.")).Distinct().ToList();

    void SetStatusDeferred(string s) => SetStatus(s);

    public Error Join(string address, int port, string name, string password)
    {
        Shutdown();
        _name = name; _password = password; _address = address; _port = port; _dropped = false;
        _token = Joins.Remembered(address, port);
        Session.SetName(name);
        _peer = new ENetMultiplayerPeer();
        var err = _peer.CreateClient(address, port);
        if (err != Error.Ok) { SetStatus($"cannot connect: {err}"); return err; }
        Multiplayer.MultiplayerPeer = _peer;
        Multiplayer.ConnectedToServer += _onConnected = () =>
        {
            SetStatus(_token != null ? "connected, coming back in…" : "connected, knocking…");
            SendRaw(1, new JoinRequest { Name = _name, Password = _password, Token = _token, AppVersion = AppVersion, Protocol = ProtocolVersion.Current });
        };
        Multiplayer.ConnectionFailed += _onFailed = () =>
        {
            SetStatus("connection failed");
            TurnedAway?.Invoke($"No answer from {address}:{port}.");
        };
        Multiplayer.ServerDisconnected += _onServerGone = () =>
        {
            SetStatus("disconnected from host");
            if (Session.Me.PeerId == 0) { TurnedAway?.Invoke("The host closed the connection."); return; }
            var why = "the connection to the host was lost (the host closed the table, or the network dropped). Join again once the host is back."
                    + (MirrorPath != null ? " This PC's copy of the game is kept: Host a game, then Mirrored games, continues it if the host cannot come back." : "");
            Callable.From(() => Drop(why)).CallDeferred();
        };
        Session.Assets.RequestRemote = _transfers.QueueFetch;
        Session.Assets.RequestInBackground = _transfers.Prefetch;
        _transfers.Local = InstalledCopies();
        _transfers.Local?.Warm();
        _prefetched.Clear();
        SetStatus($"connecting to {address}:{port}…");
        return Error.Ok;
    }

    LocalCopies? InstalledCopies()
    {
        var installed = Path.GetFullPath(PackageStore.DefaultRoot);
        var here = Path.GetFullPath(Session.Store.Root);
        return string.Equals(installed.TrimEnd(Path.DirectorySeparatorChar), here.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            ? null : new LocalCopies(installed, here);
    }

    const double PrefetchEverySeconds = 2;
    double _prefetchedAt;
    readonly HashSet<string> _prefetched = new();

    void PrefetchWhatIsOnTheTable()
    {
        var now = Time.GetTicksMsec() / 1000.0;
        if (now - _prefetchedAt < PrefetchEverySeconds) return;
        _prefetchedAt = now;
        foreach (var (package, rel) in PieceFiles.Of(Session.View, Session.Store))
            if (_prefetched.Add(package + "/" + rel)) Session.Assets.Prefetch(package, rel);
    }

    public void Shutdown()
    {
        if (_onPeerIn != null) Multiplayer.PeerConnected -= _onPeerIn;
        if (_onPeerOut != null) Multiplayer.PeerDisconnected -= _onPeerOut;
        if (_onConnected != null) Multiplayer.ConnectedToServer -= _onConnected;
        if (_onFailed != null) Multiplayer.ConnectionFailed -= _onFailed;
        if (_onServerGone != null) Multiplayer.ServerDisconnected -= _onServerGone;
        _onPeerIn = null; _onPeerOut = null; _onConnected = null; _onFailed = null; _onServerGone = null;
        _transfers.Reset();
        _transfers.Local = null;
        _saves.CloseLog();
        if (_peer is { } closing) { BestEffort.Try(() => closing.Close()); _peer = null; }
        if (Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer) Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        Server = null;
        _saves.Stop();
        CloseUpnp();
    }

    public override void _ExitTree() => Shutdown();

    void SendCommand(Command c)
    {
        if (Server != null)
        {
            KTable.Core.WireTap.Note("cmd", 1, c);
            Dispatch(Server.Handle(1, c));
        }
        else SendRaw(1, new CommandMessage { Command = c });
    }

    void SendRaw(long peerId, Message m)
    {
        if (PieceNode.DebugDice && m is not FilePiece) GD.Print($"[net {System.DateTime.Now:HH:mm:ss.fff}] send {m.GetType().Name} to {peerId}{(m is CommandMessage cm1 ? " " + cm1.Command.GetType().Name : "")}");
        KTable.Core.WireTap.Note("send", peerId, m);
        if (m is FilePiece piece) { RpcId(peerId, MethodName.RecvBulk, BulkFrame.Encode(piece)); return; }
        RpcId(peerId, MethodName.Recv, Framing.ForWire(Json.SerializeBytes<Message>(m)));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 1)]
    void RecvBulk(byte[] data)
    {
        if (Server == null && Multiplayer.GetRemoteSenderId() != 1) return;
        FilePiece piece;
        try { piece = BulkFrame.Decode(data); }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or NotSupportedException)
        {
            GD.PushError("bad file piece: " + e.Message);
            return;
        }
        Route(piece);
    }

    void Dispatch(List<Outgoing> outs)
    {
        foreach (var o in outs)
        {
            if (o.PeerId != 1) { SendRaw(o.PeerId, o.Message); continue; }
            KTable.Core.WireTap.Note("local", 1, o.Message);
            try { Deliver(o.Message); }
            catch (Exception e) { GD.PushError($"the host's table could not take {o.Message.GetType().Name}: {e}"); }
        }
        if (Server != null)
        {
            List<Outgoing> said;
            try { said = Server.ConsoleOut(); } catch (Exception e) { GD.PushError("[scripts] console: " + e.Message); return; }
            if (said.Count > 0) Dispatch(said);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void Recv(byte[] data)
    {
        if (Server == null && Multiplayer.GetRemoteSenderId() != 1) return;
        var stranger = Server != null && Server.Peer(Multiplayer.GetRemoteSenderId()) == null;
        Message m;
        try { m = Json.Deserialize<Message>(Framing.Unpack(data, stranger ? Framing.MaxFromStranger : Framing.MaxUnpacked)); }
        catch (Exception e)
        {
            GD.PushError("bad message: " + e.Message);
            if (Server == null) AskForTheTable();
            return;
        }
        Route(m);
    }

    void Route(Message m)
    {
        var stranger = Server != null && Server.Peer(Multiplayer.GetRemoteSenderId()) == null;
        if (stranger && m is not JoinRequest) return;
        KTable.Core.WireTap.Note("recv", Multiplayer.GetRemoteSenderId(), m);

        if (Server != null)
        {
            var from = Multiplayer.GetRemoteSenderId();
            switch (m)
            {
                case JoinRequest j: Dispatch(Server.Join(from, j, _peer?.GetPeer((int)from)?.GetRemoteAddress())); break;
                case Admit or Refuse or SetRole or Revoke or SetPassword: Dispatch(Server.Control(from, m)); break;
                case CommandMessage cm: Dispatch(Server.Handle(from, cm.Command)); break;
                case NeedAsset na: _transfers.Serve(from, na); break;
                case UploadAsset ua: _transfers.ReceiveUpload(from, ua); break;
                case ChunkAck ack: _transfers.OnAck(from, ack); break;
                case GetTurns: Dispatch(Server.TurnHistory(from)); break;
                case GetSnapshot: Dispatch(Server.Resync(from)); break;
                case GetTurn gt: Dispatch(Server.TurnSnapshot(from, gt.Index)); break;
                case RunLua or GetScripts: Dispatch(Server.Request(from, m)); break;
            }
            return;
        }
        Deliver(m);
    }

    void Deliver(Message m)
    {
        if (PieceNode.DebugDice && m is not FilePiece) GD.Print($"[net {System.DateTime.Now:HH:mm:ss.fff}] recv {m.GetType().Name}{(m is CommandMessage cm0 ? " " + cm0.Command.GetType().Name + " seq " + cm0.Command.Seq : "")}");
        switch (m)
        {
            case JoinResult jr:
                if (jr.Waiting) { SetStatus("Waiting for an umpire to let you in…"); AtTheDoor?.Invoke(); return; }
                if (!jr.Ok)
                {
                    SetStatus("not let in: " + jr.Error);
                    if (Session.Me.PeerId != 0) Drop(jr.Error);
                    else TurnedAway?.Invoke("Not let in: " + jr.Error);
                    return;
                }
                Session.OnJoined(jr);
                if (jr.Token != null) Joins.Remember(_address, _port, jr.Token);
                SetStatus(Server != null ? Status : $"joined as {Fmt.Role(jr.Role)}");
                if (Server == null && Session.IsUmpire) _saves.StartMirror(jr);
                Joined?.Invoke();
                break;
            case Knock k: Session.OnKnock(k); break;
            case RoleChanged rc:
                if (Server == null && MirrorPath != null && !Visibility.IsUmpire(rc.Role)) _saves.StopMirror();
                Session.OnRoleChanged(rc);
                SetStatus($"you are now {Fmt.Role(rc.Role)}");
                if (Server == null && Session.IsUmpire && MirrorPath == null) _saves.StartMirror(new JoinResult { Created = Session.Created });
                break;
            case AccessChanged ac: Session.OnAccess(ac.Access); break;
            case CommandMessage cm: Session.OnCommand(cm.Command); break;
            case Snapshot s: Session.SetView(s.View); break;
            case PeerList pl: Session.OnPeers(pl); break;
            case ErrorMessage e: Session.OnError(e.Text); break;
            case AssetChunk ac: _transfers.ReceiveChunk(ac); break;
            case TurnList tl: Session.OnTurns(tl); break;
            case ChunkAck ack: _transfers.OnAck(1, ack); break;
            case TurnState ts: Session.Review(ts.State, ts.Info); break;
            case ScriptConsole or ScriptList: Session.OnScriptMessage(m); break;
        }
    }

    public void Door(Message m) { if (Server != null) Dispatch(Server.Control(1, m)); else SendRaw(1, m); }

    public void RequestTurns() { if (Server != null) Dispatch(Server.TurnHistory(1)); else SendRaw(1, new GetTurns()); }
    public void RequestTurn(int index) { if (Server != null) Dispatch(Server.TurnSnapshot(1, index)); else SendRaw(1, new GetTurn { Index = index }); }

    public event Action<long, string, Vec2>? PointerReceived;

    public void SendPointer(Vec2 p)
    {
        if (Server != null)
        {
            foreach (var peer in Server.Peers)
                if (Visibility.IsUmpire(peer.Role) && peer.PeerId != 1) RpcId(peer.PeerId, nameof(PointerDown), 1L, p.X, p.Z);
        }
        else if (Multiplayer.MultiplayerPeer != null && Session.IsUmpire)
            RpcId(1, nameof(PointerUp), p.X, p.Z);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void PointerUp(double x, double z)
    {
        if (Server == null) return;
        var from = Multiplayer.GetRemoteSenderId();
        var peer = Server.Peer(from);
        if (peer == null || !Visibility.IsUmpire(peer.Role)) return;
        foreach (var other in Server.Peers)
            if (Visibility.IsUmpire(other.Role) && other.PeerId != 1 && other.PeerId != from) RpcId(other.PeerId, nameof(PointerDown), (long)from, x, z);
        PointerReceived?.Invoke(from, peer.Name, new Vec2(x, z));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void PointerDown(long peerId, double x, double z)
    {
        if (Multiplayer.GetRemoteSenderId() != 1 || !Session.IsUmpire) return;
        var name = Session.Peers.FirstOrDefault(p => p.PeerId == peerId)?.Name ?? (peerId == 1 ? "host" : $"umpire {peerId}");
        PointerReceived?.Invoke(peerId, name, new Vec2(x, z));
    }

    public event Action<long, IReadOnlyList<CarriedPiece>, HandAt, double>? CarryLayoutReceived;
    public event Action<long, HandAt, double>? CarryPlaceReceived;
    public event Action<long, double, IReadOnlyList<MotionSample>>? FlightReceived;

    bool AtATable => Multiplayer.MultiplayerPeer is ENetMultiplayerPeer && Session.Me.PeerId != 0;

    public void ShareCarryLayout(IReadOnlyList<CarriedPiece> pieces, HandAt at, double t)
    {
        if (Server != null) { RelayCarryLayout(1, pieces, at, t); return; }
        if (!AtATable) return;
        var (layers, ids, poses) = CarryWire.Pack(pieces);
        RpcId(1, nameof(CarryLayoutUp), t, layers, ids, poses, at.X, at.Z, at.Y);
    }

    public void ShareCarryPlace(HandAt at, double t)
    {
        if (Server != null) { RelayCarryPlace(1, at, t); return; }
        if (AtATable) RpcId(1, nameof(CarryPlaceUp), t, at.X, at.Z, at.Y);
    }

    public void ShareFlight(double t, IReadOnlyList<MotionSample> samples)
    {
        if (Server != null) { RelayFlight(1, t, samples); return; }
        if (!AtATable) return;
        var (layers, ids, poses) = CarryWire.PackSamples(samples);
        RpcId(1, nameof(FlightUp), t, layers, ids, poses);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void CarryLayoutUp(double t, string[] layers, string[] ids, float[] poses, float x, float z, float y)
    {
        var at = new HandAt(x, z, y);
        if (Server == null || !double.IsFinite(t) || CarryWire.Unpack(layers, ids, poses) is not { } pieces || !CarryWire.IsFinite(at)) return;
        RelayCarryLayout(Multiplayer.GetRemoteSenderId(), pieces, at, t);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void CarryPlaceUp(double t, float x, float z, float y)
    {
        var at = new HandAt(x, z, y);
        if (Server == null || !double.IsFinite(t) || !CarryWire.IsFinite(at)) return;
        RelayCarryPlace(Multiplayer.GetRemoteSenderId(), at, t);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void FlightUp(double t, string[] layers, string[] ids, float[] poses)
    {
        if (Server == null || !double.IsFinite(t) || CarryWire.UnpackSamples(layers, ids, poses) is not { } samples) return;
        RelayFlight(Multiplayer.GetRemoteSenderId(), t, samples);
    }

    void RelayCarryLayout(long from, IReadOnlyList<CarriedPiece> pieces, HandAt at, double t)
    {
        foreach (var (to, seen) in Server!.CarryLayout(from, pieces))
        {
            if (to == 1) { CarryLayoutReceived?.Invoke(from, seen, at, t); continue; }
            var (layers, ids, poses) = CarryWire.Pack(seen);
            RpcId(to, nameof(CarryLayoutDown), from, t, layers, ids, poses, at.X, at.Z, at.Y);
        }
    }

    void RelayCarryPlace(long from, HandAt at, double t)
    {
        foreach (var to in Server!.CarryWatchers(from))
            if (to == 1) CarryPlaceReceived?.Invoke(from, at, t);
            else RpcId(to, nameof(CarryPlaceDown), from, t, at.X, at.Z, at.Y);
    }

    void RelayFlight(long from, double t, IReadOnlyList<MotionSample> samples)
    {
        foreach (var (to, seen) in Server!.MotionSeenBy(from, samples, s => (s.Piece.Layer, s.Piece.Id)))
        {
            if (to == 1) { FlightReceived?.Invoke(from, t, seen); continue; }
            var (layers, ids, poses) = CarryWire.PackSamples(seen);
            RpcId(to, nameof(FlightDown), from, t, layers, ids, poses);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void CarryLayoutDown(long carrier, double t, string[] layers, string[] ids, float[] poses, float x, float z, float y)
    {
        var at = new HandAt(x, z, y);
        if (Multiplayer.GetRemoteSenderId() != 1 || !double.IsFinite(t) || CarryWire.Unpack(layers, ids, poses) is not { } pieces || !CarryWire.IsFinite(at)) return;
        CarryLayoutReceived?.Invoke(carrier, pieces, at, t);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void CarryPlaceDown(long carrier, double t, float x, float z, float y)
    {
        var at = new HandAt(x, z, y);
        if (Multiplayer.GetRemoteSenderId() != 1 || !double.IsFinite(t) || !CarryWire.IsFinite(at)) return;
        CarryPlaceReceived?.Invoke(carrier, at, t);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
    void FlightDown(long carrier, double t, string[] layers, string[] ids, float[] poses)
    {
        if (Multiplayer.GetRemoteSenderId() != 1 || !double.IsFinite(t) || CarryWire.UnpackSamples(layers, ids, poses) is not { } samples) return;
        FlightReceived?.Invoke(carrier, t, samples);
    }

    public void SharePack(string packId)
    {
        var store = Session.Store;
        var hash = UserPacks.Hash(store, packId);
        if (Server == null)
        {
            if (!PackManifest.IsUserPackId(packId)) { Session.OnError("only your own (user.) packs can be shared from a client"); return; }
            var pack = Json.Load<PackManifest>(Path.Combine(store.PackageDir(packId), "pack.json"));
            foreach (var a in pack.Assets.Values) _transfers.Upload(packId, a.File.Replace(Path.DirectorySeparatorChar, '/'));
            _transfers.Upload(packId, "pack.json");
        }
        Session.Do(new AddPack { Id = packId, Hash = hash });
    }

    readonly Queue<Command> _afterUploads = new();

    public void ShareThen(IEnumerable<string> files, Command c)
    {
        if (Server != null) { Session.Do(c); return; }
        foreach (var rel in files) _transfers.Upload(UploadAsset.ReliefPackage, rel);
        _afterUploads.Enqueue(c);
    }

    public override void _Process(double delta)
    {
        _transfers.Pump();
        while (_afterUploads.Count > 0 && _transfers.UploadsDone) Session.Do(_afterUploads.Dequeue());
        if (Server == null && Session is { Me.PeerId: not 0 }) PrefetchWhatIsOnTheTable();
        _saves.Harvest();
        if (Server == null && _saves.MirrorPath != null) { _saves.TickMirror(delta); return; }
        if (Server == null) return;
        var tickClock = System.Diagnostics.Stopwatch.StartNew();
        try { Dispatch(Server.Scripts.Tick(Time.GetTicksMsec() / 1000.0)); }
        catch (Exception e) { GD.PushError("[scripts] tick failed: " + e); }
        if (tickClock.ElapsedMilliseconds > 20) GD.Print($"[scripts] tick {tickClock.ElapsedMilliseconds} ms");
        _saves.Tick(delta);
    }

    public void SaveNow(bool closing = false) => _saves.SaveNow(closing);

    public string SaveLogState => _saves.SaveLogState;

    void WriteBackDone(string error) => _saves.WriteBackDone(error);
}

public static class Joins
{
    static string FilePath => System.IO.Path.Combine(OS.GetUserDataDir(), "joins.txt");

    static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var tab = line.IndexOf('\t');
                    if (tab > 0) d[line[..tab]] = line[(tab + 1)..].Trim();
                }
        }
        catch (Exception e) { Problems.TellOnce("joins-read", $"The tables this PC was let in by (joins.txt) could not be read ({e.Message}): you wait at the door again at each of them."); }
        return d;
    }

    public static string? Remembered(string address, int port) => Load().GetValueOrDefault($"{address.Trim()}:{port}");

    public static void Remember(string address, int port, string token)
    {
        var d = Load();
        d[$"{address.Trim()}:{port}"] = token;
        try { Write(d); } catch (Exception e) { Problems.TellOnce("joins-write", $"This PC could not note that it was let in ({e.Message}): next time you wait at the door again."); }
    }

    public static void Forget(string address, int port)
    {
        var d = Load();
        if (d.Remove($"{address.Trim()}:{port}"))
        {
            try { Write(d); }
            catch (Exception e) { Problems.TellOnce("joins-write", $"This PC could not forget the table at {address.Trim()}:{port} in joins.txt ({e.Message}): it will try to come straight back in there."); }
        }
    }

    static void Write(Dictionary<string, string> d) => Json.WriteAtomic(FilePath, string.Concat(d.Select(kv => kv.Key + "\t" + kv.Value + "\n")));
}
