using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Agent;
using KTable.Client.Effects;
using KTable.Client.Scripting;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class StudioTools
{
    readonly AgentEndpoint _a;
    readonly StudioAgentUndo _undo = new();
    PieceStudio? _undoFor, _for;
    StudioSteps? _steps;

    public StudioTools(AgentEndpoint a) { _a = a; }

    StudioAgentUndo History(PieceStudio s)
    {
        if (_undoFor != s) { _undo.Clear(); _undoFor = s; }
        return _undo;
    }

    static readonly string[] Symbols = { "none", "infantry", "cavalry", "artillery", "horse-artillery", "engineers", "hq", "text" };
    static readonly string[] Shapes = { "token", "square", "round", "hex" };

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("studio_list_pieces",
            "The kinds of piece in the pack open in the piece studio: id, name, kind, size, counters, text lines, looks, markers, and whether each has a script.",
            Args.Schema(), _ => On(s => Task.FromResult(new ToolResult(Json(new JsonArray(s.Pack.Pieces.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(d => (JsonNode?)Summary(s, d)).ToArray()))))));
        yield return new AgentTool("studio_read_piece",
            "One kind of piece in full as the pack keeps it (its script included), with a picture of its front.",
            Args.Schema(new Arg("id", "string", "the kind's id", true)),
            a => On(s => Task.FromResult(Def(s, a) is { } d ? new ToolResult(JsonSerializer.Serialize(d, Opts), false, Pictures(FacePng(s, PieceFaces.Of(d).FirstOrDefault()?.Sha))) : NoKind(a))));
        yield return new AgentTool("studio_make_block",
            "A new block (the wargame unit kind): a coloured face with lettering and a unit symbol, kept as a unit plate layer (kind block; studio_read_face / studio_write_face change it later). Selects it.",
            Args.Schema(
                new Arg("name", "string", "the kind's name", true),
                new Arg("width", "number", "map units across (default 2)"), new Arg("height", "number", "map units down (default half the width)"),
                new Arg("fill", "string", "face colour, #rrggbb"), new Arg("border", "string", "border colour, #rrggbb"),
                new Arg("label", "string", "lettering on the face (default the name)"), new Arg("label_colour", "string", "#rrggbb"),
                new Arg("symbol", "string", "one of " + string.Join(", ", Symbols)), new Arg("symbol_text", "string", "the text when symbol is text"),
                new Arg("border_width", "number", $"the border in pixels of the face (0–{MaxBorderPx}; the face's long side is {FaceLong} or more)")),
            MakeBlock);
        yield return new AgentTool("studio_new_piece",
            "A new flat kind: a plain token, square, round or hex counter in a colour, ready for studio_write_face to draw on. Selects it.",
            Args.Schema(new Arg("name", "string", "the kind's name", true), new Arg("shape", "string", "token, square, round or hex", true),
                new Arg("width", "number", "map units across (default 1)"), new Arg("height", "number", "map units down (default: by shape)"), new Arg("colour", "string", "#rrggbb")),
            NewPiece);
        yield return new AgentTool("studio_pieces_from_pictures",
            "One new kind per picture file (PNG, JPEG, WebP, SVG, BMP, TGA, TIFF), cut along its see-through edge when it has one.",
            Args.Schema(new Arg("paths", "array", "full paths of picture files", true)), FromPictures);
        yield return new AgentTool("studio_import_models",
            "One new 3D kind per model file (glTF, GLB, FBX, OBJ with MTL, STL, PLY, Collada .dae).",
            Args.Schema(new Arg("paths", "array", "full paths of model files", true)), ImportModels);
        yield return new AgentTool("studio_new_effect",
            "A new effect kind (smoke, musketry, cannon, fire…): from a preset by name, or a kind with its settings. Presets: " + string.Join(", ", FirmeEffects.Presets.Select(p => p.Name)) + ". Kinds: " + string.Join(", ", EffectKinds.Kinds.Select(k => k.Id)) + ".",
            Args.Schema(new Arg("name", "string", "the kind's name", true), new Arg("preset", "string", "a preset's name"), new Arg("kind", "string", "an effect kind when there is no preset"),
                new Arg("width", "number", "map units"), new Arg("height", "number", "map units")),
            NewEffect);
        yield return new AgentTool("studio_set_piece",
            "Changes a kind as its Block layer and Piece tab do: name, size, thickness, tint (a block's side colour, a model's colour), outline, tags, or name box (where its name prints: fractions of the face, x and y from the top left). " +
            "Give width or height alone to keep the proportions, both to change them (the faces stretch with the piece); a model's width is how wide it stands on the table. One look's own size and outline: studio_set_look.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("name", "string", "new name"), new Arg("width", "number", "map units"), new Arg("height", "number", "map units"),
                new Arg("thickness", "number", "map units"), new Arg("tint", "string", "#rrggbb"), new Arg("outline", "string", OutlineList), new Arg("tags", "array", "the whole list of tags"),
                new Arg("name_box", "object", "{x, y, w, h} as fractions of the face, or null for the usual place")),
            SetPiece);
        yield return new AgentTool("studio_frame",
            "Frames a flat kind as the studio's Frame… does: a border along its outline and a colour behind its pictures where they are see-through, on every face that has a picture. off takes the frame away.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("border", "string", "#rrggbb (default near black)"), new Arg("border_width", "number", "0–48, pixels on a 512-pixel face (default 10; 0 for none)"),
                new Arg("behind", "string", "#rrggbb (default parchment)"), new Arg("off", "boolean", "true: take the frame off")),
            Frame);
        yield return new AgentTool("studio_set_counter",
            "Adds or changes a counter (a number every piece of the kind carries): its range, start, and where it prints (side front or back, x, y, w, h as fractions of that face from the top left). Markers it puts down beside the piece: studio_set_markers.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("name", "string", "the counter", true), new Arg("new_name", "string", "rename it"),
                new Arg("min", "number", "lowest"), new Arg("max", "number", "highest"), new Arg("default", "number", "start value"),

                new Arg("side", "string", "front or back"), new Arg("x", "number", "0–1"), new Arg("y", "number", "0–1"), new Arg("w", "number", "0–1"), new Arg("h", "number", "0–1"),
                new Arg("show_name", "boolean", "print its name"), new Arg("show_buttons", "boolean", "print − and + on it")),
            SetCounter);
        yield return new AgentTool("studio_set_text_line",
            "Adds or changes a text line (a named line of text every piece of the kind carries) and where it prints (side, x, y, w, h as fractions of that face). Text lines and counters print on every look; what a script draws stays on the look it was drawn on (redraw it from on_state), so names belong in text lines.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("name", "string", "the text line", true), new Arg("new_name", "string", "rename it"), new Arg("default", "string", "its starting text"),
                new Arg("side", "string", "front or back"), new Arg("x", "number", "0–1"), new Arg("y", "number", "0–1"), new Arg("w", "number", "0–1"), new Arg("h", "number", "0–1")),
            SetTextLine);
        yield return new AgentTool("studio_remove",
            "Takes a counter, a text line or a look (by number from 1) away from a kind.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("counter", "string", "a counter's name"), new Arg("text_line", "string", "a text line's name"), new Arg("look", "number", "a look's number from 1")),
            Remove);
        yield return new AgentTool("studio_add_look",
            "Adds a look (a state the piece turns to; number keys at the table) as a copy of look 1. While it stays a copy, drawing look 1 changes it too; draw it on its own with studio_write_face (its face keys are returned). Its own size and outline: studio_set_look.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("name", "string", "the look's name")),
            AddLook);
        yield return new AgentTool("studio_rename_look",
            "Renames a look.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("look", "number", "its number from 1", true), new Arg("name", "string", "the new name", true)),
            RenameLook);
        yield return new AgentTool("studio_set_markers",
            "Sets the markers of a kind: the pieces of this pack an umpire puts down beside it from its right-click menu, each at its place (map units from the piece's centre, x right, z down). Replaces the list.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("markers", "array", "[{kind, x, z}], empty for none", true)),
            SetMarkers);
        yield return new AgentTool("studio_set_effect",
            "Changes an effect kind's settings: kind, amount, height, puff, speed, wind, every (seconds), colour, light, smoke, volley (together or rolling).",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("kind", "string", string.Join(", ", EffectKinds.Kinds.Select(k => k.Id))),
                new Arg("amount", "number", "how much"), new Arg("height", "number", "how high"), new Arg("puff", "number", "puff size"), new Arg("speed", "number", "speed"),
                new Arg("wind", "number", "how much the wind carries it"), new Arg("every", "number", "seconds between shots"), new Arg("colour", "string", "#rrggbb"),
                new Arg("light", "boolean", "flashes light"), new Arg("smoke", "boolean", "makes smoke"), new Arg("volley", "string", "together or rolling")),
            SetEffect);
        yield return new AgentTool("studio_write_script",
            "Sets a kind's script in the pack (every piece of that kind runs it) and runs it on the studio's table; the same safety check as the Script tab. Returns what it printed; fails when the script fails as it loads. An empty text removes it.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("text", "string", "the whole Lua script", true)),
            WriteScript);
        yield return new AgentTool("studio_duplicate_piece",
            "Copies a kind (looks, counters, script and all) under a new name.",
            Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("name", "string", "the copy's name")),
            Duplicate);
        yield return new AgentTool("studio_delete_piece",
            "Deletes a kind from the pack (studio_undo brings it back).",
            Args.Schema(new Arg("id", "string", "the kind's id", true)),
            Delete);
        foreach (var t in MoreTools()) yield return t;
    }

    IEnumerable<AgentTool> MoreTools()
    {
        foreach (var t in UnitTools()) yield return t;
        foreach (var t in ModelTools()) yield return t;
        foreach (var t in PackTools()) yield return t;
        foreach (var t in FaceTools()) yield return t;
        yield return new AgentTool("studio_undo",
            "Undoes the agent's last change in the piece studio (a kind made, changed, drawn or deleted). Changes made by hand undo in the studio itself.",
            Args.Schema(), Undo);
        yield return new AgentTool("studio_steps",
            "Drives the studio's own tools as a person would, steps separated by ';'. Prefer studio_write_face for drawing. Steps: " + string.Join(", ", StudioSteps.Verbs) +
            ". Example: 'piece:<id>; view:paint; tool:brush; ink:#c0392b; size:12; stroke:100,100,300,180; save'. Coordinates are picture pixels in the Paint view; view is paint, model or table.",
            Args.Schema(new Arg("steps", "string", "steps separated by ';'", true)),
            Steps);
    }

    async Task<ToolResult> On(Func<PieceStudio, Task<ToolResult>> work) => _a.Studio is { } s && _a.InStudio ? await work(s) : NotOpen;

    async Task<ToolResult> Change(JsonElement a, string what, Func<PieceStudio, PieceDef, Task<ToolResult>> work)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        if (Def(s, a) is not { } d) return NoKind(a);
        if (await s.SettleWork() is { } problem) return ToolResult.Fail("The studio could not save what is open first: " + problem);
        if (!s.Pack.Pieces.Contains(d)) return NoKind(a);
        History(s).Edited(s.Pack, d, $"{what} ({d.Name})");
        var r = await work(s, d);
        if (!_undo.DropIfSame(d)) _a.Log($"{what}: {d.Name}");
        return r;
    }

    internal static ToolResult NotOpen => ToolResult.Fail("The piece studio is not open: Piece studio on the title screen → Edit pieces opens a pack in it.");

    static PieceDef? Def(PieceStudio s, JsonElement a) => a.Str("id") is { } id ? s.Pack.Pieces.FirstOrDefault(d => d.Id == id) : null;

    static ToolResult NoKind(JsonElement a) => ToolResult.Fail($"No kind {a.Str("id")} in this pack: studio_list_pieces lists them.");

    static string Json(JsonNode n) => n.ToJsonString(Opts);

    static IReadOnlyList<byte[]>? Pictures(params byte[]?[] pngs) => pngs.OfType<byte[]>().ToList() is { Count: > 0 } l ? l : null;

    static JsonObject Summary(PieceStudio s, PieceDef d) => new()
    {
        ["id"] = d.Id, ["name"] = d.Name, ["kind"] = d.Kind, ["outline"] = d.Kind == "model" || EffectFx.IsEffect(d) ? null : s.Framer.NameOf(d, 0).ToLowerInvariant(),
        ["w"] = d.Size.W, ["h"] = d.Size.H, ["thickness"] = d.Thickness, ["tint"] = d.Tint,
        ["counters"] = new JsonArray(d.Counters.Select(c => (JsonNode?)JsonValue.Create($"{c.Name} {c.Min}–{c.Max} from {c.Default}" + (c.Place != null ? $" on the {c.Place.Side}" : ""))).ToArray()),
        ["textLines"] = new JsonArray(d.TextLines.Select(t => (JsonNode?)JsonValue.Create(t.Name)).ToArray()),
        ["looks"] = new JsonArray(d.States.Select((x, i) => (JsonNode?)JsonValue.Create(LookLine(s, d, i))).ToArray()),
        ["markers"] = new JsonArray(d.Attachments.Select(m => (JsonNode?)JsonValue.Create(FormattableString.Invariant($"{s.Pack.Pieces.FirstOrDefault(p => p.Id == m.Piece)?.Name ?? m.Piece} at x {m.Offset.X:0.##}, z {m.Offset.Z:0.##}"))).ToArray()),
        ["faces"] = new JsonArray(PieceFaces.Of(d).Select(f => (JsonNode?)JsonValue.Create(f.Key)).ToArray()),
        ["effect"] = d.Effect?.Kind, ["script"] = d.Script is { Length: > 0 }, ["tags"] = new JsonArray(d.Tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
    };

    static string LookLine(PieceStudio s, PieceDef d, int i)
    {
        var st = d.States[i];
        var name = st.Name.Length > 0 ? st.Name : $"Look {i + 1}";
        if (i > 0 && PieceFaces.Follows(d, i)) name += ", a copy of look 1";
        if (!PieceFaces.OwnSize(d, i)) return name;
        var size = st.Size ?? d.Size;
        return name + FormattableString.Invariant($", its own size {size.W:0.###} × {size.H:0.###} and outline {s.Framer.NameOf(d, i).ToLowerInvariant()}");
    }

    static string Colour(string? hex, string fallback)
    {
        if (hex == null) return fallback;
        var h = hex.Trim();
        if (!h.StartsWith('#')) h = "#" + h;
        if (h.Length == 7) h += "ff";
        return h.Length == 9 && h.Skip(1).All(Uri.IsHexDigit) ? h.ToLowerInvariant() : fallback;
    }

    static bool IsColour(string? hex) => hex != null && Colour(hex, "") != "";

    static List<string>? Strings(JsonElement a, string name)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
    }

    static void Edit(PieceStudio s, PieceDef d, Action<PieceDef> change)
    {
        if (d == s.Sel) { s.Apply(change, list: true, inspector: true); return; }
        change(d);
        s.SavePack();
        s.T.RebuildPiecesOf(d.Id);
        s.RebuildList();
    }

    ToolResult Made(PieceStudio s, PieceDef d, string what)
    {
        History(s).Made(d, $"{what} {d.Name}");
        _a.Log($"{what} {d.Name}");
        return new ToolResult(FormattableString.Invariant($"Made {d.Name} ({d.Id}), {d.Size.W:0.###} × {d.Size.H:0.###} map units, selected.\n") + Json(Summary(s, d)), false, Pictures(FacePng(s, PieceFaces.Of(d).FirstOrDefault()?.Sha)));
    }

    async Task<ToolResult> MakeBlock(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        var name = a.Str("name")?.Trim();
        if (string.IsNullOrEmpty(name)) return ToolResult.Fail("name is needed.");
        double w = Math.Clamp(a.Num("width") ?? 2, MinSpan, MaxSpan), h = Math.Clamp(a.Num("height") ?? w / 2, MinSpan, MaxSpan);
        var symbol = a.Str("symbol") ?? "infantry";
        if (!Symbols.Contains(symbol)) return ToolResult.Fail("symbol is one of " + string.Join(", ", Symbols));
        var (bw, bh) = FacePixels(w, h);
        var spec = new BlockSpec
        {
            BW = bw, BH = bh, Label = a.Str("label") ?? name, Symbol = symbol, SymbolText = a.Str("symbol_text") ?? "",
            Fill = Colour(a.Str("fill"), new BlockSpec().Fill), Border = Colour(a.Str("border"), new BlockSpec().Border), LabelColor = Colour(a.Str("label_colour"), new BlockSpec().LabelColor),
        };
        if (a.Num("border_width") is { } px) spec.BorderPx = (float)Math.Clamp(px, 0, MaxBorderPx);
        var made = await s.NewPieces.NewBlock(spec, name, Math.Max(w, h));
        made.Size = new Size2(w, h);
        return Made(s, made, "made the block");
    }

    const double MinSpan = 0.05, MaxSpan = 2000;
    const float FaceLong = 512, FaceShortest = 64, MaxBorderPx = 120;

    static (float w, float h) FacePixels(double w, double h)
    {
        double ratio = Math.Max(w, h) / Math.Min(w, h);
        float longSide = (float)Math.Min(MaxSide, Math.Max(FaceLong, Math.Round(FaceShortest * ratio)));
        float shortSide = (float)Math.Max(8, Math.Round(longSide / ratio));
        return w >= h ? (longSide, shortSide) : (shortSide, longSide);
    }

    async Task<ToolResult> NewPiece(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        var name = a.Str("name")?.Trim();
        if (string.IsNullOrEmpty(name)) return ToolResult.Fail("name is needed.");
        var shape = a.Str("shape") ?? "";
        if (!Shapes.Contains(shape)) return ToolResult.Fail("shape is one of " + string.Join(", ", Shapes));
        double w = Math.Clamp(a.Num("width") ?? 1, MinSpan, MaxSpan);
        double h = Math.Clamp(a.Num("height") ?? shape switch { "token" => w / 2, "hex" => w * 444 / 512, _ => w }, MinSpan, MaxSpan);
        var outline = shape switch { "round" => "circle", "hex" => "hex", _ => "box" };
        int pw = w >= h ? 512 : (int)Math.Round(512 * w / h), ph = h >= w ? 512 : (int)Math.Round(512 * h / w);
        var doc = new ArtDoc { W = pw, H = ph };
        var colour = Colour(a.Str("colour"), "#d9cfb5ff");
        var field = new ArtLayer { Name = "Field", Kind = "paint" };
        field.Ops.Add(new PaintOp { K = "shape", P = ArtXf.Flat(ArtDraw.Outline(outline, pw, ph)), W = 0, Fc = colour, C = colour });
        doc.Layers.Add(field);
        doc.Layers.Add(new ArtLayer { Name = "Paint", Kind = "paint" });
        doc.Selected = doc.Layers[^1].Id;
        return Made(s, await s.NewPieces.NewFromDoc(doc, name, outline, Math.Max(w, h)), "made");
    }

    async Task<ToolResult> FromPictures(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        var paths = Strings(a, "paths") ?? new();
        var missing = paths.Where(p => !File.Exists(p)).ToList();
        if (paths.Count == 0 || missing.Count > 0) return ToolResult.Fail(paths.Count == 0 ? "paths is needed." : "No such file: " + string.Join(", ", missing));
        var before = s.Pack.Pieces.ToHashSet();
        await s.NewPieces.AddImageFilesAsync(paths.ToArray());
        return NewKinds(s, before, "made from a picture");
    }

    async Task<ToolResult> ImportModels(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        var paths = Strings(a, "paths") ?? new();
        var missing = paths.Where(p => !File.Exists(p)).ToList();
        if (paths.Count == 0 || missing.Count > 0) return ToolResult.Fail(paths.Count == 0 ? "paths is needed." : "No such file: " + string.Join(", ", missing));
        var before = s.Pack.Pieces.ToHashSet();
        await s.NewPieces.ImportModelsAsync(paths.ToArray());
        return NewKinds(s, before, "imported");
    }

    ToolResult NewKinds(PieceStudio s, HashSet<PieceDef> before, string what)
    {
        var made = s.Pack.Pieces.Where(d => !before.Contains(d)).ToList();
        if (made.Count == 0) return ToolResult.Fail("Nothing could be read from those files.");
        foreach (var d in made) History(s).Made(d, $"{what} {d.Name}");
        _a.Log($"{what}: {string.Join(", ", made.Select(d => d.Name))}");
        return new ToolResult($"{made.Count} new kind{(made.Count == 1 ? "" : "s")}:\n" + Json(new JsonArray(made.Select(d => (JsonNode?)Summary(s, d)).ToArray())));
    }

    async Task<ToolResult> NewEffect(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        var name = a.Str("name")?.Trim();
        if (string.IsNullOrEmpty(name)) return ToolResult.Fail("name is needed.");
        EffectDef fx; double w, h;
        if (a.Str("preset") is { } presetName)
        {
            var p = FirmeEffects.Presets.FirstOrDefault(x => x.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (p.Fx == null) return ToolResult.Fail($"No preset {presetName}.");
            fx = p.Fx.Clone(); w = a.Num("width") ?? p.W; h = a.Num("height") ?? p.H;
        }
        else
        {
            var kind = a.Str("kind") ?? "";
            if (!EffectKinds.Kinds.Any(k => k.Id == kind)) return ToolResult.Fail("preset or kind is needed; kinds: " + string.Join(", ", EffectKinds.Kinds.Select(k => k.Id)));
            fx = new EffectDef { Kind = kind }; w = a.Num("width") ?? 2; h = a.Num("height") ?? 2;
        }
        var before = s.Pack.Pieces.ToHashSet();
        s.NewPieces.NewEffect(name, Math.Clamp(w, MinSpan, MaxSpan), Math.Clamp(h, MinSpan, MaxSpan), fx);
        await s.Frames(2);
        return NewKinds(s, before, "made the effect");
    }

    static readonly string OutlineList = string.Join(", ", PieceFramer.Outlines.Select(o => o.ToLowerInvariant()));

    internal static string? OutlineNamed(string? said) => said == null ? null
        : PieceFramer.Outlines.FirstOrDefault(o => o.Equals(said.Trim(), StringComparison.OrdinalIgnoreCase) || PieceFramer.ShapeOf(o) == said.Trim().ToLowerInvariant());

    internal static Size2 Proportional(Size2 now, double? w, double? h) =>
        w is > 0 && h is > 0 ? new Size2(w.Value, h.Value)
        : w is > 0 ? new Size2(w.Value, now.W > 1e-9 ? w.Value * now.H / now.W : w.Value)
        : h is > 0 ? new Size2(now.H > 1e-9 ? h.Value * now.W / now.H : h.Value, h.Value)
        : now;

    Task<ToolResult> SetPiece(JsonElement a) => Change(a, "changed", async (s, d) =>
    {
        var name = a.Str("name")?.Trim();
        var w = a.Num("width"); var h = a.Num("height"); var th = a.Num("thickness"); var tint = a.Str("tint");
        if (tint != null && !IsColour(tint)) return ToolResult.Fail("tint is #rrggbb.");
        var outline = OutlineNamed(a.Str("outline"));
        if (a.Str("outline") != null && outline == null) return ToolResult.Fail("outline is one of " + OutlineList + ".");
        bool flat = d.Kind != "model" && !EffectFx.IsEffect(d);
        if (outline != null && !flat) return ToolResult.Fail("A model or an effect has no outline.");
        var tags = Strings(a, "tags");
        JsonElement nb = default;
        bool box = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name_box", out nb);
        FaceBox? nameBox = null;
        if (box && nb.ValueKind == JsonValueKind.Object)
            nameBox = new FaceBox { X = Clamp01(nb.Num("x") ?? 0.07), Y = Clamp01(nb.Num("y") ?? 0.02), W = Clamp01(nb.Num("w") ?? 0.86), H = Clamp01(nb.Num("h") ?? 0.2) };
        Edit(s, d, x =>
        {
            if (!string.IsNullOrEmpty(name)) x.Name = name;
            if (d.Kind != "model" && (w is > 0 || h is > 0)) x.Size = Proportional(x.Size, w, h);
            if (th is > 0) x.Thickness = th.Value;
            if (tint != null) x.Tint = Colour(tint, "#ffffffff")[..7].ToUpperInvariant();
            if (tags != null) x.Tags = tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList();
            if (box) x.NameBox = nameBox;
        });
        if (d.Kind == "model" && (w is > 0 || h is > 0))
        {
            if (await OpenModel(s, d) is not { } m) return ToolResult.Fail($"{d.Name} could not be opened in 3D to size it.");
            m.SetWidth(Proportional(d.Size, w, h).W);
            await m.Bake();
            if (m.Problem != null) return ToolResult.Fail($"{d.Name}'s new width could not be saved: {m.Problem}");
        }
        if (outline != null) await s.Framer.SetOutline(d, 0, outline);
        return new ToolResult(Json(Summary(s, d)), false, Pictures(FacePng(s, PieceFaces.Of(d).FirstOrDefault()?.Sha)));
    });

    Task<ToolResult> Frame(JsonElement a) => Change(a, "framed", async (s, d) =>
    {
        if (d.Kind == "model" || EffectFx.IsEffect(d)) return ToolResult.Fail("Only flat kinds take a frame.");
        if (a.Flag("off") == true)
        {
            await s.Framer.Unframe(d);
            return new ToolResult($"{d.Name} has no frame.", false, Pictures(FacePng(s, PieceFaces.Of(d).FirstOrDefault()?.Sha)));
        }
        var border = a.Str("border"); var behind = a.Str("behind");
        if (border != null && !IsColour(border) || behind != null && !IsColour(behind)) return ToolResult.Fail("border and behind are #rrggbb.");
        float width = (float)Math.Clamp(a.Num("border_width") ?? 10, 0, 48);
        await s.Framer.Frame(d, Hex.To(Colour(border, "#1f1f1fff"), Colors.Black), width, Hex.To(Colour(behind, "#d9cfb5ff"), Colors.White));
        return new ToolResult($"Framed {d.Name} along its outline ({s.Framer.NameOf(d, 0).ToLowerInvariant()}).", false, Pictures(FacePng(s, PieceFaces.Of(d).FirstOrDefault()?.Sha)));
    });

    static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    static PrintPlace? Place(JsonElement a, PrintPlace? now, int index, out string? problem)
    {
        problem = null;
        var side = a.Str("side");
        if (side != null && side is not (PrintPlace.Front or PrintPlace.Back)) { problem = "side is front or back"; return now; }
        if (side == null && a.Num("x") == null && a.Num("y") == null && a.Num("w") == null && a.Num("h") == null) return now;
        var p = now ?? PiecePrinting.Fresh(side ?? PrintPlace.Back, index);
        return new PrintPlace
        {
            Side = side ?? p.Side,
            X = Clamp01(a.Num("x") ?? p.X), Y = Clamp01(a.Num("y") ?? p.Y), W = Clamp01(a.Num("w") ?? p.W), H = Clamp01(a.Num("h") ?? p.H),
        };
    }

    Task<ToolResult> SetCounter(JsonElement a) => Change(a, "set a counter", (s, d) =>
    {
        var name = a.Str("name")?.Trim() ?? "";
        if (name.Length == 0) return Task.FromResult(ToolResult.Fail("name is needed."));
        var c = d.Counters.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        int index = c == null ? d.Counters.Count : d.Counters.IndexOf(c);
        var newName = a.Str("new_name")?.Trim();
        if (!string.IsNullOrEmpty(newName) && d.Counters.Any(x => x != c && x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))) return Task.FromResult(ToolResult.Fail($"{d.Name} already has a counter {newName}."));
        int min = (int)(a.Num("min") ?? c?.Min ?? 0), max = (int)(a.Num("max") ?? c?.Max ?? Math.Max(9, min));
        if (max < min) return Task.FromResult(ToolResult.Fail("max is below min."));
        int start = (int)Math.Clamp(a.Num("default") ?? c?.Default ?? min, min, max);

        var marker = a.Str("places_marker");
        if (marker is { Length: > 0 } && (marker == d.Id || !s.Pack.Pieces.Any(p => p.Id == marker))) return Task.FromResult(ToolResult.Fail($"No other kind {marker} in this pack to place."));
        var place = Place(a, c?.Place, index, out var problem);
        if (problem != null) return Task.FromResult(ToolResult.Fail(problem));
        Edit(s, d, x =>
        {
            var it = c ?? new CounterDef { Name = name };
            if (c == null) x.Counters.Add(it);
            if (!string.IsNullOrEmpty(newName)) it.Name = newName;
            it.Min = min; it.Max = max; it.Default = start;

            it.Place = place;
            if (a.Flag("show_name") is { } sn) it.ShowName = sn;
            if (a.Flag("show_buttons") is { } sb) it.ShowButtons = sb;
            if (marker is { Length: > 0 } && !x.Attachments.Any(m => m.Piece == marker)) x.Attachments.Add(new AttachmentDef { Piece = marker, Offset = new Vec2(x.Size.W * PackManifest.MarkerBeside, 0) });
        });
        return Task.FromResult(new ToolResult(JsonSerializer.Serialize(d.Counters, Opts)));
    });

    Task<ToolResult> SetTextLine(JsonElement a) => Change(a, "set a text line", (s, d) =>
    {
        var name = a.Str("name")?.Trim() ?? "";
        if (name.Length == 0) return Task.FromResult(ToolResult.Fail("name is needed."));
        var t = d.TextLines.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        int index = d.Counters.Count + (t == null ? d.TextLines.Count : d.TextLines.IndexOf(t));
        var newName = a.Str("new_name")?.Trim();
        if (!string.IsNullOrEmpty(newName) && d.TextLines.Any(x => x != t && x.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))) return Task.FromResult(ToolResult.Fail($"{d.Name} already has a text line {newName}."));
        var place = Place(a, t?.Place, index, out var problem);
        if (problem != null) return Task.FromResult(ToolResult.Fail(problem));
        Edit(s, d, x =>
        {
            var it = t ?? new TextLineDef { Name = name };
            if (t == null) x.TextLines.Add(it);
            if (!string.IsNullOrEmpty(newName)) it.Name = newName;
            if (a.Str("default") is { } def) it.Default = def;
            it.Place = place;
        });
        return Task.FromResult(new ToolResult(JsonSerializer.Serialize(d.TextLines, Opts)));
    });

    Task<ToolResult> Remove(JsonElement a) => Change(a, "took away", async (s, d) =>
    {
        if (a.Str("counter") is { } cn)
        {
            if (d.Counters.FirstOrDefault(x => x.Name.Equals(cn, StringComparison.OrdinalIgnoreCase)) is not { } c) return ToolResult.Fail($"{d.Name} has no counter {cn}.");
            Edit(s, d, x => x.Counters.Remove(c));
            return new ToolResult($"Took the counter {c.Name} away.");
        }
        if (a.Str("text_line") is { } tn)
        {
            if (d.TextLines.FirstOrDefault(x => x.Name.Equals(tn, StringComparison.OrdinalIgnoreCase)) is not { } t) return ToolResult.Fail($"{d.Name} has no text line {tn}.");
            Edit(s, d, x => x.TextLines.Remove(t));
            return new ToolResult($"Took the text line {t.Name} away.");
        }
        if (a.Num("look") is { } look)
        {
            int i = (int)look - 1;
            if (i < 0 || i >= d.States.Count) return ToolResult.Fail($"{d.Name} has {d.States.Count} looks.");
            if (!await Select(s, d)) return ToolResult.Fail($"{d.Name} could not be selected.");
            await s.RemoveStateAsync(i);
            return new ToolResult($"{d.Name} has {d.States.Count} looks now.");
        }
        return ToolResult.Fail("Name a counter, a text_line or a look.");
    });

    Task<ToolResult> AddLook(JsonElement a) => Change(a, "added a look", async (s, d) =>
    {
        if (!await Select(s, d)) return ToolResult.Fail($"{d.Name} could not be selected (the studio may be asking about unsaved work).");
        s.AddState(a.Str("name") is { Length: > 0 } n ? n : null);
        return new ToolResult($"{d.Name} has {d.States.Count} looks: {string.Join(", ", d.States.Select(x => x.Name))}. Face keys: {string.Join(", ", PieceFaces.Of(d).Select(f => f.Key))}.");
    });

    Task<ToolResult> RenameLook(JsonElement a) => Change(a, "renamed a look", (s, d) =>
    {
        int i = (int)(a.Num("look") ?? 0) - 1;
        var name = a.Str("name")?.Trim();
        if (i < 0 || i >= d.States.Count) return Task.FromResult(ToolResult.Fail($"{d.Name} has {d.States.Count} looks."));
        if (string.IsNullOrEmpty(name)) return Task.FromResult(ToolResult.Fail("name is needed."));
        Edit(s, d, x => x.States[i].Name = name);
        return Task.FromResult(new ToolResult($"Looks: {string.Join(", ", d.States.Select(x => x.Name))}."));
    });

    Task<ToolResult> SetMarkers(JsonElement a) => Change(a, "set the markers", (s, d) =>
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("markers", out var ms) || ms.ValueKind != JsonValueKind.Array) return Task.FromResult(ToolResult.Fail("markers is a list of {kind, x, z}."));
        var list = new List<AttachmentDef>();
        foreach (var m in ms.EnumerateArray())
        {
            var kind = m.Str("kind") ?? "";
            if (kind == d.Id || !s.Pack.Pieces.Any(p => p.Id == kind)) return Task.FromResult(ToolResult.Fail($"No other kind {kind} in this pack to use as a marker."));
            list.Add(new AttachmentDef { Piece = kind, Offset = new Vec2(m.Num("x") ?? d.Size.W * 0.8, m.Num("z") ?? 0) });
        }
        Edit(s, d, x => x.Attachments = list);
        return Task.FromResult(new ToolResult(Json(Summary(s, d))));
    });

    Task<ToolResult> SetEffect(JsonElement a) => Change(a, "set the effect", (s, d) =>
    {
        var kind = a.Str("kind");
        if (kind != null && !EffectKinds.Kinds.Any(k => k.Id == kind)) return Task.FromResult(ToolResult.Fail("kind is one of " + string.Join(", ", EffectKinds.Kinds.Select(k => k.Id))));
        if (d.Effect == null && kind == null) return Task.FromResult(ToolResult.Fail($"{d.Name} is not an effect: give a kind to make it one, or use studio_new_effect."));
        var colour = a.Str("colour");
        if (colour != null && colour.Length > 0 && !IsColour(colour)) return Task.FromResult(ToolResult.Fail("colour is #rrggbb."));
        var volley = a.Str("volley");
        Edit(s, d, x =>
        {
            var fx = x.Effect?.Clone() ?? new EffectDef();
            if (kind != null) fx.Kind = kind;
            if (a.Num("amount") is { } am) fx.Amount = am;
            if (a.Num("height") is { } he) fx.Height = he;
            if (a.Num("puff") is { } pu) fx.Puff = pu;
            if (a.Num("speed") is { } sp) fx.Speed = sp;
            if (a.Num("wind") is { } wi) fx.Wind = wi;
            if (a.Num("every") is { } ev) fx.Every = ev;
            if (colour != null) fx.Color = colour.Length == 0 ? null : EffectKinds.Recolour(Colour(colour, "#ffffffff").ToUpperInvariant(), fx.Color);
            if (a.Flag("light") is { } li) fx.Light = li;
            if (a.Flag("smoke") is { } sm) fx.Smoke = sm;
            if (volley != null) fx.Volley = volley.Length == 0 ? null : volley;
            x.Effect = fx;
            if (!x.Tags.Contains("effect")) x.Tags.Add("effect");
        });
        return Task.FromResult(new ToolResult(JsonSerializer.Serialize(d.Effect, Opts)));
    });

    Task<ToolResult> WriteScript(JsonElement a) => Change(a, "set the script", async (s, d) =>
    {
        var raw = a.Str("text");
        if (raw == null) return ToolResult.Fail("text is needed (an empty text removes the script).");
        var text = ScriptFiles.Norm(raw);
        string? keep = text.Trim().Length == 0 ? null : text;
        if (keep != null && KTable.Core.ScriptSafety.Inspect(keep) is { Hostile: true } v) return ToolResult.Fail("Refused, not run: " + string.Join("; ", v.Reasons));
        long seq = s.S.ConsoleLines.Count > 0 ? s.S.ConsoleLines[^1].Seq : 0;
        s.Script.SetFromOutside(d.Id, keep);
        await _a.Delay(0.5);
        var mine = s.S.View.Layer(Layers.Umpire).Where(p => p.Def == d.Id).Select(p => p.Id).ToHashSet();
        var fresh = s.S.ConsoleLines.Where(l => l.Seq > seq && (l.Source.Length == 0 || mine.Contains(l.Source))).TakeLast(60).ToList();
        bool failed = fresh.Any(l => l.Kind == "error");
        var sb = new StringBuilder(keep == null ? $"Removed the script of {d.Name}." : failed ? $"Set the script of {d.Name}; it failed as it ran (below). It stays set: fix it and write it again." : $"Set the script of {d.Name} and ran it.");
        if (fresh.Count > 0) sb.Append("\nConsole:\n").Append(string.Join("\n", fresh.Select(TableTools.Format)));
        return new ToolResult(sb.ToString(), failed);
    });

    async Task<ToolResult> Duplicate(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        if (Def(s, a) is not { } d) return NoKind(a);
        var before = s.Pack.Pieces.ToHashSet();
        await s.DuplicateSelAsync(d);
        var copy = s.Pack.Pieces.FirstOrDefault(p => !before.Contains(p));
        if (copy == null) return ToolResult.Fail("It was not copied (the studio could not save what is open).");
        if (a.Str("name") is { Length: > 0 } name) Edit(s, copy, x => x.Name = name.Trim());
        return Made(s, copy, "copied");
    }

    async Task<ToolResult> Delete(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        if (Def(s, a) is not { } d) return NoKind(a);
        if (await s.SettleWork() is { } problem) return ToolResult.Fail("The studio could not save what is open first: " + problem);
        History(s).Deleted(s.Pack, d, $"deleted {d.Name}");
        s.DeleteNow(d);
        _a.Log($"deleted {d.Name}");
        return new ToolResult($"Deleted {d.Name}. studio_undo brings it back.");
    }

    async Task<ToolResult> Undo(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        if (await s.SettleWork() is { } problem) return ToolResult.Fail("The studio could not save what is open first: " + problem);
        var what = History(s).Undo(s);
        if (what == null) return ToolResult.Fail("Nothing the agent did here is left to undo.");
        _a.Log("undid: " + what);
        return new ToolResult($"Undone: {what}. {_undo.Count} more can be undone.");
    }

    async Task<ToolResult> Steps(JsonElement a)
    {
        if (_a.Studio is not { } s || !_a.InStudio) return NotOpen;
        var all = (a.Str("steps") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (all.Length == 0) return ToolResult.Fail("steps is needed.");
        if (_for != s) { _for = s; _steps = null; }
        _steps ??= new StudioSteps(s);
        var report = new List<string>();
        bool failed = false;
        foreach (var raw in all)
        {
            if (!_a.InStudio) { report.Add("the studio closed; stopped"); failed = true; break; }
            var (verb, arg) = StudioSteps.Split(raw);
            try
            {
                if (await _steps.Run(verb, arg)) report.Add("ok " + raw);
                else { report.Add($"unknown step '{verb}'; stopped"); failed = true; break; }
            }
            catch (Exception e) when (e is FormatException or IndexOutOfRangeException or ArgumentException)
            {
                report.Add($"'{raw}' could not be read ({e.Message}); stopped");
                failed = true;
                break;
            }
            await s.Frames(2);
        }
        _a.Log($"ran {report.Count} studio step{(report.Count == 1 ? "" : "s")}");
        report.Add($"now: view {s.View}, piece {s.Sel?.Name ?? "none"} ({s.Sel?.Id ?? "-"}), face {s.FaceKey}, tool {(s.View == "model" ? s.Model?.Tool : s.Canvas.Tool)}");
        if (s.View == "paint" && s.Faces.CanvasOpen) report.Add($"picture {s.Canvas.Doc.W}×{s.Canvas.Doc.H} px");
        return new ToolResult(string.Join("\n", report), failed);
    }

    static async Task<bool> Select(PieceStudio s, PieceDef d)
    {
        if (s.Sel == d) return true;
        s.Select(d, frame: true);
        for (int i = 0; i < 120 && s.Sel != d; i++) await s.Frames(1);
        return s.Sel == d;
    }

    internal static byte[]? FacePng(PieceStudio s, string? sha, int max = 768)
    {
        if (sha == null || s.Store.AssetPath(s.Pack, sha) is not { } path || !File.Exists(path)) return null;
        var img = Image.LoadFromFile(path);
        if (img == null || img.IsEmpty()) return null;
        if (img.IsCompressed()) img.Decompress();
        int w = img.GetWidth(), h = img.GetHeight();
        if (Math.Max(w, h) > max) img.Resize(Math.Max(1, w * max / Math.Max(w, h)), Math.Max(1, h * max / Math.Max(w, h)), Image.Interpolation.Lanczos);
        return img.SavePngToBuffer();
    }
}
