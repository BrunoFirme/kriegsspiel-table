using System.Text;

namespace KTable.Core;

public sealed class HeadingSlugs
{
    readonly Dictionary<string, int> _seen = new();

    public string Next(string heading)
    {
        var slug = Slug(heading);
        if (!_seen.TryGetValue(slug, out var n)) { _seen[slug] = 1; return slug; }
        _seen[slug] = n + 1;
        return slug + "-" + n;
    }

    public static string Slug(string heading)
    {
        var sb = new StringBuilder(heading.Length);
        foreach (var c in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        }
        return sb.ToString();
    }
}
