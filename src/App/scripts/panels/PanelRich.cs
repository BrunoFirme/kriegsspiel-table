using System.Text;
using System.Text.RegularExpressions;

namespace KTable.App;

static class PanelRich
{
    static readonly Regex Tag = new(@"<(/?)(b|i|size|color)(?:=([^>]*))?>", RegexOptions.IgnoreCase);

    public static bool IsRich(string text, bool rich) => rich && Tag.IsMatch(text);

    public static string Plain(string text, bool rich) => rich ? Tag.Replace(text, "") : text;

    public static string BbCode(string text, int baseSize)
    {
        var sb = new StringBuilder();
        int at = 0;
        foreach (Match m in Tag.Matches(text))
        {
            sb.Append(text[at..m.Index].Replace("[", "[lb]"));
            at = m.Index + m.Length;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            bool close = m.Groups[1].Length > 0;
            var arg = m.Groups[3].Value.Trim('"', '\'', ' ');
            sb.Append(tag switch
            {
                "b" or "i" => close ? $"[/{tag}]" : $"[{tag}]",
                "size" => close ? "[/font_size]" : $"[font_size={(int.TryParse(arg, out var px) ? px : baseSize)}]",
                _ => close ? "[/color]" : $"[color={arg}]",
            });
        }
        return sb.Append(text[at..].Replace("[", "[lb]")).ToString();
    }
}
