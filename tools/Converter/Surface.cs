using System.Text.Json;
using System.Text.RegularExpressions;

namespace KTable.Converter;

public static partial class Surface
{
    public sealed record Row(string Area, string Name, string Status, string Where, string Instead);
    public sealed record Use(Row Row, int Count);

    static readonly string[] Exact = { "native", "compat" };
    static readonly HashSet<string> Namespaces = new(StringComparer.Ordinal)
    {
        "Player", "UI", "Wait", "Timer", "Notes", "Turns", "Lighting", "Grid", "Hands", "Physics", "Time", "Info", "Tables", "Backgrounds", "MusicPlayer", "WebRequest", "JSON", "Global",
    };
    static readonly HashSet<string> Stdlib = new(StringComparer.Ordinal) { "string", "math", "table", "os", "coroutine", "bit32", "io", "debug", "utf8", "json", "compat", "board", "game", "wait" };
    static readonly Dictionary<string, string> SubStates = new(StringComparer.Ordinal)
    {
        ["Transform"] = "TransformState", ["ColorDiffuse"] = "ColourState", ["CustomImage"] = "CustomImageState", ["CustomMesh"] = "CustomMeshState",
        ["CustomAssetbundle"] = "CustomAssetbundleState", ["CustomPDF"] = "CustomPDFState", ["CustomDeck"] = "CustomDeckState", ["Bag"] = "BagState",
        ["Text"] = "TextState", ["FogOfWar"] = "FogOfWarState", ["FogOfWarRevealer"] = "FogOfWarRevealerState", ["LayoutZone"] = "LayoutZoneState",
        ["Clock"] = "ClockState", ["Counter"] = "CounterState", ["Tablet"] = "TabletState", ["Mp3Player"] = "Mp3PlayerState", ["Calculator"] = "CalculatorState",
        ["PhysicsMaterial"] = "PhysicsMaterialState", ["Rigidbody"] = "RigidbodyState", ["JointFixed"] = "JointFixedState", ["JointHinge"] = "JointHingeState",
        ["JointSpring"] = "JointSpringState", ["Grid"] = "GridState", ["Lighting"] = "LightingState", ["Hands"] = "HandsState", ["Turns"] = "TurnsState",
        ["ComponentTags"] = "TagsState", ["MusicPlayer"] = "MusicPlayerState",
    };

    [GeneratedRegex(@"--\[(=*)\[.*?\]\1\]|--[^\n]*", RegexOptions.Singleline)] private static partial Regex CommentRx();
    [GeneratedRegex(@"\b([A-Za-z_][A-Za-z0-9_]*)\s*[.:]\s*([A-Za-z_][A-Za-z0-9_]*)\s*\(")] private static partial Regex CallRx();
    [GeneratedRegex(@"(?<![.:\w])([A-Za-z_][A-Za-z0-9_]*)\s*\(")] private static partial Regex BareRx();
    [GeneratedRegex(@"\bfunction\s+(on[A-Z][A-Za-z0-9_]*)\s*\(")] private static partial Regex EventRx();
    [GeneratedRegex(@"\bself\s*\.\s*([a-z_][A-Za-z0-9_]*)\b(?!\s*[(.:])")] private static partial Regex PropRx();

    static Dictionary<string, Dictionary<string, Row>>? _rows;
    static readonly Dictionary<string, string> _defaults = new(StringComparer.Ordinal);

    public static string LedgerPath => Path.Combine(AppContext.BaseDirectory, "surface", "ledger.json");

