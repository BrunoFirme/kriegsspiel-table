using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public static class PieceFiles
{
    public static IEnumerable<(string Package, string Path)> Of(GameState view, PackageStore store)
    {
        var onTable = view.Layers.Values.SelectMany(l => l).Select(p => p.Def).Distinct().Select(store.Def).OfType<PieceDef>();
        foreach (var def in Naming.WithParts(onTable, store.Def))
        {
            if (store.PackOfDef(def.Id) is not { } pack) continue;
            foreach (var hash in Naming.AssetsOf(def).Distinct())
                if (pack.Assets.TryGetValue(hash, out var asset)) yield return (pack.Id, asset.File.Replace('\\', '/'));
        }
        if (Backdrops.IsPicture(view.Lighting.Backdrop)) yield return (UploadAsset.ReliefPackage, view.Lighting.Backdrop);
        var decals = view.Layers.Values.SelectMany(l => l).SelectMany(p => p.Decals).Select(d => d.Picture).Distinct().ToList();
        if (decals.Count == 0) yield break;
        var packs = view.Packs.Select(store.Pack).OfType<PackManifest>().ToList();
        foreach (var picture in decals)
            if (UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, picture)) yield return (UploadAsset.ReliefPackage, picture);
            else if (packs.FirstOrDefault(p => p.Assets.ContainsKey(picture)) is { } pack)
                yield return (pack.Id, pack.Assets[picture].File.Replace('\\', '/'));
    }
}
