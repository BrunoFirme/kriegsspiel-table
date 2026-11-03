using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed record SpawnSpec(string Type, double Scale, string ImageUrl, string Name);

public sealed partial class ScriptInfo
{
    public string Kind { get; private set; } = "none";
    public string Variant { get; private set; } = "";
    public List<CounterDef> Counters { get; } = new();
    public List<TextLineDef> TextLines { get; } = new();
    public Dictionary<string, string> CounterKeys { get; } = new();
    public Dictionary<string, string> TextKeys { get; } = new();
    public Dictionary<string, SpawnSpec> Spawns { get; } = new();
    public bool HasForm, HasCondition;
    public Dictionary<int, (string kind, string name)> FieldMap { get; } = new();

    static List<(string role, string value)>? FieldsOf(string luaState)
    {
        try
        {
            using var doc = JsonDocument.Parse(luaState);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("fields", out var f) || f.ValueKind != JsonValueKind.Array) return null;
            var list = new List<(string, string)>();
            foreach (var x in f.EnumerateArray())
            {
                var role = x.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "";
                var val = "";
                if (x.TryGetProperty("value", out var v))
                {
                    var e = v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0 ? v[0] : v;
                    val = e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ValueKind == JsonValueKind.Number ? e.GetRawText() : "";
                }
                list.Add((role, val));
            }
            return list;
        }
        catch (Exception e) when (e is JsonException or ArgumentException) { return null; }
    }

    static readonly Dictionary<string, string> TextNames = new()
    {
        ["name"] = "Name", ["name1"] = "Name", ["name2"] = "Name 2", ["l1"] = "Line 1", ["l2"] = "Line 2", ["l3"] = "Line 3",
        ["text1"] = "Line 1", ["text2"] = "Line 2", ["text3"] = "Line 3", ["text"] = "Text", ["corps"] = "Corps",
        ["div"] = "Division", ["division"] = "Division", ["bde"] = "Brigade", ["brigade"] = "Brigade", ["army"] = "Army",
        ["formation"] = "Formation", ["support"] = "Support",
    };
    static readonly Dictionary<string, string> LegacyCounters = new()
    {
        ["ammo"] = "Ammo", ["men"] = "Manpower", ["mor"] = "Morale", ["can"] = "Canister", ["gun"] = "Guns",
    };

    [GeneratedRegex(@"^([A-Za-z_]\w*)\s*=\s*(""[^""]*""|'[^']*'|-?\d+(?:\.\d+)?|true|false)", RegexOptions.Multiline)]
    private static partial Regex Assign();

    public static ScriptInfo Parse(string lua, string luaState)
    {
        var si = new ScriptInfo();
        if (string.IsNullOrWhiteSpace(lua)) return si;
        var vars = new Dictionary<string, string>();
        foreach (Match m in Assign().Matches(lua)) vars.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Trim('"', '\''));
        var state = ParseState(luaState);

        if (lua.Contains("MarumEditable") && FieldsOf(luaState) is { } fields)
        {
            int nCounter = 0, nText = 0;
            for (int i = 0; i < fields.Count; i++)
            {
                var (role, value) = fields[i];
                if (role.Contains("name", StringComparison.OrdinalIgnoreCase) && !si.TextLines.Any(t => t.Name == "Name"))
                { si.TextLines.Add(new TextLineDef { Name = "Name", Default = value }); si.FieldMap[i] = ("text", "Name"); }
                else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                { var n = $"Value {++nCounter}"; si.Counters.Add(new CounterDef { Name = n, Min = Math.Min(0, iv), Max = Math.Max(99, iv), Default = iv }); si.FieldMap[i] = ("counter", n); }
                else
                { var n = $"Line {++nText}"; si.TextLines.Add(new TextLineDef { Name = n, Default = value }); si.FieldMap[i] = ("text", n); }
            }
            si.Kind = "data"; si.Variant = "editable sheet";
            return si;
        }

        if (lua.Contains("radius_1")) { si.Kind = "dropped"; si.Variant = "range rings (vector-line circles)"; return si; }
        if (lua.Contains("tableHeightOffset")) { si.Kind = "dropped"; si.Variant = "Flex Table Control"; return si; }
        if (lua.Contains("bagStatus")) { si.Kind = "dropped"; si.Variant = "fog-of-war memory bag"; return si; }
        if (lua.Contains("onObjectPickUp") && lua.Length < 600) { si.Kind = "dropped"; si.Variant = "courier pick-up hook"; return si; }

        for (int n = 1; n <= 6; n++)
        {
            if (!vars.TryGetValue($"counter{n}", out var label) && !(state?.ContainsKey($"ctr{n}") ?? false) && !vars.ContainsKey($"ctr{n}")) continue;
            if (!vars.ContainsKey($"ctr{n}") && !(state?.ContainsKey($"ctr{n}") ?? false)) continue;
            label = string.IsNullOrWhiteSpace(label) ? $"Counter {n}" : label.Trim();
            var c = new CounterDef
            {
                Name = Unique(si.Counters.Select(x => x.Name), label),
                Min = vars.TryGetValue($"MIN_CTR{n}", out var mn) ? Int(mn, 0) : 0,
                Max = vars.TryGetValue($"MAX_CTR{n}", out var mx) ? Int(mx, 9) : 99,
                Default = vars.TryGetValue($"ctr{n}", out var dv) ? Int(dv, 0) : 0,
            };
            si.Counters.Add(c);
            si.CounterKeys[$"ctr{n}"] = c.Name;
            bool spawn = vars.TryGetValue($"spawnButton{n}", out var sb) ? sb == "true" : vars.ContainsKey($"spawn{n}Btn");
            if (spawn && SpawnIn(lua, $"ctr{n}Button") is { } sp) si.Spawns[c.Name] = sp;
        }
        foreach (var (k, label) in LegacyCounters)
        {
            if (!vars.ContainsKey($"MAX_{k.ToUpperInvariant()}") && !(state?.ContainsKey(k) ?? false)) continue;
            var c = new CounterDef
            {
                Name = Unique(si.Counters.Select(x => x.Name), label),
                Min = vars.TryGetValue($"MIN_{k.ToUpperInvariant()}", out var mn) ? Int(mn, 0) : 0,
                Max = vars.TryGetValue($"MAX_{k.ToUpperInvariant()}", out var mx) ? Int(mx, 9) : 9,
                Default = vars.TryGetValue(k, out var dv) ? Int(dv, 0) : 0,
            };
            si.Counters.Add(c);
            si.CounterKeys[k] = c.Name;
        }
        if (vars.ContainsKey("MAX_VALUE"))
        {
            si.Counters.Add(new CounterDef { Name = "Value", Min = Int(vars.GetValueOrDefault("MIN_VALUE", "0"), 0), Max = Int(vars["MAX_VALUE"], 99) });
            si.CounterKeys["[0]"] = "Value";
        }

        var textKeys = new List<string>();
        foreach (var k in TextNames.Keys)
            if ((vars.ContainsKey(k) && !IsNumber(vars[k])) || (state != null && state.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String) || lua.Contains($"edit{Cap(k)}("))
                textKeys.Add(k);
        if (!textKeys.Contains("name") && !textKeys.Contains("name1") && vars.ContainsKey("nameColor") && lua.Contains("editName")) textKeys.Insert(0, "name");
        foreach (var k in textKeys)
        {
            var nm = TextNames[k];
            if (si.TextLines.Any(t => t.Name == nm)) { si.TextKeys[k] = nm; continue; }
            var def = vars.TryGetValue(k, out var dv) && !IsNumber(dv) ? dv : "";
            if (def == "Unit Name") def = "";
            si.TextLines.Add(new TextLineDef { Name = nm, Default = def });
            si.TextKeys[k] = nm;
        }
        si.TextLines.Sort((a, b) => (a.Name == "Name" ? 0 : 1).CompareTo(b.Name == "Name" ? 0 : 1));

        if (lua.Contains("function setLine") && lua.Contains("function setColumn") && !si.TextLines.Any(t => t.Name == "Formation"))
        { si.HasForm = true; si.TextLines.Add(new TextLineDef { Name = "Formation", Default = "Line" }); }
        if (lua.Contains("function setFresh") && lua.Contains("function setSpent"))
        { si.HasCondition = true; si.TextLines.Add(new TextLineDef { Name = "Condition", Default = "Fresh" }); }

        if (si.Counters.Count > 0 || si.TextLines.Count > 0) { si.Kind = "data"; si.Variant = string.Join(",", vars.Keys.OrderBy(k => k).Take(8)); }
        else { si.Kind = "dropped"; si.Variant = "unknown: " + string.Join(",", vars.Keys.OrderBy(k => k).Take(10)) + $" ({lua.Length} chars)"; }
        return si;
    }

    public (Dictionary<string, int> counters, Dictionary<string, string> text) Values(string luaState)
    {
        var counters = new Dictionary<string, int>();
        var text = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(luaState)) return (counters, text);
        if (FieldMap.Count > 0 && FieldsOf(luaState) is { } fields)
        {
            foreach (var (i, (kind, name)) in FieldMap)
            {
                if (i >= fields.Count) continue;
                var v = fields[i].value;
                if (kind == "counter" && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv)) counters[name] = iv;
                else if (kind == "text" && v.Length > 0) text[name] = v;
            }
            return (counters, text);
        }
        try
        {
            using var doc = JsonDocument.Parse(luaState);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (CounterKeys.TryGetValue("[0]", out var cn) && root.GetArrayLength() > 0 && TryInt(root[0], out var v0)) counters[cn] = v0;
                return (counters, text);
            }
            if (root.ValueKind != JsonValueKind.Object) return (counters, text);
            foreach (var p in root.EnumerateObject())
            {
                if (CounterKeys.TryGetValue(p.Name, out var cname) && TryInt(p.Value, out var iv)) counters[cname] = iv;
                else if (TextKeys.TryGetValue(p.Name, out var tname) && p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 } s)
                    text[tname] = s;
            }
            if (HasForm && root.TryGetProperty("currentForm", out var cf) && TryInt(cf, out var f)) text["Formation"] = f == 2 ? "Column" : "Line";
            if (HasCondition && root.TryGetProperty("currentState", out var cs) && TryInt(cs, out var c)) text["Condition"] = c == 2 ? "Spent" : "Fresh";
        }
        catch (JsonException) {  }
        return (counters, text);
    }

    static Dictionary<string, JsonElement>? ParseState(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try
        {
            using var doc = JsonDocument.Parse(s);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        catch (JsonException) { return null; }
    }

    static SpawnSpec? SpawnIn(string lua, string fn)
    {
        int i = lua.IndexOf("function " + fn, StringComparison.Ordinal);
        if (i < 0) return null;
        int j = lua.IndexOf("\nfunction ", i + 10, StringComparison.Ordinal);
        var body = lua[i..(j < 0 ? lua.Length : j)];
        var type = Regex.Match(body, @"type\s*=\s*['""](\w+)['""]");
        var scale = Regex.Match(body, @"scale\s*=\s*\{\s*x\s*=\s*([\d.]+)");
        var img = Regex.Match(body, @"image\s*=\s*""([^""]+)""");
        if (!type.Success || !img.Success) return null;
        var comment = Regex.Match(body, @"--\s*spawn\s+([^\r\n]+)");
        var name = comment.Success ? comment.Groups[1].Value.Trim() : "marker";
        name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        return new SpawnSpec(type.Groups[1].Value, scale.Success ? double.Parse(scale.Groups[1].Value, CultureInfo.InvariantCulture) : 0.25, img.Groups[1].Value, name);
    }

    static bool TryInt(JsonElement e, out int v)
    {
        v = 0;
        if (e.ValueKind == JsonValueKind.Number) { v = (int)Math.Round(e.GetDouble()); return true; }
        if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { v = (int)Math.Round(d); return true; }
        return false;
    }
    static int Int(string s, int def) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : def;
    static bool IsNumber(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _) || s is "true" or "false";
    static string Cap(string k) => k.Length == 0 ? k : char.ToUpperInvariant(k[0]) + k[1..];
    static string Unique(IEnumerable<string> existing, string name)
    {
        var set = existing.ToHashSet();
        if (!set.Contains(name)) return name;
        for (int i = 2; ; i++) if (!set.Contains($"{name} {i}")) return $"{name} {i}";
    }

    public string Signature => string.Join("|", Counters.Select(c => $"{c.Name}:{c.Min}:{c.Max}")) + "#" + string.Join("|", TextLines.Select(t => t.Name));
}
