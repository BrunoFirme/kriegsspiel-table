using System.Text.RegularExpressions;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed partial class BundleModels
{
    const string Tag = "bundle:";

    sealed record Baked(PieceDef Def, string Pack, string Dir, Dictionary<string, AssetEntry> Assets);

    readonly Dictionary<string, Baked> _byBundle;

    BundleModels(Dictionary<string, Baked> byBundle) => _byBundle = byBundle;

    public static BundleModels Load(string packagesRoot, string? baked = null)
    {
        var found = new Dictionary<string, Baked>(StringComparer.OrdinalIgnoreCase);
        var dirs = Directory.Exists(packagesRoot) ? Directory.EnumerateDirectories(packagesRoot).ToList() : new List<string>();
        var shared = dirs.Where(d => Path.GetFileName(d) == BundleBake.Shared).ToList();
        foreach (var dir in dirs.Except(shared).Concat(baked == null ? [] : [baked]).Concat(shared))
        {
            var path = Path.Combine(dir, "pack.json");
            if (!File.Exists(path)) continue;
            var pack = BestEffort.Read<PackManifest?>(() => Json.Load<PackManifest>(path), fallback: null);
            if (pack == null || pack.Id.StartsWith(ImportPlan.ConvertedPrefix, StringComparison.Ordinal)) continue;
            foreach (var piece in pack.Pieces)
            {
                if (piece.Kind != "model" || piece.Mesh == null) continue;
                foreach (var sha in piece.Tags.Where(t => t.StartsWith(Tag, StringComparison.Ordinal)).Select(t => t[Tag.Length..]))
                    found.TryAdd(sha, new Baked(piece, pack.Id, dir, pack.Assets));
            }
        }
        return new BundleModels(found);
    }

    public bool Any => _byBundle.Count > 0;

    public (PieceDef Def, string Pack)? For(string? bundleUrl) => Hit(bundleUrl) is { } hit ? (hit.Def, hit.Pack) : null;

    public string? FileOf(string? bundleUrl, string? sha) =>
        sha != null && Hit(bundleUrl) is { } hit && hit.Assets.TryGetValue(sha, out var asset) && Path.Combine(hit.Dir, asset.File) is var path && File.Exists(path) ? path : null;

    Baked? Hit(string? bundleUrl) => bundleUrl != null && _byBundle.TryGetValue(IdOf(bundleUrl), out var hit) ? hit : null;

    public static string IdOf(string bundleUrl) =>
        Sha().Match(bundleUrl) is { Success: true } sha ? sha.Value.ToUpperInvariant()
        : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(AssetStore.Canonical(bundleUrl))));

    [GeneratedRegex("[0-9A-Fa-f]{40}")]
    private static partial Regex Sha();
}
