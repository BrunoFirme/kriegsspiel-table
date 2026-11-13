using System.Net;
using System.Net.Http;
using KTable.Client.Updates;

namespace KTable.Client.Tests;

public class FirmeNotesTests
{
    [Fact]
    public void A_message_file_gives_its_lines_and_one_https_link()
    {
        var note = FirmeNotes.Read("\r\n  Coming to Steam!  \r\nWishlist it\r\nhttps://store.example.com/app/1\r\n");
        Assert.NotNull(note);
        Assert.Equal("Coming to Steam!\nWishlist it", note.Text);
        Assert.Equal(new Uri("https://store.example.com/app/1"), note.Link);
    }

    [Fact]
    public void An_empty_file_is_no_message_and_plain_links_are_not_followed()
    {
        Assert.Null(FirmeNotes.Read(""));
        Assert.Null(FirmeNotes.Read(" \n\n https://example.com \n"));
        var plain = FirmeNotes.Read("Hello\nhttp://example.com\nfile:///C:/x");
        Assert.NotNull(plain);
        Assert.Null(plain.Link);
        Assert.Equal("Hello\nfile:///C:/x", plain.Text);
    }

    [Fact]
    public void A_long_message_is_cut_to_its_room()
    {
        var note = FirmeNotes.Read(string.Join("\n", Enumerable.Range(0, 6).Select(i => $"line {i}")) + "\n" + new string('x', 400));
        Assert.NotNull(note);
        Assert.Equal(FirmeNotes.MostLines, note.Text.Split('\n').Length);
        var wide = FirmeNotes.Read(new string('y', 400));
        Assert.NotNull(wide);
        Assert.Equal(FirmeNotes.MostChars + 1, wide.Text.Length);
        Assert.EndsWith("…", wide.Text);
    }

    [Fact]
    public async Task The_live_message_is_read_from_main_and_a_missing_file_is_an_error()
    {
        using (var http = new HttpClient(new Served(HttpStatusCode.OK, "Coming to Steam!\n")))
            Assert.Equal("Coming to Steam!", (await FirmeNotes.Fetch(http, CancellationToken.None))?.Text);
        using (var http = new HttpClient(new Served(HttpStatusCode.OK, "")))
            Assert.Null(await FirmeNotes.Fetch(http, CancellationToken.None));
        using (var http = new HttpClient(new Served(HttpStatusCode.NotFound, "404")))
            await Assert.ThrowsAsync<HttpRequestException>(() => FirmeNotes.Fetch(http, CancellationToken.None));
        Assert.StartsWith($"https://raw.githubusercontent.com/{Releases.Repo}/main/", FirmeNotes.Live.AbsoluteUri);
    }

    sealed class Served(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }
}
