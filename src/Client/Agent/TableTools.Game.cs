using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Client.Hand;

namespace KTable.Client.Agent;

public sealed partial class TableTools
{
    public const int MaxPlaced = 200;

    static readonly string FormationWords = string.Join(", ", Formations.All.Select(k => k.Id));

    static readonly Arg StretchLength = new("length", "number", "for stretch: map units from the first piece to the last (never closer than the spacing)");

    static readonly Arg StretchPath = new("path", "array",
        "for stretch, in place of x, z, facing and length: the line to string the pieces along, [[x, z], …] map units, straight or curved; the first stands at its start, the last at its end, each facing across it as a line drawn left to right faces the map's top");

    IEnumerable<AgentTool> GameTools()
    {
        yield return new AgentTool("place_pieces",
            "Puts many pieces down at once in a formation (" + FormationWords + ") round a centre, facing a direction. Use kinds for a mix (one kind each, in order) or kind with count. Spacing defaults to the pieces' own size. Undoable piece by piece.",
            Args.Schema(new Arg("kind", "string", "one kind's id (list_kinds)"), new Arg("count", "number", "how many of it"), new Arg("kinds", "array", "kind ids, one a piece, in order"),
                new Arg("x", "number", "centre, map units (needed unless a path is given)"), new Arg("z", "number", "centre, map units"), new Arg("formation", "string", FormationWords + " (stretch when a path is given)"),
                new Arg("facing", "number", "the way the pieces face, as their turn in degrees (180 faces the map's top)"), new Arg("spacing", "number", "map units between pieces across"), new Arg("depth", "number", "map units between ranks"),
                new Arg("ranks", "number", "ranks for line, chequer, crescent and open order; how many abreast for column"), StretchLength, StretchPath,
                new Arg("labels", "array", "a name for each piece, in order")),
            PlacePieces);
        yield return new AgentTool("form_up",
            "Forms pieces already on the table up into a formation (" + FormationWords + "), each going to the nearest place in it. Round their own centre and facing unless x, z or facing are given. Markers attached to a piece go with it. One undoable change.",
            Args.Schema(new Arg("ids", "array", "the pieces' ids", true), new Arg("formation", "string", FormationWords, true),
                new Arg("x", "number", "where the formation's centre goes, map units"), new Arg("z", "number", "map units"), new Arg("facing", "number", "the way the pieces face, as their turn in degrees (180 faces the map's top)"),
                new Arg("gap", "number", "space between pieces as a share of their size: 0 touching, 0.1 close (the default), 1 a piece's width apart"),
                new Arg("ranks", "number", "ranks for line, chequer, crescent and open order; how many abreast for column"), StretchLength, StretchPath),
            FormUp);
        yield return new AgentTool("press_button",
            "Presses a button a piece's script drew on it, as a player clicking it does (so it tests what people see). By its label or its number from list_buttons.",
            Args.Schema(new Arg("id", "string", "the piece's id", true), new Arg("label", "string", "the button's label"), new Arg("number", "number", "its number from list_buttons (from 1)"), new Arg("alt", "boolean", "a right-click")),
            PressButton);
        yield return new AgentTool("list_buttons",
            "The buttons, input boxes and menu rows a piece's script gave it, and the table's own script menu rows and hotkeys.",
            Args.Schema(new Arg("id", "string", "a piece's id; leave out for the table's")), ListButtons);
        yield return new AgentTool("pick_menu_row",
            "Picks a row a script added to a piece's right-click menu, or (no id) to the table's menu, by its label.",
            Args.Schema(new Arg("id", "string", "the piece's id; leave out for the table's menu"), new Arg("label", "string", "the row's label", true), new Arg("x", "number", "for the table's menu: where it was opened"), new Arg("z", "number", "map units")),
            PickRow);
        yield return new AgentTool("press_hotkey",
            "Presses a hotkey a script registered, by its label, with these pieces selected.",
            Args.Schema(new Arg("label", "string", "the hotkey's label", true), new Arg("selected", "array", "piece ids to have selected"), new Arg("id", "string", "the piece under the pointer")),
            PressHotkey);
        yield return new AgentTool("set_piece_options",
            "Locks or unlocks a piece, sets its side tint, its size (scale), turns it face down or up, or attaches it to another piece (it then moves with it).",
            Args.Schema(new Arg("id", "string", "the piece's id", true), new Arg("locked", "boolean", "locked in place"), new Arg("tint", "string", "#rrggbb, or empty for its own"),
                new Arg("scale", "number", "1 is its own size"), new Arg("face_down", "boolean", "turned over"), new Arg("attach_to", "string", "a piece it rides on"), new Arg("detach", "boolean", "off what it rides on")),
            SetOptions);
        yield return new AgentTool("end_turn",
            "Ends the turn: the clock goes on by the turn's length, the turn is kept in the history and saved.",
            Args.Schema(), EndTurn);
        yield return new AgentTool("set_clock",
            "Sets the game clock: the turn number, the date and time (yyyy-mm-ddThh:mm) and how long each turn lasts, in minutes (60 an hour, 1440 a day).",
            Args.Schema(new Arg("turn", "number", "turn number"), new Arg("time", "string", "yyyy-mm-ddThh:mm"), new Arg("minutes_per_turn", "number", "minutes a turn lasts: 60 an hour, 1440 a day")), SetClockTool);
        yield return new AgentTool("roll_dice",
            "Rolls dice for everyone to see in the log, and returns the results.",
            Args.Schema(new Arg("count", "number", "how many (default 1)"), new Arg("sides", "number", "sides (default 6)"), new Arg("label", "string", "what the roll is for")), RollDiceTool);
        yield return new AgentTool("say",
            "Writes a line in the log everyone at the table reads.",
            Args.Schema(new Arg("text", "string", "the line", true), new Arg("colour", "string", "#rrggbb")), SayTool);
        yield return new AgentTool("draw",
            "Draws a line on the map through points (map units), as the pen does, or with text writes a name on the map in the map style's hand: one point places it by what lies under it (beside a town, along a river, spread across a patch of ground, plain on bare paper), more points are its baseline; arrow adds a head at a line's end; board puts it on a drawing layer of that name, made if missing. Undoable.",
            Args.Schema(new Arg("points", "array", "[[x, z], [x, z], …]", true), new Arg("colour", "string", "#rrggbb"), new Arg("width", "number", "map units (default 0.3)"), new Arg("text", "string", "a name to write instead of a line"), new Arg("arrow", "boolean", "a head at the end"),
                new Arg("board", "string", "the drawing layer's name (list_drawings shows them); left out, the default layer")),
            DrawTool);
        yield return new AgentTool("set_zone",
            "Sets an umpire's zone: a named polygon an umpire owns (host and lead umpires), or removes one by name.",
            Args.Schema(new Arg("name", "string", "the zone's name", true), new Arg("owner", "string", "the umpire's name"), new Arg("polygon", "array", "[[x, z], …] map units"), new Arg("colour", "string", "#rrggbb"),
                new Arg("remove", "boolean", "remove the zone of this name")),
            SetZoneTool);
        yield return new AgentTool("set_grid",
            "Lays a grid over the table, as the scenario's Grid page does (undoable): square or hex cells, a cell's size across in map units (a hex across its flats), and lighter lines inside square cells. Nothing snaps to it.",
            Args.Schema(new Arg("kind", "string", "none, square or hex", true), new Arg("cell", "number", $"map units across one cell ({GridCellMin}–{GridCellMax}; as it is if left out)"),
                new Arg("divisions", "number", $"lines inside each square cell, 1 for plain cells (1–{GridDivisionsMost})")),
            SetGridTool);
        yield return new AgentTool("set_light",
            "Sets the table's light (sun and ambient 0–1, warmth −1 to 1, shadows, or following the game clock's hour), the table under the map (0 squared paper, 0.5 grey, 1 black, 1.05 see-through: no table drawn), the backdrop seen around the table, the map style painted ground is drawn in, contour lines over the relief, a snow line (metres; above it every ground takes snow), streams from the relief, and the wind (from, in degrees; strength 0 calm to 5 a gale), for everyone at the table.",
            Args.Schema(new Arg("sun", "number", "0–1"), new Arg("ambient", "number", "0–1"), new Arg("warmth", "number", "−1 to 1"), new Arg("shadows", "boolean", "shadows"), new Arg("from_clock", "boolean", "follows the clock's hour"),
                new Arg("table_shade", "number", "the table under the map: 0 squared paper, 0.5 grey, 1 black, 1.05 see-through (no table drawn)"),
                new Arg("backdrop", "string", "around the table: none, a solid #rrggbb colour, or one of " + string.Join(", ", Backdrops.All.Select(b => b.Id)) + "; a sky brings its own light unless sun, ambient or warmth are given"),
                new Arg("map_style", "string", "painted ground drawn as an ancient, survey (18th century) or modern map"),
                new Arg("contours", "boolean", "contour lines over every relief, spaced to fit its highest ground and redrawn as it changes"),
                new Arg("snow_line", "number", "metres above which every ground takes snow, 0 for none; needs the map's scale"),
                new Arg("streams", "boolean", "brooks and rivers where the relief gathers water; needs the map's scale"),
                new Arg("wind_from", "number", "degrees"), new Arg("wind_strength", "number", "0–5")),
            SetLight);
        yield return new AgentTool("read_briefing",
            "The scenario's briefing: the written situation the umpires play from.",
            Args.Schema(), ReadBriefing);
        yield return new AgentTool("set_briefing",
            "Writes the scenario's briefing (host and lead umpires): the situation, what has happened, where things stand, each side's orders. Markdown: # headings, - lists. It is kept with the scenario and its saves, and observers do not see it. Empty clears it.",
            Args.Schema(new Arg("text", "string", "the whole briefing", true)), SetBriefingTool);
        yield return new AgentTool("set_map_scale",
            "Sets how long a map unit is, in metres (the ruler and ranges use it), and the units distances are read in (m, yd or pace).",
            Args.Schema(new Arg("metres_per_unit", "number", "metres one map unit stands for"), new Arg("units", "string", "m, yd or pace")), SetScale);
    }

