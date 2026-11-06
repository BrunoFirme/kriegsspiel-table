using System.Text.RegularExpressions;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed partial class BundleModels
{
    const string Tag = "bundle:";

    readonly Dictionary<string, (PieceDef Def, string Pack)> _byBundle;

    BundleModels(Dictionary<string, (PieceDef, string)> byBundle) => _byBundle = byBundle;

    public static BundleModels Load(string packagesRoot)
    {
        var found = new Dictionary<string, (PieceDef, string)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(packagesRoot)) return new BundleModels(found);
        foreach (var dir in Directory.EnumerateDirectories(packagesRoot))
        {
            var path = Path.Combine(dir, "pack.json");
            if (!File.Exists(path)) continue;
            var pack = BestEffort.Read<PackManifest?>(() => Json.Load<PackManifest>(path), fallback: null);
            if (pack == null) continue;
            foreach (var piece in pack.Pieces)
            {
                if (piece.Kind != "model" || piece.Mesh == null) continue;
                foreach (var sha in piece.Tags.Where(t => t.StartsWith(Tag, StringComparison.Ordinal)).Select(t => t[Tag.Length..]))
                    found.TryAdd(sha, (piece, pack.Id));
            }
        }
        return new BundleModels(found);
    }

    public bool Any => _byBundle.Count > 0;

    public (PieceDef Def, string Pack)? For(string? bundleUrl)
    {
        if (bundleUrl == null) return null;
        var sha = Sha().Match(bundleUrl);
        return sha.Success && _byBundle.TryGetValue(sha.Value, out var hit) ? hit : null;
    }

    [GeneratedRegex("[0-9A-Fa-f]{40}")]
    private static partial Regex Sha();
}
