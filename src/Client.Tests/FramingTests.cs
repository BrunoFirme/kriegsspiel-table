namespace KTable.Client.Tests;

public class FramingTests
{
    [Fact]
    public void A_small_message_goes_as_it_is()
    {
        var json = "{\"a\":1}"u8.ToArray();
        Assert.Same(json, Framing.ForWire(json));
        Assert.Same(json, Framing.Unpack(json));
    }

    [Fact]
    public void A_message_over_the_limit_is_packed_and_comes_back_whole()
    {
        var json = System.Text.Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Range(0, 20000).Select(i => $"{{\"n\":{i}}}")) + "]");
        Assert.True(json.Length > Framing.PackAbove);
        var wire = Framing.ForWire(json);
        Assert.Equal(new byte[] { 0x1F, 0x8B }, wire[..2]);
        Assert.True(wire.Length < json.Length / 2);
        Assert.Equal(json, Framing.Unpack(wire));
    }

    [Fact]
    public void Exactly_at_the_limit_is_not_packed()
    {
        var json = new byte[Framing.PackAbove];
        Array.Fill(json, (byte)'1');
        Assert.Same(json, Framing.ForWire(json));
    }

    [Fact]
    public void A_packed_message_that_unpacks_past_the_cap_is_refused()
    {
        var bomb = Framing.Pack(new byte[1_000_000]);
        Assert.True(bomb.Length < Framing.MaxFromStranger);
        Assert.Throws<InvalidDataException>(() => Framing.Unpack(bomb, Framing.MaxFromStranger));
        Assert.Equal(1_000_000, Framing.Unpack(bomb).Length);
    }

    [Fact]
    public void A_plain_message_over_the_cap_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => Framing.Unpack(new byte[100], 99));
    }

    [Fact]
    public void Short_data_is_never_taken_for_packed()
    {
        Assert.Empty(Framing.Unpack(Array.Empty<byte>()));
        Assert.Equal(new byte[] { 0x1F }, Framing.Unpack(new byte[] { 0x1F }));
    }

    [Fact]
    public void Only_an_umpire_may_send_the_host_a_large_message()
    {
        Assert.Equal(Framing.MaxFromStranger, Framing.MostFrom(null));
        Assert.Equal(Framing.MaxFromStranger, Framing.MostFrom(new KTable.Core.Model.PeerInfo { Role = KTable.Core.Model.Role.Observer }));
        Assert.Equal(Framing.MaxUnpacked, Framing.MostFrom(new KTable.Core.Model.PeerInfo { Role = KTable.Core.Model.Role.Umpire }));
    }
}
