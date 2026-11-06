using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed partial class TableTools
{
    public const int ListLimit = 300;
    public const int ConsoleLimit = 200;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly Func<ClientSession?> _session;
    readonly Func<bool> _inStudio;
    readonly Func<double, Task> _delay;
    readonly Action<string> _log;

    readonly Func<Vec2, string, List<Vec2>>? _placeName;

    public TableTools(Func<ClientSession?> session, Func<bool> inStudio, Func<double, Task> delay, Action<string> log, Func<Vec2, string, List<Vec2>>? placeName = null)
    {
        _placeName = placeName;
        _session = session;
        _inStudio = inStudio;
        _delay = delay;
        _log = log;
    }

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("list_pieces",
            "The pieces on the open table (or the piece studio's table): id, name, kind, place, and whether each runs a script. Filter by a word in the name, kind or id.",
            Args.Schema(new Arg("filter", "string", "a word the name, kind or id contains"), new Arg("limit", "number", $"at most this many (default {ListLimit})")), ListPieces);
        yield return new AgentTool("read_piece",
            "One piece in full: its kind, place, counters, text, state, tags, memo, widgets and panels, and its script's source.",
            Args.Schema(new Arg("id", "string", "the piece's id", true)), ReadPiece);
        yield return new AgentTool("read_script",
            "A script's Lua and how it runs (running, error, stopped; the last error and its line). No id: the scenario's global script.",
            Args.Schema(new Arg("id", "string", "a piece's id; leave out for the global script")), ReadScript);
        yield return new AgentTool("write_script",
            "Gives a piece its own script, or sets the global script, and runs it again from on_load; one undoable change. It passes the same safety check as the script editor. Returns how it runs and what it printed. An empty text removes it. What a table's scripts draw stays in this game; for every piece of a kind, in every game its pack goes to, use studio_write_script in the piece studio.",
            Args.Schema(new Arg("id", "string", "a piece's id; leave out for the global script"), new Arg("text", "string", "the whole Lua script", true)), WriteScript);
        yield return new AgentTool("run_lua",
            "Runs Lua inside a script, as the script console's prompt does (the host and lead umpires only); returns what it printed and returned. Changes it makes are undoable.",
            Args.Schema(new Arg("code", "string", "the Lua to run", true), new Arg("id", "string", "run it in this piece's script; leave out for the global script")), RunLua);
        yield return new AgentTool("read_console",
            "The script console: prints, errors with their piece and line, typed Lua and its results. Pass since (the last seq you saw) to read only what is new.",
            Args.Schema(new Arg("since", "number", "only lines after this seq"), new Arg("errors_only", "boolean", "errors only"), new Arg("limit", "number", $"at most this many of the newest (default {ConsoleLimit})")), ReadConsole);
        yield return new AgentTool("list_kinds",
            "The kinds of piece the game's packs offer (what place_piece can put down): id, name, pack, size.",
            Args.Schema(new Arg("filter", "string", "a word the name or id contains")), ListKinds);
        yield return new AgentTool("place_piece",
            "Puts a piece of a kind on the table at x, z (map units; list_pieces shows where others are). One undoable change.",
            Args.Schema(new Arg("kind", "string", "the kind's id (list_kinds)", true), new Arg("x", "number", "map units", true), new Arg("z", "number", "map units", true),
                new Arg("turn", "number", "degrees"), new Arg("label", "string", "its own name")), PlacePiece);
        yield return new AgentTool("move_piece",
            "Moves or turns a piece on the table. One undoable change.",
            Args.Schema(new Arg("id", "string", "the piece's id", true), new Arg("x", "number", "map units"), new Arg("z", "number", "map units"), new Arg("turn", "number", "degrees")), MovePiece);
        yield return new AgentTool("set_piece_values",
            "Sets a piece's own name, its umpires' note, its kind, its look (state number from 1), its counters and its text lines, each as the table's own commands (undoable).",
            Args.Schema(new Arg("id", "string", "the piece's id", true), new Arg("label", "string", "its own name; empty clears it"), new Arg("note", "string", "the umpires' note on it; empty clears it"),
                new Arg("kind", "string", "another kind's id (list_kinds) to turn it into, keeping its place"), new Arg("state", "number", "look number from 1"),
                new Arg("counters", "object", "counter name to a whole number"), new Arg("text", "object", "text line name to its text")), SetValues);
        yield return new AgentTool("stow_pieces",
            "Puts pieces away inside another piece, off the table, as a bag holds them; unstow_piece brings one back. One undoable change.",
            Args.Schema(new Arg("holder", "string", "the id of the piece that will hold them", true), new Arg("ids", "array", "the ids of the pieces to put in", true)), StowPieces);
        yield return new AgentTool("unstow_piece",
            "Takes a held piece out of the piece holding it and puts it on the table, beside the holder or at x, z; read_piece on the holder lists what it holds under stowed.",
            Args.Schema(new Arg("holder", "string", "the holding piece's id", true), new Arg("id", "string", "the held piece's id or name; leave out for the first held"),
                new Arg("x", "number", "map units"), new Arg("z", "number", "map units"), new Arg("turn", "number", "degrees")), UnstowPiece);
        yield return new AgentTool("delete_piece",
            "Takes a piece off the table. Undoable.",
            Args.Schema(new Arg("id", "string", "the piece's id", true)), DeletePiece);
        yield return new AgentTool("undo",
            "Undoes the last change made from this seat at the table (the agent's or the person's own, as Ctrl+Z does).",
            Args.Schema(), Undo);
        foreach (var t in GameTools()) yield return t;
        foreach (var t in DrawingTools()) yield return t;
    }

    bool CanChange(ClientSession s, out ToolResult? why)
    {
        why = null;
        if (_inStudio()) { why = ToolResult.Fail("This is the piece studio's table: change kinds with the studio_ tools."); return false; }
        if (s.Reviewing) { why = ToolResult.Fail("The table is showing a past turn: press Back to now first."); return false; }
        if (!s.CanEdit(Layers.Umpire)) { why = ToolResult.Fail("Only umpires change the table."); return false; }
        return true;
    }

    Task<ToolResult> ListKinds(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        var filter = a.Str("filter")?.Trim() ?? "";
        var rows = new JsonArray();
        foreach (var packId in s.View.Packs)
        {
            if (s.Store.Pack(packId) is not { } pack) continue;
            foreach (var d in pack.Pieces)
            {
                if (filter.Length > 0 && !d.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !d.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                rows.Add(new JsonObject { ["id"] = d.Id, ["name"] = d.Name, ["pack"] = pack.Name, ["w"] = d.Size.W, ["h"] = d.Size.H, ["looks"] = Math.Max(1, d.States.Count) });
                if (rows.Count >= ListLimit) break;
            }
        }
        return Task.FromResult(new ToolResult($"{rows.Count} kinds\n" + rows.ToJsonString(Json)));
    }

    async Task<ToolResult> PlacePiece(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var kind = a.Str("kind") ?? "";
        if (s.Def(kind) is not { } def || s.Store.PackOfDef(kind) is not { } pack || !s.View.Packs.Contains(pack.Id))
            return ToolResult.Fail($"No kind {kind} in this game's packs (list_kinds lists them; a pack is added from the Pieces drawer).");
        if (a.Num("x") is not { } x || a.Num("z") is not { } z) return ToolResult.Fail("x and z are needed.");
        var before = s.View.Layer(Layers.Umpire).Select(p => p.Id).ToHashSet();
        s.Do(new SpawnPiece { Layer = Layers.Umpire, Def = kind, Pos = new Vec2(x, z), RotDeg = a.Num("turn") ?? 0, Label = a.Str("label") is { Length: > 0 } l ? l : null });
        if (!await Until(() => s.View.Layer(Layers.Umpire).Any(p => !before.Contains(p.Id)), 4)) return ToolResult.Fail("Sent, but the table had not placed it after 4 seconds.");
        var made = s.View.Layer(Layers.Umpire).First(p => !before.Contains(p.Id));
        _log($"placed {def.Name} ({made.Id})");
        return new ToolResult(FormattableString.Invariant($"Placed {def.Name} as {made.Id} at {made.Pos.X:0.###}, {made.Pos.Z:0.###}."));
    }

    async Task<ToolResult> MovePiece(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Find(Layers.Umpire, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        var to = new Vec2(a.Num("x") ?? p.Pos.X, a.Num("z") ?? p.Pos.Z);
        var turn = a.Num("turn") ?? p.RotDeg;
        s.Do(new KTable.Core.Protocol.MovePiece { Layer = Layers.Umpire, Id = id, Pos = to, RotDeg = turn });
        await Until(() => s.View.Find(Layers.Umpire, id) is { } q && Math.Abs(q.Pos.X - to.X) < 1e-6 && Math.Abs(q.Pos.Z - to.Z) < 1e-6, 3);
        var now = s.View.Find(Layers.Umpire, id);
        _log($"moved {NameOf(s, p)}");
        return new ToolResult(now == null ? "Moved; the piece is no longer in view." : FormattableString.Invariant($"{NameOf(s, now)} is at {now.Pos.X:0.###}, {now.Pos.Z:0.###}, turned {now.RotDeg:0.#}°."));
    }

    async Task<ToolResult> SetValues(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Find(Layers.Umpire, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        var def = s.Def(p.Def);
        var sent = new List<string>();
        var problems = new List<string>();
        if (a.Str("label") is { } label) { s.Do(new SetLabel { Layer = Layers.Umpire, Id = id, Value = label.Length == 0 ? null : label }); sent.Add("name"); }
        if (a.Str("note") is { } note) { s.Do(new SetNote { Layer = Layers.Umpire, Id = id, Value = note.Length == 0 ? null : note }); sent.Add("note"); }
        if (a.Str("kind") is { Length: > 0 } kind && kind != p.Def)
        {
            if (s.Def(kind) is not { } other) problems.Add($"no kind {kind} in this game's packs (list_kinds)");
            else
            {
                s.Do(new SetDef { Layer = Layers.Umpire, Id = id, Def = kind });
                def = other;
                sent.Add("kind");
            }
        }
        if (a.Num("state") is { } st)
        {
            int looks = Math.Max(1, def?.States.Count ?? 1);
            if (st < 1 || st > looks) problems.Add($"state is 1 to {looks}");
            else { s.Do(new SetState { Layer = Layers.Umpire, Id = id, State = (int)st - 1 }); sent.Add("look"); }
        }
        if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("counters", out var cs) && cs.ValueKind == JsonValueKind.Object)
            foreach (var c in cs.EnumerateObject())
            {
                if (c.Value.ValueKind != JsonValueKind.Number) { problems.Add($"counter {c.Name} needs a number"); continue; }
                var cdef = def?.Counters.FirstOrDefault(x => x.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase));
                int v = (int)Math.Round(c.Value.GetDouble());
                if (cdef != null) v = Math.Clamp(v, cdef.Min, cdef.Max);
                s.Do(new SetCounter { Layer = Layers.Umpire, Id = id, Name = cdef?.Name ?? c.Name, Value = v });
                sent.Add($"{cdef?.Name ?? c.Name} = {v}");
            }
        if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("text", out var ts) && ts.ValueKind == JsonValueKind.Object)
            foreach (var t in ts.EnumerateObject())
            {
                var tdef = def?.TextLines.FirstOrDefault(x => x.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase));
                s.Do(new SetText { Layer = Layers.Umpire, Id = id, Name = tdef?.Name ?? t.Name, Value = t.Value.ValueKind == JsonValueKind.String ? t.Value.GetString() ?? "" : t.Value.GetRawText() });
                sent.Add($"{tdef?.Name ?? t.Name}");
            }
        if (sent.Count == 0) return ToolResult.Fail(problems.Count > 0 ? string.Join("; ", problems) : "Nothing to set: give label, note, kind, state, counters or text.");
        await Landed(s);
        _log($"set {string.Join(", ", sent)} on {NameOf(s, p)}");
        var now = s.View.Find(Layers.Umpire, id);
        var report = now == null ? "" : "\n" + JsonSerializer.Serialize(new { now.Label, now.Note, Kind = now.Def, State = now.State + 1, now.Counters, now.Text }, Json);
        return new ToolResult($"Set {string.Join(", ", sent)}." + (problems.Count > 0 ? " Not set: " + string.Join("; ", problems) + "." : "") + report, problems.Count > 0);
    }

    async Task<ToolResult> DeletePiece(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Find(Layers.Umpire, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        s.Do(new KTable.Core.Protocol.DeletePiece { Layer = Layers.Umpire, Id = id });
        await Until(() => s.View.Find(Layers.Umpire, id) == null, 3);
        _log($"took {NameOf(s, p)} ({id}) off the table");
        return new ToolResult($"Took {NameOf(s, p)} off the table (undo brings it back).");
    }

    async Task<ToolResult> StowPieces(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var holder = a.Str("holder") ?? "";
        if (s.View.Find(Layers.Umpire, holder) is not { } h) return ToolResult.Fail($"No piece {holder} on this table.");
        var given = a.TryGetProperty("ids", out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray().Select(e => e.GetString() ?? "") : [];
        var ids = given.Where(id => id.Length > 0 && id != holder).ToList();
        var found = ids.Where(id => s.View.Find(Layers.Umpire, id) != null).ToList();
        if (found.Count == 0) return ToolResult.Fail("ids names no piece on this table.");
        s.Do(new Batch { Items = found.Select(id => (Command)new Stow { Layer = Layers.Umpire, Holder = holder, Id = id }).ToList() });
        await Until(() => found.All(id => s.View.Find(Layers.Umpire, id) == null), 3);
        var lost = ids.Except(found).ToList();
        _log($"put {found.Count} piece{(found.Count > 1 ? "s" : "")} into {NameOf(s, h)} ({holder})");
        var missed = lost.Count > 0 ? $" No such piece: {string.Join(", ", lost)}." : "";
        return new ToolResult($"{NameOf(s, h)} holds {h.Stowed?.Count ?? found.Count} now.{missed}");
    }

    async Task<ToolResult> UnstowPiece(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var holder = a.Str("holder") ?? "";
        if (s.View.Find(Layers.Umpire, holder) is not { } h) return ToolResult.Fail($"No piece {holder} on this table.");
        if (h.Stowed is not { Count: > 0 } held) return ToolResult.Fail($"{NameOf(s, h)} holds nothing.");
        var which = a.Str("id")?.Trim() ?? "";
        var target = which.Length == 0 ? h.NextOut(Random.Shared) : held.FirstOrDefault(p => p.Id == which)
            ?? held.FirstOrDefault(p => string.Equals(NameOf(s, p), which, StringComparison.OrdinalIgnoreCase));
        if (target == null) return ToolResult.Fail($"{NameOf(s, h)} holds no {which}; read_piece {holder} lists what it holds.");
        var cmd = new Unstow { Layer = Layers.Umpire, Holder = holder, Id = target.Id, RotDeg = a.Num("turn") };
        if (a.Num("x") is { } x && a.Num("z") is { } z) cmd.Pos = new Vec2(x, z);
        s.Do(cmd);
        await Until(() => s.View.Find(Layers.Umpire, target.Id) != null, 3);
        var p = s.View.Find(Layers.Umpire, target.Id);
        _log($"took {NameOf(s, target)} ({target.Id}) out of {NameOf(s, h)}");
        return new ToolResult(p == null ? $"{NameOf(s, target)} did not come out." : $"{NameOf(s, target)} ({target.Id}) is on the table at {Math.Round(p.Pos.X, 2)}, {Math.Round(p.Pos.Z, 2)}.");
    }

    async Task<ToolResult> Undo(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        if (s.MyUndoable.Count == 0) return ToolResult.Fail("Nothing from this seat to undo.");
        s.UndoLast();
        await Landed(s);
        _log("undid the last change");
        return new ToolResult("Undone.");
    }

    ClientSession? Open(out ToolResult? why)
    {
        why = null;
        var s = _session();
        if (s == null) { why = ToolResult.Fail("No game is open and the piece studio is closed: host or join a game, or open a pack in the piece studio."); return null; }
        if (!s.SeesBoard) { why = ToolResult.Fail("This seat does not see the table."); return null; }
        return s;
    }

    static string NameOf(ClientSession s, PieceInstance p) => p.Label ?? s.Def(p.Def)?.Name ?? p.Def;

    Task<ToolResult> ListPieces(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        var filter = a.Str("filter")?.Trim() ?? "";
        int limit = (int)Math.Clamp(a.Num("limit") ?? ListLimit, 1, 5000);
        var rows = new JsonArray();
        int total = 0;
        foreach (var (layer, pieces) in s.View.Layers)
            foreach (var p in pieces)
            {
                var name = NameOf(s, p);
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !p.Def.Contains(filter, StringComparison.OrdinalIgnoreCase) && !p.Id.Contains(filter,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (++total > limit) continue;
                rows.Add(new JsonObject
                {
                    ["id"] = p.Id, ["name"] = name, ["kind"] = p.Def, ["layer"] = layer,
                    ["x"] = Math.Round(p.Pos.X, 3), ["z"] = Math.Round(p.Pos.Z, 3), ["turn"] = Math.Round(p.RotDeg, 1),
                    ["script"] = p.Script != null ? "own" : s.Def(p.Def)?.Script is { Length: > 0 } ? "kind" : null,
                    ["holds"] = p.Stowed is { Count: > 0 } held ? held.Count : null,
                });
            }
        var head = total > limit ? $"{limit} of {total} pieces (raise limit or filter for the rest)" : $"{total} pieces";
        return Task.FromResult(new ToolResult(head + "\n" + rows.ToJsonString(Json)));
    }

    Task<ToolResult> ReadPiece(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        var id = a.Str("id") ?? "";
        if (Find(s, id) is not { } p) return Task.FromResult(ToolResult.Fail($"No piece {id} on this table."));
        var def = s.Def(p.Def);
        var node = JsonSerializer.SerializeToNode(p, Json)!.AsObject();
        node["kindName"] = def?.Name;
        node["kindSize"] = def == null ? null : new JsonObject { ["w"] = def.Size.W, ["h"] = def.Size.H, ["thickness"] = def.Thickness };
        node["kindStates"] = def == null ? null : new JsonArray(def.States.Select(x => (JsonNode?)JsonValue.Create(x.Name)).ToArray());
        node["kindCounters"] = def == null ? null : JsonSerializer.SerializeToNode(def.Counters, Json);
        node["kindTextLines"] = def == null ? null : JsonSerializer.SerializeToNode(def.TextLines, Json);
        node["scriptSource"] = p.Script != null ? "own" : def?.Script is { Length: > 0 } ? "kind" : "none";
        node.Remove("Script");
        return Task.FromResult(new ToolResult(node.ToJsonString(Json)));
    }

    Task<ToolResult> ReadScript(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        var id = a.Str("id") ?? "";
        if (!s.IsUmpire) return Task.FromResult(ToolResult.Fail("Only umpires read scripts."));
        string? text;
        string whose;
        if (id.Length == 0) { text = s.View.Script; whose = "the global script"; }
        else if (Find(s, id) is { } p) { text = p.Script ?? s.Def(p.Def)?.Script; whose = p.Script != null ? $"{NameOf(s, p)}'s own script" : $"the script of every {s.Def(p.Def)?.Name ?? p.Def} (its kind's, from its pack)"; }
        else return Task.FromResult(ToolResult.Fail($"No piece {id} on this table."));
        var sb = new StringBuilder();
        sb.Append(whose).Append(": ").Append(Status(s, id)).Append('\n');
        sb.Append(text is { Length: > 0 } ? text : "-- no script");
        return Task.FromResult(new ToolResult(sb.ToString()));
    }

    static string Status(ClientSession s, string id)
    {
        var info = s.Scripts.FirstOrDefault(i => i.Id == id);
        if (info == null) return "not running";
        var line = info.Status;
        if (info.LastError != null) line += $"; last error{(info.ErrorLine > 0 ? $" at line {info.ErrorLine}" : "")}: {info.LastError}";
        if (info.Timers > 0) line += $"; {info.Timers} timer{(info.Timers == 1 ? "" : "s")}";
        return line;
    }

    async Task<ToolResult> WriteScript(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (_inStudio()) return ToolResult.Fail("This is the piece studio's table: use studio_write_script to change a kind's script in the pack.");
        var id = a.Str("id") ?? "";
        var raw = a.Str("text");
        if (raw == null) return ToolResult.Fail("text is needed (an empty text removes the script).");
        var text = Scripting.ScriptFiles.Norm(raw);
        string? keep = text.Trim().Length == 0 ? null : text;
        if (keep != null && ScriptSafety.Inspect(keep) is { Hostile: true } v) return ToolResult.Fail("Refused, not run: " + string.Join("; ", v.Reasons));
        if (s.Reviewing) return ToolResult.Fail("The table is showing a past turn: press Back to now first.");
        long seq = LastSeq(s);
        int lists = 0;
        void Listed() => lists++;
        s.ScriptsChanged += Listed;
        try
        {
            string what;
            Func<bool> taken;
            if (id.Length == 0)
            {
                if (!s.IsLead) return ToolResult.Fail("Only the host and the lead umpires change the global script.");
                s.Do(new SetGlobalScript { Text = keep });
                what = "the global script";
                taken = () => s.View.Script == keep;
            }
            else
            {
                if (!s.CanEdit(Layers.Umpire)) return ToolResult.Fail("Only umpires change scripts.");
                if (Find(s, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
                s.Do(new SetScript { Layer = Layers.Umpire, Id = id, Text = keep });
                what = $"the script of {NameOf(s, p)} ({id})";
                taken = () => Find(s, id)?.Script == keep;
            }
            if (!await Until(taken, 4)) return ToolResult.Fail($"Sent, but the table had not taken {what} after 4 seconds.");
            _log($"set {what}");
            s.Request(new GetScripts());
            await Until(() => lists > 0, 3);
            bool failed = s.ConsoleLines.Any(l => l.Seq > seq && l.Kind == "error") || s.Scripts.FirstOrDefault(i => i.Id == id)?.Status == "error";
            return new ToolResult($"Set {what}. {(keep == null ? "Removed." : Status(s, id))}" + Since(s, seq, 60), failed);
        }
        finally { s.ScriptsChanged -= Listed; }
    }

    async Task<ToolResult> RunLua(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!s.IsLead) return ToolResult.Fail("Only the host and the lead umpires run Lua at the table.");
        var code = a.Str("code")?.Trim() ?? "";
        if (code.Length == 0) return ToolResult.Fail("code is needed.");
        var id = a.Str("id") ?? "";
        if (id.Length > 0 && Find(s, id) == null) return ToolResult.Fail($"No piece {id} on this table.");
        long seq = LastSeq(s);
        int lists = 0;
        void Listed() => lists++;
        s.ScriptsChanged += Listed;
        try
        {
            s.Request(new RunLua { Target = id, Code = code });
            _log(id.Length == 0 ? "ran Lua in the global script" : $"ran Lua in {id}");
            await Until(() => lists > 0, 5);
        }
        finally { s.ScriptsChanged -= Listed; }
        var said = Since(s, seq, 100);
        return new ToolResult(said.Length == 0 ? "Ran; nothing printed or returned." : said.TrimStart('\n'));
    }

    Task<ToolResult> ReadConsole(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        if (!s.IsUmpire) return Task.FromResult(ToolResult.Fail("Only umpires read the script console."));
        long since = (long)(a.Num("since") ?? 0);
        bool errors = a.Flag("errors_only") ?? false;
        int limit = (int)Math.Clamp(a.Num("limit") ?? ConsoleLimit, 1, ClientSession.ConsoleKeep);
        var lines = s.ConsoleLines.Where(l => l.Seq > since && (!errors || l.Kind == "error")).TakeLast(limit).ToList();
        var last = s.ConsoleLines.Count > 0 ? s.ConsoleLines[^1].Seq : 0;
        var sb = new StringBuilder($"last seq {last}; {lines.Count} line{(lines.Count == 1 ? "" : "s")}\n");
        foreach (var l in lines) sb.Append(Format(l)).Append('\n');
        return Task.FromResult(new ToolResult(sb.ToString().TrimEnd()));
    }

    static long LastSeq(ClientSession s) => s.ConsoleLines.Count > 0 ? s.ConsoleLines[^1].Seq : 0;

    static string Since(ClientSession s, long seq, int limit)
    {
        var lines = s.ConsoleLines.Where(l => l.Seq > seq).TakeLast(limit).ToList();
        return lines.Count == 0 ? "" : "\nConsole:\n" + string.Join("\n", lines.Select(Format));
    }

    public static string Format(ConsoleLine l)
    {
        var where = (l.Source.Length == 0 ? "global" : $"{l.Name ?? l.Source} ({l.Source})") + (l.Line > 0 ? $" line {l.Line}" : "");
        var kind = l.Kind switch { "error" => "error: ", "todo" => "todo: ", "input" => "> ", "result" => "= ", _ => "" };
        return $"[{l.Seq.ToString(CultureInfo.InvariantCulture)}] {where}: {kind}{l.Text}";
    }

    static PieceInstance? Find(ClientSession s, string id)
    {
        if (id.Length == 0) return null;
        foreach (var layer in s.View.Layers.Keys) if (s.View.Find(layer, id) is { } p) return p;
        return null;
    }

    Task<bool> Until(Func<bool> done, double seconds) => Waits.Until(done, seconds, _delay);

    Task<bool> Landed(ClientSession s) => Waits.Landed(s, _delay);
}
