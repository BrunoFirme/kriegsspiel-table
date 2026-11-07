using System;
using System.Text;

namespace KTable.App;

static class PanelTyping
{
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
