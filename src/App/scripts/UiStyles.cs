using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KTable.Core.Model;

namespace KTable.App;

static class UiStyles
{
    sealed record Style(string Tag, string[] Classes, Dictionary<string, string> Attributes);

    public static List<UiNode> Styled(IEnumerable<UiNode> roots)
    {
        var list = roots.ToList();
        var styles = new List<Style>();
        foreach (var n in list) Collect(n, styles);
        return list.Select(n => Apply(n, styles)).ToList();
    }

    static void Collect(UiNode n, List<Style> styles)
    {
        if (n.Tag == "Defaults")
        {
            foreach (var d in n.Children)
                styles.Add(new Style(d.Tag, ClassesOf(d), d.Attributes.Where(a => a.Key != "class").ToDictionary(a => a.Key, a => a.Value)));
            return;
        }
        foreach (var c in n.Children) Collect(c, styles);
    }

    static string[] ClassesOf(UiNode n) => n.Attributes.TryGetValue("class", out var c) ? c.Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];

    static UiNode Apply(UiNode n, List<Style> styles)
    {
        if (n.Tag == "Defaults" || styles.Count == 0) return n;
        var classes = ClassesOf(n);
        var merged = new Dictionary<string, string>();
        foreach (var s in styles.Where(s => s.Classes.Length == 0 && s.Tag == n.Tag)) foreach (var (k, v) in s.Attributes) merged[k] = v;
        foreach (var cls in classes)
            foreach (var s in styles.Where(s => s.Classes.Contains(cls) && (s.Tag == n.Tag || s.Tag == "Defaults")))
                foreach (var (k, v) in s.Attributes) merged[k] = v;
        foreach (var (k, v) in n.Attributes) merged[k] = v;
        return new UiNode { Tag = n.Tag, Attributes = merged, Children = n.Children.Select(c => Apply(c, styles)).ToList() };
    }

    static readonly Regex RichTag = new(@"<(/?)(b|i|size|color)(?:=([^>]*))?>", RegexOptions.IgnoreCase);

    public static bool IsRich(string text) => RichTag.IsMatch(text);

    public static string BbCode(string text)
    {
        var sb = new StringBuilder();
        int at = 0;
        foreach (Match m in RichTag.Matches(text))
        {
            sb.Append(text[at..m.Index].Replace("[", "[lb]"));
            at = m.Index + m.Length;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            bool close = m.Groups[1].Length > 0;
            var arg = m.Groups[3].Value.Trim('"', '\'', ' ');
            sb.Append(tag switch
            {
                "b" or "i" => close ? $"[/{tag}]" : $"[{tag}]",
                "size" => close ? "[/font_size]" : $"[font_size={(int.TryParse(arg, out var px) ? px : 14)}]",
                _ => close ? "[/color]" : $"[color={arg}]",
            });
        }
        return sb.Append(text[at..].Replace("[", "[lb]")).ToString();
    }

    public static string Valid(string text, string mode) => mode switch
    {
        "Integer" => KeepWhere(text, (c, i, s) => char.IsDigit(c) || (c == '-' && i == 0)),
        "Decimal" => Decimal(text),
        "Alphanumeric" => KeepWhere(text, (c, _, _) => char.IsAsciiLetterOrDigit(c)),
        "Name" => KeepWhere(text, (c, i, s) => char.IsLetter(c) || (c is ' ' or '\'' or '-' && i > 0 && s.Length > 0 && s[^1] != c)),
        "EmailAddress" => KeepWhere(text, (c, _, _) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-/=?^_`{|}~.@".Contains(c)),
        _ => text,
    };

    static string KeepWhere(string text, Func<char, int, StringBuilder, bool> ok)
    {
        var sb = new StringBuilder();
        foreach (var c in text) if (ok(c, sb.Length, sb)) sb.Append(c);
        return sb.ToString();
    }

    static string Decimal(string text)
    {
        bool dot = false;
        return KeepWhere(text, (c, i, _) =>
        {
            if (char.IsDigit(c)) return true;
            if (c == '-' && i == 0) return true;
            if (c != '.' || dot) return false;
            dot = true;
            return true;
        });
    }
}
