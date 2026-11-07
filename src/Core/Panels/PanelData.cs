using System.Text;
using System.Xml;
using System.Xml.Linq;
using KTable.Core.Model;

namespace KTable.Core.Panels;

public static class PanelData
{
    public const string Blank = "<!-- Xml UI. See documentation: https://api.tabletopsimulator.com/ui/introUI/ -->";
    public const string EmptyLayout = "<XmlLayout></XmlLayout>";
    const string Wrapper = "XML";
    const string LessThan = "ttamp;t", GreaterThan = "ttgt;t";
    static readonly string[] RichTags = { "b", "i", "textcolor", "textsize" };

    public static bool IsNothing(string? xml) => string.IsNullOrEmpty(xml) || xml == EmptyLayout || xml == Blank;

    public static List<UiNode>? Parse(string xml)
    {
        XElement root;
        try { root = XElement.Parse("<" + Wrapper + ">" + xml + "</" + Wrapper + ">"); }
        catch (XmlException) { return null; }
        return root.Elements().Select(Node).ToList();
    }

    static UiNode Node(XElement e)
    {
        var n = new UiNode { Tag = e.Name.ToString() };
        foreach (var a in e.Attributes()) n.Attributes.Add(a.Name.ToString(), a.Value);
        if (e.Elements().Any(c => RichTags.Contains(c.Name.ToString())))
        {
            using var reader = e.CreateReader();
            reader.MoveToContent();
            n.Value = reader.ReadInnerXml();
            return n;
        }
        if (e.FirstNode is XText t) n.Value = t.Value;
        foreach (var c in e.Elements()) n.Children.Add(Node(c));
        return n;
    }

    public static string Write(IReadOnlyList<UiNode>? roots)
    {
        if (roots == null) return "";
        var text = new StringBuilder();
        foreach (var n in roots)
        {
            XElement e;
            try { e = Element(n); }
            catch (Exception ex) when (ex is XmlException or ArgumentException) { return ""; }
            text.Append(e.ToString().ReplaceLineEndings("\r\n")).Append('\n');
        }
        return Indented(text.ToString()).Replace(LessThan, "<").Replace(GreaterThan, ">");
    }

    static XElement Element(UiNode n)
    {
        var e = new XElement(n.Tag);
        if (n.Value != null) e.SetValue(n.Value.Replace("<", LessThan).Replace(">", GreaterThan));
        foreach (var (k, v) in n.Attributes) e.Add(new XAttribute(k.Replace("\"", ""), v.Replace("\"", "")));
        foreach (var c in n.Children) e.Add(Element(c));
        return e;
    }

    static string Indented(string text)
    {
        var sb = new StringBuilder(text.Length * 2);
        bool lineStart = true;
        foreach (var c in text)
        {
            if (lineStart && char.IsWhiteSpace(c))
            {
                sb.Append("  ");
                continue;
            }
            lineStart = c == '\n';
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static UiNode? ById(IReadOnlyList<UiNode>? roots, string id)
    {
        if (roots == null) return null;
        foreach (var n in roots) if (Find(n, id) is { } f) return f;
        return null;
    }

    public static List<int>? PathOf(IReadOnlyList<UiNode>? roots, string id)
    {
        if (roots == null) return null;
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i].Attributes.TryGetValue("id", out var v) && v == id) return new List<int> { i };
            if (PathOf(roots[i].Children, id) is { } deeper) return deeper.Prepend(i).ToList();
        }
        return null;
    }

    public static UiNode? At(IReadOnlyList<UiNode> roots, IReadOnlyList<int> path)
    {
        UiNode? at = null;
        IReadOnlyList<UiNode> list = roots;
        foreach (var i in path)
        {
            if (i < 0 || i >= list.Count) return null;
            at = list[i];
            list = at.Children;
        }
        return at;
    }

    static UiNode? Find(UiNode n, string id)
    {
        if (n.Attributes.TryGetValue("id", out var v) && v == id) return n;
        foreach (var c in n.Children) if (Find(c, id) is { } f) return f;
        return null;
    }
}
