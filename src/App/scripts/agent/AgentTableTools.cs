using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Client.Agent;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class AgentTableTools
{
    readonly AgentEndpoint _a;

    public AgentTableTools(AgentEndpoint a) { _a = a; }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("add_map_sheets",
            "Adds map sheets to the table, laid side by side and unlocked: pictures (any size; big maps are cut into tiles, which can take a while), and relief: " +
            "3D terrain (glTF/GLB, FBX, OBJ+MTL, STL, PLY, Collada .dae, Blender .blend when Blender is installed) and heightmaps (16-bit PNG, GeoTIFF elevation, ESRI .asc, SRTM .hgt, .xyz). " +
            "A relief and a picture named alike become one sheet, the picture laid over the hills; a model without a paired picture brings its own texture. pairs sets them by hand. " +
            "Tiles cut from one map keep their places and meet without a step. blank adds plain ground with no file, to build terrain from nothing. Then place, size and lock them with set_map_sheet.",
            Args.Schema(new Arg("paths", "array", "full paths of picture and relief files, or web links of pictures, in order left to right"),
                new Arg("pairs", "array", "[{relief, picture}, …]: a relief file and the picture to lay on it (leave picture out for the model's own texture, or set own_texture false for plain ground)"),
                new Arg("as_placed", "boolean", "lay relief tiles out as their files place them (true if left out)"),
                new Arg("one_zero", "boolean", "one lowest point for all the reliefs added together (true if left out)"),
                new Arg("blank", "array", "[width, depth] in map units: also add a blank ground sheet (plain, flat) to shape and paint with shape_ground"),
                new Arg("scale", "number", "metres to the map unit, when the map has no scale yet (50 is usual); blank ground needs one, and a relief file that knows its metres sets it by itself")), AddSheets);
        yield return new AgentTool("list_map_sheets",
            "The map sheets on the table: id, name, centre, size, turn, locked, layer, whether it has a picture and a relief, the relief's height ×, and its highest point in map units.",
            Args.Schema(), _ => Task.FromResult(ListSheets()));
        yield return new AgentTool("view_map",
            "A picture of the table from straight above, north up, with a labelled grid in map units: the coordinates place_piece and place_pieces take. Without x, z and size it shows every map sheet. The view of the person at this PC does not move. Also lists the pieces in the area.",
            Args.Schema(new Arg("x", "number", "centre, map units"), new Arg("z", "number", "centre, map units"), new Arg("size", "number", "map units across"),
                new Arg("grid", "number", "map units between grid lines (chosen to suit the size if left out)"), new Arg("pixels", "number", $"the picture's width and height ({MapView.MinPixels}–{MapView.MaxPixels}, {MapView.DefaultPixels} if left out)")),
            ViewMap);
        yield return new AgentTool("set_map_sheet",
            "Places, sizes (width; the height follows the picture), turns, layers, crops, renames, locks or removes a map sheet; adds, replaces or removes its relief, sets the relief's height × (0 flat, 1 the model's own proportions), and lays a new picture on it.",
            Args.Schema(new Arg("id", "string", "the sheet's id (list_map_sheets)", true), new Arg("x", "number", "centre, map units"), new Arg("z", "number", "centre, map units"),
                new Arg("width", "number", "map units"), new Arg("turn", "number", "degrees"), new Arg("layer", "number", "stacking order: higher lies on top"), new Arg("name", "string", "new name"), new Arg("locked", "boolean", "locked"), new Arg("remove", "boolean", "take it off the table"),
                new Arg("height", "number", $"relief height × (0–{StateMachine.MostHeight})"), new Arg("relief", "string", "full path of a 3D terrain or heightmap file, stretched over this sheet"),
                new Arg("remove_relief", "boolean", "take the relief off (a sheet keeps a picture or a relief)"), new Arg("picture", "string", "full path or web link of a picture to lay on this sheet"),
                new Arg("crop", "array", "[x, y, w, h]: the part of the picture shown, as fractions 0–1 of the whole; [0, 0, 1, 1] shows it whole. The map keeps its scale")),
            SetSheet);
        yield return new AgentTool("copy_pieces",
            "Copies pieces onto the clipboard as they stand, with their counters, text lines, look, own name, note, tags, scale, script and whatever is attached to them. " +
            "The clipboard outlives the game, so paste_pieces lays them out in another scenario after leave_game and host_game, and a pack the other game has not got comes into it with them.",
            Args.Schema(new Arg("ids", "array", "the pieces' ids (list_pieces)", true)), CopyPieces);
        yield return new AgentTool("paste_pieces",
            "Lays what is on the clipboard on the table around x, z, each piece where it stood in relation to the others and as high above the ground as it was. One undoable change.",
            Args.Schema(new Arg("x", "number", "map units", true), new Arg("z", "number", "map units", true)), PastePieces);
        yield return new AgentTool("list_saves",
            "The saves of the game being played: its turns kept so far and the saves made by hand in its folder (host only).",
            Args.Schema(), ListSaves);
        yield return new AgentTool("save_game",
            "Saves the table as it is now under a name, beside the game's turns (host only). A name already taken fails unless replace is true: then the old save goes to the trash and this one takes its name.",
            Args.Schema(new Arg("name", "string", "the save's name", true), new Arg("replace", "boolean", "replace a save of the same name")), SaveGame);
        yield return new AgentTool("load_save",
            "Puts the table back as a save or a kept turn left it, for everyone (host only); start_over goes back to the first turn kept.",
            Args.Schema(new Arg("name", "string", "a save's name (list_saves)"), new Arg("turn", "number", "a kept turn's number"), new Arg("start_over", "boolean", "the first turn kept")),
            LoadSave);
        foreach (var t in ReliefTools()) yield return t;
        foreach (var t in MeasureTools()) yield return t;
        foreach (var t in RecordTools()) yield return t;
    }

    (Session s, Hud hud)? Table(out ToolResult? why)
    {
        why = null;
        if (_a.InStudio || _a.TableSession() is not { } s || _a.TableHud() is not { } hud) { why = ToolResult.Fail("No game is open: host_game first."); return null; }
        return (s, hud);
    }

    static bool CanChange(Session s, out ToolResult? why)
    {
        why = null;
        if (!s.CanEdit(Layers.Umpire)) { why = ToolResult.Fail("Only umpires change the table, and not while a past turn is shown."); return false; }
        return true;
    }

    const int PasteWaits = 240;
    const double PasteWait = 0.05;

    static List<string> Ids(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty("ids", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Distinct().ToList()
            : new List<string>();

    Task<ToolResult> CopyPieces(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return Task.FromResult(why!);
        var ids = Ids(a);
        if (ids.Count == 0) return Task.FromResult(ToolResult.Fail("ids is needed."));
        var nodes = ids.Select(id => hud.T.Pieces.NodeOf(Layers.Umpire, id)).OfType<PieceNode>().ToList();
        var lost = ids.Where(id => hud.T.Pieces.NodeOf(Layers.Umpire, id) == null).ToList();
        if (nodes.Count == 0) return Task.FromResult(ToolResult.Fail("No such piece on the table: " + string.Join(", ", lost)));
        hud.T.Clip.Copy(nodes, cut: false);
        int held = Clippings.Held?.Pieces.Count ?? nodes.Count;
        _a.Log($"copied {held} piece{(held > 1 ? "s" : "")} to the clipboard");
        var missed = lost.Count > 0 ? $" No such piece: {string.Join(", ", lost)}." : "";
        return Task.FromResult(new ToolResult($"{held} piece{(held > 1 ? "s" : "")} on the clipboard, from {s.View.Name}.{missed} paste_pieces puts them down, in this game or in the next one opened."));
    }

    async Task<ToolResult> PastePieces(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (!CanChange(s, out why)) return why!;
        if (Clippings.Held is not { Pieces.Count: > 0 } held) return ToolResult.Fail("The clipboard is empty: copy_pieces first.");
        if (a.Num("x") is not { } x || a.Num("z") is not { } z) return ToolResult.Fail("x and z are needed.");
        var before = s.View.Layer(Layers.Umpire).Select(p => p.Id).ToHashSet();
        hud.T.Clip.Paste(new Vec2(x, z));
        var made = new List<PieceInstance>();
        for (int i = 0; i < PasteWaits && made.Count == 0; i++)
        {
            await _a.Delay(PasteWait);
            made = s.View.Layer(Layers.Umpire).Where(p => !before.Contains(p.Id)).ToList();
        }
        int leftOut = held.Pieces.Count - made.Count;
        if (made.Count == 0)
            return ToolResult.Fail("Nothing was pasted: the packs these pieces come from are not in this game and did not come in. list_packs shows what is on this PC, add_pack brings one in.");
        _a.Log($"pasted {made.Count} piece{(made.Count > 1 ? "s" : "")} from {held.From}");
        var rest = leftOut > 0 ? $" {leftOut} left out: their pack is not in this game." : "";
        return new ToolResult($"{made.Count} piece{(made.Count > 1 ? "s" : "")} pasted at {x:0.##}, {z:0.##}: {string.Join(", ", made.Select(p => p.Id))}.{rest}");
    }

    async Task<ToolResult> AddSheets(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (!CanChange(s, out why)) return why!;
        var given = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("paths", out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new List<string>();
        var (paths, noPicture) = await PictureLinks.Local(given, s.Store.Root);
        if (noPicture != null) return ToolResult.Fail(noPicture);
        var pairs = Pairs(a) ?? new List<ReliefJob>();
        var all = paths.Concat(pairs.Select(j => j.Relief)).Concat(pairs.Where(j => j.Picture != null).Select(j => j.Picture!)).ToList();
        var blank = Blank(a);
        var missing = all.Where(x => !File.Exists(x)).ToList();
        if (all.Count == 0 && blank == null) return ToolResult.Fail("paths, pairs or blank is needed.");
        if (missing.Count > 0) return ToolResult.Fail("No such file: " + string.Join(", ", missing));
        var reliefs = paths.Where(ReliefImport.IsRelief).ToList();
        var pictures = paths.Except(reliefs).ToList();
        var jobs = pairs.Concat(ReliefSheets.Paired(reliefs, pictures)).ToList();
        var used = jobs.Where(j => j.Picture != null).Select(j => j.Picture!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rest = pictures.Where(x => !used.Contains(x)).ToList();
        int expected = jobs.Count + rest.Count + (blank != null ? 1 : 0);
        int before = s.View.Sheets.Count;
        double? metresPerUnit = a.Num("scale");
        if (blank != null && !ReliefTool.Scaled(s) && metresPerUnit is not > 0) return ToolResult.Fail("Blank ground on a map without a scale needs scale, metres to the unit (50 is usual).");
        if (metresPerUnit is > 0 && !ReliefTool.Scaled(s)) s.Do(new SetCalibration { UnitsPerMeter = 1 / metresPerUnit.Value, Units = s.View.Calibration.Units });
        if (blank is var (bw, bd)) hud.Map.Reliefs.AddBlank(bw, bd);
        if (jobs.Count > 0) hud.Map.Reliefs.Import(jobs, a.Flag("as_placed") != false, a.Flag("one_zero") != false, !ReliefTool.Scaled(s), () => { if (rest.Count > 0) hud.Map.AddPictures(rest); });
        else if (rest.Count > 0) hud.Map.AddPictures(rest);
        for (int i = 0; i < 6000 && s.View.Sheets.Count < before + expected; i++) await _a.Delay(0.1);
        int added = s.View.Sheets.Count - before;
        _a.Log($"added {added} map sheet{(added == 1 ? "" : "s")}");
        var text = added == expected ? $"Added {added} sheet{(added == 1 ? "" : "s")}, unlocked." : $"Added {added} of {expected} sheets (the log says why the others were not).";
        return new ToolResult(text + "\n" + ListSheets().Text, added < expected);
    }

    async Task<ToolResult> ViewMap(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (Godot.DisplayServer.GetName() == "headless") return ToolResult.Fail("No picture: this run has no screen.");
        var (min, max) = Extent(s);
        var centre = new Vec2(a.Num("x") ?? (min.X + max.X) / 2, a.Num("z") ?? (min.Z + max.Z) / 2);
        var size = Math.Clamp(a.Num("size") ?? Math.Max(max.X - min.X, max.Z - min.Z) * 1.05, MapView.MinSize, MapView.MaxSize);
        var step = a.Num("grid") is { } g && g > 0 ? Math.Max(g, size / 100) : MapView.Step(size);
        var pixels = (int)Math.Clamp(a.Num("pixels") ?? MapView.DefaultPixels, MapView.MinPixels, MapView.MaxPixels);
        var shot = await MapView.Render(hud, hud.T, centre, size, step, pixels);
        if (shot == null) return ToolResult.Fail("No picture: the window is not drawing (minimised?).");
        var metres = s.View.Calibration.UnitsPerMeter is { } upm && upm > 0 ? $" (about {MapView.Num(step / upm)} m)" : " (no map scale set yet: set_map_scale)";
        var head = FormattableString.Invariant($"x {MapView.Num(centre.X - size / 2)} to {MapView.Num(centre.X + size / 2)}, z {MapView.Num(centre.Z - size / 2)} to {MapView.Num(centre.Z + size / 2)}; ") +
                   $"a grid line every {MapView.Num(step)} map units{metres}. North is up: x grows to the right, z grows upward.";
        return new ToolResult(head + "\n" + PiecesIn(s, centre, size), false, new[] { shot });
    }

    const int PiecesListed = 60;

    static string PiecesIn(Session s, Vec2 centre, double size)
    {
        var rows = new JsonArray();
        int total = 0;
        foreach (var (layer, pieces) in s.View.Layers)
            foreach (var p in pieces)
            {
                if (Math.Abs(p.Pos.X - centre.X) > size / 2 || Math.Abs(p.Pos.Z - centre.Z) > size / 2) continue;
                if (++total > PiecesListed) continue;
                rows.Add(new JsonObject { ["id"] = p.Id, ["name"] = p.Label ?? s.Def(p.Def)?.Name ?? p.Def, ["layer"] = layer, ["x"] = Math.Round(p.Pos.X, 2), ["z"] = Math.Round(p.Pos.Z, 2), ["turn"] = Math.Round(p.RotDeg, 1) });
            }
        if (total == 0) return "No pieces in this area.";
        var head = total > PiecesListed ? $"{PiecesListed} of the {total} pieces in this area (list_pieces for all):" : $"The {total} pieces in this area:";
        return head + "\n" + rows.ToJsonString();
    }

    const double EmptyTableSize = 100;

    static (Vec2 min, Vec2 max) Extent(Session s)
    {
        double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
        void Take(double x, double z)
        {
            x0 = Math.Min(x0, x);
            z0 = Math.Min(z0, z);
            x1 = Math.Max(x1, x);
            z1 = Math.Max(z1, z);
        }
        foreach (var sh in s.View.Sheets)
        {
            var r = sh.RotDeg * Math.PI / 180;
            double hx = Math.Abs(Math.Cos(r)) * sh.Size.W / 2 + Math.Abs(Math.Sin(r)) * sh.Size.H / 2;
            double hz = Math.Abs(Math.Sin(r)) * sh.Size.W / 2 + Math.Abs(Math.Cos(r)) * sh.Size.H / 2;
            Take(sh.Pos.X - hx, sh.Pos.Z - hz);
            Take(sh.Pos.X + hx, sh.Pos.Z + hz);
        }
        if (s.View.Sheets.Count == 0)
            foreach (var p in s.View.Layers.Values.SelectMany(l => l)) Take(p.Pos.X, p.Pos.Z);
        if (x0 > x1) return (new Vec2(-EmptyTableSize / 2, -EmptyTableSize / 2), new Vec2(EmptyTableSize / 2, EmptyTableSize / 2));
        if (x1 - x0 < MapView.MinSize && z1 - z0 < MapView.MinSize)
        {
            x0 -= 10;
            x1 += 10;
            z0 -= 10;
            z1 += 10;
        }
        return (new Vec2(x0, z0), new Vec2(x1, z1));
    }

    ToolResult ListSheets()
    {
        if (Table(out var why) is not var (s, _)) return why!;
        var rows = new JsonArray(s.View.Sheets.Select(x => (JsonNode?)new JsonObject
        {
            ["id"] = x.Id, ["name"] = x.Name, ["x"] = Math.Round(x.Pos.X, 3), ["z"] = Math.Round(x.Pos.Z, 3),
            ["width"] = Math.Round(x.Size.W, 3), ["height"] = Math.Round(x.Size.H, 3), ["turn"] = Math.Round(x.RotDeg, 1), ["locked"] = x.Locked,
            ["layer"] = x.Z, ["picture"] = x.HasPicture, ["relief"] = x.Relief is { Length: > 0 },
            ["relief_height"] = x.Relief is { Length: > 0 } ? Math.Round(x.Height, 3) : null,
            ["highest"] = s.Relief.Grid(x) is { } g ? Math.Round(g.Highest * Ground.FullWidth(x) * x.Height, 3) : null,
            ["painted"] = s.Relief.Grid(x)?.Painted == true,
            ["metres_per_unit"] = s.View.Calibration.UnitsPerMeter is { } upm && upm > 0 ? Math.Round(1 / upm, 3) : null,
        }).ToArray());
        return new ToolResult(rows.Count == 0 ? "No map sheets yet: add_map_sheets." : rows.ToJsonString(Opts));
    }

    async Task<ToolResult> SetSheet(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (!CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Sheets.FirstOrDefault(x => x.Id == id) is not { } sh) return ToolResult.Fail($"No map sheet {id} (list_map_sheets).");
        if (a.Flag("remove") == true)
        {
            s.Do(new RemoveSheet { Id = id });
            await _a.Landed(s);
            _a.Log($"removed the map sheet {sh.Name}");
            return new ToolResult($"Removed {sh.Name}.");
        }
        if (a.Str("name") is { Length: > 0 } name) s.Do(new RenameSheet { Id = id, Name = name.Trim() });
        if (a.Str("picture") is { Length: > 0 } picture && await SetPictureOf(hud, sh, picture) is { IsError: true } badPicture) return badPicture;
        if (a.Str("relief") is { Length: > 0 } relief && await AddReliefTo(s, hud, sh, relief) is { IsError: true } badRelief) return badRelief;
        if (a.Flag("remove_relief") == true)
        {
            var now = s.View.Sheets.First(x => x.Id == id);
            if (!now.HasPicture) return ToolResult.Fail("This sheet is its relief alone: remove the sheet instead, or lay a picture on it first.");
            s.Do(new SetSheetRelief { Id = id, Relief = null, Height = 1 });
        }
        if (a.Num("height") is { } hx)
        {
            var now = s.View.Sheets.First(x => x.Id == id);
            if (now.Relief is not { Length: > 0 }) return ToolResult.Fail("This sheet has no relief: add one with relief.");
            s.Do(new SetSheetRelief { Id = id, Relief = now.Relief, Height = Math.Clamp(hx, 0, StateMachine.MostHeight) });
        }
        if (a.Flag("locked") == false && sh.Locked) s.Do(new LockSheet { Id = id, Locked = false });
        if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("crop", out var crop))
        {
            if (crop.ValueKind != JsonValueKind.Array || crop.GetArrayLength() != 4 || crop.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
                return ToolResult.Fail("crop is [x, y, w, h], fractions 0–1 of the picture.");
            var c = crop.EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var box = new FaceBox { X = c[0], Y = c[1], W = c[2], H = c[3] };
            bool whole = box.X <= 0.0005 && box.Y <= 0.0005 && box.W >= 0.999 && box.H >= 0.999;
            if (!whole && !SheetGeometry.Valid(box)) return ToolResult.Fail("crop must lie inside the picture: x and y from 0, w and h above 0, x + w and y + h at most 1.");
            s.Do(new CropSheet { Id = id, Crop = whole ? null : box });
        }
        if (a.Num("x") != null || a.Num("z") != null || a.Num("turn") != null || a.Num("width") != null || a.Num("layer") != null)
        {
            double aspect = sh.Size.W > 0 ? sh.Size.H / sh.Size.W : 1;
            Size2? size = a.Num("width") is { } w && w > 0 ? new Size2(w, w * aspect) : null;
            s.Do(new MoveSheet { Id = id, Pos = new Vec2(a.Num("x") ?? sh.Pos.X, a.Num("z") ?? sh.Pos.Z), RotDeg = a.Num("turn") ?? sh.RotDeg, Z = a.Num("layer") is { } layer ? (int)layer : sh.Z, Size = size });
        }
        if (a.Flag("locked") == true) s.Do(new LockSheet { Id = id, Locked = true });
        await _a.Landed(s);
        _a.Log($"set the map sheet {sh.Name}");
        return ListSheets();
    }

    (string dir, string run, string? folder)? Play(Session s, Hud hud, out ToolResult? why)
    {
        why = null;
        if (!s.IsHost) { why = ToolResult.Fail("Only the host saves and loads."); return null; }
        if (hud.N.Saves.RunInPlay is not var (dir, run)) { why = ToolResult.Fail("This table is not one of a scenario's games."); return null; }
        return (dir, run, SaveStore.Default.RunFolderOf(dir, run));
    }

    async Task<ToolResult> ListSaves(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (Play(s, hud, out why) is not var (_, _, folder)) return why!;
        await _a.FreshTurns(s, hud);
        var o = new JsonObject
        {
            ["turns"] = new JsonArray(s.Turns.Select(t => (JsonNode?)new JsonObject { ["turn"] = t.Turn, ["time"] = t.GameTime, ["pieces"] = t.Pieces }).ToArray()),
            ["saves"] = new JsonArray((folder != null && Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*" + SaveStore.Ext).OrderBy(File.GetLastWriteTimeUtc) : Enumerable.Empty<string>())
                .Select(f => (JsonNode?)JsonValue.Create(Path.GetFileNameWithoutExtension(f))).ToArray()),
        };
        return new ToolResult(o.ToJsonString(Opts));
    }

    async Task<ToolResult> SaveGame(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (Play(s, hud, out why) is not var (_, _, folder)) return why!;
        if (folder == null) return ToolResult.Fail("The game's folder could not be found.");
        var name = a.Str("name")?.Trim() ?? "";
        if (name.Length == 0) return ToolResult.Fail("name is needed.");
        var done = new TaskCompletionSource<(string? path, string? why)>();
        hud.N.Saves.SaveAs(folder, name, a.Flag("replace") == true, (path, problem) => done.TrySetResult((path, problem)));
        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(2)));
        if (finished != done.Task) return ToolResult.Fail("The save did not finish in two minutes.");
        var (saved, problem) = await done.Task;
        if (problem != null) return ToolResult.Fail("Not saved: " + problem);
        _a.Log($"saved {Path.GetFileNameWithoutExtension(saved)}");
        hud.Log($"Saved [b]{Fmt.Bb(Path.GetFileNameWithoutExtension(saved) ?? name)}[/b]");
        return new ToolResult($"Saved {Path.GetFileNameWithoutExtension(saved)}.");
    }

    async Task<ToolResult> LoadSave(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (Play(s, hud, out why) is not var (_, _, folder)) return why!;
        LoadFrom from;
        if (a.Flag("start_over") == true || a.Num("turn") is not null)
        {
            await _a.FreshTurns(s, hud);
            var turn = a.Flag("start_over") == true ? s.Turns.OrderBy(t => t.Index).FirstOrDefault() : s.Turns.FirstOrDefault(t => t.Turn == (int)a.Num("turn")!.Value);
            if (turn == null) return ToolResult.Fail("No such turn kept (list_saves).");
            from = new LoadFrom(SaveNames.Turn(turn.Turn, turn.GameTime), TurnIndex: turn.Index);
        }
        else if (a.Str("name") is { Length: > 0 } name)
        {
            var file = folder == null ? null : Path.Combine(folder, name + SaveStore.Ext);
            if (file == null || !File.Exists(file)) return ToolResult.Fail($"No save {name} in this game's folder (list_saves).");
            from = new LoadFrom(name, File: file);
        }
        else return ToolResult.Fail("Give name, turn or start_over.");
        var done = new TaskCompletionSource<string?>();
        s.Review(null, null);
        hud.N.Saves.Load(from, problem => done.TrySetResult(problem));
        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(3)));
        if (finished != done.Task) return ToolResult.Fail("Loading did not finish in three minutes.");
        if (await done.Task is { } problem) return ToolResult.Fail("Not loaded: " + problem);
        await _a.Delay(0.5);
        _a.Log($"loaded {from.Name}");
        return new ToolResult($"Loaded {from.Name}: turn {s.View.Clock.Turn} · {s.View.Clock.GameTime}.");
    }
}
