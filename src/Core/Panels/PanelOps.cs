using KTable.Core.Model;

namespace KTable.Core.Panels;

public static class PanelOps
{
    public const string SetAttribute = "attribute", SetAttributes = "attributes", Show = "show", Hide = "hide", SetClass = "class", SetValue = "value";
    public const string On = "on", Off = "off", Seen = "seen";
    public const string ClassWarning = "[XmlLayout][XmlElement][SetAttribute]:: Please use 'SetClass', 'AddClass', and/or 'RemoveClass' to manipulate the class attribute.";

    public static List<string> Run(List<UiNode>? data, UiLayout? layout, string kind, string id, IReadOnlyDictionary<string, string> attrs, string? value)
    {
        var said = new List<string>();
        var node = PanelData.ById(data, id);
        var path = layout != null && layout.Ids.TryGetValue(id, out var p) ? p : null;
        var e = path == null ? null : layout!.At(path);
        switch (kind)
        {
            case SetAttribute or SetAttributes:
                if (node != null) foreach (var (k, v) in attrs) node.Attributes[k] = v;
                if (e == null) break;
                if (kind == SetAttribute) foreach (var (k, v) in attrs) SetAndApply(e, k, v, layout, path, said);
                else Apply(e, attrs, layout, path);
                break;
            case Show or Hide:
                if (node != null) node.Attributes["active"] = kind == Show ? "true" : "false";
                if (e != null) ShowOrHide(layout!, path!, e, kind == Show);
                break;
            case SetClass:
                if (node != null) node.Attributes["class"] = value ?? "";
                if (e != null) ClassTo(layout!, path!, e, (value ?? "").Contains(' ') ? (value ?? "").Split(' ').ToList() : new List<string> { value ?? "" });
                break;
            case SetValue:
                if (node != null) node.Value = value;
                if (e != null && e.Tag == PanelTags.Text) e.Shown = PanelText.FromValue(value ?? "");
                break;
        }
        return said;
    }

    public static void SetOwn(UiElement e, string name, string value, List<string>? said = null)
    {
        if (name == "class")
        {
            said?.Add(ClassWarning);
            return;
        }
        e.Attributes[name] = value;
        if (!e.Own.Contains(name, StringComparer.InvariantCultureIgnoreCase)) e.Own.Add(name);
    }

    public static void SetAndApply(UiElement e, string name, string value, UiLayout? layout = null, List<int>? path = null, List<string>? said = null)
    {
        SetOwn(e, name, value, said);
        var one = UiAttributes.Fresh();
        one[name] = value;
        if (layout != null && path != null && name.Equals("id", StringComparison.OrdinalIgnoreCase) && value.Length > 0) PanelIds.Register(layout, value, path);
        Effects(e, one);
    }

    public static void Apply(UiElement e, IReadOnlyDictionary<string, string> attrs, UiLayout? layout = null, List<int>? path = null)
    {
        foreach (var (k, v) in attrs) e.Attributes[k] = v;
        if (layout != null && path != null && attrs.TryGetValue("id", out var id) && id.Length > 0) PanelIds.Register(layout, id, path);
        Effects(e, attrs);
    }

    static void Effects(UiElement e, IReadOnlyDictionary<string, string> applied)
    {
        foreach (var k in applied.Keys)
        {
            if (k.Equals("active", StringComparison.OrdinalIgnoreCase) || k.Equals("visibility", StringComparison.OrdinalIgnoreCase)) Activate(e);
        }
        PanelTags.AfterApply(e, applied);
    }

    public static void Activate(UiElement e)
    {
        if (e.Attributes.ContainsKey("visibility")) e.Active = Seen;
        else e.Active = Bool(e.Get("active")) ? On : Off;
    }

    public static bool Bool(string? s) => s != null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");

    static void ShowOrHide(UiLayout layout, List<int> path, UiElement e, bool show)
    {
        bool shownAbove = Above(layout, path).All(a => a.Active != Off);
        if (show)
        {
            e.Active = On;
            if (shownAbove) SetOwn(e, "active", "true");
            return;
        }
        if (!shownAbove || e.Active == Off) return;
        e.Active = Off;
        SetOwn(e, "active", "false");
    }

    static IEnumerable<UiElement> Above(UiLayout layout, List<int> path)
    {
        for (int n = 1; n < path.Count; n++) if (layout.At(path.Take(n).ToList()) is { } a) yield return a;
    }

    public static void AddClass(UiLayout layout, List<int> path, string cls)
    {
        if (layout.At(path) is not { } e || e.Classes.Contains(cls)) return;
        ClassTo(layout, path, e, e.Classes.Append(cls).ToList());
    }

    public static void RemoveClass(UiLayout layout, List<int> path, string cls)
    {
        if (layout.At(path) is not { } e || !e.Classes.Contains(cls)) return;
        ClassTo(layout, path, e, e.Classes.Where(c => c != cls).ToList());
    }

    static void ClassTo(UiLayout layout, List<int> path, UiElement e, List<string> classes)
    {
        var removed = e.Classes.Where(c => !classes.Contains(c)).ToList();
        e.Classes = classes;
        e.Attributes["class"] = string.Join(" ", classes);
        ClassRemoved(layout, e, removed);
        ClassChanged(layout, Chain(layout, path), e);
    }

    static PanelSelectors.Chain Chain(UiLayout layout, List<int> path)
    {
        PanelSelectors.Chain c = new(null, null, "XmlLayout");
        for (int n = 1; n <= path.Count; n++) c = new PanelSelectors.Chain(layout.At(path.Take(n).ToList()), c);
        return c;
    }

    static void ClassChanged(UiLayout layout, PanelSelectors.Chain self, UiElement e)
    {
        if (layout.Rules.Any(r => r.Type.Equals(e.Tag, StringComparison.OrdinalIgnoreCase)))
        {
            var own = UiAttributes.Fresh();
            foreach (var k in e.Own) own[k] = e.Get(k) ?? "";
            var merged = PanelSelectors.Merge(layout, self, e.Tag, own, e.Classes);
            var add = UiAttributes.Fresh();
            foreach (var (k, v) in merged) if (!e.Own.Contains(k)) add[k] = v;
            Apply(e, add);
        }
        foreach (var c in e.Children) ClassChanged(layout, new PanelSelectors.Chain(c, self), c);
    }

    static void ClassRemoved(UiLayout layout, UiElement e, List<string> removed)
    {
        if (!layout.Rules.Any(r => r.Type.Equals(e.Tag, StringComparison.OrdinalIgnoreCase))) return;
        var reset = new List<string>();
        foreach (var cls in removed)
            if (PanelSelectors.Rule(layout, e.Tag, cls) is { } rule) reset.AddRange(rule.Attributes.Keys.Where(a => !e.Own.Contains(a)));
        if (reset.Count == 0) return;
        foreach (var cls in new[] { "all" }.Concat(e.Classes))
            if (PanelSelectors.Rule(layout, e.Tag, cls) is { } kept) reset.RemoveAll(a => kept.Attributes.ContainsKey(a));
        if (reset.Count == 0) return;
        var back = UiAttributes.Fresh();
        foreach (var a in reset) back[a] = PanelResets.Of(e.Tag, a);
        Apply(e, back);
    }
}

public static class PanelIds
{
    public static void Register(UiLayout layout, string id, List<int> path)
    {
        var old = layout.Ids.FirstOrDefault(p => p.Value.SequenceEqual(path)).Key;
        if (old != null && !old.Equals(id, StringComparison.OrdinalIgnoreCase)) layout.Ids.Remove(old);
        layout.Ids[id] = path;
    }
}
