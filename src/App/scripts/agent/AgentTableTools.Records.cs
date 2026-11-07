using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Client.Agent;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class AgentTableTools
{
    const int LogShown = 60, LogMost = 1000, ChangesShown = 100, BookSide = 1568;
    const double Moved = 0.01, Turned = 0.5, CopyWaitMinutes = 5, PlayWaitMinutes = 3;

    IEnumerable<AgentTool> RecordTools()
    {
        yield return new AgentTool("read_log",
            "The Log as everyone at the table reads it: chat, rolls, turns, who came and went, what changed. Each line has its number; pass since (the last number you read) for only what is new.",
            Args.Schema(new Arg("since", "number", "only lines after this number"), new Arg("limit", "number", $"at most this many of the newest (1–{LogMost}, {LogShown} if left out)")),
            x => Task.FromResult(ReadLog(x)));
        yield return new AgentTool("drawing_layers",
            "The drawing layers, as the Layers panel shows them: list them; add one; rename or remove one (removing takes its drawings with it, and the last layer is cleared instead); " +
            "hide or show one on this PC's screen only (the pen is locked while the layer it draws on is hidden); or pick the layer the person's pen draws on. draw takes a layer by name too.",
            Args.Schema(new Arg("action", "string", "list, add, rename, remove, hide, show or pick", true), new Arg("layer", "string", "the layer's name or id"), new Arg("name", "string", "for add and rename: the new name")),
            DrawingLayers);
        yield return new AgentTool("read_book",
            "A page of a book piece (one whose looks are pages, as Read… opens it), as a picture, with how many pages it has.",
            Args.Schema(new Arg("id", "string", "the book piece's id", true), new Arg("page", "number", "page number from 1 (its page on the table if left out)")),
            x => Task.FromResult(ReadBook(x)));
        yield return new AgentTool("set_view",
            "The person's own view: the map from straight above or the 3D table, plates behind the labels beside pieces, piece ids on hover, and the sound. Change them only when the person asks; with nothing given, says how they stand.",
            Args.Schema(new Arg("map_view", "boolean", "the map from straight above (true) or the 3D table (false)"), new Arg("label_plates", "boolean", "plates behind the labels beside pieces"),
                new Arg("piece_ids", "boolean", "piece ids, module GUIDs and kinds on hover"), new Arg("sound", "boolean", "sound on")),
            x => Task.FromResult(SetView(x)));
        yield return new AgentTool("look_back",
            "Reads a kept turn or a save without loading it (host only): its clock, and what differs from the table now, piece by piece (added, gone, moved or turned, changed). " +
            "show puts the person's own table on it as History does, where nothing can be changed; now brings them back to the live table.",
            Args.Schema(new Arg("turn", "number", "a kept turn's number (list_saves)"), new Arg("save", "string", "a save's name in this game's folder (list_saves)"),
                new Arg("show", "boolean", "also show it on the person's table, as History does"), new Arg("now", "boolean", "back to the live table, alone")),
            LookBack);
        yield return new AgentTool("saves",
            "The Saves window's work, for the game on the table (host only). list: every folder, play and save under the scenario's saves, and the trash. new_play: a fresh play of the scenario, from its start, the table now, or a save (name, from, play to put it on the table). " +
            "rename, delete (to the trash; never the play on the table), new_folder, move (into a folder) and restore (from the trash, by its name in list). Paths are relative to the saves, folders with /, a save without its extension.",
            Args.Schema(new Arg("action", "string", "list, new_play, rename, delete, new_folder, move or restore", true), new Arg("path", "string", "the save or folder to act on"),
                new Arg("name", "string", "rename's new name, new_folder's or new_play's name, restore's trash name"), new Arg("into", "string", "move's or new_folder's folder (the saves themselves if left out)"),
                new Arg("from", "string", "new_play: start (default), now, or a save's path"), new Arg("play", "boolean", "new_play: put it on the table now")),
            Saves);
        yield return new AgentTool("save_scenario_copy",
            "Saves a copy of the table as it is now as a new scenario (map sheets, every piece, clock and scale), as Save a copy as a new scenario… does; the table's own scenario is left as it is.",
            Args.Schema(new Arg("name", "string", "the new scenario's name", true)),
            SaveScenarioCopy);
    }

    ToolResult ReadLog(JsonElement a)
    {
        if (Table(out var why) is not var (_, hud)) return why!;
        var lines = hud.LogPanel.Text.GetParsedText().Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        long first = hud.LogPanel.Written - lines.Count + 1;
        long since = (long)(a.Num("since") ?? 0);
        int limit = Math.Clamp((int)(a.Num("limit") ?? LogShown), 1, LogMost);
        var numbered = lines.Select((l, i) => (n: first + i, l)).Where(x => x.n > since).ToList();
        var shown = numbered.Skip(Math.Max(0, numbered.Count - limit)).ToList();
        if (shown.Count == 0) return new ToolResult($"Nothing new in the Log (last line {hud.LogPanel.Written}).");
        var skipped = numbered.Count - shown.Count;
        return new ToolResult((skipped > 0 ? $"{skipped} earlier lines left out.\n" : "") + string.Join("\n", shown.Select(x => $"{x.n}  {x.l}")));
    }

    async Task<ToolResult> DrawingLayers(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        var action = (a.Str("action") ?? "").Trim().ToLowerInvariant();
        var t = hud.T;
        BoardDef? Named(string? key) => key is not { Length: > 0 } k ? null
            : s.View.Boards.FirstOrDefault(b => b.Id == k) ?? s.View.Boards.FirstOrDefault(b => string.Equals(b.Name, k.Trim(), StringComparison.OrdinalIgnoreCase));
        ToolResult Listed(string head) => new(head + new JsonArray(s.View.Boards.Select(b => (JsonNode?)new JsonObject
        {
            ["id"] = b.Id, ["name"] = b.Name, ["drawings"] = s.View.Drawings.Values.Sum(l => l.Count(x => x.Board == b.Id)),
            ["shown"] = !t.HiddenBoards.Contains(b.Id), ["pen"] = t.DrawBoard() == b.Id,
        }).ToArray()).ToJsonString(Opts));
        if (action == "list") return Listed("");
        if (action is "hide" or "show" or "pick")
        {
            if (Named(a.Str("layer")) is not { } b) return ToolResult.Fail("layer is needed: a layer's name or id (list).");
            if (action == "hide") t.HiddenBoards.Add(b.Id);
            else if (action == "show") t.HiddenBoards.Remove(b.Id);
            else t.Board = b.Id;
            t.RefreshDrawings();
            hud.Strip.SyncPenLock();
            return Listed($"{b.Name}: {(action == "pick" ? "the pen draws on it" : action == "hide" ? "hidden on this screen" : "shown")}.\n");
        }
        if (!CanChange(s, out why)) return why!;
        switch (action)
        {
            case "add":
                s.Do(new AddBoard { Name = a.Str("name")?.Trim() ?? "" });
                break;
            case "rename":
                if (Named(a.Str("layer")) is not { } r) return ToolResult.Fail("layer is needed: a layer's name or id (list).");
                if (a.Str("name")?.Trim() is not { Length: > 0 } to) return ToolResult.Fail("name is needed.");
                s.Do(new RenameBoard { Id = r.Id, Name = to });
                break;
            case "remove":
                if (Named(a.Str("layer")) is not { } gone) return ToolResult.Fail("layer is needed: a layer's name or id (list).");
                s.Do(new RemoveBoard { Id = gone.Id });
                break;
            default: return ToolResult.Fail("action is list, add, rename, remove, hide, show or pick.");
        }
        await _a.Landed(s);
        _a.Log($"{action} a drawing layer");
        return Listed("");
    }

    ToolResult ReadBook(JsonElement a)
    {
        if (Table(out var why) is not var (s, _)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Find(Layers.Umpire, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        var def = s.Store.Def(p.Def);
        if (!BookWindow.Is(def)) return ToolResult.Fail($"{PieceName(s, p)} is not a book: a book's looks are its pages.");
        var pages = def!.States.Select(st => st.Image).Where(i => i is { Length: > 0 }).Select(i => i!).ToList();
        if (pages.Count == 0) return ToolResult.Fail("This book has no pages.");
        int page = a.Num("page") is { } n ? (int)n : Math.Clamp(p.State, 0, pages.Count - 1) + 1;
        if (page < 1 || page > pages.Count) return ToolResult.Fail($"page is 1 to {pages.Count}.");
        var pack = s.Store.PackOfDef(p.Def);
        if (pack == null || s.Store.AssetPath(pack, pages[page - 1]) is not { } file || !File.Exists(file)) return ToolResult.Fail("This page's picture is not on this PC.");
        var img = Image.LoadFromFile(file);
        if (img == null || img.IsEmpty()) return ToolResult.Fail("This page's picture could not be read.");
        if (img.IsCompressed()) img.Decompress();
        int side = Math.Max(img.GetWidth(), img.GetHeight());
        if (side > BookSide) img.Resize(img.GetWidth() * BookSide / side, img.GetHeight() * BookSide / side, Image.Interpolation.Lanczos);
        return new ToolResult($"{PieceName(s, p)} · page {page} of {pages.Count}.", false, new[] { img.SavePngToBuffer() });
    }

    ToolResult SetView(JsonElement a)
    {
        if (Table(out var why) is not var (_, hud)) return why!;
        var t = hud.T;
        var cfg = t.Settings;
        if (a.Flag("map_view") is { } top && top != t.Rig.TopDown) t.Rig.ToggleTopDown();
        if (a.Flag("label_plates") is { } plates && plates != cfg.LabelPlates) hud.SetLabelPlates(plates);
        if (a.Flag("piece_ids") is { } ids && ids != cfg.MakerTools) { cfg.MakerTools = ids; cfg.Save(); }
        if (a.Flag("sound") is { } sound && sound == cfg.SoundMuted)
        {
            cfg.SoundMuted = !sound;
            cfg.Save();
            Sfx.Current?.Apply();
        }
        var now = new JsonObject { ["map_view"] = t.Rig.TopDown, ["label_plates"] = cfg.LabelPlates, ["piece_ids"] = cfg.MakerTools, ["sound"] = !cfg.SoundMuted };
        return new ToolResult(now.ToJsonString(Opts));
    }

    async Task<ToolResult> LookBack(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (a.Flag("now") == true)
        {
            s.Review(null, null);
            return new ToolResult("The person's table shows the live game again.");
        }
        if (Play(s, hud, out why) is not var (_, _, folder)) return why!;
        if (hud.N.Server is not { } server) return ToolResult.Fail("Only the host reads past turns.");
        GameState past;
        TurnInfo info;
        if (a.Num("turn") is { } turn)
        {
            int index = server.Turns.FindIndex(t => t.Turn == (int)turn);
            if (index < 0) return ToolResult.Fail("No such turn kept (list_saves).");
            var record = server.Turns[index];
            past = await Task.Run(record.Unpack);
            info = new TurnInfo { Index = index, Turn = record.Turn, GameTime = record.GameTime, Pieces = record.Pieces };
        }
        else if (a.Str("save") is { Length: > 0 } name)
        {
            var file = folder == null ? null : Path.Combine(folder, name + SaveStore.Ext);
            if (file == null || !File.Exists(file)) return ToolResult.Fail($"No save {name} in this game's folder (list_saves).");
            var store = s.Store;
            past = await Task.Run(() => KTable.Server.SaveLoader.Load(file, store).State);
            info = new TurnInfo { Index = -1, Turn = past.Clock.Turn, GameTime = past.Clock.GameTime, SaveName = name };
        }
        else return ToolResult.Fail("Give turn or save, or now to come back to the live table.");
        var result = Differences(s, past, s.Live);
        if (a.Flag("show") == true)
        {
            s.Review(past, info);
            result["shown"] = "on the person's table, as History shows it; look_back now brings it back";
        }
        return new ToolResult(result.ToJsonString(Opts));
    }

    static JsonObject Differences(Session s, GameState past, GameState now)
    {
        var then = past.Layer(Layers.Umpire).ToDictionary(p => p.Id);
        var live = now.Layer(Layers.Umpire).ToDictionary(p => p.Id);
        JsonObject Brief(PieceInstance p) => new() { ["id"] = p.Id, ["name"] = PieceName(s, p), ["x"] = Math.Round(p.Pos.X, 2), ["z"] = Math.Round(p.Pos.Z, 2) };
        var added = live.Values.Where(p => !then.ContainsKey(p.Id)).ToList();
        var gone = then.Values.Where(p => !live.ContainsKey(p.Id)).ToList();
        var moved = new List<JsonNode>();
        var changed = new List<JsonNode>();
        foreach (var (id, was) in then)
        {
            if (!live.TryGetValue(id, out var isNow)) continue;
            if ((isNow.Pos - was.Pos).Length > Moved || Math.Abs(Mathf.Wrap(isNow.RotDeg - was.RotDeg, -180, 180)) > Turned)
                moved.Add(new JsonObject
                {
                    ["id"] = id, ["name"] = PieceName(s, isNow), ["from"] = Point(was.Pos), ["to"] = Point(isNow.Pos),
                    ["turned"] = Math.Round(Mathf.Wrap(isNow.RotDeg - was.RotDeg, -180, 180), 1),
                });
            var what = new List<string>();
            if (was.State != isNow.State) what.Add($"look {was.State + 1} → {isNow.State + 1}");
            foreach (var k in was.Counters.Keys.Union(isNow.Counters.Keys))
                if (was.Counters.GetValueOrDefault(k) != isNow.Counters.GetValueOrDefault(k)) what.Add($"{k} {was.Counters.GetValueOrDefault(k)} → {isNow.Counters.GetValueOrDefault(k)}");
            foreach (var k in was.Text.Keys.Union(isNow.Text.Keys))
                if (was.Text.GetValueOrDefault(k) != isNow.Text.GetValueOrDefault(k)) what.Add($"{k} \"{was.Text.GetValueOrDefault(k)}\" → \"{isNow.Text.GetValueOrDefault(k)}\"");
            if (was.Label != isNow.Label) what.Add($"name \"{was.Label}\" → \"{isNow.Label}\"");
            if (was.FaceDown != isNow.FaceDown) what.Add(isNow.FaceDown ? "turned face down" : "turned face up");
            if ((was.Stowed?.Count ?? 0) != (isNow.Stowed?.Count ?? 0)) what.Add($"holds {was.Stowed?.Count ?? 0} → {isNow.Stowed?.Count ?? 0}");
            if (what.Count > 0) changed.Add(new JsonObject { ["id"] = id, ["name"] = PieceName(s, isNow), ["changes"] = string.Join("; ", what) });
        }
        JsonArray Few(IEnumerable<JsonNode> rows) => new(rows.Take(ChangesShown).Select(r => (JsonNode?)r).ToArray());
        return new JsonObject
        {
            ["then"] = $"turn {past.Clock.Turn} · {past.Clock.GameTime}", ["now"] = $"turn {now.Clock.Turn} · {now.Clock.GameTime}",
            ["pieces_then"] = then.Count, ["pieces_now"] = live.Count,
            ["added_since"] = Few(added.Select(Brief)), ["gone_since"] = Few(gone.Select(Brief)), ["moved_since"] = Few(moved), ["changed_since"] = Few(changed),
            ["note"] = $"each list shows at most {ChangesShown}; drawings: {past.Drawings.Values.Sum(l => l.Count)} then, {now.Drawings.Values.Sum(l => l.Count)} now",
        };
    }

    async Task<ToolResult> Saves(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (Play(s, hud, out why) is not var (dir, run, _)) return why!;
        var store = SaveStore.Default;
        var root = store.SavesRoot(dir);
        string? Under(string? rel)
        {
            if (rel is not { Length: > 0 } r) return null;
            var full = Path.GetFullPath(Path.Combine(root, r.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return null;
            return Directory.Exists(full) ? full : File.Exists(full + SaveStore.Ext) ? full + SaveStore.Ext : null;
        }
        string Rel(string full)
        {
            var r = Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
            return r.EndsWith(SaveStore.Ext) ? r[..^SaveStore.Ext.Length] : r;
        }
        var action = (a.Str("action") ?? "").Trim().ToLowerInvariant();
        if (action == "list") return new ToolResult(SavesTree(store, dir, run, root, Rel).ToJsonString(Opts));
        string? into = a.Str("into") is { Length: > 0 } i ? Under(i) : root;
        try
        {
            switch (action)
            {
                case "rename":
                    if (Under(a.Str("path")) is not { } renamed) return ToolResult.Fail("path is needed: a save or folder under the saves (list).");
                    if (a.Str("name")?.Trim() is not { Length: > 0 } to) return ToolResult.Fail("name is needed.");
                    return Done("renamed to " + Rel(store.Rename(dir, renamed, to)));
                case "delete":
                    if (Under(a.Str("path")) is not { } going || SaveStore.SamePath(going, root)) return ToolResult.Fail("path is needed: a save or folder under the saves (list).");
                    store.TrashSaves(dir, going, Repeatable.UtcNow, run);
                    return Done($"{Rel(going)} went to the trash, kept {SaveStore.TrashDays} days");
                case "new_folder":
                    if (into == null || !Directory.Exists(into)) return ToolResult.Fail("into is a folder under the saves (list).");
                    return Done("made " + Rel(store.MakeFolder(dir, into, a.Str("name")?.Trim() is { Length: > 0 } fn ? fn : "New folder")));
                case "move":
                    if (Under(a.Str("path")) is not { } moving) return ToolResult.Fail("path is needed: a save or folder under the saves (list).");
                    if (into == null || !Directory.Exists(into)) return ToolResult.Fail("into is a folder under the saves (list).");
                    return Done("moved to " + Rel(store.MoveSave(dir, moving, into)));
                case "restore":
                    var trashName = a.Str("name")?.Trim() ?? "";
                    if (store.SavesTrash(dir).FirstOrDefault(x => string.Equals(x.Name, trashName, StringComparison.OrdinalIgnoreCase)) is not { } back)
                        return ToolResult.Fail("name is a trashed save's or folder's name (list shows the trash).");
                    store.Restore(back.Entry, s.Store.Root, Repeatable.UtcNow);
                    return Done($"{back.Name} is back where it was");
                case "new_play":
                    return await NewPlay(a, s, hud, dir, root, Under);
                default:
                    return ToolResult.Fail("action is list, new_play, rename, delete, new_folder, move or restore.");
            }
        }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { return ToolResult.Fail("Not done: " + e.Message); }

        ToolResult Done(string what)
        {
            _a.Log("saves: " + what);
            return new ToolResult(char.ToUpperInvariant(what[0]) + what[1..] + ".");
        }
    }

    static JsonObject SavesTree(SaveStore store, string dir, string run, string root, Func<string, string> rel)
    {
        var plays = store.Runs(dir).ToDictionary(r => Path.GetFullPath(r.Folder), r => r.Id, StringComparer.OrdinalIgnoreCase);
        JsonObject Folder(string path)
        {
            var o = new JsonObject { ["path"] = SaveStore.SamePath(path, root) ? "" : rel(path) };
            if (plays.TryGetValue(Path.GetFullPath(path), out var id)) o["play"] = id == run ? "the play on the table now" : "a play";
            var saves = Directory.GetFiles(path, "*" + SaveStore.Ext).OrderByDescending(File.GetLastWriteTimeUtc);
            o["saves"] = new JsonArray(saves.Select(f => (JsonNode?)JsonValue.Create(Path.GetFileNameWithoutExtension(f))).ToArray());
            var folders = Directory.GetDirectories(path).Where(d => !Path.GetFileName(d).StartsWith('.')).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase);
            o["folders"] = new JsonArray(folders.Select(d => (JsonNode?)Folder(d)).ToArray());
            return o;
        }
        Directory.CreateDirectory(root);
        var tree = Folder(root);
        tree["trash"] = new JsonArray(store.SavesTrash(dir).Select(t => (JsonNode?)JsonValue.Create(t.Name + (t.Folder ? "/" : ""))).ToArray());
        return tree;
    }

    async Task<ToolResult> NewPlay(JsonElement a, Session s, Hud hud, string dir, string root, Func<string?, string?> under)
    {
        if (hud.N.Server is not { } server) return ToolResult.Fail("Only the host makes plays.");
        var store = SaveStore.Default;
        var name = SaveNames.Clean(a.Str("name") ?? "", store.NextRunName(dir));
        var from = (a.Str("from") ?? "start").Trim();
        LoadFrom source;
        if (string.Equals(from, "now", StringComparison.OrdinalIgnoreCase))
        {
            var (state, turns) = (server.State.DeepClone(), server.Turns.ToList());
            source = new LoadFrom("the table", Start: () => (state, turns), PlayName: name);
        }
        else if (string.Equals(from, "start", StringComparison.OrdinalIgnoreCase))
        {
            var (packs, first) = (s.Store, server.Turns.FirstOrDefault());
            source = new LoadFrom("the scenario's start", Start: () => SavesWindow.ScenarioStart(packs, dir, first), PlayName: name);
        }
        else if (under(from) is { } file && File.Exists(file)) source = new LoadFrom(Path.GetFileNameWithoutExtension(file), File: file, PlayName: name);
        else return ToolResult.Fail("from is start, now, or a save's path (list).");
        source = source with { PlaceIn = root };
        var done = new TaskCompletionSource<string?>();
        if (a.Flag("play") == true)
        {
            s.Review(null, null);
            hud.N.Saves.Load(source, problem => done.TrySetResult(problem));
        }
        else hud.N.Saves.MakePlay(source, (_, problem) => done.TrySetResult(problem));
        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(PlayWaitMinutes)));
        if (finished != done.Task) return ToolResult.Fail($"The play was not made in {PlayWaitMinutes} minutes.");
        if (await done.Task is { } problem) return ToolResult.Fail("Not made: " + problem);
        _a.Log($"made the play {name}");
        return new ToolResult(a.Flag("play") == true ? $"{name} is on the table, from {source.Name}." : $"Made {name}, from {source.Name}.");
    }

    async Task<ToolResult> SaveScenarioCopy(JsonElement a)
    {
        if (Table(out var why) is not var (s, _)) return why!;
        var title = a.Str("name")?.Trim() is { Length: > 0 } n ? n : "";
        if (title.Length == 0) return ToolResult.Fail("name is needed.");
        var snapshot = s.Live.DeepClone();
        var store = s.Store;
        var work = Task.Run(() => ScenarioFiles.SaveTable(store, snapshot, title, null));
        var finished = await Task.WhenAny(work, Task.Delay(TimeSpan.FromMinutes(CopyWaitMinutes)));
        if (finished != work) return ToolResult.Fail($"The copy is still being saved after {CopyWaitMinutes} minutes; it carries on in the background.");
        if (work.Exception?.InnerException is { } e) return ToolResult.Fail("Not saved: " + e.Message);
        _a.Log($"saved a copy of the table as the scenario {title}");
        return new ToolResult($"Saved the scenario {title}: {snapshot.Sheets.Count} map sheets, {snapshot.Layer(Layers.Umpire).Count} pieces. host_game opens it after leave_game.");
    }
}
