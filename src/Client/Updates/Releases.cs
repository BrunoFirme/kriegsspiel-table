using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using KTable.Core;

namespace KTable.Client.Updates;

public sealed record SetupFile(string Name, Uri Url, string Sha256, long Size);

public sealed record Release(Version Number, string Name, string Page, SetupFile? Setup);

public enum Download { WindowsSetup, LinuxArchive }

public static partial class Releases
{
    public const string Repo = "BrunoFirme/kriegsspiel-table";
    public static readonly Uri Feed = new($"https://api.github.com/repos/{Repo}/releases?per_page={FeedLength}");
    public const string DownloadsAt = $"https://github.com/{Repo}/releases/download/";
    public const string PagesAt = $"https://github.com/{Repo}/releases/";
    const int FeedLength = 20;
    const long FeedMaxBytes = 4 * 1024 * 1024;
    const long SetupMaxBytes = 1024L * 1024 * 1024;
    const int BufferSize = 1 << 16;
    const string DigestPrefix = "sha256:";
    const string AppTitle = "Kriegsspiel Table ";
    static readonly TimeSpan AskLimit = TimeSpan.FromSeconds(20);
    static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(30);

    [GeneratedRegex(@"^v?(\d{1,6})\.(\d{1,6})\.(\d{1,6})(?:-[0-9A-Za-z.]+)?$")] private static partial Regex TagPattern();
    [GeneratedRegex(@"^[A-Za-z0-9._-]+-setup\.exe$")] private static partial Regex SetupNamePattern();
    [GeneratedRegex(@"^[A-Za-z0-9._-]+-linux-x86_64\.tar\.gz$")] private static partial Regex LinuxNamePattern();

    static Regex NamePattern(Download kind) => kind == Download.LinuxArchive ? LinuxNamePattern() : SetupNamePattern();

    public static Version? NumberOf(string tag)
    {
        var m = TagPattern().Match(tag.Trim());
        if (!m.Success) return null;
        int Part(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
        return new Version(Part(1), Part(2), Part(3));
    }

    public static Release? NewestAfter(string feedJson, Version running, Download kind = Download.WindowsSetup)
    {
        using var doc = JsonDocument.Parse(feedJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        Release? best = null;
        foreach (var r in doc.RootElement.EnumerateArray())
            if (Read(r, kind) is { } found && found.Number > running && (best == null || found.Number > best.Number)) best = found;
        return best;
    }

    static Release? Read(JsonElement r, Download kind)
    {
        if (r.ValueKind != JsonValueKind.Object || Bool(r, "draft")) return null;
        if (NumberOf(Text(r, "tag_name")) is not { } number) return null;
        var page = Text(r, "html_url");
        if (!page.StartsWith(PagesAt, StringComparison.Ordinal)) return null;
        var name = Text(r, "name");
        name = name.StartsWith(AppTitle, StringComparison.OrdinalIgnoreCase) ? name[AppTitle.Length..].Trim() : name.Trim();
        if (name.Length == 0) name = number.ToString(3);
        return new Release(number, name, page, SetupOf(r, NamePattern(kind)));
    }

    static SetupFile? SetupOf(JsonElement r, Regex named)
    {
        if (!r.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var a in assets.EnumerateArray())
        {
            if (a.ValueKind != JsonValueKind.Object) continue;
            var name = Text(a, "name");
            var url = Text(a, "browser_download_url");
            var digest = Text(a, "digest");
            long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
            if (!named.IsMatch(name) || !url.StartsWith(DownloadsAt, StringComparison.Ordinal)) continue;
            if (!digest.StartsWith(DigestPrefix, StringComparison.Ordinal) || size is <= 0 or > SetupMaxBytes) continue;
            var hex = digest[DigestPrefix.Length..];
            if (hex.Length != SHA256.HashSizeInBytes * 2 || !hex.All(char.IsAsciiHexDigit)) continue;
            return new SetupFile(name, new Uri(url), hex, size);
        }
        return null;
    }

    static string Text(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static bool Bool(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    public static HttpClient NewClient(string userAgent)
    {
        var http = new HttpClient { Timeout = AskLimit, MaxResponseContentBufferSize = FeedMaxBytes };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return http;
    }

    public static async Task<Release?> Check(HttpClient http, Version running, Download kind, CancellationToken stop)
    {
        using var ask = new HttpRequestMessage(HttpMethod.Get, Feed);
        ask.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var answer = await http.SendAsync(ask, stop).ConfigureAwait(false);
        answer.EnsureSuccessStatusCode();
        return NewestAfter(await answer.Content.ReadAsStringAsync(stop).ConfigureAwait(false), running, kind);
    }

    public static async Task<string> Fetch(HttpClient http, SetupFile setup, string folder, Action<long> got, CancellationToken stop)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, setup.Name);
        var part = file + ".part";
        try
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(stop);
            stall.CancelAfter(StallLimit);
            using (var answer = await http.GetAsync(setup.Url, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false))
            {
                answer.EnsureSuccessStatusCode();
                await using var from = await answer.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
                await using var to = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[BufferSize];
                long total = 0;
                int n;
                while ((n = await ReadOrStall(from, buffer, stall).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > setup.Size) throw new InvalidDataException("the download is larger than the release says");
                    sha.AppendData(buffer, 0, n);
                    await to.WriteAsync(buffer.AsMemory(0, n), stop).ConfigureAwait(false);
                    got(total);
                }
                if (total != setup.Size) throw new InvalidDataException("the download stopped short");
                if (!Convert.ToHexString(sha.GetHashAndReset()).Equals(setup.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("the download does not match the release's checksum");
                await to.FlushAsync(stop).ConfigureAwait(false);
                to.Flush(flushToDisk: true);
            }
            File.Move(part, file, overwrite: true);
            return file;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            BestEffort.DeleteFile(part);
            throw new TimeoutException("the download stalled");
        }
        catch
        {
            BestEffort.DeleteFile(part);
            throw;
        }
    }

    static async Task<int> ReadOrStall(Stream from, byte[] buffer, CancellationTokenSource stall)
    {
        stall.CancelAfter(StallLimit);
        return await from.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
    }
}
