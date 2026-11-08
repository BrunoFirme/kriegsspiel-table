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

    [Fact]
    public async Task A_model_link_is_kept_turned_as_the_game_reads_a_model_by_its_hash_only_when_it_is_a_model_and_a_picture_link_stays_a_picture()
    {
        var obj = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', "# box", "v 0 0 0", "v 1 0 0", "v 0 1 0", "f 1 2 3", ""));
        var web = new Web(u => u.AbsolutePath switch
        {
            "/gun.obj" => Bytes(obj),
            "/arms.png" => Bytes(Png),
            "/page.html" => Bytes("<html>v not a model</html>"u8.ToArray()),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var store = new WebPictures(_root, new HttpClient(web));
        var name = await store.FetchModel("https://example.com/gun.obj");
        Assert.NotNull(name);
        Assert.True(UploadAsset.IsWebModel(UploadAsset.ReliefPackage, name));
        Assert.Equal(KTable.Core.Model.ModuleMeshes.Placed(System.Text.Encoding.UTF8.GetString(obj).Split('\n'), 1), File.ReadAllText(store.PathOf(name)));
        Assert.Contains("v -1 0 0", File.ReadAllText(store.PathOf(name)));
        Assert.Null(await store.FetchModel("https://example.com/page.html"));
        Assert.Null(await store.FetchModel("https://example.com/arms.png"));
        Assert.Null(await store.FetchModel("https://example.com/gone.obj"));
        var picture = await store.Fetch("https://example.com/arms.png");
        Assert.True(UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, picture!));
        Assert.Null(await store.FetchModel("https://example.com/arms.png"));
        Assert.True(UploadAsset.PathAllowed(UploadAsset.ReliefPackage, name));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
    }
}
