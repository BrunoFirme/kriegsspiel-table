using System.Xml;
using KTable.Core.Model;

namespace KTable.Core.Panels;

public sealed partial class PanelBuild
{
    public const string Inserted = "<Defaults><Color name=\"white\" color=\"#FFFFFF\"/><Color name=\"brown\" color=\"#713B17\\\"/><Color name=\"red\" color=\"#DA1918\"/>"
        + "<Color name=\"orange\" color=\"#F4641D\"/><Color name=\"yellow\" color=\"#E7E52C\"/><Color name=\"green\" color=\"#31B32B\"/><Color name=\"teal\" color=\"#21B19B\"/>"
        + "<Color name=\"blue\" color=\"#1F87FF\"/><Color name=\"purple\" color=\"#A020F0\"/><Color name=\"pink\" color=\"#F570CE\"/><Color name=\"grey\" color=\"#AAAAAA\"/>"
        + "<Color name=\"black\" color=\"#191919\"/>\t<Button onClickSound=\"Defaults/Sounds/ClickUI\" audioVolume=\"0.3\"/><InputField onClickSound=\"Defaults/Sounds/ClickUI\" audioVolume=\"0.3\"/>"
        + "<Toggle onClickSound=\"Defaults/Sounds/ClickUI\" audioVolume=\"0.3\"/><ToggleButton onClickSound=\"Defaults/Sounds/ClickUI\" audioVolume=\"0.3\"/>"
        + "<Slider onClickSound=\"Defaults/Sounds/ClickUI\" audioVolume=\"0.3\"/><Dropdown onClickSound=\"Defaults/Sounds/ClickUI\" audioVolume=\"0.3\"/></Defaults>";
    public const int MaxDepth = 20, MaxElements = 20_000;
    const string Root = "XmlLayout";

    readonly UiLayout _layout = new();
    readonly List<string> _said = new();
    int _count;

    public List<string> Said => _said;

    public static (UiLayout Layout, List<string> Said) Of(string xml)
    {
        var b = new PanelBuild();
        try { b.Run("<" + Root + ">" + Inserted + xml + "</" + Root + ">"); }
        catch (Exception e) when (e is XmlException or ArgumentException or InvalidOperationException or FormatException or PanelTooBig)
        {
            b._layout.Broken = e.Message;
        }
        return (b._layout, b._said);
    }

