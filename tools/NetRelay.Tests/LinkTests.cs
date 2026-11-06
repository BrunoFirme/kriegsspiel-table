namespace KTable.NetRelay.Tests;

public sealed class LinkTests
{
    const int Packets = 200_000;
    const int PacketBytes = 1000;
    const int UdpIpHeaderBytes = 28;

    static Link Up(Profile profile, int seed = 1) => new(profile, Direction.Up, new Random(seed));

    static List<Passage> Send(Link link, int count, double every = 1) =>
        Enumerable.Range(0, count).Select(i => link.Pass(PacketBytes, i * every)).ToList();

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(2, 1)]
    [InlineData(5, 3)]
    [InlineData(8, 4)]
    public void LossMatchesThePercentageOverManyPackets(double percent, double burst)
    {
        var sent = Send(Up(Profile.Lan with { LossPercent = percent, BurstLength = burst }), Packets);
        var lost = 100.0 * sent.Count(p => p.Fate == Fate.Lost) / Packets;
        Assert.InRange(lost, percent * 0.9, percent * 1.1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void LossComesInBurstsOfTheGivenMeanLength(double burst)
    {
        var sent = Send(Up(Profile.Lan with { LossPercent = 5, BurstLength = burst }), Packets);
        var runs = new List<int>();
        var run = 0;
        foreach (var p in sent)
        {
            if (p.Fate == Fate.Lost) { run++; continue; }
            if (run > 0) runs.Add(run);
            run = 0;
        }
        Assert.InRange(runs.Average(), burst * 0.9, burst * 1.1);
    }

    [Fact]
    public void FullLossDropsEverythingAndNoLossDropsNothing()
    {
        Assert.All(Send(Up(Profile.Lan with { LossPercent = 100 }), 1000), p => Assert.Equal(Fate.Lost, p.Fate));
        Assert.All(Send(Up(Profile.Lan), 1000), p => Assert.Equal(Fate.Delivered, p.Fate));
    }

    [Fact]
    public void EachDirectionCarriesHalfThePing()
    {
        var link = Up(Profile.Lan with { PingMs = 120 });
        Assert.Equal(1060, link.Pass(PacketBytes, 1000).At, 6);
    }

    [Fact]
    public void JitterStaysWithinItsRangeAndKeepsTheOrder()
    {
        var sent = Send(Up(Profile.Lan with { PingMs = 100, JitterMs = 30 }), 10_000);
        var late = sent.Select((p, i) => p.At - i).ToList();
        Assert.All(late, d => Assert.InRange(d, 20 - 1e-9, 80 + 30));
        Assert.True(sent.Zip(sent.Skip(1)).All(pair => pair.Second.At >= pair.First.At));
    }

    [Fact]
    public void LatePacketsAreOvertakenByLaterOnes()
    {
        var sent = Send(Up(Profile.Lan with { PingMs = 50, ReorderPercent = 5 }), 10_000);
        var overtaken = sent.Zip(sent.Skip(1)).Count(pair => pair.Second.At < pair.First.At);
        Assert.InRange(overtaken, 400, 600);
    }

    [Fact]
    public void DuplicatesArriveTwice()
    {
        var sent = Send(Up(Profile.Lan with { DuplicatePercent = 10 }), 10_000);
        Assert.InRange(sent.Count(p => p.CopyAt != null), 900, 1100);
        Assert.All(sent.Where(p => p.CopyAt != null), p => Assert.Equal(p.At, p.CopyAt!.Value));
    }

    [Fact]
    public void BandwidthSpacesPacketsByTheirSize()
    {
        var link = Up(Profile.Lan with { UpKbps = 1000 });
        var sent = Send(link, 10, every: 0);
        var perPacketMs = (PacketBytes + UdpIpHeaderBytes) * 8 / 1000.0;
        for (var i = 0; i < sent.Count; i++) Assert.Equal((i + 1) * perPacketMs, sent[i].At, 6);
        Assert.Equal(10 * perPacketMs, link.QueuedMs(0), 6);
    }

    [Fact]
    public void TheOtherDirectionsLimitDoesNotApply()
    {
        var link = new Link(Profile.Lan with { UpKbps = 1 }, Direction.Down, new Random(1));
        Assert.All(Send(link, 100, every: 0), p => Assert.Equal(0, p.At));
    }

    [Fact]
    public void AFullQueueDropsInsteadOfWaitingLonger()
    {
        var link = Up(Profile.Lan with { UpKbps = 1000, QueueMs = 100 });
        var sent = Send(link, 100, every: 0);
        var waited = sent.Where(p => p.Fate == Fate.Delivered).Max(p => p.At);
        Assert.Contains(sent, p => p.Fate == Fate.Overflow);
        Assert.InRange(waited, 100, 100 + 9);
    }

    [Fact]
    public void AChangedProfileAppliesToTheNextPacket()
    {
        var link = Up(Profile.Lan);
        Assert.Equal(0, link.Pass(PacketBytes, 0).At);
        link.Profile = Profile.Lan with { PingMs = 200 };
        Assert.Equal(100, link.Pass(PacketBytes, 0).At);
    }
}
