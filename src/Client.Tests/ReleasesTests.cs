using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using KTable.Client.Updates;
using KTable.Core;

namespace KTable.Client.Tests;

public class ReleasesTests
{
    static readonly byte[] SetupBytes = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 31)).ToArray();
    static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    static object Asset(string name, long size, string? digest, string? url = null) => new
    {
        name, size, digest,
        browser_download_url = url ?? $"{Releases.DownloadsAt}v0/{name}",
    };

    static object Entry(string tag, string name, bool draft = false, params object[] assets) => new
    {
        tag_name = tag, name, draft,
        html_url = $"{Releases.PagesAt}tag/{tag}",
        assets,
    };

    static string Feed(params object[] entries) => JsonSerializer.Serialize(entries);

    [Fact]
    public void A_tag_reads_as_its_three_numbers_whatever_the_suffix()
    {
        Assert.Equal(new Version(0, 2, 0), Releases.NumberOf("v0.2.0-beta"));
        Assert.Equal(new Version(1, 10, 3), Releases.NumberOf("1.10.3"));
        Assert.Equal(new Version(0, 1, 0), Releases.NumberOf("0.1.0"));
        Assert.Null(Releases.NumberOf("v0.2"));
        Assert.Null(Releases.NumberOf("latest"));
        Assert.Null(Releases.NumberOf("v0.2.0 beta"));
        Assert.Null(Releases.NumberOf(""));
    }

    [Fact]
    public void The_newest_release_above_the_running_one_is_offered_prereleases_included_drafts_never()
    {
        var setup = Asset("KriegsspielTable-0.3-beta-setup.exe", 1000, "sha256:" + new string('a', 64));
        var feed = Feed(
            Entry("v0.2.0-beta", "Kriegsspiel Table 0.2 beta"),
            Entry("v0.4.0-beta", "Kriegsspiel Table 0.4 beta", draft: true),
            Entry("v0.3.0-beta", "Kriegsspiel Table 0.3 beta", false, setup, Asset("KriegsspielTable-0.3-beta-win64.zip", 2000, "sha256:" + new string('b', 64))),
            Entry("nightly", "something else"));
        var r = Releases.NewestAfter(feed, new Version(0, 1, 0));
        Assert.NotNull(r);
        Assert.Equal(new Version(0, 3, 0), r.Number);
        Assert.Equal("0.3 beta", r.Name);
        Assert.Equal($"{Releases.PagesAt}tag/v0.3.0-beta", r.Page);
        Assert.Equal("KriegsspielTable-0.3-beta-setup.exe", r.Setup?.Name);
        Assert.Equal(1000, r.Setup?.Size);
        Assert.Null(Releases.NewestAfter(feed, new Version(0, 3, 0)));
        Assert.Null(Releases.NewestAfter("[]", new Version(0, 1, 0)));
        Assert.Null(Releases.NewestAfter("{\"message\":\"rate limited\"}", new Version(0, 1, 0)));
    }

    [Fact]
    public void A_setup_without_a_sound_checksum_size_name_or_home_is_not_offered_for_install()
    {
        var good = "sha256:" + new string('c', 64);
        object[] bad =
        {
            Asset("KriegsspielTable-0.2-beta-setup.exe", 1000, null),
            Asset("KriegsspielTable-0.2-beta-setup.exe", 1000, "md5:" + new string('c', 32)),
            Asset("KriegsspielTable-0.2-beta-setup.exe", 1000, "sha256:" + new string('c', 63)),
            Asset("KriegsspielTable-0.2-beta-setup.exe", 1000, "sha256:" + new string('z', 64)),
            Asset("KriegsspielTable-0.2-beta-setup.exe", 0, good),
            Asset("KriegsspielTable-0.2-beta-setup.exe", 5L * 1024 * 1024 * 1024, good),
            Asset("..\\evil-setup.exe", 1000, good),
            Asset("KriegsspielTable-0.2-beta-setup.exe", 1000, good, "https://example.com/KriegsspielTable-0.2-beta-setup.exe"),
            Asset("KriegsspielTable-0.2-beta-setup.msi", 1000, good),
        };
        foreach (var a in bad)
        {
            var r = Releases.NewestAfter(Feed(Entry("v0.2.0-beta", "Kriegsspiel Table 0.2 beta", false, a)), new Version(0, 1, 0));
            Assert.NotNull(r);
            Assert.Null(r.Setup);
        }
        var stranger = Feed(new { tag_name = "v0.2.0-beta", name = "x", draft = false, html_url = "https://example.com/release", assets = Array.Empty<object>() });
        Assert.Null(Releases.NewestAfter(stranger, new Version(0, 1, 0)));
    }

    sealed class Served(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    static SetupFile Setup(byte[] expected) => new("KriegsspielTable-9.9-beta-setup.exe", new Uri($"{Releases.DownloadsAt}v9/x"), Sha(expected), expected.Length);

    [Fact]
    public async Task A_download_is_kept_only_when_its_size_and_checksum_match_the_release()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-releases-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var http = new HttpClient(new Served(SetupBytes)))
            {
                long seen = 0;
                var file = await Releases.Fetch(http, Setup(SetupBytes), dir, n => seen = n, CancellationToken.None);
                Assert.Equal(SetupBytes, File.ReadAllBytes(file));
                Assert.Equal(SetupBytes.Length, seen);
            }
            var tampered = (byte[])SetupBytes.Clone();
            tampered[^1] ^= 1;
            foreach (var (served, expected) in new[] { (tampered, SetupBytes), (SetupBytes[..^10], SetupBytes), (SetupBytes, SetupBytes[..^10]) })
            {
                BestEffort.DeleteFolder(dir);
                using var http = new HttpClient(new Served(served));
                await Assert.ThrowsAsync<InvalidDataException>(() => Releases.Fetch(http, Setup(expected), dir, _ => { }, CancellationToken.None));
                Assert.Empty(Directory.GetFiles(dir));
            }
        }
        finally { BestEffort.DeleteFolder(dir); }
    }
}
