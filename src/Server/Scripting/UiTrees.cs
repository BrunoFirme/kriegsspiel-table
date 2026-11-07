using System.Text;
using System.Xml.Linq;
using KTable.Core.Model;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal static class UiTrees
{
    public static string ValueKey(UiNode? n) => n?.Tag switch { "Toggle" or "ToggleButton" => "isOn", "Slider" or "ProgressBar" or "Dropdown" => "value", _ => "text" };

    public static string AttrText(DynValue v) => v.Type switch
    {
        DataType.Boolean => v.Boolean ? "true" : "false",
        DataType.Number => v.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        DataType.Nil or DataType.Void => "",
        _ => v.CastToString() ?? v.ToPrintString(),
    };

    sealed class UiBudget { public int Nodes; }

    static void Count(UiBudget b, int depth)
    {
        if (depth > ScriptHost.MaxUiDepth) throw new ScriptRuntimeException($"ui.set: the panel nests deeper than {ScriptHost.MaxUiDepth} levels (a table that holds itself?)");
        if (++b.Nodes > ScriptHost.MaxUiNodes) throw new ScriptRuntimeException($"ui.set: more than {ScriptHost.MaxUiNodes} elements in one panel tree");
    }

    public static List<UiNode> TreeOf(DynValue v)
    {
        if (v.Type == DataType.String) return ParseXml(v.String);
        if (v.Type != DataType.Table) return new();
        var b = new UiBudget();
        if (v.Table.Get("tag").Type == DataType.String) return new() { NodeOf(v.Table, 1, b) };
        var list = new List<UiNode>();
        foreach (var e in v.Table.Values) if (e.Type == DataType.Table && e.Table.Get("tag").Type == DataType.String) list.Add(NodeOf(e.Table, 1, b));
        return list;
    }

    static UiNode NodeOf(Table t, int depth, UiBudget b)
    {
        Count(b, depth);
        var n = new UiNode { Tag = t.Get("tag").String };
        var attrs = t.Get("attributes");
        if (attrs.Type == DataType.Table) foreach (var pair in attrs.Table.Pairs) if (pair.Key.Type == DataType.String) n.Attributes[pair.Key.String] = AttrText(pair.Value);
        var value = t.Get("value");
        if (value.Type is DataType.String or DataType.Number) n.Attributes["text"] = AttrText(value);
        var children = t.Get("children");
        if (children.Type == DataType.Table) foreach (var c in children.Table.Values) if (c.Type == DataType.Table && c.Table.Get("tag").Type == DataType.String) n.Children.Add(NodeOf(c.Table, depth + 1,
            b));
        return n;
    }

    public static DynValue NodeTable(Script vm, UiNode n)
    {
        var t = DynValue.NewTable(vm);
        t.Table["tag"] = DynValue.NewString(n.Tag);
        var attrs = DynValue.NewTable(vm);
        foreach (var (k, v) in n.Attributes) if (k != "text") attrs.Table[k] = DynValue.NewString(v);
        t.Table["attributes"] = attrs;
        if (n.Attributes.TryGetValue("text", out var text)) t.Table["value"] = DynValue.NewString(text);
        var children = DynValue.NewTable(vm);
        foreach (var c in n.Children) children.Table.Append(NodeTable(vm, c));
        t.Table["children"] = children;
        return t;
    }

    public static List<UiNode> ParseXml(string xml)
    {
        var list = new List<UiNode>();
        if (string.IsNullOrWhiteSpace(xml)) return list;
        XElement root;
        try { root = XElement.Parse("<Root>" + xml + "</Root>", LoadOptions.PreserveWhitespace); }
        catch (System.Xml.XmlException e) { throw new ScriptRuntimeException("ui.set: the XML does not parse: " + e.Message); }
        var b = new UiBudget();
        foreach (var e in root.Elements()) list.Add(NodeOf(e, 1, b));
        return list;
    }

    static UiNode NodeOf(XElement e, int depth, UiBudget b)
    {
        Count(b, depth);
        var n = new UiNode { Tag = e.Name.LocalName };
        foreach (var a in e.Attributes()) n.Attributes[a.Name.LocalName] = a.Value;
        var text = string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (text.Length > 0 && !n.Attributes.ContainsKey("text")) n.Attributes["text"] = text;
        foreach (var c in e.Elements()) n.Children.Add(NodeOf(c, depth + 1, b));
        return n;
    }

    public static string ToXml(IEnumerable<UiNode> tree)
    {
        var sb = new StringBuilder();
        foreach (var n in tree) Write(sb, n, 0);
        return sb.ToString();
    }

    static void Write(StringBuilder sb, UiNode n, int depth)
    {
        sb.Append(' ', depth * 2).Append('<').Append(n.Tag);
        foreach (var (k, v) in n.Attributes) if (k != "text") sb.Append(' ').Append(k).Append("=\"").Append(Escape(v)).Append('"');
        var text = n.Attributes.GetValueOrDefault("text");
        if (n.Children.Count == 0 && string.IsNullOrEmpty(text)) { sb.Append("/>\n"); return; }
        sb.Append('>');
        if (!string.IsNullOrEmpty(text)) sb.Append(Escape(text));
        if (n.Children.Count > 0)
        {
            sb.Append('\n');
            foreach (var c in n.Children) Write(sb, c, depth + 1);
            sb.Append(' ', depth * 2);
        }
        sb.Append("</").Append(n.Tag).Append(">\n");
    }

    static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
