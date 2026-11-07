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
        foreach (var sha in Naming.AssetsOf(copy).Distinct().ToList())
            if (source.Assets.TryGetValue(sha, out var a) && AssetFile(from, a) is { } file)
                DefFactory.Put(pack, sha, Path.GetExtension(a.File).TrimStart('.'), file, a.Kind, a.W, a.H);
        mirrors.Apply(copy, pack, m);
        foreach (var f in copy.Faces) f.Up = m.Up(f.Up);
        copy.Id = $"{pack.Short}.{DefFactory.Slug(copy.Name)}-mirrored-{DefFactory.Hash8(key)}";
        pack.ByKey[key] = copy;
        pack.M.Pieces.Add(copy);
        return r with { Def = copy, Copied = true };
    }

    PackManifest? Manifest(string id)
    {
        if (_packs.TryGetValue(id, out var known)) return known;
        string? text = id == DefFactory.StdDicePack ? Embedded("pack.json") : BestEffort.Read<string?>(() => File.ReadAllText(Path.Combine(packagesRoot, id, "pack.json")), fallback: null);
        return _packs[id] = text == null ? null : BestEffort.Read<PackManifest?>(() => Json.Deserialize<PackManifest>(text), fallback: null);
    }

    string? AssetFile(string id, AssetEntry a)
    {
        if (id != DefFactory.StdDicePack)
        {
            var path = Path.Combine(packagesRoot, id, a.File);
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

    static string? Embedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{DefFactory.StdDicePack}/{name}");
        return stream == null ? null : new StreamReader(stream).ReadToEnd();
    }
}
