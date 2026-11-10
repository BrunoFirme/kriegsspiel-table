using System.Text;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public sealed class WebPictures(string packagesRoot, HttpClient? http = null)
{
    static readonly HttpClient Shared = NewClient();

    public const long MostBytes = 16L * 1024 * 1024;
    const string IndexFile = "urls.json";

    readonly object _gate = new();
    Dictionary<string, string>? _known;

    string Folder => Path.Combine(packagesRoot, UploadAsset.ReliefPackage, UploadAsset.WebPictures);

    public static readonly HttpClient PublicWeb = NewClient(publicOnly: true);

    public static HttpClient NewClient(bool publicOnly = false)
    {
        var handler = new SocketsHttpHandler();
        if (publicOnly)
        {
            handler.UseProxy = false;
            handler.ConnectCallback = ConnectPublic;
        }
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KriegsspielTable");
        return c;
    }

    static async ValueTask<Stream> ConnectPublic(SocketsHttpConnectionContext context, CancellationToken stop)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = (await Dns.GetHostAddressesAsync(host, stop)).Where(IsPublic).ToArray();
        if (addresses.Length == 0) throw new HttpRequestException($"{host} is not on the public web");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, stop);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 || b[0] >= 224 || b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] == 169 && b[1] == 254
                || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 192 && b[1] == 0 && b[2] == 0 || b[0] == 198 && b[1] is 18 or 19);
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;
        var v6 = address.GetAddressBytes();
        return !(address.Equals(IPAddress.IPv6None) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (v6[0] & 0xFE) == 0xFC);
    }

    public Task<string?> Fetch(string url) => FetchAs(url, UploadAsset.WebPictures, Keep);

    public Task<string?> FetchModel(string url) => FetchAs(url, UploadAsset.WebModels, KeepModel, PlacedModel);

    const string PlacedModel = "placed lit ";

    async Task<string?> FetchAs(string url, string folder, Func<byte[], string?> keep, string memo = "")
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        if (Known(memo + url) is { } had && had.StartsWith(folder + "/", StringComparison.Ordinal) && File.Exists(PathOf(had))) return had;
        try
        {
            using var resp = await (http ?? Shared).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > MostBytes) return null;
            await using var s = await resp.Content.ReadAsStreamAsync();
            if (await ReadAtMost(s) is not { } bytes || keep(bytes) is not { } name) return null;
            Remember(memo + url, name);
            return name;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException) { return null; }
    }

    public string? Keep(byte[] bytes)
    {
        if (bytes.Length > MostBytes || KindOf(bytes) is not { } ext || !ContentLimits.PictureAllowed(bytes, ext)) return null;
        var name = $"{UploadAsset.WebPictures}/{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}.{ext}";
        var file = PathOf(name);
        if (File.Exists(file)) return name;
        Directory.CreateDirectory(Folder);
        Json.WriteAtomic(file, bytes);
        return name;
    }

    public string? KeepModel(byte[] bytes)
    {
        if (bytes.Length > ContentLimits.MaxModelBytes || !LooksLikeObj(bytes)) return null;
        var placed = new UTF8Encoding(false).GetBytes(ModuleMeshes.Placed(Encoding.UTF8.GetString(bytes).Split(LineBreak), 1));
        var name = $"{UploadAsset.WebModels}/{Convert.ToHexString(SHA256.HashData(placed)).ToLowerInvariant()}.obj";
        var file = PathOf(name);
        if (File.Exists(file)) return name;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        Json.WriteAtomic(file, placed);
        return name;
    }

    const char LineBreak = '\n';

    public static bool LooksLikeObj(byte[] bytes)
    {
        if (Array.IndexOf(bytes, (byte)0) >= 0) return false;
        bool vertex = false, face = false;
        foreach (var raw in System.Text.Encoding.UTF8.GetString(bytes).Split(LineBreak))
        {
            var line = raw.TrimStart();
            vertex |= line.StartsWith("v ", StringComparison.Ordinal);
            face |= line.StartsWith("f ", StringComparison.Ordinal);
            if (vertex && face) return true;
        }
        return false;
    }

    public string PathOf(string name) => Path.Combine(packagesRoot, UploadAsset.ReliefPackage, name);

    public static string? KindOf(ReadOnlySpan<byte> b) =>
        b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }) ? "png"
        : b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "jpg"
        : b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8) ? "webp"
        : b.Length >= 2 && b[0] == 'B' && b[1] == 'M' ? "bmp"
        : null;

    static async Task<byte[]?> ReadAtMost(Stream s)
    {
        using var into = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buffer)) > 0)
        {
            if (into.Length + n > MostBytes) return null;
            into.Write(buffer, 0, n);
        }
        return into.ToArray();
    }

    string? Known(string url)
    {
        lock (_gate) return Index().GetValueOrDefault(url);
    }

    void Remember(string url, string name)
    {
        lock (_gate)
        {
            var index = Index();
            index[url] = name;
            BestEffort.Try(() => { Directory.CreateDirectory(Folder); Json.WriteAtomic(Path.Combine(Folder, IndexFile), System.Text.Json.JsonSerializer.Serialize(index)); });
        }
    }

    Dictionary<string, string> Index() => _known ??= BestEffort.Read(
        () => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Folder, IndexFile))) ?? new(),
        new Dictionary<string, string>());
}