    static Dictionary<string, Dictionary<string, Row>> Rows()
    {
        if (_rows != null) return _rows;
        var rows = new Dictionary<string, Dictionary<string, Row>>(StringComparer.Ordinal);
        if (File.Exists(LedgerPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(LedgerPath));
            foreach (var area in doc.RootElement.EnumerateObject())
            {
                if (area.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var d in area.Value.EnumerateObject()) _defaults[d.Name] = d.Value.GetString() ?? "";
                    continue;
                }
                var table = rows[area.Name] = new Dictionary<string, Row>(StringComparer.Ordinal);
                foreach (var e in area.Value.EnumerateArray())
                {
                    var name = e.GetProperty("row").GetString() ?? "";
                    table[name] = new Row(area.Name, name, Str(e, "status"), Str(e, "where"), Str(e, "instead"));
                }
            }
        }
        return _rows = rows;
    }

    static string Canonical(System.Text.Json.Nodes.JsonNode? n)
    {
        switch (n)
        {
            case null: return "null";
            case System.Text.Json.Nodes.JsonObject o: return "{" + string.Join(",", o.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => JsonSerializer.Serialize(kv.Key) + ":" + Canonical(kv.Value)))
                + "}";
            case System.Text.Json.Nodes.JsonArray a: return "[" + string.Join(",", a.Select(Canonical)) + "]";
        }
        var v = (System.Text.Json.Nodes.JsonValue)n;
        if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        if (v.TryGetValue<string>(out var s)) return JsonSerializer.Serialize(s);
        if (v.TryGetValue<double>(out var d)) return double.IsFinite(d) && Math.Abs(d) < 1e15
            && d == Math.Floor(d) ? ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture) : d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        return v.ToJsonString();
    }

    static bool Informs(string row, System.Text.Json.Nodes.JsonNode? v)
    {
        if (v is System.Text.Json.Nodes.JsonArray a) return a.Count > 0;
        if (v is System.Text.Json.Nodes.JsonObject o && o.Any(kv => kv.Value is System.Text.Json.Nodes.JsonObject or System.Text.Json.Nodes.JsonArray)) return o.Count > 0;
        return !_defaults.TryGetValue(row, out var d) || Canonical(v) != d;
    }

    static string Str(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static bool Loaded => Rows().Count > 0;

    static string UpperFirst(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string Camelify(string s)
    {
        var parts = s.Split('_');
        return parts.Length == 1 ? s : parts[0] + string.Concat(parts.Skip(1).Select(UpperFirst));
    }

    static IEnumerable<string> Folds(string name) => new[] { name, UpperFirst(name), Camelify(name), UpperFirst(Camelify(name)) }.Distinct();

    static Row? Find(string area, string name, string prefix = "")
    {
        if (!Rows().TryGetValue(area, out var table)) return null;
        foreach (var f in Folds(name))
        {
            if (table.TryGetValue(prefix + f, out var row)) return row;
            var lower = f.Length > 0 ? char.ToLowerInvariant(f[0]) + f[1..] : f;
            if (table.TryGetValue(prefix + lower, out row)) return row;
        }
        return null;
    }

    public static List<Use> NotExact(TtsSave save)
    {
        var counts = new Dictionary<Row, int>();
        void Hit(Row? row)
        {
            if (row == null || Exact.Contains(row.Status)) return;
            counts[row] = counts.GetValueOrDefault(row) + 1;
        }
        foreach (var o in save.Objects) ScanObject(o, Hit);
        ScanLua(save.Lua, Hit);
        return counts.Select(kv => new Use(kv.Key, kv.Value)).OrderByDescending(u => u.Count).ThenBy(u => u.Row.Name, StringComparer.Ordinal).ToList();
    }

    static void ScanObject(TtsObj o, Action<Row?> hit)
    {
        hit(Find("types", o.Name));
        foreach (var (k, v) in o.J)
        {
            if (!(SubStates.TryGetValue(k, out var sub) && v is System.Text.Json.Nodes.JsonObject j))
            {
                if (Informs("ObjectState." + k, v)) hit(Find("save", "ObjectState." + k));
                continue;
            }
            hit(Find("save", "ObjectState." + k));
            foreach (var (k2, v2) in j)
            {
                var nested = v2 as System.Text.Json.Nodes.JsonObject;
                string? inner = k == "CustomImage" ? k2 + "State" : k == "CustomMesh" ? "CustomShaderState" : null;
                if (inner == null || nested == null)
                {
                    if (Informs(sub + "." + k2, v2)) hit(Find("save", sub + "." + k2));
                    continue;
                }
                hit(Find("save", sub + "." + k2));
                foreach (var (k3, v3) in nested)
                    if (Informs(inner + "." + k3, v3)) hit(Find("save", inner + "." + k3));
            }
        }
        foreach (var c in o.Children) ScanObject(c, hit);
        foreach (var c in o.Contained) ScanObject(c, hit);
        foreach (var (s, i) in o.StateList().states.Select((s, i) => (s, i))) if (!ReferenceEquals(s, o)) ScanObject(s, hit);
        ScanLua(o.Lua, hit);
    }

    static void ScanLua(string lua, Action<Row?> hit)
    {
        if (string.IsNullOrWhiteSpace(lua)) return;
        var text = CommentRx().Replace(lua, " ");
        foreach (Match m in CallRx().Matches(text))
        {
            string recv = m.Groups[1].Value, name = m.Groups[2].Value;
            if (Namespaces.Contains(recv)) hit(Find("namespaces", name, recv + ".") ?? Find("namespaces", "*", recv + "."));
            else if (!Stdlib.Contains(recv)) hit(Find("object", name));
        }
        foreach (Match m in BareRx().Matches(text)) hit(Find("global", m.Groups[1].Value));
        foreach (Match m in EventRx().Matches(text)) hit(Find("events", m.Groups[1].Value));
        foreach (Match m in PropRx().Matches(text)) hit(Find("object", m.Groups[1].Value));
    }
}
