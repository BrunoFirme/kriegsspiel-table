using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KTable.Converter.Bundles;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed partial class BundleBake
{
    public const string PackId = "bundle-models";
    public const string Shared = "user.bundle-models";
    const string PackName = "Models from module asset bundles";
    const string Licence = "models read from the asset bundles of imported modules";
    public const string Tag = "bundle:";
    const string ReadWith = "bundle-read:tiled";
    const string BundleType = "Custom_Assetbundle";
    const int AtlasMost = 2048;
    const int Digits = 4;

    readonly Config _cfg;
    readonly AssetStore _assets;
    readonly Report _rep;

    public BundleBake(Config cfg, AssetStore assets, Report rep)
    {
        _cfg = cfg;
        _assets = assets;
        _rep = rep;
    }

    public int Run(ModuleSave save)
    {
        var wanted = new List<(string Sha, string Url)>();
        foreach (var o in save.Objects.SelectMany(o => o.Walk()))
        {
            if (o.Name != BundleType || o.BundleUrl is not { } url || _cfg.IsTableWall(url)) continue;
            if (BundleEffects.IsEffect(o)) continue;
            var found = BundleModels.IdOf(url);
            if (!wanted.Any(w => w.Sha == found)) wanted.Add((found, url));
        }
        var bundleLinks = save.Objects.SelectMany(o => o.Walk()).Where(o => o.Name == BundleType)
            .SelectMany(o => new[] { o.BundleUrl, o.BundleSecondaryUrl }).OfType<string>().Distinct().ToList();
        if (wanted.Count == 0) return 0;

        var dir = Dir(_cfg.Cache);
        var assetsDir = Path.Combine(dir, "assets");
        var packPath = Path.Combine(dir, "pack.json");
        var had = File.Exists(packPath) ? BestEffort.Read<PackManifest?>(() => Json.Load<PackManifest>(packPath), fallback: null) : null;
        var already = new HashSet<string>(
            (had?.Pieces ?? new List<PieceDef>()).Where(p => p.Tags.Contains(ReadWith))
                .SelectMany(p => p.Tags).Where(t => t.StartsWith(Tag, StringComparison.Ordinal)).Select(t => t[Tag.Length..]),
            StringComparer.OrdinalIgnoreCase);
        var todo = wanted.Where(w => !already.Contains(w.Sha)).ToList();
        if (todo.Count == 0) return 0;

        var read = ReadAll(todo, SharedFiles(bundleLinks));
        if (read.Count == 0) return 0;

        Directory.CreateDirectory(assetsDir);

        var fresh = new HashSet<string>(read.Select(m => Tag + m.Sha), StringComparer.OrdinalIgnoreCase);
        var pieces = (had?.Pieces ?? new List<PieceDef>())
            .Where(p => p.Kind == "model" && !p.Tags.Any(fresh.Contains)).ToList();
        var entries = new Dictionary<string, AssetEntry>();
        foreach (var piece in pieces)
            foreach (var sha in new[] { piece.Mesh, piece.Texture })
                if (sha != null && had!.Assets.TryGetValue(sha, out var kept)) entries[sha] = kept;

        var atlases = new Dictionary<string, (Atlas Atlas, string Sha)>(StringComparer.Ordinal);
        foreach (var model in read)
        {
            var set = string.Join(",", model.Textures.Select(t => t.Sha).Distinct().OrderBy(t => t, StringComparer.Ordinal));
            if (!atlases.TryGetValue(set, out var own))
            {
                var made = ModelAtlas.Pack(model.Textures.DistinctBy(t => t.Sha).ToList(), AtlasMost);
                var madeSha = Convert.ToHexString(SHA256.HashData(made.Png)).ToLowerInvariant();
                var madeFile = Path.Combine(assetsDir, madeSha + ".png");
                if (!File.Exists(madeFile)) Json.WriteAtomic(madeFile, made.Png);
                entries[madeSha] = new AssetEntry { File = $"assets/{madeSha}.png", Kind = "image", W = made.Width, H = made.Height };
                atlases[set] = own = (made, madeSha);
            }
            var (atlas, atlasSha) = own;
            var obj = ModelAtlas.Obj(model, atlas.Places, atlas.Width, atlas.Height);
            var bytes = new UTF8Encoding(false).GetBytes(obj);
            var meshSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var meshFile = Path.Combine(assetsDir, meshSha + ".obj");
            if (!File.Exists(meshFile)) Json.WriteAtomic(meshFile, bytes);
            entries[meshSha] = new AssetEntry { File = $"assets/{meshSha}.obj", Kind = "mesh" };
            pieces.Add(new PieceDef
            {
                Id = $"{PackId}.{Slug(model.Name)}-{model.Sha[..8].ToLowerInvariant()}",
                Name = Pretty(model.Name),
                Kind = "model",
                HeightScale = 1,
                Mesh = meshSha,
                Texture = atlasSha,
                Size = new Size2(Math.Round(model.High[0] - model.Low[0], Digits), Math.Round(model.High[2] - model.Low[2], Digits)),
                Shape = "box",
                Thickness = Math.Round(model.High[1] - model.Low[1], Digits),
                ModuleOrigin = Math.Round(-model.Low[1], Digits),
                Tags = new List<string> { "model", Tag + model.Sha, ReadWith },
            });
        }

        pieces = pieces.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        var manifest = new PackManifest
        {
            Id = PackId,
            Name = had?.Name is { Length: > 0 } name ? name : PackName,
            Source = new PackSource { License = Licence },
            Pieces = pieces,
            Assets = entries,
        };
        Json.Save(packPath, manifest);

        var keep = new HashSet<string>(pieces.SelectMany(p => new[] { p.Mesh, p.Texture }).Where(s => s != null).Select(s => s!));
        foreach (var file in Directory.EnumerateFiles(assetsDir))
            if (!keep.Contains(Path.GetFileNameWithoutExtension(file)))
                BestEffort.DeleteFile(file);

        Console.WriteLine($"  {read.Count} model(s) read from asset bundles, with {atlases.Count} texture(s) of their own");
        return read.Count;
    }

    public static string Dir(string cache) => Path.Combine(cache, PackId);

    public static bool Bakes(string pack) => pack is PackId or Shared;

    Dictionary<string, string> SharedFiles(List<string> links)
    {
        var shared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
            if (OnThisPc(link) is { } file)
                foreach (var name in BundleScene.FilesIn(file))
                    shared.TryAdd(name, file);
        return shared;
    }

    string? OnThisPc(string url)
    {
        var entry = _assets.Get(url);
        var file = entry == null ? null : _assets.FilePath(entry);
        return file != null && File.Exists(file) ? file : null;
    }

    List<BundleModel> ReadAll(List<(string Sha, string Url)> todo, Dictionary<string, string> shared)
    {
        var read = new List<BundleModel>();
        using var reader = new BundleScene.Reader(shared);
        foreach (var (sha, url) in todo)
        {
            var file = OnThisPc(url);
            if (file == null)
            {
                _rep.Note($"The asset bundle {sha[..12]} is not on this PC, so the model in it is not here.");
                continue;
            }
            var (model, lost, failed) = reader.Read(file, sha);
            if (failed != null)
            {
                _rep.Note($"The asset bundle {sha[..12]} could not be read, so the model in it is not here ({failed}).");
                continue;
            }
            if (model == null)
            {
                if (lost > 0) _rep.Note($"The asset bundle {sha[..12]} holds {lost} model part(s) this PC cannot read, and nothing else, so nothing came of it.");
                continue;
            }
            if (lost > 0) _rep.Note($"{lost} part(s) of '{model.Name}' lean on Unity's own resources and are not in the bundle; the rest is read.");
            read.Add(model);
        }
        return read;
    }

    static string Slug(string name)
    {
        var slug = NonWord().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length > 0 ? slug : "model";
    }

    static string Pretty(string name)
    {
        var text = name.Replace('_', ' ').Trim();
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : "Model";
    }

    [GeneratedRegex("[0-9A-Fa-f]{40}")] private static partial Regex Sha();
    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonWord();
}
