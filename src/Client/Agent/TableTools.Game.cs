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

    static readonly string FormationWords = string.Join("; ", Enum.GetValues<FormationGroup>().Select(g =>
        g.ToString().ToLowerInvariant() + (g == FormationGroup.Columns ? ", facing their way of march" : "") + ": " + string.Join(", ", Formations.All.Where(k => k.Group == g).Select(k => k.Id))))
        + "; block is a column about as deep as it is wide";

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
                new Arg("gap", "number", "interval: space between pieces side by side as a share of a piece's width: 0 touching, 0.1 close (the default), 1 a piece's width apart"),
                new Arg("distance", "number", "space between ranks as a share of a piece's width: 0 closed up, 1 full distance (defaults to gap)"),
                new Arg("ranks", "number", "ranks for line, chequer, crescent and open order; how many abreast for column"), StretchLength, StretchPath),
            FormUp);
        yield return new AgentTool("set_piece_options",
            "Locks or unlocks a piece, sets its side tint, its size (scale), turns it face down or up, lets pieces be dropped into it (holds), sets whether it collides with other pieces, carries what rests on it, is taken by a selection box, " +
            "flies at an altitude over the ground and other pieces, shows how far it is moved, whether a die lands on its real face and whether an effect runs, attaches it to another piece (it then moves with it), or marks its kind a favourite in the Pieces panel, as the right-click Options do.",
            Args.Schema(new Arg("id", "string", "the piece's id", true), new Arg("locked", "boolean", "locked in place"), new Arg("tint", "string", "#rrggbb, or empty for its own"),
                new Arg("scale", "number", "1 is its own size"), new Arg("face_down", "boolean", "turned over"), new Arg("holds", "boolean", "pieces dropped on it go inside"), new Arg("collides", "boolean", "other pieces push against it and rest on it (true for most)"),
                new Arg("flies", "boolean", "in the air: drawn at its altitude with a line and its shadow to the ground, passing over pieces; false lands it"),
                new Arg("altitude", "number", FormattableString.Invariant($"how high it flies in table units, {PieceInstance.LeastAltitude} to {PieceInstance.MostAltitude}, kept while landed")),
                new Arg("carries", "boolean", "what rests on it moves with it (true for most)"), new Arg("box_selected", "boolean", "a selection box takes it (true for most)"),
                new Arg("shows_move", "boolean", "shows how far it is moved while it is dragged"), new Arg("running", "boolean", "an effect piece plays (smoke rises, fire burns)"), new Arg("smoke", "number", "how thick an effect's smoke is, " + SmokeDensity.Range),
                new Arg("thrown_for_real", "boolean", "a die shows the face it lands on, as thrown, instead of a fair random face"), new Arg("attach_to", "string", "a piece it rides on"), new Arg("detach", "boolean", "off what it rides on"),
                new Arg("favourite", "boolean", "its kind among the favourites in the Pieces panel, on this PC")),
            SetOptions);
        yield return new AgentTool("end_turn",
            "Ends the turn: the clock goes on by the turn's length, the turn is kept in the save on the table.",
            Args.Schema(), EndTurn);
        yield return new AgentTool("set_clock",
            "Sets the game clock: the turn number, the date and time (yyyy-mm-ddThh:mm) and how long each turn lasts, in minutes (60 an hour, 1440 a day).",
            Args.Schema(new Arg("turn", "number", "turn number"), new Arg("time", "string", "yyyy-mm-ddThh:mm"), new Arg("minutes_per_turn", "number", "minutes a turn lasts: 60 an hour, 1440 a day")), SetClockTool);
        yield return new AgentTool("roll_dice",
            "Rolls dice for everyone to see in the Log, and returns the results.",
            Args.Schema(new Arg("count", "number", "how many (default 1)"), new Arg("sides", "number", "sides (default 6)"), new Arg("label", "string", "what the roll is for")), RollDiceTool);
        yield return new AgentTool("say",
            "Writes a line in the Log everyone at the table reads.",
            Args.Schema(new Arg("text", "string", "the line", true), new Arg("colour", "string", "#rrggbb")), SayTool);
        yield return new AgentTool("draw",
            "Draws a line on the map through points (map units), as the pen does, or with text writes a name on the map in the map style's hand: one point places it by what lies under it (beside a town, along a river, spread across a patch of ground, plain on bare paper), more points are its baseline; arrow adds a head at a line's end; shape draws the pen's shapes instead: a rectangle or an ellipse in the box between two points, a curve or a curved arrow bending through the points, " +
            "or an area washed in the colour round three or more; opacity makes the ink see-through; board puts it on a drawing layer of that name, made if missing. Undoable.",
            Args.Schema(new Arg("points", "array", "[[x, z], [x, z], …]", true), new Arg("colour", "string", "#rrggbb"), new Arg("width", "number", "map units (default 0.3)"), new Arg("text", "string", "a name to write instead of a line"), new Arg("arrow", "boolean", "a head at the end"),
                new Arg("shape", "string", string.Join(", ", DrawShapes) + " (line if left out)"), new Arg("opacity", "number", $"{LeastInk}–1, how much the ink hides what is under it (1 if left out)"),
                new Arg("board", "string", "the drawing layer's name (list_drawings shows them); left out, the default layer")),
            DrawTool);
        yield return new AgentTool("set_zone",
            "Sets an umpire's zone: a named polygon an umpire owns (host and lead umpires), or removes one by name.",
            Args.Schema(new Arg("name", "string", "the zone's name", true), new Arg("owner", "string", "the umpire's name"), new Arg("polygon", "array", "[[x, z], …] map units"), new Arg("colour", "string", "#rrggbb"),
                new Arg("remove", "boolean", "remove the zone of this name")),
            SetZoneTool);
        yield return new AgentTool("set_grid",
            "Lays a grid over the table, as the scenario's Grid page does (undoable): square or hex cells, a cell's size across in map units (a hex across its flats), lighter lines inside square cells, and whether pieces set down by hand snap to the cells' centres.",
            Args.Schema(new Arg("kind", "string", "none, square or hex", true), new Arg("cell", "number", $"map units across one cell ({GridCellMin}–{GridCellMax}; as it is if left out)"),
                new Arg("divisions", "number", $"lines inside each square cell, 1 for plain cells (1–{GridDivisionsMost})"),
                new Arg("snap", "boolean", "pieces set down by hand land on the nearest cell's centre, the smaller cells when divided (as it is if left out)")),
            SetGridTool);
        yield return new AgentTool("set_light",
            "Sets the table's light (a preset, sun and ambient 0–1, warmth −1 to 1, where the sun stands, shadows, or following the game clock's hour), the table under the map (0 squared paper, 0.5 grey, 1 black, 1.05 see-through: no table drawn), the backdrop seen around the table (a sky, a colour or a picture of your own), the map style painted ground is drawn in, contour lines over the relief, a snow line (metres; above it every ground takes snow), streams from the relief, and the wind (from, in degrees; strength 0 calm to 5 a gale), for everyone at the table.",
            Args.Schema(new Arg("preset", "string", "the light as one of the presets, before sun, ambient and warmth are applied: " + string.Join(", ", LightPresets.All.Select(p => p.Name))),
                new Arg("sun", "number", "0–1"), new Arg("ambient", "number", "0–1"), new Arg("warmth", "number", "−1 to 1"),
                new Arg("sun_from", "number", "the compass bearing the light comes from, in degrees: 0 north, 90 east, 180 south, 270 west"),
                new Arg("sun_height", "number", "the sun's height above the horizon, 5–90 degrees; 90 is straight down"),
                new Arg("shadows", "boolean", "shadows of pieces and hills"), new Arg("from_clock", "boolean", "follows the clock's hour"),
                new Arg("table_shade", "number", "the table under the map: 0 squared paper, 0.5 grey, 1 black, 1.05 see-through (no table drawn)"),
                new Arg("backdrop", "string", "around the table: none, a solid #rrggbb colour, a picture's full path or web link (a panorama twice as wide as tall wraps all the way round), or one of " +
                    string.Join(", ", Backdrops.All.Select(b => b.Id)) + "; a sky brings its own light unless the light is given too"),
                new Arg("map_style", "string", "painted ground drawn as an ancient, survey (18th century), modern or staff (one ink) map"),
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
            "Sets how long a map unit is, in metres (the ruler and ranges use it), the one measure distances are read in (m, km, yd, mi, pace, or grid for cells of the grid), and where north is.",
            Args.Schema(new Arg("metres_per_unit", "number", "metres one map unit stands for"), new Arg("units", "string", "m, km, yd, mi, pace or grid"),
                new Arg("north", "number", "degrees clockwise from the top of the map to north (0 when north is the top)")), SetScale);
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

    internal static List<string> List(JsonElement a, string name) =>
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
        double distance = Math.Clamp(a.Num("distance") ?? gap, 0, Formations.MostGap);
        var pitch = Formations.PitchOf(sizes.Max(z => z.W), sizes.Max(z => z.H), gap, distance);
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
        if (a.Flag("collides") is { } collides) { s.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.NoCollision, Value = !collides }); done.Add(collides ? "collides" : "passes through pieces"); }
        if (a.Num("altitude") is { } altitude)
        {
            if (altitude < PieceInstance.LeastAltitude || altitude > PieceInstance.MostAltitude)
                return ToolResult.Fail(FormattableString.Invariant($"altitude runs {PieceInstance.LeastAltitude} to {PieceInstance.MostAltitude}."));
            s.Do(new SetAltitude { Layer = Layers.Umpire, Id = id, Value = altitude });
            done.Add(FormattableString.Invariant($"altitude {altitude:0.##}"));
        }
        if (a.Flag("flies") is { } flies) { s.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.Flies, Value = flies }); done.Add(flies ? "flying" : "landed"); }
        if (a.Flag("thrown_for_real") is { } real) { s.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.PhysicalRoll, Value = real }); done.Add(real ? "lands on its real face" : "rolls a fair face"); }
        if (a.Flag("holds") is { } holds) { s.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.Holds, Value = holds }); done.Add(holds ? "holds pieces" : "holds no more pieces"); }
        if (a.Flag("carries") is { } carries) { s.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.NotSticky, Value = !carries }); done.Add(carries ? "carries what rests on it" : "leaves what rests on it"); }
        if (a.Flag("box_selected") is { } boxed) { s.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.NotBoxSelected, Value = !boxed }); done.Add(boxed ? "taken by a selection box" : "left out of selection boxes"); }
        if (a.Flag("shows_move") is { } shows) { s.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.MeasuresMoves, Value = shows }); done.Add(shows ? "shows how far it is moved" : "does not show how far it is moved"); }
        if (a.Flag("running") is { } running)
        {
            if (!PieceKinds.IsEffect(s.Def(p.Def))) return ToolResult.Fail($"{NameOf(s, p)} is not an effect: only effects run.");
            s.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.EffectOff, Value = !running });
            done.Add(running ? "runs" : "stopped");
        }
        if (a.Num("smoke") is { } smoke)
        {
            if (s.Def(p.Def)?.Effect is not { } fx || !KTable.Client.Effects.EffectKinds.HasSmokeSetting(fx)) return ToolResult.Fail($"{NameOf(s, p)} makes no smoke.");
            if (!SmokeDensity.Known(smoke)) return ToolResult.Fail("smoke runs " + SmokeDensity.Range + ".");
            s.Do(new SetSmoke { Layer = Layers.Umpire, Id = id, Value = smoke == fx.Density ? null : smoke });
            done.Add($"smoke {KTable.Client.Effects.EffectKinds.DensityText(smoke).ToLowerInvariant()}");
        }
        if (a.Flag("favourite") is { } favourite)
        {
            if (_hooks.Favourite is not { } hearts) return ToolResult.Fail("Favourites are kept by the app's Pieces panel.");
            hearts(p.Def, favourite);
            done.Add(favourite ? "its kind a favourite" : "its kind no longer a favourite");
        }
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
        var said = $"{NameOf(s, p)}: {string.Join(", ", done)}.";
        if (now == null) return new ToolResult(said);
        var holdsNow = now.HoldsPieces(s.Store.Def(now.Def));
        var shown = new
        {
            now.Locked, Tint = now.TintOverride, now.Scale, now.FaceDown, Collides = !now.NoCollision, Carries = !now.NotSticky, BoxSelected = !now.NotBoxSelected,
            ShowsMove = now.MeasuresMoves, now.Flies, Altitude = now.FlightAltitude,
            Running = PieceKinds.IsEffect(s.Def(now.Def)) ? !now.EffectOff : (bool?)null, ThrownForReal = now.PhysicalRoll, Holds = holdsNow, now.Attached,
            Favourite = _hooks.Favourite?.Invoke(now.Def, null),
        };
        return new ToolResult(said + "\n" + JsonSerializer.Serialize(shown, Json));
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
        if (!s.IsUmpire) return ToolResult.Fail("Only umpires write in the Log.");
        var text = a.Str("text")?.Trim() ?? "";
        if (text.Length == 0) return ToolResult.Fail("text is needed.");
        s.Do(new Say { Text = text, Color = a.Str("colour") });
        await Landed(s);
        return new ToolResult("Written in the Log.");
    }

    async Task<ToolResult> DrawTool(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var pts = Points(a, "points");
        var text = a.Str("text")?.Trim() ?? "";
        if (pts is not { Count: >= 1 } || pts.Count > 2000 || text.Length == 0 && pts.Count < 2) return ToolResult.Fail("points is [[x, z], …], 2 to 2000 of them, or one or more with text.");
        var shape = (a.Str("shape") ?? "line").Trim().ToLowerInvariant();
        if (!DrawShapes.Contains(shape)) return ToolResult.Fail("shape is " + string.Join(", ", DrawShapes) + ".");
        if (text.Length == 0 && Shaped(shape, pts) is { } badShape) return badShape;
        var colour = a.Str("colour") ?? "#C0392B";
        if (a.Num("opacity") is { } opacity)
        {
            if (!Backdrops.IsColour(colour) || colour.Length != HexColour.Length) return ToolResult.Fail("opacity goes with a #rrggbb colour.");
            var alpha = (int)Math.Round(Math.Clamp(opacity, LeastInk, 1) * 255);
            if (alpha < 255) colour += alpha.ToString("x2", CultureInfo.InvariantCulture);
        }
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
        var placed = pts.Count == 1 ? _hooks.PlaceName?.Invoke(pts[0], text) ?? pts : pts;
        var strokes = text.Length > 0 ? new List<Stroke> { new() { Points = placed, Text = text, Width = 0, Color = "", Board = board } }
            : ShapeStrokes(shape, pts, a.Flag("arrow") == true, new Stroke { Width = width, Color = colour, Board = board });
        foreach (var st in strokes) s.Do(new Draw { Layer = Layers.Umpire, Stroke = st });
        int made = strokes.Count;
        await Until(() => s.View.DrawLayer(Layers.Umpire).Count(x => !before.Contains(x.Id)) >= made, 3);
        var ids = s.View.DrawLayer(Layers.Umpire).Where(x => !before.Contains(x.Id)).Select(x => x.Id).ToList();
        _log("drew on the map");
        var did = text.Length > 0 ? $"Wrote {text}" : shape == "line" ? $"Drew a line of {pts.Count} points{(made > 1 ? " with an arrowhead" : "")}" : $"Drew a {shape.Replace('_', ' ')}";
        return new ToolResult($"{did} ({string.Join(", ", ids)}; erase_drawings takes them).");
    }

    static readonly string[] DrawShapes = { "line", "rectangle", "ellipse", "curve", "curved_arrow", "area" };
    const double LeastInk = 0.05, ArrowWing = 0.6, ArrowLength = 4, LeastArrow = 0.6;
    const string HexColour = "#rrggbb";

    static ToolResult? Shaped(string shape, List<Vec2> pts) => shape switch
    {
        "rectangle" or "ellipse" when pts.Count != 2 => ToolResult.Fail($"A {shape} takes two points: opposite corners of its box."),
        "area" when pts.Count < 3 => ToolResult.Fail("An area takes three or more points round it."),
        "curve" or "curved_arrow" when pts.Count < 2 => ToolResult.Fail("A curve takes two or more points it bends through."),
        _ => null,
    };

    static List<Stroke> ShapeStrokes(string shape, List<Vec2> pts, bool arrow, Stroke ink)
    {
        Stroke Line(List<Vec2> p, bool fill = false) => new() { Points = p, Width = ink.Width, Color = ink.Color, Board = ink.Board, Fill = fill };
        switch (shape)
        {
            case "rectangle":
                return new() { Line(Polyline.ShapePoints("rect", pts[0], pts[1])) };
            case "ellipse":
                return new() { Line(Polyline.ShapePoints("ellipse", pts[0], pts[1])) };
            case "area":
                return new() { Line(Polyline.Smooth(pts, true), fill: true) };
            case "curve" or "curved_arrow":
                var body = Polyline.Smooth(pts, false);
                return shape == "curved_arrow" && Polyline.PathHead(body) is { } head ? new() { Line(body), Line(head) } : new() { Line(body) };
        }
        var lines = new List<Stroke> { Line(pts) };
        var tip = pts[^1];
        var from = pts[^2];
        double dx = tip.X - from.X, dz = tip.Z - from.Z, len = Math.Sqrt(dx * dx + dz * dz);
        if (!arrow || len <= 1e-6) return lines;
        dx /= len;
        dz /= len;
        double size = Math.Max(ink.Width * ArrowLength, LeastArrow);
        Vec2 Wing(double side) => new(tip.X - dx * size + -dz * size * ArrowWing * side, tip.Z - dz * size + dx * size * ArrowWing * side);
        lines.Add(Line(new List<Vec2> { Wing(1), tip, Wing(-1) }));
        return lines;
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
        var snap = a.Flag("snap") ?? was.Snap;
        s.Do(new SetGrid { Grid = new GridDef { Kind = kind, Pitch = cell, Subdivisions = divisions, Color = was.Color, Snap = snap } });
        await Landed(s);
        _log(kind == "none" ? "took the grid off" : $"laid a {kind} grid");
        return new ToolResult(kind == "none" ? "No grid."
            : $"A {kind} grid, {cell:0.##} map units a cell{(kind == "square" && divisions > 1 ? $", divided into {divisions}" : "")}{(snap ? "; pieces snap to it" : "")}.");
    }

    async Task<ToolResult> SetLight(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var done = new List<string>();
        var files = new List<string>();
        var given = a.Str("backdrop")?.Trim();
        var backdrop = given?.ToLowerInvariant() is { } asked ? asked == "none" ? Backdrops.None : asked : null;
        if (backdrop != null && backdrop.Length > 0 && Backdrops.Find(backdrop) == null && !Backdrops.IsColour(backdrop))
        {
            if (!LooksLikeFile(given!)) return ToolResult.Fail($"No backdrop {backdrop}: none, a #rrggbb colour, a picture's file or web link, or one of {string.Join(", ", Backdrops.All.Select(b => b.Id))}.");
            if (_hooks.KeepPicture is not { } keep) return ToolResult.Fail("A backdrop picture is chosen in the app's Scenario properties.");
            if (await keep(given!, true) is not { } kept) return ToolResult.Fail($"{given} could not be used as a backdrop.");
            backdrop = kept;
            files.Add(kept);
        }
        Lighting? preset = null;
        if (a.Str("preset") is { Length: > 0 } presetName)
        {
            preset = LightPresets.All.FirstOrDefault(p => string.Equals(p.Name, presetName.Trim(), StringComparison.OrdinalIgnoreCase)).Light;
            if (preset == null) return ToolResult.Fail("preset is one of " + string.Join(", ", LightPresets.All.Select(p => p.Name)) + ".");
        }
        var style = a.Str("map_style")?.Trim().ToLowerInvariant() is { } named ? named == "survey" ? MapStyles.Survey : named : null;
        if (style != null && MapStyles.Of(style) != style) return ToolResult.Fail("map_style is ancient, survey, modern or staff.");
        var snow = a.Num("snow_line");
        var streams = a.Flag("streams");
        if ((snow != null || streams == true) && s.View.Calibration.UnitsPerMeter is not > 0) return ToolResult.Fail(GroundKinds.NeedsScale + " (set_map_scale).");
        var sunFrom = a.Num("sun_from");
        var sunHeight = a.Num("sun_height");
        if (sunFrom is { } f && !double.IsFinite(f) || sunHeight is { } hh && !double.IsFinite(hh)) return ToolResult.Fail("sun_from and sun_height are numbers.");
        bool lightGiven = a.Num("sun") != null || a.Num("ambient") != null || a.Num("warmth") != null || a.Flag("shadows") != null || preset != null || sunFrom != null || sunHeight != null;
        bool lookGiven = a.Flag("from_clock") != null || a.Num("table_shade") != null || a.Str("backdrop") != null || style != null || a.Flag("contours") != null || snow != null || streams != null;
        if (lightGiven || lookGiven)
        {
            var l = s.View.Lighting;
            if (preset != null) l = LightPresets.Lit(l, preset);
            else if (!lightGiven && backdrop != null && Backdrops.Find(backdrop)?.Light is { } suggested && LightPresets.Named(suggested) is { } skyLight)
                l = LightPresets.Toned(l, skyLight);
            var next = l.Clone();
            next.SunEnergy = Math.Clamp(a.Num("sun") ?? l.SunEnergy, 0, 1);
            next.Ambient = Math.Clamp(a.Num("ambient") ?? l.Ambient, 0, 1);
            next.Warmth = Math.Clamp(a.Num("warmth") ?? l.Warmth, -1, 1);
            next.SunFrom = Calibration.Turned(sunFrom ?? l.SunFrom);
            next.SunHeight = Math.Clamp(sunHeight ?? l.SunHeight, Lighting.LowestSun, Lighting.HighestSun);
            next.Shadows = a.Flag("shadows") ?? l.Shadows;
            next.FromClock = a.Flag("from_clock") ?? l.FromClock;
            next.TableShade = Math.Clamp(a.Num("table_shade") ?? l.TableShade, 0, Lighting.SeeThrough);
            next.Backdrop = backdrop ?? l.Backdrop;
            next.MapStyle = style ?? l.MapStyle;
            next.Contours = a.Flag("contours") ?? l.Contours;
            next.SnowLine = Math.Max(0, snow ?? l.SnowLine);
            next.Streams = streams ?? l.Streams;
            var lit = new SetLighting { Lighting = next };
            if (files.Count > 0 && _hooks.SendWithFiles is { } send) send(files, lit);
            else s.Do(lit);
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

    static bool LooksLikeFile(string s) => s.Contains('/') || s.Contains('\\') || Path.HasExtension(s);

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

    static readonly string[] UnitNames = Calibration.Measures.Select(m => m.Id).ToArray();

    async Task<ToolResult> SetScale(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var units = a.Str("units");
        if (units != null && !UnitNames.Contains(units)) return ToolResult.Fail("units is one of " + string.Join(", ", UnitNames));
        double? perMetre = s.View.Calibration.UnitsPerMeter;
        if (a.Num("metres_per_unit") is { } m) { if (m <= 0) return ToolResult.Fail("metres_per_unit is above 0."); perMetre = 1 / m; }
        var north = a.Num("north");
        if (north is { } n && !double.IsFinite(n)) return ToolResult.Fail("north is a number of degrees.");
        s.Do(new SetCalibration { UnitsPerMeter = perMetre, Units = units ?? s.View.Calibration.Units, North = north });
        await Landed(s);
        _log("set the map scale");
        var scale = s.View.Calibration.UnitsPerMeter is { } u ? FormattableString.Invariant($"One map unit is {1 / u:0.###} m") : "The map has no scale yet";
        return new ToolResult(FormattableString.Invariant($"{scale}; distances read in {s.View.Calibration.Units}; north is {s.View.Calibration.North:0}° clockwise from the top of the map."));
    }
}
