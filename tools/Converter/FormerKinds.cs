using System.Globalization;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public static class FormerKinds
{
    public sealed record Carried(int Updated, int Kept);

    static readonly Func<PieceDef, string?>[] Signs =
    [
        d => d.Mesh,
        d => d.Texture ?? d.Image,
        d => d.Image,
        d => d.Back,
        d => d.ScriptHash,
        d => string.Create(CultureInfo.InvariantCulture, $"{d.Kind}|{d.Shape}|{d.Size.W:0.###}x{d.Size.H:0.###}"),
    ];

    public static Carried Carry(string packDir, string? formerPackDir, Report rep)
    {
        var file = Path.Combine(packDir, "pack.json");
        var formerFile = formerPackDir == null ? null : Path.Combine(formerPackDir, "pack.json");
        if (formerFile == null || !File.Exists(formerFile) || !File.Exists(file)) return new(0, 0);
        PackManifest former;
        try { former = Json.Load<PackManifest>(formerFile); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            rep.Note($"The pack that was here could not be read ({e.Message}), so pieces already placed from it keep their old kinds only if it is restored from the trash");
            return new(0, 0);
        }
        var fresh = Json.Load<PackManifest>(file);
        var carried = Into(fresh, former, a => CopyAsset(formerPackDir!, packDir, a));
        Json.Save(file, fresh);
        if (carried.Updated > 0) rep.Note($"{carried.Updated} kinds changed in the module: pieces of them already placed in saves take the new look");
        if (carried.Kept > 0) rep.Note($"{carried.Kept} kinds are gone from the module: kept in the pack so pieces already placed still show, and not offered in Pieces");
        return carried;
    }

    static bool CopyAsset(string fromDir, string toDir, AssetEntry a)
    {
        var to = Path.Combine(toDir, a.File);
        if (File.Exists(to)) return true;
        var from = Path.Combine(fromDir, a.File);
        if (!File.Exists(from)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to);
        return true;
    }

    public static Carried Into(PackManifest fresh, PackManifest former, Func<AssetEntry, bool> copy)
    {
        var now = fresh.Pieces.DistinctBy(d => d.Id).ToDictionary(d => d.Id, StringComparer.Ordinal);
        var old = former.Pieces.DistinctBy(d => d.Id).ToDictionary(d => d.Id, StringComparer.Ordinal);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        var open = fresh.Pieces.Where(d => !old.ContainsKey(d.Id)).ToList();
        var left = new List<PieceDef>();
        foreach (var o in old.Values)
        {
            if (now.TryGetValue(o.Id, out var same)) Remember(same, o.Formerly ?? [], now);
            else if (!o.Retired) left.Add(o);
        }
        foreach (var group in left.GroupBy(d => d.Name, StringComparer.Ordinal))
            foreach (var (o, n) in Pairs(group.ToList(), open.Where(d => d.Name == group.Key).ToList()))
            {
                renamed[o.Id] = n.Id;
                Remember(n, (o.Formerly ?? []).Prepend(o.Id), now);
                open.Remove(n);
            }
        var keep = old.Values.Where(o => !now.ContainsKey(o.Id) && !renamed.ContainsKey(o.Id)).ToList();
        var kept = keep.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        for (int i = 0; i < keep.Count; i++)
            foreach (var id in References(keep[i]))
                if (!now.ContainsKey(id) && !renamed.ContainsKey(id) && old.TryGetValue(id, out var part) && kept.Add(id)) keep.Add(part);
        foreach (var d in keep)
        {
            Retarget(d, id => renamed.GetValueOrDefault(id, id));
            d.Retired = true;
            fresh.Pieces.Add(d);
            foreach (var h in Naming.AssetsOf(d).Distinct())
                if (!fresh.Assets.ContainsKey(h) && former.Assets.TryGetValue(h, out var a) && copy(a)) fresh.Assets[h] = a;
        }
        return new(renamed.Count, keep.Count);
    }

    static IEnumerable<(PieceDef Old, PieceDef New)> Pairs(List<PieceDef> olds, List<PieceDef> news)
    {
        foreach (var sign in Signs)
        {
            if (olds.Count <= 1 || news.Count <= 1) break;
            foreach (var o in olds.ToList())
            {
                if (sign(o) is not { } s) continue;
                var mine = olds.Where(x => sign(x) == s).ToList();
                var theirs = news.Where(x => sign(x) == s).ToList();
                if (mine.Count != 1 || theirs.Count != 1) continue;
                yield return (o, theirs[0]);
                olds.Remove(o);
                news.Remove(theirs[0]);
            }
        }
        if (olds.Count == 1 && news.Count == 1) yield return (olds[0], news[0]);
    }

    static void Remember(PieceDef d, IEnumerable<string> ids, Dictionary<string, PieceDef> now)
    {
        var all = (d.Formerly ?? []).Concat(ids).Where(id => id != d.Id && !now.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
        d.Formerly = all.Count > 0 ? all : null;
    }

    static IEnumerable<string> References(PieceDef d) =>
        d.Attachments.Select(a => a.Piece).Concat(d.AllParts.Select(p => p.Def)).Concat(d.Counters.Select(c => c.SpawnsMarker).OfType<string>());

    static void Retarget(PieceDef d, Func<string, string> to)
    {
        foreach (var a in d.Attachments) a.Piece = to(a.Piece);
        foreach (var p in d.AllParts) p.Def = to(p.Def);
        foreach (var c in d.Counters) if (c.SpawnsMarker != null) c.SpawnsMarker = to(c.SpawnsMarker);
    }
}
