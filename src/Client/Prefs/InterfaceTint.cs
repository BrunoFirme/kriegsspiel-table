namespace KTable.Client.Prefs;

public readonly record struct Rgb(float R, float G, float B)
{
    public float Luminance => 0.2126f * Linear(R) + 0.7152f * Linear(G) + 0.0722f * Linear(B);

    public static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    public static Rgb FromHsv(float hue, float strength, float value)
    {
        float h = (hue % 360 + 360) % 360 / 60, f = h - MathF.Floor(h);
        float p = value * (1 - strength), q = value * (1 - strength * f), t = value * (1 - strength * (1 - f));
        return (int)h switch
        {
            0 => new(value, t, p),
            1 => new(q, value, p),
            2 => new(p, value, t),
            3 => new(p, q, value),
            4 => new(t, p, value),
            _ => new(value, p, q),
        };
    }

    public (float Hue, float Strength) HueAndStrength()
    {
        float most = MathF.Max(R, MathF.Max(G, B)), least = MathF.Min(R, MathF.Min(G, B)), span = most - least;
        if (most <= 0 || span <= 0) return (0, 0);
        float hue = most == R ? (G - B) / span % 6 : most == G ? (B - R) / span + 2 : (R - G) / span + 4;
        return ((hue * 60 + 360) % 360, span / most);
    }

    public static Rgb? FromHex(string hex)
    {
        if (!Backdrops.IsColour(hex)) return null;
        int n = Convert.ToInt32(hex[1..], 16);
        return new((n >> 16 & 0xff) / 255f, (n >> 8 & 0xff) / 255f, (n & 0xff) / 255f);
    }

    public string Hex => $"#{Byte(R):x2}{Byte(G):x2}{Byte(B):x2}";

    static int Byte(float c) => (int)MathF.Round(Math.Clamp(c, 0, 1) * 255);
}

public static class InterfaceTint
{
    public const string Charcoal = "";
    public const float MostStrength = 0.6f;
    const float ChipValue = 0.5f;

    public static readonly Rgb Bar = new(0.071f, 0.067f, 0.059f);
    public static readonly Rgb Surface = new(0.102f, 0.098f, 0.090f);
    public static readonly Rgb Raised = new(0.137f, 0.129f, 0.118f);
    public static readonly Rgb Overlay = new(0.196f, 0.188f, 0.173f);

    public static readonly Rgb Accent = new(0.788f, 0.635f, 0.298f);
    public static readonly Rgb AccentInk = new(0.867f, 0.733f, 0.427f);
    public static readonly Rgb OnAccent = new(0.11f, 0.09f, 0.055f);
    public const float LeastHueStrength = 0.12f;

    public static readonly IReadOnlyList<(string Name, string Tint)> Presets = new[]
    {
        ("Charcoal", Charcoal),
        ("Walnut", "#805f42"),
        ("Olive", "#7a8052"),
        ("Slate", "#546880"),
    };

    public static bool IsTint(string tint) => tint.Length == 0 || Backdrops.IsColour(tint);

    public static string Kept(string? tint) => tint is { } t && IsTint(t) ? t.ToLowerInvariant() : Charcoal;

    public static Rgb Tinted(Rgb own, string tint)
    {
        if (Rgb.FromHex(tint) is not { } picked) return own;
        var (hue, strength) = picked.HueAndStrength();
        return AtLuminance(hue, MathF.Min(strength, MostStrength), own.Luminance);
    }

    public static Rgb Hued(Rgb own, string tint)
    {
        if (Rgb.FromHex(tint) is not { } picked) return own;
        var (hue, strength) = picked.HueAndStrength();
        if (strength < LeastHueStrength) return own;
        float luminance = own.Luminance;
        return AtLuminance(hue, StrongestReaching(hue, own.HueAndStrength().Strength, luminance), luminance);
    }

    static float StrongestReaching(float hue, float strength, float luminance)
    {
        const int Halvings = 24;
        if (Rgb.FromHsv(hue, strength, 1).Luminance >= luminance) return strength;
        float low = 0, high = strength;
        for (int i = 0; i < Halvings; i++)
        {
            float mid = (low + high) / 2;
            if (Rgb.FromHsv(hue, mid, 1).Luminance >= luminance) low = mid;
            else high = mid;
        }
        return low;
    }

    public static Rgb Chip(string tint)
    {
        var (hue, strength) = (Rgb.FromHex(tint) ?? Surface).HueAndStrength();
        return Rgb.FromHsv(hue, MathF.Min(strength, MostStrength), ChipValue);
    }

    public static string? Named(string tint) => Presets.FirstOrDefault(p => p.Tint == Kept(tint)).Name;

    static Rgb AtLuminance(float hue, float strength, float luminance)
    {
        const int Halvings = 32;
        float low = 0, high = 1;
        for (int i = 0; i < Halvings; i++)
        {
            float mid = (low + high) / 2;
            if (Rgb.FromHsv(hue, strength, mid).Luminance < luminance) low = mid;
            else high = mid;
        }
        return Rgb.FromHsv(hue, strength, (low + high) / 2);
    }
}
