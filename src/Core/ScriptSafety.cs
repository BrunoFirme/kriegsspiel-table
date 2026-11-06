using System.Text;
using System.Text.RegularExpressions;

namespace KTable.Core;

public static partial class ScriptSafety
{
    public sealed record Verdict(string Script, IReadOnlyList<string> Reasons)
    {
        public bool Clean => Reasons.Count == 0;
        public bool Hostile => Reasons.Count > 0;
    }

    public static readonly string[] ForbiddenGlobals = { "WebRequest", "io", "load", "loadstring", "dofile", "loadfile", "require", "debug", "package" };

    public const string KnownBlock = "the known self-copying 'Object base code' block (copies itself into every object and fetches code from the web), cut out";
    public const string Web = "calls the web (WebRequest)";
    public const string CopiesCode = "copies running code into objects (setLuaScript with getLuaScript)";
    public const string EveryObject = "writes a script into every object (setLuaScript on a getAllObjects/getObjects loop or in onObjectSpawn)";
    public const string CodeFromData = "runs code built from data (load, loadstring, dofile, loadfile, require)";
    public const string SystemLibs = "reaches the system (io, debug, package)";
    public const string Hidden = "hides code behind a run of blank space";

    const string BlockStart = "--[[Object base code]]";
    const string BlockEnd = "--[[Spawning object]]";
    static readonly string[] ReversedMarkers = { "tcejbo gninwapS", "edoc esab tcejbO" };

    [GeneratedRegex(@"\bWebRequest\b")] private static partial Regex WebRx();
    [GeneratedRegex(@"\bsetLuaScript\b")] private static partial Regex SetScriptRx();
    [GeneratedRegex(@"\bgetLuaScript\b")] private static partial Regex GetScriptRx();
    [GeneratedRegex(@"for\s+\w+\s*,\s*(\w+)\s+in\s+(?:ipairs|pairs)\s*\(\s*(?:getAllObjects|getObjects)\s*\(\s*\)\s*\)\s*do(?:(?!\bend\b).){0,400}?\b\1\s*\.\s*setLuaScript|function\s+onObjectSpawn\s*\(\s*(\w+)\s*\)(?:(?!\bend\b).){0,400}?\b\2\s*\.\s*setLuaScript", RegexOptions.Singleline)]
    private static partial Regex EveryObjectRx();
    [GeneratedRegex(@"\b(loadstring|dofile|loadfile|require)\s*\(|(?<![\w.])load\s*\(")] private static partial Regex CodeFromDataRx();
    [GeneratedRegex(@"\b(io|debug|package)\s*\.")] private static partial Regex SystemLibsRx();
    [GeneratedRegex(@"[ \t]{100,}\S")] private static partial Regex HiddenRx();

    public static Verdict Inspect(string? lua)
    {
        var reasons = new List<string>();
        var s = lua ?? "";
        if (s.Trim().Length == 0) return new Verdict("", reasons);

        bool cut = false;
        int start = s.IndexOf(BlockStart, StringComparison.Ordinal);
        if (start >= 0)
        {
            int end = s.IndexOf(BlockEnd, start, StringComparison.Ordinal);
            s = end >= 0 ? s.Remove(start, end + BlockEnd.Length - start) : s[..start];
            reasons.Add(KnownBlock);
            cut = true;
        }
        else if (ReversedMarkers.Any(m => s.Contains(m, StringComparison.Ordinal)))
        {
            reasons.Add(KnownBlock);
            return new Verdict("", reasons);
        }

        var code = StripComments(s);
        if (WebRx().IsMatch(code)) reasons.Add(Web);
        bool sets = SetScriptRx().IsMatch(code);
        if (sets && GetScriptRx().IsMatch(code)) reasons.Add(CopiesCode);
        if (sets && EveryObjectRx().IsMatch(code)) reasons.Add(EveryObject);
        if (CodeFromDataRx().IsMatch(code)) reasons.Add(CodeFromData);
        if (SystemLibsRx().IsMatch(code)) reasons.Add(SystemLibs);
        if (HiddenRx().IsMatch(code)) reasons.Add(Hidden);

        if (reasons.Count > (cut ? 1 : 0)) return new Verdict("", reasons);
        return new Verdict(cut ? s.TrimEnd() : s, reasons);
    }

    internal static string StripComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (c == '"' || c == '\'')
            {
                int j = i + 1;
                while (j < n && s[j] != c && s[j] != '\n') { if (s[j] == '\\') j++; j++; }
                j = Math.Min(j + 1, n);
                sb.Append(s, i, j - i);
                i = j;
                continue;
            }
            if (c == '[' && LongBracket(s, i, out int level, out int open))
            {
                int j = CloseOf(s, i + open, level);
                sb.Append(s, i, j - i);
                i = j;
                continue;
            }
            if (c == '-' && i + 1 < n && s[i + 1] == '-')
            {
                int j;
                if (i + 2 < n && s[i + 2] == '[' && LongBracket(s, i + 2, out int lvl, out int op)) j = CloseOf(s, i + 2 + op, lvl);
                else { j = i; while (j < n && s[j] != '\n' && s[j] != '\r') j++; }
                sb.Append(' ');
                i = j;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    static bool LongBracket(string s, int i, out int level, out int open)
    {
        level = 0;
        open = 0;
        int j = i + 1;
        while (j < s.Length && s[j] == '=') { level++; j++; }
        if (j < s.Length && s[j] == '[') { open = j - i + 1; return true; }
        return false;
    }

    static int CloseOf(string s, int from, int level)
    {
        int close = s.IndexOf("]" + new string('=', level) + "]", from, StringComparison.Ordinal);
        return close < 0 ? s.Length : close + level + 2;
    }
}
