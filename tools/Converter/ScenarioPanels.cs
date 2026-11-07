using System.Text.RegularExpressions;
using KTable.Core.Model;
using KTable.Core.Panels;

namespace KTable.Converter;

public sealed partial class ScenarioBuilder
{
    static readonly Regex PanelLink = new(@"https?://[^""'<>\s]+");
    readonly Dictionary<string, string> _uiLinks = new();

    void ReadPanelPictures(ModuleSave master, Scenario scenario, PackOut extras)
    {
        var where = $"{_cfg.Scenario.Id} panel picture";
        scenario.UiAssets = AssetsOf(master.UiAssetStates, extras, where);
        scenario.Ui = Panels(master.XmlUi, extras, $"{_cfg.Scenario.Id} Global panels", out var layout);
        scenario.UiLayout = layout;
        if (layout != null && !PanelState.Loads(scenario.UiAssets)) (scenario.Ui, scenario.UiLayout) = (new(), Stuck());
    }

    List<UiAsset>? AssetsOf(List<(string Name, string Url, int Type)> assets, PackOut extras, string where)
    {
        if (assets.Count == 0) return null;
        return assets.Select(a => new UiAsset
        {
            Name = a.Name, Type = a.Type,
            Url = a.Type == 0 && a.Name.Trim().Length > 0 && a.Url.Trim().Length > 0 ? _defs.UiAsset(extras, a.Url.Trim(), where) ?? a.Url : a.Url,
        }).ToList();
    }

    List<UiNode> ObjectPanels(ModuleObj o, PackOut extras, string where, out UiLayout? layout, out List<UiAsset>? assets)
    {
        assets = AssetsOf(o.UiAssetStates, extras, where + " panel picture");
        layout = null;
        var ui = o.XmlUi.Trim().Length == 0 ? new() : Panels(o.XmlUi, extras, where + " panels", out layout);
        if (layout == null || PanelState.Loads(assets)) return ui;
        layout = Stuck();
        return new();
    }

    List<UiNode> Panels(string xml, PackOut extras, string where, out UiLayout? layout)
    {
        layout = null;
        if (PanelData.IsNothing(xml)) return new();
        foreach (Match m in PanelLink.Matches(xml))
            if (!_uiLinks.ContainsKey(m.Value) && _defs.UiAsset(extras, m.Value, where) is { } picture) _uiLinks[m.Value] = picture;
        var (built, _) = PanelBuild.Of(xml);
        layout = built;
        if (built.Broken != null)
        {
            _rep.Note($"{where}: the module's panels do not build in its digital tabletop either ({built.Broken}), so none come over.");
            return new();
        }
        return PanelData.Parse(xml) ?? new();
    }

    static UiLayout Stuck() => new() { Broken = PanelState.NeverLoads };

    static IEnumerable<string> PanelAssets(Scenario scenario, IEnumerable<PieceInstance> pieces) =>
        (scenario.UiAssets ?? new()).Concat(pieces.SelectMany(p => p.UiAssets ?? new())).Select(a => a.Url).Concat(scenario.UiLinks?.Values ?? Enumerable.Empty<string>());
}
