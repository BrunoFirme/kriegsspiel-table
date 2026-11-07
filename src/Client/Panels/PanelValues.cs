using System.Globalization;
using System.Text.RegularExpressions;

namespace KTable.Client.Panels;

public readonly record struct V2(float X, float Y)
{
    public static readonly V2 Zero = new(0, 0), One = new(1, 1), Half = new(0.5f, 0.5f);
    public float this[int axis] => axis == 0 ? X : Y;
    public V2 With(int axis, float v) => axis == 0 ? this with { X = v } : this with { Y = v };
    public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);
    public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);
    public static V2 operator *(V2 a, V2 b) => new(a.X * b.X, a.Y * b.Y);
    public static V2 operator *(V2 a, float k) => new(a.X * k, a.Y * k);
}

public readonly record struct V3(float X, float Y, float Z)
{
    public static readonly V3 Zero = new(0, 0, 0), One = new(1, 1, 1);
}

public readonly record struct Paint(float R, float G, float B, float A)
{
    public static readonly Paint White = new(1, 1, 1, 1), Clear = new(0, 0, 0, 0), Black = new(0, 0, 0, 1);
    public Paint Times(Paint o) => new(R * o.R, G * o.G, B * o.B, A * o.A);
    public Paint Scaled(float k) => new(R * k, G * k, B * k, A * k);
    public static Paint Bytes(int r, int g, int b, int a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);
}

public readonly record struct Offsets(int Left, int Right, int Top, int Bottom)
{
    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;
}

public sealed record ColorBlock(Paint Normal, Paint Highlighted, Paint Pressed, Paint Selected, Paint Disabled, float Multiplier = 1, float Fade = 0.1f)
{
    public static readonly ColorBlock Default = new(Paint.White, Paint.Bytes(245, 245, 245), Paint.Bytes(200, 200, 200), Paint.Bytes(245, 245, 245), new Paint(0.78431f, 0.78431f, 0.78431f, 0.50196f));
    public static readonly ColorBlock Black = new(Paint.Black, Paint.Black, Paint.Black, Paint.Black, Paint.Black, 1, 0);
}

public static class PanelValues
{
    static readonly Regex RgbNumber = new(@"(\d+[.]\d+)|(\d+)");
    static readonly char[] NumberSeparators = { ' ', 'x', ',' };
    static readonly char[] ColorSeparators = { ' ', '|' };
    static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    static readonly Dictionary<string, Paint> EngineColors = new()
    {
        ["black"] = Paint.Black, ["blue"] = new(0, 0, 1, 1), ["clear"] = Paint.Clear, ["cyan"] = new(0, 1, 1, 1), ["gray"] = new(0.5f, 0.5f, 0.5f, 1), ["grey"] = new(0.5f, 0.5f, 0.5f, 1),
        ["green"] = new(0, 1, 0, 1), ["magenta"] = new(1, 0, 1, 1), ["red"] = new(1, 0, 0, 1), ["white"] = Paint.White, ["yellow"] = new(1, 0.92156863f, 0.015686275f, 1),
    };

    public static bool IsNone(string? s) => string.IsNullOrEmpty(s) || s.Equals("none", StringComparison.OrdinalIgnoreCase) || (s.StartsWith('{') && s.EndsWith('}'));

    public static bool Bool(string? s) => s != null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");

    public static float Float(string? s) => float.TryParse(s, NumberStyles.Float, Culture, out var f) && float.IsFinite(f) ? f : 0;

    public static int Int(string? s) => int.TryParse(s, NumberStyles.Integer, Culture, out var i) ? i : 0;

    public static List<float> Floats(string? s)
    {
        var list = new List<float>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var part in s.Trim('(', ')').Split(NumberSeparators)) list.Add(part.Length == 0 ? 0 : Float(part));
        return list;
    }

    static List<int> Ints(string? s)
    {
        var list = new List<int>();
        if (string.IsNullOrEmpty(s)) return list;
        foreach (var part in s.Trim('(', ')').Split(NumberSeparators))
            list.Add(part.Length > 0 && int.TryParse(part, NumberStyles.Any, Culture, out var i) ? i : 0);
        return list;
    }

    public static V2 Vector2(string? s)
    {
        var f = Floats(s);
        if (f.Count == 0) return V2.Zero;
        return new V2(f[0], f.Count > 1 ? f[1] : f[0]);
    }

    public static V3 Vector3(string? s)
    {
        var f = Floats(s);
        if (f.Count == 0) return V3.Zero;
        if (f.Count == 1) return new V3(f[0], f[0], f[0]);
        return new V3(f[0], f[1], f.Count > 2 ? f[2] : 0);
    }

    public static Offsets Padding(string? s)
    {
        var i = Ints(s);
        if (i.Count == 0) return default;
        int l = i[0], r = i.Count > 1 ? i[1] : l, t = i.Count > 2 ? i[2] : r, b = i.Count > 3 ? i[3] : t;
        return new Offsets(l, r, t, b);
    }

    public static Paint Color(string s, IReadOnlyDictionary<string, string>? named) => TryColor(s, named) ?? Paint.Clear;

    public static Paint? TryColor(string s, IReadOnlyDictionary<string, string>? named)
    {
        if (s.StartsWith('{')) return Paint.White;
        var lower = s.ToLowerInvariant();
        if (named != null && named.TryGetValue(lower, out var n)) return Color(n, null);
        if (lower.StartsWith('#')) return Hex(lower);
        if (lower.StartsWith("rgb"))
        {
            var m = RgbNumber.Matches(lower);
            if (m.Count >= 3)
                return new Paint(Float(m[0].Value), Float(m[1].Value), Float(m[2].Value), m.Count == 4 ? Float(m[3].Value) : 1);
        }
        return EngineColors.TryGetValue(lower, out var c) ? c : Paint.Clear;
    }

    static Paint? Hex(string hex)
    {
        hex = hex.Replace("0x", "").Replace("#", "");
        if (hex.Length < 6) return Paint.Clear;
        if (!byte.TryParse(hex[..2], NumberStyles.HexNumber, Culture, out var r) || !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, Culture, out var g)
            || !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, Culture, out var b)) return null;
        byte a = 255;
        if (hex.Length == 8 && !byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, Culture, out a)) return null;
        return Paint.Bytes(r, g, b, a);
    }

    public static List<Paint> Colors(string s, IReadOnlyDictionary<string, string>? named) => s.Split(ColorSeparators).Select(p => Color(p, named)).ToList();

    public static ColorBlock Block(string s, IReadOnlyDictionary<string, string>? named)
    {
        var c = Colors(s, named);
        var normal = c.Count > 0 ? c[0] : Paint.White;
        var high = c.Count > 1 ? c[1] : Paint.Clear;
        var pressed = c.Count > 2 ? c[2] : Paint.White;
        var selected = c.Count is > 2 and < 5 ? c[2] : c.Count > 4 ? c[3] : Paint.Clear;
        var disabled = c.Count is > 3 and < 5 ? c[3] : c.Count > 4 ? c[4] : new Paint(1, 1, 1, 0.5f);
        return new ColorBlock(normal, high, pressed, selected, disabled, 1, 0);
    }

    public static T? Enum<T>(string? s) where T : struct, System.Enum =>
        s != null && System.Enum.TryParse<T>(s.Trim(), true, out var v) && System.Enum.IsDefined(v) ? v : null;
}

public enum Anchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }

public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
