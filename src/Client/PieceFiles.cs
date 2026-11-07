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
        var decals = view.Layers.Values.SelectMany(l => l).SelectMany(p => p.Decals.Select(d => d.Picture).Concat(p.FacePicture is { } fp ? [fp] : []))
            .Concat(view.Decals.Values.SelectMany(l => l).Select(d => d.Picture)).Concat(view.DecalPalette.Select(d => d.Picture)).Distinct().ToList();
        foreach (var picture in decals)
            if (DecalFile(view, store, picture) is { } file) yield return file;
    }

    public static (string Package, string Path)? DecalFile(GameState view, PackageStore store, string picture, string? ownDef = null)
    {
        if (UploadAsset.IsWebPicture(UploadAsset.ReliefPackage, picture)) return (UploadAsset.ReliefPackage, picture);
        var own = ownDef == null ? null : store.PackOfDef(ownDef);
        var packs = view.Packs.Select(store.Pack).OfType<PackManifest>();
        foreach (var p in own == null ? packs : packs.Prepend(own))
            if (p.Assets.TryGetValue(picture, out var a)) return (p.Id, a.File.Replace('\\', '/'));
        return null;
    }
}
