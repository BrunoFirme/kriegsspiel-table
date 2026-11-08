using System.Net;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed class AddressTests : ConverterHarness
{
    static (HttpListener Listener, int Port) Listen()
    {
        for (int port = 18731; port < 18760; port++)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }
        throw new InvalidOperationException("no free local port for the test server");
    }

    [Fact]
    public async Task An_address_without_http_is_fetched_as_the_tabletop_fetches_it_even_after_an_older_import_marked_it_dead()
    {
        var cfg = TempConfig();
        var (listener, port) = Listen();
        using var png = new SKBitmap(4, 4);
        var bytes = png.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        var serving = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.ContentType = "image/png";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });
        var address = $"127.0.0.1:{port}/field.png";
        Directory.CreateDirectory(cfg.Cache);
        File.WriteAllText(Path.Combine(cfg.Cache, "index.json"), System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, CacheEntry> { [AssetStore.Key(address)] = new() { Status = "dead", Error = "not a web address (no http/https)", Url = address } }, Config.Opts));
        var assets = new AssetStore(cfg, new Report());

        await assets.FetchAll(new[] { address });

        await serving;
        listener.Stop();
        Assert.Equal("ok", assets.Get(address)?.Status);
    }
}
