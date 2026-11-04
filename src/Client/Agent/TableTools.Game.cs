using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed partial class TableTools
{
    public const int MaxPlaced = 200;

    IEnumerable<AgentTool> GameTools()
    {
        yield return new AgentTool("place_pieces",
            "Puts many pieces down at once in a formation (" + string.Join(", ", Formations.Names) + ") round a centre, facing a direction. Use kinds for a mix (one kind each, in order) or kind with count. Spacing defaults to the pieces' own size. Undoable piece by piece.",
            Args.Schema(new Arg("kind", "string", "one kind's id (list_kinds)"), new Arg("count", "number", "how many of it"), new Arg("kinds", "array", "kind ids, one a piece, in order"),
                new Arg("x", "number", "centre, map units", true), new Arg("z", "number", "centre, map units", true), new Arg("formation", "string", string.Join(", ", Formations.Names)),
                new Arg("facing", "number", "degrees (0 = the map's top)"), new Arg("spacing", "number", "map units between pieces across"), new Arg("depth", "number", "map units between ranks"),
                new Arg("labels", "array", "a name for each piece, in order")),
            PlacePieces);
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
            "Ends the turn: the clock goes on by the turn's minutes, the turn is kept in the history and saved.",
            Args.Schema(), EndTurn);
        yield return new AgentTool("set_clock",
            "Sets the game clock: the turn number, the date and time (yyyy-mm-ddThh:mm) and the minutes each turn lasts.",
            Args.Schema(new Arg("turn", "number", "turn number"), new Arg("time", "string", "yyyy-mm-ddThh:mm"), new Arg("minutes_per_turn", "number", "minutes")), SetClockTool);
        yield return new AgentTool("roll_dice",
            "Rolls dice for everyone to see in the log, and returns the results.",
            Args.Schema(new Arg("count", "number", "how many (default 1)"), new Arg("sides", "number", "sides (default 6)"), new Arg("label", "string", "what the roll is for")), RollDiceTool);
        yield return new AgentTool("say",
            "Writes a line in the log everyone at the table reads.",
            Args.Schema(new Arg("text", "string", "the line", true), new Arg("colour", "string", "#rrggbb")), SayTool);
        yield return new AgentTool("draw",
            "Draws a line on the map through points (map units), as the pen does; arrow adds a head at the end. Undoable.",
            Args.Schema(new Arg("points", "array", "[[x, z], [x, z], …]", true), new Arg("colour", "string", "#rrggbb"), new Arg("width", "number", "map units (default 0.3)"), new Arg("arrow", "boolean", "a head at the end")),
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
            "Sets the table's light (sun and ambient 0–1, warmth −1 to 1, shadows, or following the game clock's hour), the table under the map (0 squared paper, 0.5 grey, 1 black), the backdrop seen around the table and the wind (from, in degrees; strength 0 calm to 5 a gale), for everyone at the table.",
            Args.Schema(new Arg("sun", "number", "0–1"), new Arg("ambient", "number", "0–1"), new Arg("warmth", "number", "−1 to 1"), new Arg("shadows", "boolean", "shadows"), new Arg("from_clock", "boolean", "follows the clock's hour"),
                new Arg("table_shade", "number", "the table under the map: 0 squared paper, 0.5 grey, 1 black"),
                new Arg("backdrop", "string", "around the table: none, a solid #rrggbb colour, or one of " + string.Join(", ", Backdrops.All.Select(b => b.Id)) + "; a sky brings its own light unless sun, ambient or warmth are given"),
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

    static List<string> List(JsonElement a, string name) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new();

    async Task<ToolResult> PlacePieces(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var kinds = List(a, "kinds");
        if (kinds.Count == 0 && a.Str("kind") is { Length: > 0 } one) kinds = Enumerable.Repeat(one, (int)Math.Clamp(a.Num("count") ?? 1, 1, MaxPlaced)).ToList();
        if (kinds.Count == 0 || kinds.Count > MaxPlaced) return ToolResult.Fail($"Give kind and count, or kinds (1 to {MaxPlaced} pieces).");
        foreach (var k in kinds.Distinct())
            if (s.Def(k) is null || s.Store.PackOfDef(k) is not { } pack || !s.View.Packs.Contains(pack.Id)) return ToolResult.Fail($"No kind {k} in this game's packs (list_kinds).");
        var formation = a.Str("formation") ?? "line";
        if (!Formations.Names.Contains(formation)) return ToolResult.Fail("formation is one of " + string.Join(", ", Formations.Names));
        if (a.Num("x") is not { } x || a.Num("z") is not { } z) return ToolResult.Fail("x and z are needed.");
        var first = s.Def(kinds[0])!;
        double across = a.Num("spacing") ?? first.Size.W * 1.1, deep = a.Num("depth") ?? first.Size.H * 1.6;
        double facing = a.Num("facing") ?? 0;
        var at = Formations.Positions(kinds.Count, formation, new Vec2(x, z), facing, across, deep);
        var labels = List(a, "labels");
        var before = s.View.Layer(Layers.Umpire).Select(p => p.Id).ToHashSet();
        for (int i = 0; i < kinds.Count; i++)
            s.Do(new SpawnPiece { Layer = Layers.Umpire, Def = kinds[i], Pos = at[i], RotDeg = facing, Label = i < labels.Count && labels[i].Length > 0 ? labels[i] : null });
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
            o["widgets"] = new JsonArray(p.Widgets.Select((w, i) => (JsonNode?)new JsonObject { ["number"] = i + 1, ["kind"] = w.Kind, ["label"] = w.Label, ["face"] = w.Face, ["value"] = w.Kind == "input" ? w.Value : null, ["presses"] = w.OnClick != null || w.OnChange != null }).ToArray());
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
        await Until(() => s.ConsoleLines.Any(l => l.Seq > seq), 1.5);
        await _delay(0.3);
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
            ? p.Widgets.FindIndex(w => w.OnClick != null && w.Label.Equals(label, StringComparison.OrdinalIgnoreCase)) is var exact and >= 0 ? exact : p.Widgets.FindIndex(w => w.OnClick != null && w.Label.Contains(label, StringComparison.OrdinalIgnoreCase))
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
        if (a.Num("scale") is { } scale) { if (scale is <= 0 or > 50) return ToolResult.Fail("scale is above 0 and at most 50."); s.Do(new SetScale { Layer = Layers.Umpire, Id = id, Scale = scale }); done.Add("sized"); }
        if (a.Flag("face_down") is { } down) { s.Do(new FlipPiece { Layer = Layers.Umpire, Id = id, FaceDown = down }); done.Add(down ? "turned face down" : "turned face up"); }
        if (a.Str("attach_to") is { Length: > 0 } parent)
        {
            if (Find(s, parent) == null || parent == id) return ToolResult.Fail($"No other piece {parent} to attach to.");
            s.Do(new Attach { Layer = Layers.Umpire, Parent = parent, Child = id }); done.Add("attached to " + parent);
        }
        if (a.Flag("detach") == true && s.View.Layer(Layers.Umpire).FirstOrDefault(x => x.Attached.Contains(id)) is { } holder)
        {
            s.Do(new Detach { Layer = Layers.Umpire, Child = id }); done.Add("detached from " + holder.Id);
        }
        if (done.Count == 0) return ToolResult.Fail("Nothing to set.");
        await _delay(0.2);
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
        if (minutes is < 1 or > 100000) return ToolResult.Fail("minutes_per_turn is at least 1.");
        s.Do(new SetClock { Turn = (int)(a.Num("turn") ?? c.Turn), GameTime = t.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), IncrementMinutes = minutes });
        await _delay(0.2);
        _log("set the clock");
        return new ToolResult($"Turn {s.View.Clock.Turn} · {s.View.Clock.GameTime} · {s.View.Clock.IncrementMinutes} minutes a turn.");
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
        await _delay(0.1);
        return new ToolResult("Written in the log.");
    }

    async Task<ToolResult> DrawTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var pts = Points(a, "points");
        if (pts is not { Count: >= 2 } || pts.Count > 2000) return ToolResult.Fail("points is [[x, z], …], 2 to 2000 of them.");
        var colour = a.Str("colour") ?? "#C0392B";
        double width = Math.Clamp(a.Num("width") ?? 0.3, 0.02, 20);
        var before = s.View.DrawLayer(Layers.Umpire).Select(x => x.Id).ToHashSet();
        int made = 1;
        s.Do(new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = pts, Width = width, Color = colour } });
        if (a.Flag("arrow") == true)
        {
            var tip = pts[^1]; var from = pts[^2];
            double dx = tip.X - from.X, dz = tip.Z - from.Z, len = Math.Sqrt(dx * dx + dz * dz);
            if (len > 1e-6)
            {
                dx /= len; dz /= len;
                double head = Math.Max(width * 4, 0.6);
                Vec2 Wing(double side) => new(tip.X - dx * head + -dz * head * 0.6 * side, tip.Z - dz * head + dx * head * 0.6 * side);
                s.Do(new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = new List<Vec2> { Wing(1), tip, Wing(-1) }, Width = width, Color = colour } });
                made++;
            }
        }
        await Until(() => s.View.DrawLayer(Layers.Umpire).Count(x => !before.Contains(x.Id)) >= made, 3);
        var ids = s.View.DrawLayer(Layers.Umpire).Where(x => !before.Contains(x.Id)).Select(x => x.Id).ToList();
        _log("drew on the map");
        return new ToolResult($"Drew a line of {pts.Count} points{(made > 1 ? " with an arrowhead" : "")} ({string.Join(", ", ids)}; erase_drawings takes them).");
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
            await _delay(0.2);
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
        await _delay(0.2);
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
        await _delay(0.2);
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
        if (a.Num("sun") != null || a.Num("ambient") != null || a.Num("warmth") != null || a.Flag("shadows") != null || a.Flag("from_clock") != null || a.Num("table_shade") != null || a.Str("backdrop") != null)
        {
            var l = s.View.Lighting;
            bool lightGiven = a.Num("sun") != null || a.Num("ambient") != null || a.Num("warmth") != null || a.Flag("shadows") != null;
            if (!lightGiven && backdrop != null && Backdrops.Find(backdrop)?.Light is { } suggested && LightPresets.Named(suggested) is { } preset)
                l = LightPresets.Lit(l, preset);
            s.Do(new SetLighting { Lighting = new Lighting
            {
                SunEnergy = Math.Clamp(a.Num("sun") ?? l.SunEnergy, 0, 1), Ambient = Math.Clamp(a.Num("ambient") ?? l.Ambient, 0, 1), Warmth = Math.Clamp(a.Num("warmth") ?? l.Warmth, -1, 1),
                Shadows = a.Flag("shadows") ?? l.Shadows, FromClock = a.Flag("from_clock") ?? l.FromClock, TableShade = Math.Clamp(a.Num("table_shade") ?? l.TableShade, 0, 1), Backdrop = backdrop ?? l.Backdrop,
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
        await _delay(0.2);
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
        await _delay(0.2);
        _log("set the map scale");
        return new ToolResult(FormattableString.Invariant($"One map unit is {(s.View.Calibration.UnitsPerMeter is { } u ? 1 / u : double.NaN):0.###} m; distances read in {s.View.Calibration.Units}."));
    }
}
