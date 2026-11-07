using System.Text.RegularExpressions;
using System.Xml;
using KTable.Core.Model;

namespace KTable.Core.Panels;

public sealed partial class PanelBuild
{
    void Defaults(XmlReader reader)
    {
        while (reader.Read())
        {
            if (!reader.IsStartElement()) continue;
            var name = reader.Name;
            var attrs = Attributes(reader);
            switch (name)
            {
                case "Color":
                    Color(attrs);
                    break;
                case "Tooltip":
                    _layout.Tooltip = attrs;
                    break;
                case "TextMeshProMaterial" or "Animation":
                    break;
                default:
                    Rule(name, attrs);
                    break;
            }
        }
    }

    void Color(Dictionary<string, string> attrs)
    {
        if (!attrs.ContainsKey("name") || !attrs.ContainsKey("color"))
        {
            _said.Add("[XmlLayout] Warning: Named Color tag without a name and/or color - both are required.");
            return;
        }
        _layout.Colors[attrs["name"]] = attrs["color"];
    }

    void Rule(string type, Dictionary<string, string> attrs)
    {
        if (PanelTags.Find(type) == null)
        {
            _said.Add($"[XmlLayout] Unknown tag '{type}'.\r\nTag Handlers must inherit from 'ElementTagHandler', and must be named {{Tag}}TagHandler.");
            return;
        }
        var classes = attrs.TryGetValue("class", out var c) ? ClassList(c) : new List<string> { "all" };
        foreach (var cls in classes)
        {
            var rule = _layout.Rules.FirstOrDefault(r => r.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && r.Selector.Equals(cls, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                int order = _layout.Rules.Count(r => r.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
                rule = new UiRule { Type = type, Selector = cls, Order = order };
                _layout.Rules.Add(rule);
            }
            var merged = UiAttributes.Folded(rule.Attributes);
            foreach (var (k, v) in attrs) merged[k] = v;
            merged.Remove("class");
            rule.Attributes = merged;
        }
    }

    Dictionary<string, string> Merge(UiElement e, Parent at, string type, Dictionary<string, string> own) =>
        PanelSelectors.Merge(_layout, new PanelSelectors.Chain(e, Chain(at)), type, own, e.Classes);

    static PanelSelectors.Chain? Chain(Parent p) => p.Element == null ? new PanelSelectors.Chain(null, null, p.Tag) : new PanelSelectors.Chain(p.Element, p.Up == null ? null : Chain(p.Up));
}

public static class PanelSelectors
{
    public sealed record Chain(UiElement? Element, Chain? Up, string? Tag = null)
    {
        public string TagType => Element?.Tag ?? Tag ?? "";
    }

    static readonly Regex Splitter = new("(?<=[>:])");

    public static Dictionary<string, string> Merge(UiLayout layout, Chain self, string type, Dictionary<string, string> own, List<string> classes)
    {
        if (!layout.Rules.Any(r => r.Type.Equals(type, StringComparison.OrdinalIgnoreCase))) return own;
        var merged = UiAttributes.Folded(Rule(layout, type, "all")?.Attributes);
        var list = own.TryGetValue("class", out var c) ? PanelBuild.ClassList(c) : new List<string>();
        list.AddRange(classes);
        list.Remove("all");
        if (list.Count > 1) list = list.Distinct().OrderBy(x => Rule(layout, type, x)?.Order ?? int.MaxValue).ToList();
        list.Insert(0, "all");
        foreach (var item in list)
            foreach (var rule in Potential(layout, type, item))
                if (Matches(rule.Selector, item, self)) foreach (var (k, v) in rule.Attributes) merged[k] = v;
        foreach (var (k, v) in own) merged[k] = v;
        return merged;
    }

    public static UiRule? Rule(UiLayout layout, string type, string selector) =>
        layout.Rules.FirstOrDefault(r => r.Type.Equals(type, StringComparison.OrdinalIgnoreCase) && r.Selector.Equals(selector, StringComparison.OrdinalIgnoreCase));

    static List<UiRule> Potential(UiLayout layout, string type, string cls)
    {
        var key = $"{type}>{cls}";
        var seen = layout.Seen.Where(r => r.Key == key).ToList();
        if (seen.Count > 0 || layout.Seen.Any(r => r.Key == key && r.Selector.Length == 0)) return seen.Where(r => r.Selector.Length > 0).ToList();
        var tagSelector = ("@" + type).ToLowerInvariant();
        var found = layout.Rules.Where(r => r.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
            && (r.Selector.EndsWith(cls, StringComparison.OrdinalIgnoreCase) || r.Selector.EndsWith(tagSelector, StringComparison.OrdinalIgnoreCase))).Select(r => r.Clone()).ToList();
        foreach (var r in found) r.Key = key;
        layout.Seen.AddRange(found);
        if (found.Count == 0) layout.Seen.Add(new UiRule { Type = type, Selector = "", Key = key });
        return found;
    }

    static bool Matches(string selector, string item, Chain self)
    {
        var parts = Splitter.Split(selector);
        Array.Reverse(parts);
        if (parts.Length == 1) return Strip(parts[0]).Equals(item, StringComparison.OrdinalIgnoreCase);
        Chain? at = self;
        foreach (var part in parts)
        {
            var name = Strip(part);
            bool isTag = name.StartsWith('@');
            if (isTag) name = name[1..];
            if (part.EndsWith('>')) at = at?.Up;
            else if (part.EndsWith(':'))
            {
                bool found = false;
                while (!found && at != null)
                {
                    at = at.Up;
                    if (at != null) found = isTag ? at.TagType.Equals(name, StringComparison.OrdinalIgnoreCase) : HasClass(at, name);
                }
            }
            if (at == null) return false;
            if (isTag ? !at.TagType.Equals(name, StringComparison.OrdinalIgnoreCase) : !HasClass(at, name)) return false;
        }
        return true;
    }

    static string Strip(string s) => s.Replace(">", "").Replace(":", "");

    public static bool HasClass(Chain c, string cls)
    {
        if (c.Element == null) return false;
        if (c.Element.Classes.Count == 0 && c.Element.Attributes.TryGetValue("class", out var attr)) c.Element.Classes.AddRange(attr.Split(' ', ','));
        return c.Element.Classes.Any(x => string.Equals(x, cls, StringComparison.OrdinalIgnoreCase));
    }
}
