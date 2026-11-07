using KTable.Core.Model;
using KTable.Core.Panels;

namespace KTable.Core.Protocol;

public static partial class StateMachine
{
    static string? ApplySetUi(GameState g, SetUi su, ref Command? inverse)
    {
        var root = g.UiRoot(su.Layer, su.Piece);
        if (root == null) return "no such piece";
        var piece = su.Piece == null ? null : g.Find(su.Layer ?? Layers.Umpire, su.Piece);
        var layout = piece == null ? g.UiLayout : piece.UiLayout;
        var assets = piece == null ? g.UiAssets : piece.UiAssets;
        inverse = new SetUi
        {
            Layer = su.Layer, Piece = su.Piece, Tree = root.Select(n => n.Clone()).ToList(), Layout = layout?.Clone() ?? new UiLayout(),
            Assets = su.Assets == null ? null : assets?.Select(a => a.Clone()).ToList() ?? new(),
        };
        List<UiNode> tree;
        UiLayout built;
        if (su.Xml != null)
        {
            built = PanelBuild.Of(su.Xml).Layout;
            tree = built.Broken == null ? PanelData.Parse(su.Xml) ?? new() : root.Select(n => n.Clone()).ToList();
        }
        else
        {
            tree = su.Tree.Select(n => n.Clone()).ToList();
            built = su.Layout?.Clone() ?? PanelState.Legacy(tree);
        }
        var newAssets = su.Assets?.Select(a => a.Clone()).ToList();
        if (piece == null)
        {
            g.Ui = tree;
            g.UiLayout = built;
            if (newAssets != null) g.UiAssets = newAssets;
        }
        else
        {
            piece.Ui = tree;
            piece.UiLayout = built;
            if (newAssets != null) piece.UiAssets = newAssets;
        }
        return null;
    }

    static string? ApplySetUiPictures(GameState g, SetUiPictures sup, ref Command? inverse)
    {
        inverse = new SetUiPictures { Pictures = new(g.UiPictures) };
        g.UiPictures = new(sup.Pictures);
        return null;
    }

    static string? ApplySetUiAttributes(GameState g, SetUiAttributes sua, ref Command? inverse)
    {
        var data = g.UiRoot(sua.Layer, sua.Piece);
        if (data == null) return "no such piece";
        var piece = sua.Piece == null ? null : g.Find(sua.Layer ?? Layers.Umpire, sua.Piece);
        var layout = piece == null ? g.UiLayout : piece.UiLayout;
        if (sua.Kind == null) return Legacy(data, layout, sua, ref inverse);
        if (sua.Kind == "restore") return Restore(data, layout, sua, ref inverse);
        if (sua.Kind is not (PanelOps.SetAttribute or PanelOps.SetAttributes or PanelOps.Show or PanelOps.Hide or PanelOps.SetClass or PanelOps.SetValue)) return "unknown panel change";
        var dataPath = PanelData.PathOf(data, sua.Id);
        var node = dataPath == null ? null : PanelData.At(data, dataPath);
        var path = layout != null && layout.Ids.TryGetValue(sua.Id, out var p) ? p.ToList() : null;
        var element = path == null ? null : layout!.At(path);
        inverse = new SetUiAttributes
        {
            Layer = sua.Layer, Piece = sua.Piece, Id = sua.Id, Kind = "restore", Data = node == null ? null : Shallow(node), DataPath = dataPath,
            Live = element?.Clone(), LivePath = path, Ids = layout?.Ids.ToDictionary(x => x.Key, x => x.Value.ToList()),
        };
        PanelOps.Run(data, layout, sua.Kind, sua.Id, sua.Attributes, sua.Value);
        return null;
    }

    static UiNode Shallow(UiNode n) => new() { Tag = n.Tag, Attributes = new(n.Attributes, StringComparer.OrdinalIgnoreCase), Value = n.Value };

    static string? Restore(List<UiNode> data, UiLayout? layout, SetUiAttributes sua, ref Command? inverse)
    {
        if (sua.Data != null && sua.DataPath != null && PanelData.At(data, sua.DataPath) is { } node)
        {
            node.Attributes = sua.Data.Attributes;
            node.Value = sua.Data.Value;
        }
        if (layout != null && sua.LivePath is { Count: > 0 } path && sua.Live != null)
        {
            var list = path.Count == 1 ? layout.Elements : layout.At(path.Take(path.Count - 1).ToList())?.Children;
            if (list != null && path[^1] < list.Count) list[path[^1]] = sua.Live.Clone();
        }
        if (layout != null && sua.Ids != null) layout.Ids = sua.Ids.ToDictionary(x => x.Key, x => x.Value.ToList());
        inverse = null;
        return null;
    }

    static string? Legacy(List<UiNode> data, UiLayout? layout, SetUiAttributes sua, ref Command? inverse)
    {
        var node = PanelData.ById(data, sua.Id);
        if (node == null) return "no such element";
        var old = new Dictionary<string, string>();
        foreach (var (k, v) in sua.Attributes)
        {
            old[k] = node.Attributes.TryGetValue(k, out var ov) ? ov : "";
            node.Attributes[k] = v;
        }
        if (layout?.ById(sua.Id) is { } e) PanelOps.Apply(e, sua.Attributes);
        inverse = new SetUiAttributes { Layer = sua.Layer, Piece = sua.Piece, Id = sua.Id, Attributes = old };
        return null;
    }

    static string? ApplyUiEvent(GameState g, UiEvent ue) => g.UiRoot(ue.Layer, ue.Piece) == null ? "no such piece" : null;
}
