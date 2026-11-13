using System.Net.Http;

namespace KTable.Client.Updates;

public sealed record FirmeNote(string Text, Uri? Link);

public static class FirmeNotes
{
    public const string ShippedAt = "brand/message.txt";
    public static readonly Uri Live = new($"https://raw.githubusercontent.com/{Releases.Repo}/main/src/App/{ShippedAt}");
    public const int MostChars = 200, MostLines = 3;
    const string Ellipsis = "…";

    public static FirmeNote? Read(string file)
    {
        var text = new List<string>();
        Uri? link = null;
        foreach (var raw in file.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (Uri.TryCreate(line, UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
            {
                if (url.Scheme == Uri.UriSchemeHttps) link = url;
            }
            else if (text.Count < MostLines) text.Add(line);
        }
        if (text.Count == 0) return null;
        var joined = string.Join("\n", text);
        return new FirmeNote(joined.Length > MostChars ? joined[..MostChars].TrimEnd() + Ellipsis : joined, link);
    }

    public static async Task<FirmeNote?> Fetch(HttpClient http, CancellationToken stop)
    {
        using var answer = await http.GetAsync(Live, stop).ConfigureAwait(false);
        answer.EnsureSuccessStatusCode();
        return Read(await answer.Content.ReadAsStringAsync(stop).ConfigureAwait(false));
    }
}