    void Run(string xml)
    {
        var settings = new XmlReaderSettings { IgnoreWhitespace = true, IgnoreComments = true, IgnoreProcessingInstructions = true, DtdProcessing = DtdProcessing.Prohibit };
        RefuseDeepNesting(xml, settings);
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, settings);
        reader.ReadToFollowing(Root);
        var top = new Parent(null, Root, _layout.Elements, new List<int>());
        Children(reader, top, 1);
    }

    static void RefuseDeepNesting(string xml, XmlReaderSettings settings)
    {
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, settings);
        try
        {
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element && reader.Depth > MaxDepth) throw new PanelTooBig($"the panels nest deeper than {MaxDepth} levels");
        }
        catch (XmlException) { }
    }

    sealed record Parent(UiElement? Element, string Tag, List<UiElement> Into, List<int> Path)
    {
        public Parent? Up { get; init; }
    }

    void Children(XmlReader reader, Parent at, int depth)
    {
        while (reader.Read())
        {
            if (reader.IsStartElement())
            {
                using var sub = reader.ReadSubtree();
                sub.Read();
                Node(sub, at, depth);
            }
            else if (reader.NodeType != XmlNodeType.EndElement)
            {
                var text = Cleanup(reader.ReadContentAsString());
                if (at.Element != null) at.Element.Attributes["text"] = text;
            }
        }
    }

    static string Cleanup(string text)
    {
        text = text.Trim();
        while (text.Contains("  ")) text = text.Replace("  ", " ");
        while (text.Contains("\r\n ")) text = text.Replace("\r\n ", "\r\n");
        return text;
    }

    void Node(XmlReader reader, Parent at, int depth)
    {
        if (depth > MaxDepth) throw new PanelTooBig($"the panels nest deeper than {MaxDepth} levels");
        var name = reader.Name;
        if (name.Equals("Defaults", StringComparison.OrdinalIgnoreCase))
        {
            Defaults(reader);
            return;
        }
        var own = Attributes(reader);
        if (name.Equals("Include", StringComparison.OrdinalIgnoreCase))
        {
            _said.Add($"[XmlLayout][{Root}] Error locating include file : '{(own.GetValueOrDefault("path") ?? "").Replace(".xml", "")}'.");
            return;
        }
        var tag = PanelTags.Find(name);
        if (tag == null)
        {
            _said.Add($"[XmlLayout] Unknown tag '{name}'.\r\nTag Handlers must inherit from 'ElementTagHandler', and must be named {{Tag}}TagHandler.");
            return;
        }
        if (++_count > MaxElements) throw new PanelTooBig($"more than {MaxElements} elements in one panel tree");
        var e = new UiElement { Tag = tag, Attributes = own };
        var path = at.Path.Append(at.Into.Count).ToList();
        at.Into.Add(e);
        var here = new Parent(e, tag, e.Children, path) { Up = at };
        if (PanelTags.ReadsOwnChildren(tag)) OwnChildren(reader, e);
        else Children(reader, here, depth + 1);
        Finish(e, at, path, name);
    }

    static Dictionary<string, string> Attributes(XmlReader reader)
    {
        var d = UiAttributes.Fresh();
        for (int i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToNextAttribute();
            d.Add(reader.Name, reader.Value);
        }
        reader.MoveToElement();
        return d;
    }

    void OwnChildren(XmlReader reader, UiElement e)
    {
        reader.MoveToContent();
        var node = new XmlDocument().ReadNode(reader);
        if (node == null) return;
        if (e.Tag == PanelTags.Dropdown)
        {
            e.Options = new();
            foreach (XmlNode c in node.ChildNodes)
                if (c is System.Xml.XmlElement ce && ce.Name.Equals("Option", StringComparison.OrdinalIgnoreCase))
                    e.Options.Add(new UiNode { Tag = "Option", Attributes = OptionAttributes(ce), Value = ce.InnerText });
            return;
        }
        e.Inner = PanelText.FromInnerXml(node.InnerXml);
    }

    static Dictionary<string, string> OptionAttributes(System.Xml.XmlElement ce)
    {
        var d = UiAttributes.Fresh();
        foreach (XmlAttribute a in ce.Attributes) d[a.Name] = a.Value;
        return d;
    }

    void Finish(UiElement e, Parent at, List<int> path, string written)
    {
        if (e.Attributes.TryGetValue("id", out var id))
        {
            if (_layout.Ids.ContainsKey(id)) _said.Add("[XmlLayout] Ignoring duplicate id value '" + id + ". Id values must be unique.");
            else _layout.Ids[id] = path;
        }
        e.Own = e.Attributes.Keys.ToList();
        var merged = Merge(e, at, written, e.Attributes);
        e.Classes = e.Attributes.TryGetValue("class", out var cls) ? ClassList(cls) : new();
        foreach (var (k, v) in merged) e.Attributes[k] = v;
        if (merged.TryGetValue("id", out var mid) && mid.Length > 0) PanelIds.Register(_layout, mid, path);
        if (merged.ContainsKey("active") || merged.ContainsKey("visibility")) PanelOps.Activate(e);
        PanelTags.AfterApply(e, merged);
        if (e.Tag == PanelTags.AccordionSection) PanelOps.Expanded(_layout, path, PanelOps.Bool(e.Get("expanded")));
        if (e.Tag == "TMP_Option" && at.Element is { Tag: "TextMeshProDropdown" } dropdown) Option(dropdown, merged);
        if (!PanelTags.Renders(e.Tag)) Unrender(at, path);
    }

    static void Option(UiElement dropdown, Dictionary<string, string> attrs)
    {
        dropdown.Options ??= new();
        var option = UiAttributes.Fresh();
        foreach (var (k, v) in attrs) option[k] = v;
        dropdown.Options.Add(new UiNode { Tag = "Option", Attributes = option, Value = attrs.GetValueOrDefault("text") ?? "" });
    }

    void Unrender(Parent at, List<int> path)
    {
        at.Into.RemoveAt(at.Into.Count - 1);
        foreach (var k in _layout.Ids.Where(p => p.Value.Count >= path.Count && p.Value.Take(path.Count).SequenceEqual(path)).Select(p => p.Key).ToList()) _layout.Ids.Remove(k);
    }

    public static List<string> ClassList(string? s) => s == null ? new() : s.Split(',', ' ').Select(x => x.Trim().ToLowerInvariant()).ToList();
}

public sealed class PanelTooBig(string message) : Exception(message);
