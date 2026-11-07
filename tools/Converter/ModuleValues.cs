using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KTable.Converter;

public static class ModuleValues
{
    public static string? Text(JsonNode? n)
    {
        if (n is not JsonValue v) return null;
        if (v.TryGetValue<string>(out var s)) return s;
        if (v.TryGetValue<bool>(out var b)) return b ? bool.TrueString : bool.FalseString;
        if (Numeric(v) is { } d) return d.ToString(CultureInfo.InvariantCulture);
        return null;
    }

    public static bool Flag(JsonNode? n, bool unset)
    {
        if (n is not JsonValue v) return unset;
        if (v.TryGetValue<bool>(out var b)) return b;
        if (Numeric(v) is { } d) return d != 0;
        if (v.TryGetValue<string>(out var s) && bool.TryParse(s.Trim(), out var parsed)) return parsed;
        return unset;
    }

    public static double Number(JsonNode? n, double unset)
    {
        if (n is not JsonValue v) return unset;
        if (Numeric(v) is { } d) return d;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
        if (v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
        return unset;
    }

    public static double Finite(JsonNode? n, double unset) => Number(n, unset) is var d && double.IsFinite(d) ? d : unset;

    public static int Whole(JsonNode? n, int unset)
    {
        if (n is JsonValue v && v.TryGetValue<int>(out var i)) return i;
        var d = Number(n, double.NaN);
        return double.IsFinite(d) && Math.Abs(d) <= int.MaxValue ? (int)Math.Round(d) : unset;
    }

    static double? Numeric(JsonValue v) =>
        v.GetValueKind() == JsonValueKind.Number && double.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static bool NotANumber(JsonNode? n) => double.IsNaN(Number(n, 0));
}
