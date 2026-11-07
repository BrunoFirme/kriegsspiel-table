using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

public static class Naming
{
    public static string Slug(string name, string fallback, int max)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        if (slug.Length == 0) return fallback;
        return slug.Length > max ? slug[..max].Trim('-') : slug;
    }

    public static string ScenarioDir(string name) => Slug(name, "scenario", 60);

    public static string UserPackId(string name) => PackManifest.UserPrefix + Slug(name, "my-pieces", 50);

    public static string PieceSlug(string name) => Slug(name, "piece", 40);

    public static string NewPieceId(PackManifest pack, string name)
    {
        var stem = $"{pack.Id}.{PieceSlug(name)}";
        string id;
        do id = $"{stem}-{Repeatable.NewGuid().ToString("N")[..8]}";
        while (pack.Pieces.Any(p => p.Id == id));
        return id;
    }

    public static string PieceName(string stem)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(stem, "^[0-9A-Fa-f]{32,}", "").Replace('_', ' ').Replace('-', ' ').Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length == 0 ? "Piece" : char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static List<PieceDef> WithParts(IEnumerable<PieceDef> defs, Func<string, PieceDef?> find)
    {
        var keep = defs.DistinctBy(d => d.Id).ToList();
        var ids = keep.Select(d => d.Id).ToHashSet();
        for (var i = 0; i < keep.Count; i++)
            foreach (var part in keep[i].Attachments.Select(a => a.Piece).Concat(keep[i].Counters.Select(c => c.SpawnsMarker)).OfType<string>())
                if (ids.Add(part) && find(part) is { } extra) keep.Add(extra);
        return keep;
    }

    public static IEnumerable<string> AssetsOf(PieceDef d)
    {
        foreach (var h in new[] { d.Image, d.Back, d.Texture, d.Normal, d.Mesh, d.AddOnMesh, d.AddOnTexture }) if (h != null) yield return h;
        foreach (var s in d.States)
        {
            foreach (var h in new[] { s.Image, s.Back, s.Texture, s.Mesh }) if (h != null) yield return h;
            foreach (var decal in s.Decals ?? []) yield return decal.Picture;
        }
    }
}
