using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace KTable.NetRelay.Tests;

public sealed class RelayTests : IDisposable
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);
    static readonly TimeSpan Silence = TimeSpan.FromMilliseconds(300);

    readonly UdpClient _host = new(new IPEndPoint(IPAddress.Loopback, 0));
    readonly ConcurrentQueue<IPEndPoint> _heardFrom = new();
    readonly CancellationTokenSource _stop = new();
    readonly List<string> _said = [];
    readonly Relay _relay;

    public RelayTests()
    {
        _ = Echo();
        _relay = new Relay((IPEndPoint)_host.Client.LocalEndPoint!, [new LaneSetting(0, "lan"), new LaneSetting(0, "ping=80")], 0, IPAddress.Loopback, s => { lock (_said) _said.Add(s); });
    }

    async Task Echo()
    {
        while (!_stop.IsCancellationRequested)
        {
            UdpReceiveResult got;
            try { got = await _host.ReceiveAsync(_stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _heardFrom.Enqueue(got.RemoteEndPoint);
            await _host.SendAsync(got.Buffer, got.RemoteEndPoint);
        }
    }

    UdpClient Client() => new(new IPEndPoint(IPAddress.Loopback, 0));

    IPEndPoint LaneAt(int index) => new(IPAddress.Loopback, _relay.Lanes[index].Port);

    static async Task<byte[]?> Answer(UdpClient client, TimeSpan wait)
    {
        using var timeout = new CancellationTokenSource(wait);
        try { return (await client.ReceiveAsync(timeout.Token)).Buffer; }
        catch (OperationCanceledException) { return null; }
    }

    [Fact]
    public async Task PacketsReachTheHostAndItsRepliesComeBack()
    {
        using var client = Client();
        byte[] hello = [1, 2, 3, 4];
        await client.SendAsync(hello, LaneAt(0));
        Assert.Equal(hello, await Answer(client, Patience));
        Assert.Contains("1 client ", _relay.Status());
    }

    [Fact]
    public async Task EachClientReachesTheHostFromItsOwnPort()
    {
        using var first = Client();
        using var second = Client();
        await first.SendAsync(new byte[] { 1 }, LaneAt(0));
        await second.SendAsync(new byte[] { 2 }, LaneAt(0));
        Assert.NotNull(await Answer(first, Patience));
        Assert.NotNull(await Answer(second, Patience));
        Assert.Equal(2, _heardFrom.Select(e => e.Port).Distinct().Count());
        Assert.Equal(2, _relay.Lanes[0].Clients);
    }

    [Fact]
    public async Task PingDelaysTheRoundTrip()
    {
        using var client = Client();
        var watch = Stopwatch.StartNew();
        await client.SendAsync(new byte[] { 1 }, LaneAt(1));
        Assert.NotNull(await Answer(client, Patience));
        Assert.InRange(watch.Elapsed.TotalMilliseconds, 79, 1000);
    }

    [Fact]
    public async Task ACutLaneCarriesNothingUntilRestored()
    {
        using var client = Client();
        Assert.Contains("CUT", _relay.Do($"cut {_relay.Lanes[0].Port}"));
        await client.SendAsync(new byte[] { 1 }, LaneAt(0));
        Assert.Null(await Answer(client, Silence));
        Assert.Equal(1, _relay.Lanes[0].Counts.Cut);
        _relay.Do($"restore {_relay.Lanes[0].Port}");
        await client.SendAsync(new byte[] { 2 }, LaneAt(0));
        Assert.Equal(new byte[] { 2 }, await Answer(client, Patience));
    }

    [Fact]
    public async Task ATimedCutEndsByItself()
    {
        _relay.Do($"cut all 0.2");
        Assert.All(_relay.Lanes, l => Assert.True(l.IsCut));
        await Task.Delay(Silence);
        Assert.All(_relay.Lanes, l => Assert.False(l.IsCut));
    }

    [Fact]
    public async Task SetChangesAConnectionWhileRunning()
    {
        using var client = Client();
        Assert.Contains("loss=100", _relay.Do($"set {_relay.Lanes[0].Port} loss=100"));
        await client.SendAsync(new byte[] { 1 }, LaneAt(0));
        Assert.Null(await Answer(client, Silence));
        Assert.Equal(1, _relay.Lanes[0].Counts.Lost);
    }

    [Fact]
    public void CommandsAnswerInPlainWords()
    {
        Assert.Contains("there is no connection on 1", _relay.Do("cut 1"));
        Assert.Contains("there is no profile called nowhere", _relay.Do("set all nowhere"));
        Assert.Contains("not understood", _relay.Do("jump"));
        Assert.Contains("seconds", _relay.Do("cut all soon"));
        Assert.Contains("upload 2 Mbit/s", _relay.Do("hostup 2000"));
        Assert.Contains("mobile", _relay.Do("profiles"));
        Assert.StartsWith("host ", _relay.Do(""));
    }

    [Fact]
    public void ATakenPortSaysSoAndLeavesNothingOpen()
    {
        var taken = _relay.Lanes[0].Port;
        Assert.Throws<SocketException>(() => new Relay((IPEndPoint)_host.Client.LocalEndPoint!, [new LaneSetting(0, "lan"), new LaneSetting(taken, "lan")], 0, IPAddress.Loopback, _ => { }));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _relay.Dispose();
        _host.Dispose();
    }
}
