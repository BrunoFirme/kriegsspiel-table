using System.Text.RegularExpressions;
using KTable.Core.Model;
using KTable.Server.Scripting;
using MoonSharp.Interpreter;

namespace KTable.Converter;

public sealed partial class ScenarioBuilder
{
    Dictionary<string, string> _uiPictures = new();

    void ReadPanelPictures(ModuleSave master, Scenario scenario, PackOut extras)
    {
        var where = $"{_cfg.Scenario.Id} panel picture";
        _uiPictures = PicturesOf(master.UiAssets, extras, where, new());
        scenario.UiPictures = new(_uiPictures);
        scenario.Ui = Panels(master.XmlUi, _uiPictures, extras, $"{_cfg.Scenario.Id} Global panels");
    }

    Dictionary<string, string> PicturesOf(List<(string Name, string Url)> assets, PackOut extras, string where, Dictionary<string, string> over)
    {
        var map = new Dictionary<string, string>(over);
        foreach (var (name, url) in assets)
            if (_defs.UiAsset(extras, url, where) is { } picture) map[name] = picture;
        return map;
    }

    List<UiNode> ObjectPanels(ModuleObj o, PackOut extras, string where) =>
        o.XmlUi.Trim().Length == 0 ? new() : Panels(o.XmlUi, PicturesOf(o.UiAssets, extras, where + " panel picture", _uiPictures), extras, where + " panels");

    List<UiNode> Panels(string xml, Dictionary<string, string> pictures, PackOut extras, string where)
    {
        if (xml.Trim().Length == 0) return new();
        var text = _defs.RewriteUrls(Regex.Replace(xml, @"<\?xml[^>]*\?>", ""), extras, where);
        List<UiNode> tree;
        try { tree = ScriptHost.ParseXml(text); }
        catch (ScriptRuntimeException e)
        {
            _rep.Note($"{where}: the module's panels do not read as XML ({e.Message}), so they are left out.");
            return new();
        }
        foreach (var n in tree) NamePictures(n, pictures);
        return tree;
    }

    static void NamePictures(UiNode n, Dictionary<string, string> pictures)
    {
        foreach (var (k, v) in n.Attributes.ToList())
            if (IsPictureAttribute(k) && pictures.TryGetValue(v.Trim(), out var picture)) n.Attributes[k] = picture;
        foreach (var c in n.Children) NamePictures(c, pictures);
    }

    static bool IsPictureAttribute(string name) =>
        name.Contains("image", StringComparison.OrdinalIgnoreCase) || name.Contains("icon", StringComparison.OrdinalIgnoreCase) || name.Equals("sprite", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> PanelPictures(IEnumerable<UiNode> tree) =>
        tree.SelectMany(n => n.Attributes.Where(a => IsPictureAttribute(a.Key)).Select(a => a.Value).Concat(PanelPictures(n.Children)));
}
