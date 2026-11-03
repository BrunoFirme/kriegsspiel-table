using System.Text.RegularExpressions;
using KTable.Server.Scripting;

namespace KTable.Client.Scripting;

public static class LuaText
{
    static readonly Regex Member = new(@"([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\.(\w*)$");
    static readonly Regex Word = new(@"(\w*)$");
    static readonly Regex Chain = new(@"([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)$");
    static readonly Regex Defined = new(@"\bfunction\s+([A-Za-z_]\w*)|\blocal\s+([A-Za-z_]\w*)");

    public static (string? receiver, string part, bool code) Before(string text)
    {
        if (!IsCode(text)) return (null, "", false);
        var m = Member.Match(text);
        if (m.Success) return (m.Groups[1].Value, m.Groups[2].Value, true);
        return (null, Word.Match(text).Groups[1].Value, true);
    }

    public static bool IsCode(string text)
    {
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') quote = c;
            else if (c == '-' && i + 1 < text.Length && text[i + 1] == '-') return false;
        }
        return quote == '\0';
    }

    public static IEnumerable<ApiEntry> Completions(string? receiver, string part) =>
        (receiver != null ? ApiDoc.MembersOf(receiver) : ApiDoc.TopLevel).Where(e => e.Name.StartsWith(part, StringComparison.Ordinal));

    public static IEnumerable<string> OwnNames(string script, string part)
    {
        var known = ApiDoc.TopLevel.Select(e => e.Name).ToHashSet();
        foreach (var name in Defined.Matches(script).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Distinct())
            if (name.StartsWith(part, StringComparison.Ordinal) && name != part && !known.Contains(name))
                yield return name;
    }

    public static ApiEntry? EntryAt(string line, int col)
    {
        col = Math.Clamp(col, 0, line.Length);
        static bool W(char c) => char.IsLetterOrDigit(c) || c == '_';
        int start = col, end = col;
        while (start > 0 && W(line[start - 1])) start--;
        while (end < line.Length && W(line[end])) end++;
        if (end <= start) return null;
        var name = line[start..end];
        string? receiver = null;
        if (start > 0 && line[start - 1] == '.')
        {
            var m = Chain.Match(line[..(start - 1)]);
            receiver = m.Success ? m.Groups[1].Value : "piece";
        }
        return ApiDoc.Lookup(receiver, name);
    }

    public static string Describe(ApiEntry e) =>
        e.Display + "\n" + e.Summary
        + string.Concat(e.Params.Select(p => $"\n  {p.Name}: {p.Type}{(p.Optional ? ", optional" : "")}. {p.Text}"))
        + (e.Returns != null ? $"\nReturns {e.Returns}{(e.ReturnText != null ? ": " + e.ReturnText : ".")}" : "")
        + (e.Tts != null ? $"\nTTS: {e.Tts}" : "") + "\nF1: the reference";
}
