using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KTable.Core.Panels;

public static class PanelText
{
    public const string Language = "en";
    static readonly string[] Languages =
    {
        "en", "fr", "de", "pl", "ru", "zh-CN", "zh-TW", "tr", "it", "cs", "fi", "hu", "es", "es-US", "da", "ja", "ko", "pt-BR", "th", "sv", "nl", "vi", "ro", "uk", "nb", "el", "ar", "pt",
    };
    static readonly Regex Escaped = new(@"\\[uU](?<Value>[a-zA-Z0-9]{4})");
    static readonly (string From, string To)[] Entities = { ("&lt;", "<"), ("&gt;", ">"), ("&amp;", "&"), ("&nbsp;", " "), ("<br/>", "\n"), ("<br />", "\n") };

    public static string Decode(string value) => Escaped.Replace(value, m => ((char)int.Parse(m.Groups["Value"].Value, NumberStyles.HexNumber)).ToString());

    public static string FromInnerXml(string inner)
    {
        var s = Strip(inner);
        s = s.Replace("<br/>", "\n").Replace("<br />", "\n");
        return Decode(Lines(s));
    }

    public static string FromValue(string value)
    {
        var s = Strip(Localize(value));
        foreach (var (from, to) in Entities) s = Regex.Replace(s, Regex.Escape(from), to.Replace("$", "$$"), RegexOptions.IgnoreCase);
        return Decode(Lines(s));
    }

    public static string FromAttribute(string text) => Decode(Localize(text));

    static string Strip(string s)
    {
        s = s.Replace(" xmlns=\"XmlLayout\"", "").Replace(" xmlns=\"http://www.w3schools.com\"", "").Replace("<![CDATA[", "").Replace("]]>", "");
        s = Regex.Replace(s, "<textcolor color=", "<color=", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "</textcolor", "</color", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<textsize size=", "<size=", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "</textsize", "</size", RegexOptions.IgnoreCase);
        s = s.Trim();
        return Regex.Replace(s, " {2,}", " ", RegexOptions.IgnoreCase);
    }

    static string Lines(string s)
    {
        if (!s.Contains('\n')) return s;
        s = s.Replace("\\n", "\n");
        return string.Join("\n", s.Split('\n').Select(l => l.Trim()));
    }

    public static string Localize(string text)
    {
        if (!text.Contains('{') || !Languages.Any(c => IndexOf(c, text) >= 0)) return text;
        int at = IndexOf(Language, text);
        if (at < 0) foreach (var c in Languages) if ((at = IndexOf(c, text)) >= 0) break;
        return at < 0 ? text : Pick(text, at);
    }

    static string Pick(string text, int index)
    {
        var sb = new StringBuilder();
        if (text[0] == '[')
        {
            int close = 0;
            for (int i = 0; i < index && text[i] != '{'; i++) if (text[i] == ']') close = i;
            if (close > 0) sb.Append(text, 0, close + 1);
        }
        for (int k = index; k < text.Length && text[k] != '{'; k++) sb.Append(text[k]);
        if (sb.Length == 0) return text;
        if (sb[^1] is '\n' or '\r') sb.Length--;
        return sb.ToString();
    }

    static int IndexOf(string code, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            int end = i + code.Length + 2;
            if (end >= text.Length) continue;
            if (string.Compare(text, i + 1, code, 0, code.Length, StringComparison.OrdinalIgnoreCase) == 0 && text[end - 1] == '}') return end;
        }
        const int Short = 2;
        for (int k = 0; k < text.Length; k++)
        {
            if (text[k] != '{') continue;
            int end = k + Short + 2;
            if (end >= text.Length || string.Compare(text, k + 1, code, 0, Short, StringComparison.OrdinalIgnoreCase) != 0) continue;
            if (text[end - 1] == '}') return end;
            if (end + 3 < text.Length && text[end - 1] == '-')
            {
                if (text[end + 2] == '}') return end + 3;
                if (text[end + 3] == '}' && end + 4 < text.Length) return end + 4;
            }
        }
        return -1;
    }
}
