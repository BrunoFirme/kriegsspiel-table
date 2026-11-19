using System.Globalization;

namespace KTable.Core;

public static class Calc
{
    const double Thousand = 1e3, Million = 1e6;
    const char Number = 'n', Now = 'x';

    readonly record struct Tok(char Kind, double Value);

    public static double? Evaluate(string text, double current, bool negatives, bool shorthand = false, bool grouped = false)
    {
        if (Tokens(text, shorthand, grouped) is not { Count: > 0 } tokens) return null;
        bool relative = tokens[0].Kind is '*' or '/' or '+' || tokens[0].Kind == '-' && !negatives;
        if (relative) tokens.Insert(0, new Tok(Now, 0));
        int at = 0;
        var value = Sum(tokens, ref at, current);
        return value is { } v && at == tokens.Count && double.IsFinite(v) ? v : null;
    }

    static List<Tok>? Tokens(string text, bool shorthand, bool grouped)
    {
        var list = new List<Tok>();
        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            if (char.IsDigit(c) || c is '.' or ',' && i + 1 < text.Length && char.IsDigit(text[i + 1]))
            {
                var digits = new System.Text.StringBuilder();
                while (i < text.Length)
                {
                    if (char.IsDigit(text[i]) || text[i] is '.' or ',') digits.Append(text[i++]);
                    else if (GroupGap(text, i, digits)) i++;
                    else break;
                }
                if (Read(digits.ToString(), grouped) is not { } n) return null;
                if (shorthand && i < text.Length && text[i] is 'k' or 'K' or 'M') n *= text[i++] == 'M' ? Million : Thousand;
                list.Add(new Tok(Number, n));
                continue;
            }
            i++;
            char? kind = c switch { '+' or '-' or '*' or '/' or '(' or ')' => c, '×' => '*', '÷' => '/', '−' or '–' => '-', 'x' or 'X' => Now, _ => null };
            if (kind is { } k) list.Add(new Tok(k, 0));
            else if (!char.IsLetter(c) && !char.IsWhiteSpace(c) && c != '°') return null;
        }
        return list;
    }

    const int GroupDigits = 3;

    static bool GroupGap(string text, int at, System.Text.StringBuilder before)
    {
        if (text[at] is not (' ' or ' ' or ' ') || at + 1 + GroupDigits > text.Length) return false;
        string sofar = before.ToString();
        if (sofar.Length == 0 || !sofar.All(char.IsDigit)) return false;
        for (int k = 1; k <= GroupDigits; k++) if (!char.IsDigit(text[at + k])) return false;
        return at + 1 + GroupDigits == text.Length || !char.IsDigit(text[at + 1 + GroupDigits]);
    }

    static double? Read(string digits, bool grouped)
    {
        if (digits.LastIndexOf(',') > digits.IndexOf('.') && digits.Contains('.')) return null;
        bool decimalComma = !digits.Contains('.') && digits.Count(c => c == ',') == 1 && !(grouped && digits.Length - digits.IndexOf(',') - 1 == GroupDigits);
        var plain = decimalComma ? digits.Replace(',', '.') : digits.Replace(",", "");
        return double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    static double? Sum(List<Tok> t, ref int at, double current)
    {
        if (Product(t, ref at, current) is not { } v) return null;
        while (at < t.Count && t[at].Kind is '+' or '-')
        {
            bool add = t[at++].Kind == '+';
            if (Product(t, ref at, current) is not { } w) return null;
            v = add ? v + w : v - w;
        }
        return v;
    }

    static double? Product(List<Tok> t, ref int at, double current)
    {
        if (Factor(t, ref at, current) is not { } v) return null;
        while (at < t.Count && t[at].Kind is '*' or '/')
        {
            bool times = t[at++].Kind == '*';
            if (Factor(t, ref at, current) is not { } w || !times && w == 0) return null;
            v = times ? v * w : v / w;
        }
        return v;
    }

    static double? Factor(List<Tok> t, ref int at, double current)
    {
        if (at >= t.Count) return null;
        var tok = t[at++];
        switch (tok.Kind)
        {
            case Number:
                return tok.Value;
            case Now:
                return current;
            case '-':
                return Factor(t, ref at, current) is { } v ? -v : null;
            case '+':
                return Factor(t, ref at, current);
            case '(':
                var inner = Sum(t, ref at, current);
                return inner != null && at < t.Count && t[at++].Kind == ')' ? inner : null;
            default:
                return null;
        }
    }
}
