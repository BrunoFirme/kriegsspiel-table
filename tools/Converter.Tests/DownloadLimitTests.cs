using System.Net;
using KTable.Core;

namespace KTable.Converter.Tests;

public sealed class DownloadLimitTests : ConverterHarness
{
    sealed class Endless : Stream
    {
        public long Given;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Given; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { Given += count; return count; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task A_download_stops_once_it_passes_the_size_a_file_may_be()
    {
        const long Most = 1 << 20;
        var body = new Endless();
        using var cts = new CancellationTokenSource();

        var e = await Assert.ThrowsAsync<TooLargeException>(() => AssetStore.ReadBody(body, null, Most, cts));

        Assert.Contains(ContentLimits.Describe(Most), e.Message);
        Assert.InRange(body.Given, Most, 2 * Most);
    }

    [Fact]
    public async Task A_download_within_the_size_a_file_may_be_arrives_whole_whatever_length_the_server_claimed()
    {
        var sent = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        using var cts = new CancellationTokenSource();

        Assert.Equal(sent, await AssetStore.ReadBody(new MemoryStream(sent), sent.Length, sent.Length, cts));
        Assert.Equal(sent, await AssetStore.ReadBody(new MemoryStream(sent), null, sent.Length, cts));
        Assert.Equal(sent, await AssetStore.ReadBody(new MemoryStream(sent), int.MaxValue, sent.Length, cts));
        Assert.Equal(sent, await AssetStore.ReadBody(new MemoryStream(sent), 5, sent.Length, cts));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-1L, 0)]
    [InlineData(4096L, 4096)]
    [InlineData((long)int.MaxValue, AssetStore.MostRoomAtFirst)]
    [InlineData(long.MaxValue, AssetStore.MostRoomAtFirst)]
    public void A_server_s_claimed_length_reserves_no_more_than_a_small_start(long? claimed, int reserved) =>
        Assert.Equal(reserved, AssetStore.RoomAtFirst(claimed));

    [Fact]
    public void The_importer_s_web_client_connects_only_to_public_addresses_and_still_unpacks()
    {
        using var web = Assert.IsType<SocketsHttpHandler>(AssetStore.PublicWeb());

        Assert.NotNull(web.ConnectCallback);
        Assert.False(web.UseProxy);
        Assert.Equal(DecompressionMethods.All, web.AutomaticDecompression);
    }

    [Fact]
    public async Task An_address_on_this_pc_is_never_downloaded_by_an_import()
    {
        var cfg = TempConfig();
        var server = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var asked = server.AcceptTcpClientAsync();
        var address = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/field.png";
        try
        {
            var assets = new AssetStore(cfg, new Report());

            await assets.FetchAll(new[] { address });

            Assert.False(asked.IsCompleted);
            Assert.Null(assets.Get(address));
            Assert.Contains("not on the public web", assets.Why(address));
        }
        finally { server.Stop(); }
    }
}
