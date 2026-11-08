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
            if (pack == null || pack.Id.StartsWith(ImportPlan.ConvertedPrefix, StringComparison.Ordinal)) continue;
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

    public (PieceDef Def, string Pack)? For(string? bundleUrl) =>
        bundleUrl != null && _byBundle.TryGetValue(IdOf(bundleUrl), out var hit) ? hit : null;

    public static string IdOf(string bundleUrl) =>
        Sha().Match(bundleUrl) is { Success: true } sha ? sha.Value.ToUpperInvariant()
        : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(AssetStore.Canonical(bundleUrl))));

    [GeneratedRegex("[0-9A-Fa-f]{40}")]
    private static partial Regex Sha();
}
