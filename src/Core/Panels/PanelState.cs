using KTable.Core.Model;

namespace KTable.Core.Panels;

public static class PanelState
{
    public static void Ensure(GameState g)
    {
        if (g.UiLayout == null && g.Ui.Count > 0) g.UiLayout = Legacy(g.Ui);
        if (g.UiAssets == null && g.UiPictures.Count > 0) g.UiAssets = Assets(g.UiPictures);
        foreach (var p in g.Layers.Values.SelectMany(l => l)) Ensure(p);
    }

    public static void Ensure(PieceInstance p)
    {
        if (p.UiLayout == null && p.Ui.Count > 0) p.UiLayout = Legacy(p.Ui);
        foreach (var s in p.Stowed ?? []) Ensure(s);
    }

    public static UiLayout Legacy(List<UiNode> tree) => PanelBuild.Of(PanelData.Write(tree)).Layout;

    public const int ImageType = 0, BundleType = 1;
    public const string NeverLoads = "an asset of a type the tabletop does not load keeps the panels loading for ever";

    public static bool Loads(IEnumerable<UiAsset>? assets) => assets == null || assets.All(a => a.Type is ImageType or BundleType);

    public static List<UiAsset> Assets(Dictionary<string, string> pictures) => pictures.Select(p => new UiAsset { Name = p.Key, Url = p.Value }).ToList();
}
