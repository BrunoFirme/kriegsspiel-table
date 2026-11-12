using System.Text;

namespace KTable.Core.Model;

public enum MarkupLevel { Line, Lowered, Raised }

public readonly record struct MarkupRun(string Text, bool Bold, bool Italic, bool Underline, bool Strike, MarkupLevel Level, string? Color)
{
    public bool Plain => !Bold && !Italic && !Underline && !Strike && Level == MarkupLevel.Line && Color == null;
}

public static class Markup
{
    public const double SmallScale = 0.75;

    const int LongestSymbol = 10, AlphaDigits = 2, ColourDigits = 6, ColourAlphaDigits = 8;
    const string LinkOpens = "url=";

    public static string Plain(string text) => text.Contains('[') ? string.Concat(Runs(text).Select(r => r.Text)) : text;

    public static List<MarkupRun> Runs(string text)
    {
        var runs = new List<MarkupRun>();
        var colours = new List<string>();
        var style = new MarkupRun("", false, false, false, false, MarkupLevel.Line, null);
        var plain = new StringBuilder();
        void Close()
        {
            if (plain.Length == 0) return;
            if (runs.Count > 0 && runs[^1] with { Text = "" } == style) runs[^1] = style with { Text = runs[^1].Text + plain };
            else runs.Add(style with { Text = plain.ToString() });
            plain.Clear();
        }
        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '[' && SymbolAt(text, i) is var (symbol, length) && Applied(symbol, style, colours) is { } next)
            {
                Close();
                style = next;
                i += length;
                continue;
            }
            plain.Append(text[i++]);
        }
        Close();
        return runs;
    }

    static (string Symbol, int Length)? SymbolAt(string text, int at)
    {
        if (text.AsSpan(at + 1).StartsWith(LinkOpens, StringComparison.Ordinal))
            return (LinkOpens, text.IndexOf(']', at) is var shut and >= 0 ? shut - at + 1 : text.Length - at);
        int end = text.IndexOf(']', at + 1, Math.Min(LongestSymbol - 1, text.Length - at - 1));
        return end < 0 ? null : (text[(at + 1)..end], end - at + 1);
    }

    static MarkupRun? Applied(string symbol, MarkupRun style, List<string> colours)
    {
        switch (symbol)
        {
            case "b" or "B": return style with { Bold = true };
            case "/b" or "/B": return style with { Bold = false };
            case "i" or "I": return style with { Italic = true };
            case "/i" or "/I": return style with { Italic = false };
            case "u" or "U": return style with { Underline = true };
            case "/u" or "/U": return style with { Underline = false };
            case "s" or "S": return style with { Strike = true };
            case "/s" or "/S": return style with { Strike = false };
            case "sub" or "SUB": return style with { Level = MarkupLevel.Lowered };
            case "sup" or "SUP": return style with { Level = MarkupLevel.Raised };
            case "/sub" or "/SUB" or "/sup" or "/SUP": return style with { Level = MarkupLevel.Line };
            case "c" or "C" or "/c" or "/C" or "/url" or "/URL": return style;
            case "-":
                if (colours.Count > 0) colours.RemoveAt(colours.Count - 1);
                return style with { Color = colours.Count > 0 ? colours[^1] : null };
        }
        if (symbol.StartsWith(LinkOpens, StringComparison.Ordinal)) return style;
        if (!symbol.All(Uri.IsHexDigit)) return null;
        if (symbol.Length == AlphaDigits) return style;
        if (symbol.Length is not (ColourDigits or ColourAlphaDigits)) return null;
        colours.Add("#" + symbol.ToUpperInvariant());
        return style with { Color = colours[^1] };
    }
}
