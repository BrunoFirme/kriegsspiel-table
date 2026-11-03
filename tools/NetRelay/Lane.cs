using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using static System.FormattableString;

namespace KTable.NetRelay;

public sealed class Counts
{
    long _upPackets, _upBytes, _downPackets, _downBytes, _lost, _overflowed, _cut, _duplicated, _unsent;

    public long UpPackets => Interlocked.Read(ref _upPackets);
    public long UpBytes => Interlocked.Read(ref _upBytes);
    public long DownPackets => Interlocked.Read(ref _downPackets);
    public long DownBytes => Interlocked.Read(ref _downBytes);
    public long Lost => Interlocked.Read(ref _lost);
    public long Overflowed => Interlocked.Read(ref _overflowed);
    public long Cut => Interlocked.Read(ref _cut);
    public long Duplicated => Interlocked.Read(ref _duplicated);
    public long Unsent => Interlocked.Read(ref _unsent);

    internal void Heard(Direction direction, int bytes)
    {
        if (direction == Direction.Up) { Interlocked.Increment(ref _upPackets); Interlocked.Add(ref _upBytes, bytes); }
        else { Interlocked.Increment(ref _downPackets); Interlocked.Add(ref _downBytes, bytes); }
    }

    internal void Dropped(Fate fate)
    {
        if (fate == Fate.Lost) Interlocked.Increment(ref _lost);
        else Interlocked.Increment(ref _overflowed);
    }

    internal void CutOff() => Interlocked.Increment(ref _cut);
    internal void Copied() => Interlocked.Increment(ref _duplicated);
    internal void NotSent() => Interlocked.Increment(ref _unsent);
}

public sealed class Lane : IDisposable
{
    const int MaxDatagramBytes = 65_535;
    const int SocketBufferBytes = 4 << 20;
    const double ForgetClientAfterMs = 120_000;
    const double MsPerSecond = 1000;
    static readonly TimeSpan SweepEvery = TimeSpan.FromSeconds(10);

    sealed class Flow(EndPoint client, Socket upstream) : IDisposable
    {
        public EndPoint Client { get; } = client;
        public Socket Upstream { get; } = upstream;
        public double LastHeard { get; set; } = Clock.Now;
        public void Dispose() => Upstream.Dispose();
    }

    readonly Socket _listen;
    readonly IPEndPoint _host;
    readonly Scheduler _scheduler;
    readonly Link _hostUp, _up, _down;
    readonly Action<string> _say;
    readonly ConcurrentDictionary<EndPoint, Flow> _flows = new();
    readonly CancellationTokenSource _stop = new();
    readonly Timer _sweep;
    double _cutUntil;
    string _spec;

    public Lane(IPEndPoint listenOn, IPEndPoint host, string spec, Scheduler scheduler, Link hostUp, Action<string> say)
    {
        var profile = Profile.Parse(spec);
        _host = host; _spec = spec; _scheduler = scheduler; _hostUp = hostUp; _say = say;
        _up = new Link(profile, Direction.Up);
        _down = new Link(profile, Direction.Down);
        _listen = Socket(listenOn.AddressFamily);
        try { _listen.Bind(listenOn); }
        catch (SocketException) { _listen.Dispose(); throw; }
        Port = ((IPEndPoint)_listen.LocalEndPoint!).Port;
        Watch(Task.Run(Listen), "listening for clients");
        _sweep = new Timer(_ => Sweep(), null, SweepEvery, SweepEvery);
    }

    public int Port { get; }
    public Counts Counts { get; } = new();
    public int Clients => _flows.Count;
    public string Spec => _spec;
    public Profile Profile => _up.Profile;
    public bool IsCut => Clock.Now < Volatile.Read(ref _cutUntil);

    public void Set(string spec)
    {
        var profile = Profile.Parse(spec);
        _up.Profile = _down.Profile = profile;
        _spec = spec;
    }

    public void Cut(double? seconds) =>
        Volatile.Write(ref _cutUntil, seconds is { } s ? Clock.Now + s * MsPerSecond : double.PositiveInfinity);

    public void Restore() => Volatile.Write(ref _cutUntil, 0);

    public string Status()
    {
        var now = Clock.Now;
        var c = Counts;
        var until = Volatile.Read(ref _cutUntil);
        var cut = now >= until ? "" : double.IsPositiveInfinity(until) ? "  CUT" : Invariant($"  CUT {(until - now) / MsPerSecond:0} s more");
        var queue = Math.Max(_down.QueuedMs(now), _up.QueuedMs(now));
        return Invariant($"{Port,-6} {_spec,-14} {Clients} client{(Clients == 1 ? " " : "s")}  ")
            + Invariant($"up {c.UpPackets} pk {Size(c.UpBytes)}  down {c.DownPackets} pk {Size(c.DownBytes)}  ")
            + Invariant($"lost {c.Lost}  overflow {c.Overflowed}  cut {c.Cut}  queue {queue:0} ms{cut}");
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1 << 10 => Invariant($"{bytes} B"),
        < 1 << 20 => Invariant($"{bytes / 1024.0:0.#} KB"),
        _ => Invariant($"{bytes / (1024.0 * 1024):0.#} MB"),
    };

