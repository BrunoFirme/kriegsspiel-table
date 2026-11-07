using System.Text.Json.Serialization;
using KTable.Core.Model;

namespace KTable.Core.Panels;

public sealed class UiLayout
{
    Dictionary<string, List<int>> _ids = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, string> _colors = UiAttributes.Fresh();
    public List<UiElement> Elements { get; set; } = new();
    public List<UiRule> Rules { get; set; } = new();
    public List<UiRule> Seen { get; set; } = new();
    public Dictionary<string, string> Colors { get => _colors; set => _colors = UiAttributes.Folded(value); }
    public Dictionary<string, List<int>> Ids { get => _ids; set => _ids = Folded(value); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, string>? Tooltip { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Broken { get; set; }

    static Dictionary<string, List<int>> Folded(Dictionary<string, List<int>>? from)
    {
        var d = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        if (from != null) foreach (var (k, v) in from) d[k] = v;
        return d;
    }

    public UiLayout Clone() => new()
    {
        Elements = Elements.Select(e => e.Clone()).ToList(), Rules = Rules.Select(r => r.Clone()).ToList(), Seen = Seen.Select(r => r.Clone()).ToList(),
        Colors = new(Colors, StringComparer.OrdinalIgnoreCase), Ids = Ids.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.OrdinalIgnoreCase),
        Tooltip = Tooltip == null ? null : new(Tooltip, StringComparer.OrdinalIgnoreCase), Broken = Broken,
    };

    public UiElement? ById(string id)
    {
        if (!Ids.TryGetValue(id, out var path)) return null;
        return At(path);
    }

    public UiElement? At(IReadOnlyList<int> path)
    {
        var list = Elements;
        UiElement? at = null;
        foreach (var i in path)
        {
            if (i < 0 || i >= list.Count) return null;
            at = list[i];
            list = at.Children;
        }
        return at;
    }

    public IEnumerable<UiElement> All() => Elements.SelectMany(Walk);

    static IEnumerable<UiElement> Walk(UiElement e) => new[] { e }.Concat(e.Children.SelectMany(Walk));
}

public sealed class UiElement
{
    Dictionary<string, string> _attributes = UiAttributes.Fresh();
    public string Tag { get; set; } = "Panel";
    public Dictionary<string, string> Attributes { get => _attributes; set => _attributes = UiAttributes.Folded(value); }
    public List<string> Own { get; set; } = new();
    public List<string> Classes { get; set; } = new();
    public List<UiElement> Children { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Shown { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Active { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Inner { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<UiNode>? Options { get; set; }

    public string? Get(string name) => Attributes.TryGetValue(name, out var v) ? v : null;

    public UiElement Clone() => new()
    {
        Tag = Tag, Attributes = new(Attributes, StringComparer.OrdinalIgnoreCase), Own = Own.ToList(), Classes = Classes.ToList(),
        Children = Children.Select(c => c.Clone()).ToList(), Shown = Shown, Active = Active, Inner = Inner, Options = Options?.Select(o => o.Clone()).ToList(),
    };
}

public sealed class UiRule
{
    Dictionary<string, string> _attributes = UiAttributes.Fresh();
    public string Type { get; set; } = "";
    public string Selector { get; set; } = "all";
    public int Order { get; set; }
    public Dictionary<string, string> Attributes { get => _attributes; set => _attributes = UiAttributes.Folded(value); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Key { get; set; }

    public UiRule Clone() => new() { Type = Type, Selector = Selector, Order = Order, Attributes = new(Attributes, StringComparer.OrdinalIgnoreCase), Key = Key };
}

public sealed class UiAsset
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int Type { get; set; }

    public UiAsset Clone() => new() { Name = Name, Url = Url, Type = Type };
}
