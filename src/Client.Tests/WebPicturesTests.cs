using System.Net;
using KTable.Client;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public sealed class WebPicturesTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-webpictures-" + Guid.NewGuid().ToString("N"));

    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    sealed class Web(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public int Asked;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked++;
            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    static HttpResponseMessage Bytes(byte[] b) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(b) };

    [Fact]
    public async Task A_picture_link_is_kept_by_its_hash_and_asked_for_once_and_anything_else_gives_nothing()
    {
        var web = new Web(u => u.AbsolutePath switch
        {
            "/arms.png" => Bytes(Png),
            "/page.html" => Bytes("<html></html>"u8.ToArray()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var pictures = new WebPictures(_root, new HttpClient(web));
        var name = await pictures.Fetch("https://example.com/arms.png");
        Assert.NotNull(name);
        Assert.True(UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, name));
        Assert.Equal(Png, File.ReadAllBytes(pictures.PathOf(name)));
        Assert.Equal(name, await new WebPictures(_root, new HttpClient(web)).Fetch("https://example.com/arms.png"));
        Assert.Equal(1, web.Asked);
        Assert.Null(await pictures.Fetch("https://example.com/page.html"));
        Assert.Null(await pictures.Fetch("https://example.com/gone.png"));
        Assert.Null(await pictures.Fetch("file:///C:/Windows/win.ini"));
        Assert.Equal(name, pictures.Keep(Png));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
    }
}
