using KTable.Core;
using KTable.Core.Model;

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
    }
}
