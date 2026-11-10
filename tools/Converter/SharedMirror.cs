using System.Reflection;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed class SharedMirror(string packagesRoot, string cache, PieceMirror mirrors)
{
    readonly Dictionary<string, PackManifest?> _packs = new(StringComparer.Ordinal);

    public DefResult Of(ModuleObj o, DefResult r, PackOut pack)
    {
        var m = Mirror.Of(o);
        if (!m.Any || r.FromPack is not { } from) return r;
        var key = $"shared|{r.Def.Id}{m.Key}";
        if (pack.ByKey.TryGetValue(key, out var known)) return r with { Def = known, Copied = true };
        if (Manifest(from) is not { } source) return r;
        var copy = Json.Deserialize<PieceDef>(Json.Serialize(r.Def));
        CopyAssets(copy, from, source, pack);
        mirrors.Apply(copy, pack, m);
        foreach (var f in copy.Faces) f.Up = m.Up(f.Up);
        return Keep(r, pack, copy, key, "mirrored");
    }

    public DefResult Shaped(ModuleObj b, DefResult r, PackOut pack)
    {
        const double Even = 0.01, Digits = 4;
        if (r.FromPack == null || r.Def.Kind != "model" || !r.Def.Tags.Any(t => t.StartsWith(BundleBake.Tag, StringComparison.Ordinal)) || Math.Abs(b.ScaleX) < DefFactory.NoScale) return r;
        double up = Math.Abs(b.ScaleY / b.ScaleX), deep = Math.Abs(b.ScaleZ / b.ScaleX);
        if (Math.Abs(up - 1) < Even && Math.Abs(deep - 1) < Even) return r;
        var copy = Json.Deserialize<PieceDef>(Json.Serialize(r.Def));
        copy.Size = new Size2(copy.Size.W, Math.Round(copy.Size.H * deep, (int)Digits));
        copy.Thickness = Math.Round(copy.Thickness * up, (int)Digits);
        copy.HeightScale = Math.Round((copy.HeightScale ?? 1) * up, (int)Digits);
        var from = r.Copied ? [] : new[] { r.FromPack };
        return Own(r, pack, copy, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"shaped|{r.Def.Id}|{up:0.###}|{deep:0.###}"), "shaped", from);
    }

    public DefResult Adopted(DefResult r, PackOut pack)
    {
        if (r.Copied || r.FromPack is not { } from || !BundleBake.Bakes(from)) return r;
        var copy = Json.Deserialize<PieceDef>(Json.Serialize(r.Def));
        return Own(r, pack, copy, $"figure|{from}|{r.Def.Id}", "figure", [from]) with { Figure = true };
    }

    public DefResult Own(DefResult r, PackOut pack, PieceDef changed, string key, string why, IEnumerable<string> from)
    {
        if (pack.ByKey.TryGetValue(key, out var known)) return r with { Def = known, Copied = true };
        foreach (var id in from)
        {
            if (Manifest(id) is not { } source) return r;
            CopyAssets(changed, id, source, pack);
        }
        return Keep(r, pack, changed, key, why);
    }

    static DefResult Keep(DefResult r, PackOut pack, PieceDef copy, string key, string why)
    {
        copy.Id = $"{pack.Short}.{DefFactory.Slug(copy.Name)}-{why}-{DefFactory.Hash8(key)}";
        copy.Tags.RemoveAll(t => t.StartsWith(BundleBake.Tag, StringComparison.Ordinal));
        pack.ByKey[key] = copy;
        pack.M.Pieces.Add(copy);
        return r with { Def = copy, Copied = true };
    }

    void CopyAssets(PieceDef copy, string from, PackManifest source, PackOut pack)
    {
        foreach (var sha in Naming.AssetsOf(copy).Distinct().ToList())
            if (source.Assets.TryGetValue(sha, out var a) && AssetFile(from, a) is { } file)
                DefFactory.Put(pack, sha, Path.GetExtension(a.File).TrimStart('.'), file, a.Kind, a.W, a.H);
    }

    PackManifest? Manifest(string id)
    {
        if (_packs.TryGetValue(id, out var known)) return known;
        string? text = id == DefFactory.StdDicePack ? Embedded("pack.json") : BestEffort.Read<string?>(() => File.ReadAllText(Path.Combine(Root(id), "pack.json")), fallback: null);
        return _packs[id] = text == null ? null : BestEffort.Read<PackManifest?>(() => Json.Deserialize<PackManifest>(text), fallback: null);
    }

    string? AssetFile(string id, AssetEntry a)
    {
        if (id != DefFactory.StdDicePack)
        {
            var path = Path.Combine(Root(id), a.File);
            return File.Exists(path) ? path : null;
        }
        var name = Path.GetFileName(a.File);
        var kept = Path.Combine(cache, "shared", id, name);
        if (File.Exists(kept)) return kept;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{id}/assets/{name}");
        if (stream == null) return null;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        Json.WriteAtomic(kept, bytes.ToArray());
        return kept;
    }

    string Root(string id) => id == BundleBake.PackId ? BundleBake.Dir(cache) : Path.Combine(packagesRoot, id);

    static string? Embedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{DefFactory.StdDicePack}/{name}");
        return stream == null ? null : new StreamReader(stream).ReadToEnd();
    }
}
