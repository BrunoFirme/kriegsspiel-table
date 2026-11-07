namespace KTable.Client.Panels;

public sealed record InputRules(string Validation, string Line, bool Secret, char Mask, int Limit)
{
    public bool OneLine => Line == "SingleLine";
    public bool EnterSubmits => Line != "MultiLineNewline";
}

public static class PanelInputs
{
    const char Asterisk = '*';
    static readonly string[] Validations = { "None", "Integer", "Decimal", "Alphanumeric", "Name", "EmailAddress" };
    static readonly string[] Lines = { "SingleLine", "MultiLineSubmit", "MultiLineNewline" };

    static string Canonical(string[] names, string value) => names.FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? names[0];

    public static InputRules Of(PanelBox b)
    {
        string? Look(string key) => b.Extra.GetValueOrDefault(key);
        string validation = "None", line = Look("linetype") ?? "SingleLine";
        bool secret = false;
        switch (Look("contenttype")?.ToLowerInvariant())
        {
            case "integernumber":
                (validation, line) = ("Integer", "SingleLine");
                break;
            case "decimalnumber":
                (validation, line) = ("Decimal", "SingleLine");
                break;
            case "alphanumeric" or "name" or "emailaddress":
                (validation, line) = (Canonical(Validations, Look("contenttype")!), "SingleLine");
                break;
            case "password":
                (line, secret) = ("SingleLine", true);
                break;
            case "pin":
                (validation, line, secret) = ("Integer", "SingleLine", true);
                break;
        }
        if (Look("charactervalidation") is { } cv) validation = Canonical(Validations, cv);
        if (Look("inputtype") is { } it) secret = it.Equals("Password", StringComparison.OrdinalIgnoreCase);
        line = Canonical(Lines, line);
        var mask = Look("asteriskchar") is { Length: > 0 } a ? a[0] : Asterisk;
        return new InputRules(validation, line, secret, mask, Math.Max(0, PanelValues.Int(Look("characterlimit"))));
    }

    public static string Shown(PanelBox b, string text)
    {
        var rules = Of(b);
        return rules.Secret ? new string(rules.Mask, text.Length) : text;
    }
}