    static Socket Socket(AddressFamily family) =>
        new(family, SocketType.Dgram, ProtocolType.Udp) { ReceiveBufferSize = SocketBufferBytes, SendBufferSize = SocketBufferBytes };

    void Watch(Task loop, string what) =>
        loop.ContinueWith(t => _say($"{Port}: stopped {what}: {t.Exception!.GetBaseException().Message}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    async Task Listen()
    {
        var buffer = new byte[MaxDatagramBytes];
        EndPoint anyone = new IPEndPoint(_listen.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        while (!_stop.IsCancellationRequested)
        {
            SocketReceiveFromResult got;
            try { got = await _listen.ReceiveFromAsync(buffer, SocketFlags.None, anyone, _stop.Token); }
            catch (SocketException e) when (IsPassing(e)) { continue; }
            catch (Exception e) when (IsStopping(e)) { return; }
            FromClient(got.RemoteEndPoint, buffer.AsSpan(0, got.ReceivedBytes).ToArray());
        }
    }

    async Task FromHostTo(Flow flow)
    {
        var buffer = new byte[MaxDatagramBytes];
        while (!_stop.IsCancellationRequested)
        {
            int got;
            try { got = await flow.Upstream.ReceiveAsync(buffer, SocketFlags.None, _stop.Token); }
            catch (SocketException e) when (IsPassing(e)) { continue; }
            catch (Exception e) when (IsStopping(e)) { return; }
            FromHost(flow, buffer.AsSpan(0, got).ToArray());
        }
    }

    static bool IsPassing(SocketException e) =>
        e.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused or SocketError.MessageSize or SocketError.HostUnreachable or SocketError.NetworkUnreachable;

    static bool IsStopping(Exception e) =>
        e is OperationCanceledException or ObjectDisposedException || e is SocketException { SocketErrorCode: SocketError.OperationAborted or SocketError.Interrupted };

    void FromClient(EndPoint client, byte[] data)
    {
        var now = Clock.Now;
        var flow = _flows.GetOrAdd(client, Open);
        flow.LastHeard = now;
        Counts.Heard(Direction.Up, data.Length);
        if (IsCut) { Counts.CutOff(); return; }
        Schedule(_up.Pass(data.Length, now), () => flow.Upstream.Send(data));
    }

    void FromHost(Flow flow, byte[] data)
    {
        Counts.Heard(Direction.Down, data.Length);
        if (IsCut) { Counts.CutOff(); return; }
        var leftHost = _hostUp.Pass(data.Length, Clock.Now);
        if (leftHost.Fate != Fate.Delivered) { Counts.Dropped(leftHost.Fate); return; }
        Schedule(_down.Pass(data.Length, leftHost.At), () => _listen.SendTo(data, flow.Client));
    }

    void Schedule(Passage passage, Action send)
    {
        if (passage.Fate != Fate.Delivered) { Counts.Dropped(passage.Fate); return; }
        _scheduler.At(passage.At, () => Deliver(send));
        if (passage.CopyAt is not { } copy) return;
        Counts.Copied();
        _scheduler.At(copy, () => Deliver(send));
    }

    void Deliver(Action send)
    {
        try { send(); }
        catch (Exception e) when (e is SocketException or ObjectDisposedException) { Counts.NotSent(); }
    }

    Flow Open(EndPoint client)
    {
        var upstream = Socket(_host.AddressFamily);
        upstream.Connect(_host);
        var flow = new Flow(client, upstream);
        Watch(Task.Run(() => FromHostTo(flow)), "relaying for " + client);
        _say($"{Port}: new client {client}");
        return flow;
    }

    void Sweep()
    {
        var now = Clock.Now;
        foreach (var (client, flow) in _flows)
        {
            if (now - flow.LastHeard < ForgetClientAfterMs || !_flows.TryRemove(client, out _)) continue;
            flow.Dispose();
            _say($"{Port}: forgot client {client}, silent for {ForgetClientAfterMs / MsPerSecond:0} s");
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _sweep.Dispose();
        _listen.Dispose();
        foreach (var flow in _flows.Values) flow.Dispose();
        _flows.Clear();
    }
}
