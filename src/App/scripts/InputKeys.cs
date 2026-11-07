using System;

namespace KTable.App;

static class InputKeys
{
    public const string FourSpaces = "    ";
    public const char Refused = '\0';

    public static char Validate(string? mode, string text, char ch) => mode switch
    {
        "integer" => Digit(ch) || (ch == '-' && text.Length == 0) ? ch : Refused,
        "float" => Digit(ch) || (ch == '-' && text.Length == 0) || (ch == '.' && !text.Contains('.')) ? ch : Refused,
        "alphanumeric" => Upper(ch) || Lower(ch) || Digit(ch) ? ch : Refused,
        "username" => Upper(ch) ? char.ToLowerInvariant(ch) : Lower(ch) || Digit(ch) ? ch : Refused,
        "name" => Name(text, ch),
        _ => ch,
    };

    static bool Digit(char ch) => ch >= '0' && ch <= '9';

    static bool Upper(char ch) => ch >= 'A' && ch <= 'Z';

    static bool Lower(char ch) => ch >= 'a' && ch <= 'z';

    static char Name(string text, char ch)
    {
        int pos = text.Length;
        char before = text.Length > 0 ? text[Math.Clamp(pos, 0, text.Length - 1)] : ' ';
        char after = text.Length > 0 ? text[Math.Clamp(pos + 1, 0, text.Length - 1)] : '\n';
        if (Lower(ch)) return before == ' ' ? char.ToUpperInvariant(ch) : ch;
        if (Upper(ch)) return before != ' ' && before != '\'' ? char.ToLowerInvariant(ch) : ch;
        if (ch == '\'') return before != ' ' && before != '\'' && after != '\'' && !text.Contains('\'') ? ch : Refused;
        if (ch == ' ') return before != ' ' && before != '\'' && after != ' ' && after != '\'' ? ch : Refused;
        return Refused;
    }
}
