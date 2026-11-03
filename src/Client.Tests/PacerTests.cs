namespace KTable.Client.Tests;

public class PacerTests
{
    const int Chunk = 16 * 1024;
    double _now;

    Pacer New() => new(() => _now, Chunk);

    static void Fill(Pacer p, ref int sent)
    {
        while (p.CanSend()) p.Sent("c" + sent++, Chunk);
    }

    double RunFor(Pacer p, double seconds, double roundTrip, double bytesPerSecond)
    {
        var sent = 0;
        var queue = new Queue<(string Key, double At)>();
        var linkFreeAt = _now;
        var end = _now + seconds;
        while (_now < end)
        {
            while (p.CanSend())
            {
                var key = "c" + sent++;
                p.Sent(key, Chunk);
                linkFreeAt = Math.Max(linkFreeAt, _now) + Chunk / bytesPerSecond;
                queue.Enqueue((key, linkFreeAt + roundTrip));
            }
            var next = queue.Dequeue();
            _now = Math.Max(_now, next.At);
            p.Acked(next.Key);
        }
        return Math.Max(0, linkFreeAt - _now);
    }

    [Fact]
    public void It_starts_with_two_pieces_on_the_way()
    {
        var p = New();
        var sent = 0;
        Fill(p, ref sent);
        Assert.Equal(Pacer.MinInFlight, sent);
    }

    [Fact]
    public void On_a_fast_short_link_the_window_opens_up_to_its_limit()
    {
        var p = New();
        RunFor(p, 10, roundTrip: 0.02, bytesPerSecond: 50e6);
        Assert.Equal(Pacer.MaxInFlight, p.Window);
    }

    [Theory]
    [InlineData(0.03, 1.25e6)]
    [InlineData(0.1, 0.1e6)]
    [InlineData(0.3, 0.6e6)]
    public void On_a_slow_link_the_queue_stays_near_a_quarter_second(double roundTrip, double bytesPerSecond)
    {
        var p = New();
        var queued = RunFor(p, 30, roundTrip, bytesPerSecond);
        Assert.InRange(queued, 0, 0.6);
        Assert.InRange(p.Window * (double)Chunk / bytesPerSecond, 0, roundTrip + 0.6);
    }

    [Fact]
    public void A_sender_with_little_to_send_keeps_its_window()
    {
        var p = New();
        RunFor(p, 10, roundTrip: 0.02, bytesPerSecond: 50e6);
        var open = p.Window;
        for (var i = 0; i < 20; i++)
        {
            p.Sent("idle" + i, Chunk);
            _now += 0.5;
            p.Acked("idle" + i);
        }
        Assert.Equal(open, p.Window);
    }

    [Fact]
    public void Pieces_never_acknowledged_are_forgotten_after_ten_seconds()
    {
        var p = New();
        var sent = 0;
        Fill(p, ref sent);
        Assert.False(p.CanSend());
        _now += 11;
        Assert.True(p.CanSend());
        Assert.Equal(0, p.InFlight);
    }

    [Fact]
    public void An_unknown_or_repeated_ack_changes_nothing()
    {
        var p = New();
        p.Sent("a", Chunk);
        p.Acked("b");
        Assert.Equal(1, p.InFlight);
        p.Acked("a");
        p.Acked("a");
        Assert.Equal(0, p.InFlight);
    }
}