    static Vec2? Point(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2 && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Number
            ? new Vec2(e[0].GetDouble(), e[1].GetDouble()) : null;

    static List<Vec2>? Points(JsonElement a, string name)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        var l = v.EnumerateArray().Select(Point).ToList();
        return l.All(p => p != null) ? l.Select(p => p!.Value).ToList() : null;
    }

    static bool Given(JsonElement a, string name) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out _);

    static ToolResult? PathFor(JsonElement a, FormationKind kind, out List<Vec2>? path)
    {
        path = null;
        if (!Given(a, "path")) return null;
        if (!kind.AlongLine) return ToolResult.Fail("A path is for stretch only.");
        path = Points(a, "path");
        return path is { Count: >= 2 } && Formations.Length(path) > Formations.Least ? null : ToolResult.Fail("path is two or more [x, z] points, not all on one spot.");
    }

    static List<string> List(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new();

    async Task<ToolResult> FormUp(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var ids = List(a, "ids").Distinct().ToList();
        if (ids.Count < 2 || ids.Count > MaxPlaced) return ToolResult.Fail($"Give 2 to {MaxPlaced} ids.");
        if (Formations.Named(a.Str("formation") ?? "") is not var (kind, namedDepth)) return ToolResult.Fail("formation is one of " + FormationWords);
        if (PathFor(a, kind, out var path) is { } badPath) return badPath;
        var layer = s.View.Layer(Layers.Umpire);
        var found = new List<PieceInstance>();
        foreach (var id in ids)
        {
            if (layer.FirstOrDefault(p => p.Id == id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
            found.Add(p);
        }
        var units = found.Where(p => !found.Any(o => o != p && o.Attached.Contains(p.Id))).ToList();
        double gap = Math.Clamp(a.Num("gap") ?? Formations.DefaultGap, 0, Formations.MostGap);
        var sizes = units.Select(p => s.Def(p.Def) is { } d ? (W: d.Size.W * p.Scale, H: d.Size.H * p.Scale) : (W: 1.0, H: 1.0)).ToList();
        var pitch = new Pitch(sizes.Max(z => z.W) * (1 + gap), sizes.Max(z => z.H) * (1 + gap));
        var at = new Vec2(a.Num("x") ?? units.Average(p => p.Pos.X), a.Num("z") ?? units.Average(p => p.Pos.Z));
        double facing = path != null ? Formations.FacingOf(path) : a.Num("facing") ?? Formations.Facing(units.Select(p => p.RotDeg)) ?? units[0].RotDeg;
        int depth = a.Num("ranks") is { } r ? (int)Math.Clamp(r, 1, Formations.MostRanks) : namedDepth;
        var places = path != null ? Formations.Along(path, units.Count, pitch.Across)
            : Formations.Place(Formations.Slots(kind, units.Count, pitch, depth, span: a.Num("length") ?? 0), at, facing);
        var match = Formations.Match(units.Select(p => p.Pos).ToList(), places.Select(q => q.Pos).ToList());
        var to = units.Select((p, i) => (p.Id, places[match[i]].Pos, places[match[i]].Rot)).ToList();
        s.Do(new Batch { Items = to.Select(t => (Command)new KTable.Core.Protocol.MovePiece { Layer = Layers.Umpire, Id = t.Id, Pos = t.Pos, RotDeg = t.Rot }).ToList() });
        await Until(() => to.All(t => s.View.Find(Layers.Umpire, t.Id) is { } q && (q.Pos - t.Pos).Length < 1e-6), 4);
        _log($"formed {units.Count} pieces up in {kind.Name.ToLowerInvariant()}");
        var rows = new JsonArray(to.Select(t => (JsonNode?)new JsonObject { ["id"] = t.Id, ["x"] = Math.Round(t.Pos.X, 3), ["z"] = Math.Round(t.Pos.Z, 3), ["turn"] = Math.Round(t.Rot, 1) }).ToArray());
        return new ToolResult(FormattableString.Invariant($"Formed {units.Count} up in {kind.Name.ToLowerInvariant()}, facing {facing:0.#}°.\n") + rows.ToJsonString(Json));
    }

    async Task<ToolResult> PlacePieces(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var kinds = List(a, "kinds");
        if (kinds.Count == 0 && a.Str("kind") is { Length: > 0 } one) kinds = Enumerable.Repeat(one, (int)Math.Clamp(a.Num("count") ?? 1, 1, MaxPlaced)).ToList();
        if (kinds.Count == 0 || kinds.Count > MaxPlaced) return ToolResult.Fail($"Give kind and count, or kinds (1 to {MaxPlaced} pieces).");
        foreach (var k in kinds.Distinct())
            if (s.Def(k) is null || s.Store.PackOfDef(k) is not { } pack || !s.View.Packs.Contains(pack.Id)) return ToolResult.Fail($"No kind {k} in this game's packs (list_kinds).");
        var formation = a.Str("formation") ?? (Given(a, "path") ? "stretch" : "line");
        if (Formations.Named(formation) is not var (kind, depth)) return ToolResult.Fail("formation is one of " + FormationWords);
        if (PathFor(a, kind, out var path) is { } badPath) return badPath;
        double? x = a.Num("x"), z = a.Num("z");
        if (path == null && (x == null || z == null)) return ToolResult.Fail("x and z are needed, or a path for stretch.");
        var first = s.Def(kinds[0])!;
        double across = a.Num("spacing") ?? first.Size.W * 1.1, deep = a.Num("depth") ?? first.Size.H * 1.6;
        double facing = a.Num("facing") ?? 0;
        if (a.Num("ranks") is { } ranks) depth = (int)Math.Clamp(ranks, 1, Formations.MostRanks);
        var at = path != null ? Formations.Along(path, kinds.Count, across)
            : Formations.Place(Formations.Slots(kind, kinds.Count, new Pitch(across, deep), depth, span: a.Num("length") ?? 0), new Vec2(x ?? 0, z ?? 0), facing);
        var labels = List(a, "labels");
        var before = s.View.Layer(Layers.Umpire).Select(p => p.Id).ToHashSet();
        for (int i = 0; i < kinds.Count; i++)
            s.Do(new SpawnPiece { Layer = Layers.Umpire, Def = kinds[i], Pos = at[i].Pos, RotDeg = at[i].Rot, Label = i < labels.Count && labels[i].Length > 0 ? labels[i] : null });
        await Until(() => s.View.Layer(Layers.Umpire).Count(p => !before.Contains(p.Id)) >= kinds.Count, 6);
        var made = s.View.Layer(Layers.Umpire).Where(p => !before.Contains(p.Id)).ToList();
        _log($"placed {made.Count} pieces in {formation}");
        var rows = new JsonArray(made.Select(p => (JsonNode?)new JsonObject { ["id"] = p.Id, ["name"] = NameOf(s, p), ["x"] = Math.Round(p.Pos.X, 3), ["z"] = Math.Round(p.Pos.Z, 3) }).ToArray());
        return new ToolResult($"Placed {made.Count} of {kinds.Count} in {formation}.\n" + rows.ToJsonString(Json), made.Count < kinds.Count);
    }

    Task<ToolResult> ListButtons(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        var id = a.Str("id") ?? "";
        var o = new JsonObject();
        if (id.Length > 0)
        {
            if (Find(s, id) is not { } p) return Task.FromResult(ToolResult.Fail($"No piece {id} on this table."));
            o["widgets"] =
                new JsonArray(p.Widgets.Select((w, i) => (JsonNode?)new JsonObject { ["number"] = i + 1, ["kind"] = w.Kind, ["label"] = w.Label, ["face"] = w.Face, ["value"] = w.Kind == "input" ? w.Value : null, ["presses"] = w.OnClick != null
                    || w.OnChange != null }).ToArray());
            o["menuRows"] = new JsonArray(p.MenuItems.Select(m => (JsonNode?)JsonValue.Create(m.Label)).ToArray());
        }
        else
        {
            o["menuRows"] = new JsonArray(s.View.MenuItems.Select(m => (JsonNode?)JsonValue.Create(m.Label)).ToArray());
            o["hotkeys"] = new JsonArray(s.View.Hotkeys.Select(h => (JsonNode?)JsonValue.Create(h.Label)).ToArray());
        }
        return Task.FromResult(new ToolResult(o.ToJsonString(Json)));
    }

    async Task<ToolResult> Pressed(ClientSession s, long seq, string what)
    {
        await Landed(s);
        _log(what);
        var fresh = s.ConsoleLines.Where(l => l.Seq > seq).ToList();
        return new ToolResult(char.ToUpperInvariant(what[0]) + what[1..] + "." + Since(s, seq, 60), fresh.Any(l => l.Kind == "error"));
    }

    async Task<ToolResult> PressButton(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (Find(s, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        int index = a.Num("number") is { } n ? (int)n - 1 : a.Str("label") is { Length: > 0 } label
? p.Widgets.FindIndex(w => w.OnClick != null && w.Label.Equals(label, StringComparison.OrdinalIgnoreCase)) is var exact and >= 0 ? exact
                : p.Widgets.FindIndex(w => w.OnClick != null && w.Label.Contains(label, StringComparison.OrdinalIgnoreCase))
            : -1;
        if (index < 0 || index >= p.Widgets.Count) return ToolResult.Fail($"{NameOf(s, p)} has no such button: list_buttons shows them.");
        long seq = LastSeq(s);
        s.Do(new ClickWidget { Layer = Layers.Umpire, Id = id, Index = index + 1, Alt = a.Flag("alt") ?? false });
        return await Pressed(s, seq, $"pressed {p.Widgets[index].Label} on {NameOf(s, p)}");
    }

    async Task<ToolResult> PickRow(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var label = a.Str("label") ?? "";
        long seq = LastSeq(s);
        if (a.Str("id") is { Length: > 0 } id)
        {
            if (Find(s, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
            int i = p.MenuItems.FindIndex(m => m.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return ToolResult.Fail($"{NameOf(s, p)}'s menu has no row {label}: list_buttons shows them.");
            s.Do(new PickMenuItem { Layer = Layers.Umpire, Id = id, Index = i + 1 });
            return await Pressed(s, seq, $"picked {label} on {NameOf(s, p)}");
        }
        int g = s.View.MenuItems.FindIndex(m => m.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (g < 0) return ToolResult.Fail($"The table's menu has no row {label}: list_buttons shows them.");
        s.Do(new PickGlobalMenuItem { Index = g + 1, Pos = a.Num("x") is { } x && a.Num("z") is { } z ? new Vec2(x, z) : null });
        return await Pressed(s, seq, $"picked {label} on the table's menu");
    }

    async Task<ToolResult> PressHotkey(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var label = a.Str("label") ?? "";
        int i = s.View.Hotkeys.FindIndex(h => h.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return ToolResult.Fail($"No hotkey {label}: list_buttons shows them.");
        long seq = LastSeq(s);
        var under = a.Str("id") is { Length: > 0 } id ? Find(s, id) : null;
        s.Do(new Hotkey { Index = i + 1, Layer = under == null ? null : Layers.Umpire, Piece = under?.Id, Pos = under?.Pos, Selected = List(a, "selected") });
        return await Pressed(s, seq, $"pressed the hotkey {label}");
    }

    async Task<ToolResult> SetOptions(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (Find(s, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        var done = new List<string>();
        if (a.Flag("locked") is { } locked) { s.Do(new SetLocked { Layer = Layers.Umpire, Id = id, Value = locked }); done.Add(locked ? "locked" : "unlocked"); }
        if (a.Str("tint") is { } tint) { s.Do(new SetTint { Layer = Layers.Umpire, Id = id, Value = tint.Length == 0 ? null : tint }); done.Add("tinted"); }
        if (a.Num("scale") is { } scale)
        {
            if (scale is <= 0 or > 50) return ToolResult.Fail("scale is above 0 and at most 50.");
            s.Do(new SetScale { Layer = Layers.Umpire, Id = id, Scale = scale });
            done.Add("sized");
        }
        if (a.Flag("face_down") is { } down) { s.Do(new FlipPiece { Layer = Layers.Umpire, Id = id, FaceDown = down }); done.Add(down ? "turned face down" : "turned face up"); }
        if (a.Str("attach_to") is { Length: > 0 } parent)
        {
            if (Find(s, parent) == null || parent == id) return ToolResult.Fail($"No other piece {parent} to attach to.");
            s.Do(new Attach { Layer = Layers.Umpire, Parent = parent, Child = id });
            done.Add("attached to " + parent);
        }
        if (a.Flag("detach") == true && s.View.Layer(Layers.Umpire).FirstOrDefault(x => x.Attached.Contains(id)) is { } holder)
        {
            s.Do(new Detach { Layer = Layers.Umpire, Child = id });
            done.Add("detached from " + holder.Id);
        }
        if (done.Count == 0) return ToolResult.Fail("Nothing to set.");
        await Landed(s);
        _log($"{string.Join(", ", done)}: {NameOf(s, p)}");
        var now = Find(s, id);
        return new ToolResult($"{NameOf(s, p)}: {string.Join(", ", done)}." + (now == null ? "" : "\n" + JsonSerializer.Serialize(new { now.Locked, Tint = now.TintOverride, now.Scale, now.FaceDown, now.Attached }, Json)));
    }

    async Task<ToolResult> EndTurn(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        int turn = s.View.Clock.Turn;
        s.Do(new AdvanceClock());
        if (!await Until(() => s.View.Clock.Turn != turn, 5)) return ToolResult.Fail("The turn did not end (the clock may not be allowed to go on).");
        _log($"ended turn {turn}");
        return new ToolResult($"Turn {s.View.Clock.Turn} · {s.View.Clock.GameTime}.");
    }

    async Task<ToolResult> SetClockTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var c = s.View.Clock;
        var time = a.Str("time") ?? c.GameTime;
        if (!DateTime.TryParseExact(time, new[] { "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return ToolResult.Fail("time is yyyy-mm-ddThh:mm.");
        int minutes = (int)(a.Num("minutes_per_turn") ?? c.IncrementMinutes);
        if (minutes is < 1 or > GameClock.MostMinutes) return ToolResult.Fail($"minutes_per_turn is 1 to {GameClock.MostMinutes}.");
        s.Do(new SetClock { Turn = (int)(a.Num("turn") ?? c.Turn), GameTime = t.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), IncrementMinutes = minutes });
        await Landed(s);
        _log("set the clock");
        return new ToolResult($"Turn {s.View.Clock.Turn} · {s.View.Clock.GameTime} · {GameClock.StepText(s.View.Clock.IncrementMinutes)} a turn.");
    }

    async Task<ToolResult> RollDiceTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        int count = (int)Math.Clamp(a.Num("count") ?? 1, 1, 100), sides = (int)Math.Clamp(a.Num("sides") ?? 6, 2, 1000);
        RollDice? rolled = null;
        void Seen(Command c) { if (c is RollDice r && r.Actor == s.Me.PeerId && r.Results.Count > 0) rolled ??= r; }
        s.Applied += Seen;
        try
        {
            s.Do(new RollDice { Sides = sides, Count = count, Label = a.Str("label") ?? "" });
            await Until(() => rolled != null, 4);
        }
        finally { s.Applied -= Seen; }
        if (rolled == null) return ToolResult.Fail("The roll was sent but no result came back.");
        _log($"rolled {count}d{sides}");
        return new ToolResult($"{count}d{sides}: {string.Join(", ", rolled.Results)} (sum {rolled.Results.Sum()}).");
    }

    async Task<ToolResult> SayTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!s.IsUmpire) return ToolResult.Fail("Only umpires write in the log.");
        var text = a.Str("text")?.Trim() ?? "";
        if (text.Length == 0) return ToolResult.Fail("text is needed.");
        s.Do(new Say { Text = text, Color = a.Str("colour") });
        await Landed(s);
        return new ToolResult("Written in the log.");
    }

    async Task<ToolResult> DrawTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var pts = Points(a, "points");
        var text = a.Str("text")?.Trim() ?? "";
        if (pts is not { Count: >= 1 } || pts.Count > 2000 || text.Length == 0 && pts.Count < 2) return ToolResult.Fail("points is [[x, z], …], 2 to 2000 of them, or one or more with text.");
        var colour = a.Str("colour") ?? "#C0392B";
        double width = Math.Clamp(a.Num("width") ?? 0.3, 0.02, 20);
        var board = "";
        if (a.Str("board")?.Trim() is { Length: > 0 } named)
        {
            BoardDef? Find() => s.View.Boards.FirstOrDefault(b => string.Equals(b.Name, named, StringComparison.OrdinalIgnoreCase));
            if (Find() == null) { s.Do(new AddBoard { Name = named }); await Landed(s); }
            if (Find() is not { } found) return ToolResult.Fail($"The drawing layer {named} could not be made.");
            board = found.Id;
        }
        var before = s.View.DrawLayer(Layers.Umpire).Select(x => x.Id).ToHashSet();
        int made = 1;
        var placed = pts.Count == 1 ? _placeName?.Invoke(pts[0], text) ?? pts : pts;
        var stroke = text.Length > 0 ? new Stroke { Points = placed, Text = text, Width = 0, Color = "", Board = board } : new Stroke { Points = pts, Width = width, Color = colour, Board = board };
        s.Do(new Draw { Layer = Layers.Umpire, Stroke = stroke });
        if (text.Length == 0 && a.Flag("arrow") == true)
        {
            var tip = pts[^1];
            var from = pts[^2];
            double dx = tip.X - from.X, dz = tip.Z - from.Z, len = Math.Sqrt(dx * dx + dz * dz);
            if (len > 1e-6)
            {
                dx /= len;
                dz /= len;
                double head = Math.Max(width * 4, 0.6);
                Vec2 Wing(double side) => new(tip.X - dx * head + -dz * head * 0.6 * side, tip.Z - dz * head + dx * head * 0.6 * side);
                s.Do(new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = new List<Vec2> { Wing(1), tip, Wing(-1) }, Width = width, Color = colour, Board = board } });
                made++;
            }
        }
        await Until(() => s.View.DrawLayer(Layers.Umpire).Count(x => !before.Contains(x.Id)) >= made, 3);
        var ids = s.View.DrawLayer(Layers.Umpire).Where(x => !before.Contains(x.Id)).Select(x => x.Id).ToList();
        _log("drew on the map");
        var did = text.Length > 0 ? $"Wrote {text}" : $"Drew a line of {pts.Count} points{(made > 1 ? " with an arrowhead" : "")}";
        return new ToolResult($"{did} ({string.Join(", ", ids)}; erase_drawings takes them).");
    }

    async Task<ToolResult> SetZoneTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!s.IsLead || s.Reviewing) return ToolResult.Fail("Only the host and the lead umpires set zones.");
        if (a.Flag("remove") == true)
        {
            var gone = s.View.Zones.FirstOrDefault(zn => zn.Name.Equals(a.Str("name")?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));
            if (gone == null) return ToolResult.Fail($"No zone named {a.Str("name")}.");
            s.Do(new RemoveZone { Id = gone.Id });
            await Landed(s);
            _log($"removed the zone {gone.Name}");
            return new ToolResult($"Zone {gone.Name} removed.");
        }
        var poly = Points(a, "polygon");
        if (poly is not { Count: >= 3 }) return ToolResult.Fail("polygon is [[x, z], …] with at least three points.");
        var name = a.Str("name")?.Trim() ?? "";
        var owner = a.Str("owner")?.Trim() ?? "";
        if (name.Length == 0 || owner.Length == 0) return ToolResult.Fail("name and owner are needed.");
        int index = s.View.Zones.FindIndex(zn => zn.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var zone = new Zone { Id = index >= 0 ? s.View.Zones[index].Id : "", Name = name, Owner = owner, Color = a.Str("colour") ?? "#E0B040", Polygon = poly };
        s.Do(new SetZone { Zone = zone, Index = index >= 0 ? index : null });
        await Landed(s);
        _log($"set the zone {name}");
        return new ToolResult($"Zone {name} for {owner}: {poly.Count} corners.");
    }

    const double GridCellMin = 0.1, GridCellMax = 5000;
    const int GridDivisionsMost = 20;
    static readonly string[] GridKinds = { "none", "square", "hex" };

    async Task<ToolResult> SetGridTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var kind = a.Str("kind")?.Trim().ToLowerInvariant() ?? "";
        if (!GridKinds.Contains(kind)) return ToolResult.Fail("kind is none, square or hex.");
        var was = s.View.Grid ?? new GridDef();
        var cell = a.Num("cell") ?? was.Pitch;
        if (!double.IsFinite(cell) || cell < GridCellMin || cell > GridCellMax) return ToolResult.Fail($"cell is {GridCellMin} to {GridCellMax} map units.");
        var divisions = (int)Math.Round(a.Num("divisions") ?? was.Subdivisions);
        if (divisions < 1 || divisions > GridDivisionsMost) return ToolResult.Fail($"divisions is 1 to {GridDivisionsMost}.");
        s.Do(new SetGrid { Grid = new GridDef { Kind = kind, Pitch = cell, Subdivisions = divisions, Color = was.Color } });
        await Landed(s);
        _log(kind == "none" ? "took the grid off" : $"laid a {kind} grid");
        return new ToolResult(kind == "none" ? "No grid." : $"A {kind} grid, {cell:0.##} map units a cell{(kind == "square" && divisions > 1 ? $", divided into {divisions}" : "")}.");
    }

    async Task<ToolResult> SetLight(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var done = new List<string>();
        var backdrop = a.Str("backdrop")?.Trim().ToLowerInvariant() is { } asked ? asked == "none" ? Backdrops.None : asked : null;
        if (backdrop != null && backdrop.Length > 0 && Backdrops.Find(backdrop) == null && !Backdrops.IsColour(backdrop))
            return ToolResult.Fail($"No backdrop {backdrop}: none, a #rrggbb colour, or one of {string.Join(", ", Backdrops.All.Select(b => b.Id))}.");
        var style = a.Str("map_style")?.Trim().ToLowerInvariant() is { } named ? named == "survey" ? MapStyles.Survey : named : null;
        if (style != null && MapStyles.Of(style) != style) return ToolResult.Fail("map_style is ancient, survey or modern.");
        var snow = a.Num("snow_line");
        var streams = a.Flag("streams");
        if ((snow != null || streams == true) && s.View.Calibration.UnitsPerMeter is not > 0) return ToolResult.Fail(GroundKinds.NeedsScale + " (set_map_scale).");
        bool lightGiven = a.Num("sun") != null || a.Num("ambient") != null || a.Num("warmth") != null || a.Flag("shadows") != null;
        bool lookGiven = a.Flag("from_clock") != null || a.Num("table_shade") != null || a.Str("backdrop") != null || style != null || a.Flag("contours") != null || snow != null || streams != null;
        if (lightGiven || lookGiven)
        {
            var l = s.View.Lighting;
            if (!lightGiven && backdrop != null && Backdrops.Find(backdrop)?.Light is { } suggested && LightPresets.Named(suggested) is { } preset)
                l = LightPresets.Lit(l, preset);
            s.Do(new SetLighting { Lighting = new Lighting
            {
                SunEnergy = Math.Clamp(a.Num("sun") ?? l.SunEnergy, 0, 1), Ambient = Math.Clamp(a.Num("ambient") ?? l.Ambient, 0, 1), Warmth = Math.Clamp(a.Num("warmth") ?? l.Warmth, -1, 1),
                Shadows = a.Flag("shadows") ?? l.Shadows, FromClock = a.Flag("from_clock") ?? l.FromClock,
                TableShade = Math.Clamp(a.Num("table_shade") ?? l.TableShade, 0, Lighting.SeeThrough), Backdrop = backdrop ?? l.Backdrop, MapStyle = style ?? l.MapStyle,
                Contours = a.Flag("contours") ?? l.Contours, SnowLine = Math.Max(0, snow ?? l.SnowLine), Streams = streams ?? l.Streams,
            } });
            done.Add("light");
        }
        if (a.Num("wind_from") != null || a.Num("wind_strength") != null)
        {
            var w = s.View.Wind;
            s.Do(new SetWind { Wind = new Wind { From = ((a.Num("wind_from") ?? w.From) % 360 + 360) % 360, Strength = Math.Clamp(a.Num("wind_strength") ?? w.Strength, 0, 5) } });
            done.Add("wind");
        }
        if (done.Count == 0) return ToolResult.Fail("Nothing to set.");
        await Landed(s);
        _log("set the " + string.Join(" and ", done));
        return new ToolResult($"Set the {string.Join(" and ", done)}: wind {Wind.Words(s.View.Wind.Strength)} from {Wind.Point(s.View.Wind.From)}.");
    }

    Task<ToolResult> ReadBriefing(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        if (!s.IsUmpire) return Task.FromResult(ToolResult.Fail("Only umpires read the briefing."));
        return Task.FromResult(new ToolResult(s.View.Briefing is { Length: > 0 } b ? b : "No briefing yet."));
    }

    async Task<ToolResult> SetBriefingTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (_inStudio()) return ToolResult.Fail("This is the piece studio's table: host_game first, then write the scenario's briefing.");
        if (!s.IsLead || s.Reviewing) return ToolResult.Fail("Only the host and the lead umpires write the briefing, and not while a past turn is shown.");
        var text = a.Str("text");
        if (text == null) return ToolResult.Fail("text is needed (empty clears it).");
        if (text.Length > 200_000) return ToolResult.Fail("The briefing is at most 200,000 characters.");
        var keep = text.Trim().Length == 0 ? null : Scripting.ScriptFiles.Norm(text);
        s.Do(new SetBriefing { Text = keep });
        await Until(() => s.View.Briefing == keep, 3);
        _log(keep == null ? "cleared the briefing" : "wrote the briefing");
        return new ToolResult(keep == null ? "The briefing is cleared." : $"The briefing is set ({keep.Length} characters); umpires read it in Scenario properties → Briefing.");
    }

    static readonly string[] UnitNames = { "m", "yd", "pace" };

    async Task<ToolResult> SetScale(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var units = a.Str("units");
        if (units != null && !UnitNames.Contains(units)) return ToolResult.Fail("units is one of " + string.Join(", ", UnitNames));
        double? perMetre = s.View.Calibration.UnitsPerMeter;
        if (a.Num("metres_per_unit") is { } m) { if (m <= 0) return ToolResult.Fail("metres_per_unit is above 0."); perMetre = 1 / m; }
        s.Do(new SetCalibration { UnitsPerMeter = perMetre, Units = units ?? s.View.Calibration.Units });
        await Landed(s);
        _log("set the map scale");
        return new ToolResult(FormattableString.Invariant($"One map unit is {(s.View.Calibration.UnitsPerMeter is { } u ? 1 / u : double.NaN):0.###} m; distances read in {s.View.Calibration.Units}."));
    }
}
