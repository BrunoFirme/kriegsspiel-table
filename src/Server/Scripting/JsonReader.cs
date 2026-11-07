using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class JsonReader(Script vm, string text)
{
    const int DeepestNesting = 200;
    const int HexDigits = 4;
    const int HighSurrogateFirst = 0xD800, HighSurrogateLast = 0xDBFF, SurrogateBase = 0x2400, SurrogateStep = 0x400;
    static readonly Regex NumberPattern = new(@"\G-?([1-9][0-9]*|0)(\.[0-9]+)?([eE][-+]?[0-9]+)?", RegexOptions.CultureInvariant);
    static readonly Regex LowSurrogate = new(@"\G\\u([dD][c-fC-F][0-9a-fA-F]{2})", RegexOptions.CultureInvariant);
    int _depth;

    public DynValue Document()
    {
        if (string.IsNullOrWhiteSpace(text)) return DynValue.Nil;
        if (text.TrimStart().StartsWith('<')) throw new ScriptRuntimeException("Expected string argument to JSON.decode(), got HTML?");
        if (text[0] == '\0' || (text.Length >= 2 && text[1] == '\0')) throw new ScriptRuntimeException("JSON.decode() only works with UTF-8");
        return One(0).value;
    }

    static ScriptRuntimeException Fail(string what) => new("JSON.decode() " + what);

    int Skip(int i)
    {
        while (i < text.Length && text[i] is ' ' or '\n' or '\r' or '\t') i++;
        return i;
    }

    (DynValue value, int next) One(int start)
    {
        int i = Skip(start);
        if (i >= text.Length) throw Fail("found unexpected end of string");
        char c = text[i];
        if (c == '"') { var (s, n) = Text(i); return (DynValue.NewString(s), n); }
        if (c == '-' || char.IsAsciiDigit(c)) return Number(i);
        if (c == '{') return Nested(() => Object(i));
        if (c == '[') return Nested(() => Array(i));
        if (string.CompareOrdinal(text, i, "true", 0, 4) == 0) return (DynValue.True, i + 4);
        if (string.CompareOrdinal(text, i, "false", 0, 5) == 0) return (DynValue.False, i + 5);
        if (string.CompareOrdinal(text, i, "null", 0, 4) == 0) return (DynValue.Nil, i + 4);
        throw Fail("can't parse JSON");
    }

    (DynValue, int) Nested(Func<(DynValue, int)> read)
    {
        if (++_depth > DeepestNesting) throw Fail("found a value nested too deep");
        try { return read(); }
        finally { _depth--; }
    }

    (DynValue, int) Number(int i)
    {
        var m = NumberPattern.Match(text, i);
        if (!m.Success) throw Fail("expected number");
        if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) throw Fail("found bad number");
        return (DynValue.NewNumber(n), i + m.Length);
    }

    (string, int) Text(int start)
    {
        var sb = new StringBuilder();
        int i = start + 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"') return (sb.ToString(), i + 1);
            if (c != '\\') { sb.Append(c); i++; continue; }
            if (i + 1 >= text.Length) break;
            char e = text[i + 1];
            switch (e)
            {
                case 'b': sb.Append('\b'); i += 2; break;
                case 'f': sb.Append('\f'); i += 2; break;
                case 'n': sb.Append('\n'); i += 2; break;
                case 'r': sb.Append('\r'); i += 2; break;
                case 't': sb.Append('\t'); i += 2; break;
                case 'u' when Hex(i + 2) is { } code: i = Unicode(sb, code, i + 2 + HexDigits); break;
                default: sb.Append(e); i += 2; break;
            }
        }
        throw Fail("found unclosed string");
    }

    int? Hex(int at)
    {
        if (at + HexDigits > text.Length) return null;
        return int.TryParse(text.AsSpan(at, HexDigits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    int Unicode(StringBuilder sb, int code, int next)
    {
        if (code >= HighSurrogateFirst && code <= HighSurrogateLast && LowSurrogate.Match(text, next) is { Success: true } lo)
        {
            code = SurrogateBase + (code - HighSurrogateFirst) * SurrogateStep + int.Parse(lo.Groups[1].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            next += lo.Length;
        }
        foreach (var b in Utf8Bytes(code)) sb.Append((char)b);
        return next;
    }

    static IEnumerable<int> Utf8Bytes(int code)
    {
        const int OneByte = 127, TwoBytes = 2047, ThreeBytes = 65535, Six = 0x40, Twelve = 0x1000, Eighteen = 0x40000, Tail = 0x80;
        if (code <= OneByte) return [code];
        if (code <= TwoBytes) return [0xC0 + code / Six, Tail + code % Six];
        if (code <= ThreeBytes)
        {
            const int Lead = 0xE0, Surrogates = 0xED, LowestAfterLead = 0xA0, HighestAfterSurrogates = 0x9F, Question = '?';
            int high = Lead + code / Twelve, mid = Tail + code / Six % Six;
            if ((high == Lead && mid < LowestAfterLead) || (high == Surrogates && mid > HighestAfterSurrogates)) return [Question];
            return [high, mid, Tail + code % Six];
        }
        return [0xF0 + code / Eighteen, Tail + code / Twelve % Six, Tail + code / Six % Six, Tail + code % Six];
    }

    (DynValue, int) Object(int start)
    {
        var t = new Table(vm);
        int i = Skip(start + 1);
        if (i < text.Length && text[i] == '}') return (DynValue.NewTable(t), i + 1);
        while (i < text.Length)
        {
            if (text[i] != '"') throw Fail("expected string's opening quote");
            var (key, afterKey) = Text(i);
            i = Skip(afterKey);
            if (i >= text.Length || text[i] != ':') throw Fail("expected colon");
            var (value, afterValue) = One(i + 1);
            t[key] = value;
            i = Skip(afterValue);
            if (i < text.Length && text[i] == '}') return (DynValue.NewTable(t), i + 1);
            if (i >= text.Length || text[i] != ',') throw Fail("expected comma or '}'");
            i = Skip(i + 1);
        }
        throw Fail("found unclosed '{'");
    }

    (DynValue, int) Array(int start)
    {
        var t = new Table(vm);
        int i = Skip(start + 1);
        if (i < text.Length && text[i] == ']') return (DynValue.NewTable(t), i + 1);
        int index = 1;
        while (i < text.Length)
        {
            var (value, after) = One(i);
            if (!value.IsNil()) t[index] = value;
            index++;
            i = Skip(after);
            if (i < text.Length && text[i] == ']') return (DynValue.NewTable(t), i + 1);
            if (i >= text.Length || text[i] != ',') throw Fail("found unlcosed '['");
            i = Skip(i + 1);
        }
        throw Fail("found unlcosed '['");
    }
}
