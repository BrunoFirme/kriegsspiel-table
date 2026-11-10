using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class StudioTools
{
    const int MaxUnits = 60, MaxPictures = 12;

    static readonly string UnitShape =
        "A unit: {name, label, fill, border, label_colour (#rrggbb), border_width (pixels), symbol (" + string.Join(", ", Symbols) + "), symbol_text, width, height, thickness (map units), tint (#rrggbb), outline, " +
        "counters: [{name, min, max, default, side (front or back), x, y, w, h (fractions of that face)}], " +
        "text_lines: [{name, default, colour, side, x, y, w, h}], first_look (the name of the look it is made in; default the unit's name), looks: [its other looks: a name, or {name, width, height, outline} for a look of its own size], " +
        "markers: [{kind, x, z}] (kinds of this pack put down beside it), tags: [words], script (Lua)}. Text lines and counters print on every look; a script's drawing stays on the look it was drawn on.";

    IEnumerable<AgentTool> UnitTools()
    {
        yield return new AgentTool("make_unit",
            "Makes a whole unit in one call: a block with its lettering and symbol, its counters, text lines, looks, tags and script, and returns a picture of it. Only name is needed. " + UnitShape,
            Args.Schema(
                new Arg("name", "string", "the unit's name", true), new Arg("label", "string", "lettering on the face (default the name)"),
                new Arg("fill", "string", "#rrggbb"), new Arg("border", "string", "#rrggbb"), new Arg("label_colour", "string", "#rrggbb"), new Arg("border_width", "number", "the border in pixels of the face"),
                new Arg("symbol", "string", string.Join(", ", Symbols)), new Arg("symbol_text", "string", "the text when symbol is text"),
                new Arg("width", "number", "map units (default 2)"), new Arg("height", "number", "map units (default half the width)"), new Arg("thickness", "number", "map units"),
                new Arg("tint", "string", "the side colour, #rrggbb"), new Arg("outline", "string", OutlineList),
                new Arg("counters", "array", "[{name, min, max, default, side, x, y, w, h}]"), new Arg("text_lines", "array", "[{name, default, colour, side, x, y, w, h}]"),
                new Arg("first_look", "string", "the name of the look it is made in (default the unit's name)"),
                new Arg("looks", "array", "its other looks: names, or {name, width, height, outline}"), new Arg("markers", "array", "[{kind, x, z}]"),
                new Arg("tags", "array", "words to find it by"), new Arg("script", "string", "Lua, when it needs buttons or reactions of its own")),
            MakeUnit);
        yield return new AgentTool("make_units",
            "Makes many units at once, each as make_unit does, and returns a picture of each (up to " + MaxPictures + "). " + UnitShape,
            Args.Schema(new Arg("units", "array", "the units, each as make_unit takes", true)),
            MakeUnits);
    }

    static JsonElement With(JsonObject o, string id)
    {
        var copy = (JsonObject)o.DeepClone();
        copy["id"] = id;
        return JsonDocument.Parse(copy.ToJsonString()).RootElement.Clone();
    }

    static JsonElement Element(JsonObject o) => JsonDocument.Parse(o.ToJsonString()).RootElement.Clone();

    async Task<(ToolResult result, PieceDef? made, List<string> notes)> Unit(JsonElement a)
    {
        var notes = new List<string>();
        if (_a.Studio is not { } s || !_a.InStudio) return (NotOpen, null, notes);
        var before = s.Pack.Pieces.ToHashSet();
        var block = await MakeBlock(a);
        if (block.IsError) return (block, null, notes);
        var d = s.Pack.Pieces.FirstOrDefault(p => !before.Contains(p));
        if (d == null) return (ToolResult.Fail("The block was not made."), null, notes);
        async Task Step(string what, Task<ToolResult> run)
        {
            var r = await run;
            if (r.IsError) notes.Add($"{what}: {r.Text.Split('\n')[0]}");
        }
        foreach (var c in Objects(a, "counters")) await Step($"counter {c["name"]}", SetCounter(With(c, d.Id)));
        foreach (var t in Objects(a, "text_lines")) await Step($"text line {t["name"]}", SetTextLine(With(t, d.Id)));
        foreach (var look in Looks(a))
        {
            var name = look["name"]?.GetValue<string>() ?? $"Look {d.States.Count + 1}";
            await Step($"look {name}", AddLook(Element(new JsonObject { ["id"] = d.Id, ["name"] = name })));
            if (look.Count <= 1) continue;
            var change = (JsonObject)look.DeepClone();
            change.Remove("name");
            change["look"] = d.States.Count;
            await Step($"look {name}", SetLook(With(change, d.Id)));
        }
        if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("markers", out var markers) && markers.ValueKind == JsonValueKind.Array)
            await Step("markers", SetMarkers(Element(new JsonObject { ["id"] = d.Id, ["markers"] = JsonNode.Parse(markers.GetRawText()) })));
        if (a.Str("outline") is { } outline) await Step("outline", SetPiece(Element(new JsonObject { ["id"] = d.Id, ["outline"] = outline })));
        var body = new JsonObject { ["id"] = d.Id };
        if (a.Num("thickness") is { } thickness) body["thickness"] = thickness;
        if (a.Str("tint") is { } tint) body["tint"] = tint;
        if (body.Count > 1) await Step("thickness and tint", SetPiece(Element(body)));
        if (a.Str("first_look") is { Length: > 0 } first && d.States.Count > 0) await Step($"look {first}", RenameLook(Element(new JsonObject { ["id"] = d.Id, ["look"] = 1, ["name"] = first })));
        if (Strings(a, "tags") is { Count: > 0 } tags) await Step("tags", SetPiece(Element(new JsonObject { ["id"] = d.Id, ["tags"] = new JsonArray(tags.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) })));
        if (a.Str("script") is { Length: > 0 } script) await Step("script", WriteScript(Element(new JsonObject { ["id"] = d.Id, ["text"] = script })));
        return (new ToolResult(Json(Summary(s, d)), false, Pictures(FacePng(s, PieceFaces.Of(d).FirstOrDefault()?.Sha))), d, notes);
    }

    static IEnumerable<JsonObject> Looks(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("looks", out var v) || v.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) yield return new JsonObject { ["name"] = item.GetString() };
            else if (item.ValueKind == JsonValueKind.Object && JsonNode.Parse(item.GetRawText()) is JsonObject o) yield return o;
    }

    static IEnumerable<JsonObject> Objects(JsonElement a, string name)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object && JsonNode.Parse(item.GetRawText()) is JsonObject o) yield return o;
    }

    async Task<ToolResult> MakeUnit(JsonElement a)
    {
        var (result, made, notes) = await Unit(a);
        if (made == null) return result;
        var text = $"Made {made.Name} ({made.Id}).\n" + result.Text + (notes.Count > 0 ? "\nNot done: " + string.Join("; ", notes) : "");
        return new ToolResult(text, notes.Count > 0, result.Images);
    }

    async Task<ToolResult> MakeUnits(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        var units = Objects(a, "units").ToList();
        if (units.Count == 0) return ToolResult.Fail("units is a list of units, each as make_unit takes.");
        if (units.Count > MaxUnits) return ToolResult.Fail($"At most {MaxUnits} units at a time.");
        var sb = new StringBuilder();
        var pictures = new List<byte[]>();
        bool any = false, trouble = false;
        foreach (var u in units)
        {
            var (result, made, notes) = await Unit(Element(u));
            if (made == null)
            {
                trouble = true;
                sb.Append($"{u["name"]}: not made: {result.Text.Split('\n')[0]}\n");
                continue;
            }
            any = true;
            sb.Append($"{made.Name} ({made.Id}){(notes.Count > 0 ? " · not done: " + string.Join("; ", notes) : "")}\n");
            trouble |= notes.Count > 0;
            if (pictures.Count < MaxPictures && result.Images is { Count: > 0 } img) pictures.Add(img[0]);
        }
        return new ToolResult((any ? "Made:\n" : "") + sb.ToString().TrimEnd(), trouble, pictures.Count > 0 ? pictures : null);
    }
}
