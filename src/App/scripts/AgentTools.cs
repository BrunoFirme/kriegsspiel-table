using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Client.Scripting;
using KTable.Client.Studio;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting;

namespace KTable.App;

sealed class AppTools
{
    const int ReferenceLimit = 60_000;
    readonly AgentEndpoint _a;

    public AppTools(AgentEndpoint a) { _a = a; }

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("status",
            "What is open (the title screen, a game, or the piece studio and its pack), this seat's role, and the folder where scripts can also be edited as files.",
            Args.Schema(), _ => Task.FromResult(Status()));
        yield return new AgentTool("api_reference",
            "The scripting API reference: every call, field and event, with parameters, returns and examples. Pass find to get only the sections that mention a word (on_load, self.button, game.say…).",
            Args.Schema(new Arg("find", "string", "a word; only the sections that contain it")), a => Task.FromResult(Reference(a.Str("find"))));
        yield return new AgentTool("snapshot",
            "A picture of the app's window: the table, or the piece studio. At the table, focus (a piece) or x and z (a map point) aim the camera, distance sets how much of the table it takes in, angle how high it looks from (90 straight down, about 10 from table height) and heading which way it looks. The view of the person at this PC goes back as it was after the picture.",
            Args.Schema(new Arg("focus", "string", "a piece's id at the table"), new Arg("x", "number", "a map point to look at, with z"), new Arg("z", "number", "a map point to look at, with x"),
                new Arg("distance", "number", "map units across the view (default: three times the piece's size, or as the view is)"),
                new Arg("angle", "number", $"degrees above the table, {MinAngle}–90 (default as the view is)"), new Arg("heading", "number", "compass degrees it looks toward: 0 north, 90 east (default as the view is)")), Snapshot);
        yield return new AgentTool("show_view",
            "Turns the view of the person at this PC to a piece or a map point, aimed as snapshot aims, and leaves it there. Use it when the person asks to be shown something.",
            Args.Schema(new Arg("focus", "string", "a piece's id at the table"), new Arg("x", "number", "a map point to look at, with z"), new Arg("z", "number", "a map point to look at, with x"),
                new Arg("distance", "number", "map units across the view (default: three times the piece's size, or as the view is)"),
                new Arg("angle", "number", $"degrees above the table, {MinAngle}–90 (default as the view is)"), new Arg("heading", "number", "compass degrees it looks toward: 0 north, 90 east (default as the view is)")), ShowView);
        yield return new AgentTool("list_scenarios",
            "The scenarios on this PC that host_game can open: id and name.",
            Args.Schema(), _ => Task.FromResult(ListScenarios()));
        yield return new AgentTool("open_studio",
            "Opens the piece studio on a pack to make or change its pieces, or on a new pack. Works from the title screen; close_studio or leave_game first if something else is open.",
            Args.Schema(new Arg("pack", "string", "a pack's id (list_packs)"), new Arg("new_pack", "string", "a name for a new pack of your own")), OpenStudio);
        yield return new AgentTool("close_studio",
            "Saves and closes the piece studio, back to the title screen.",
            Args.Schema(), _ => CloseStudio());
        yield return new AgentTool("host_game",
            "Hosts a game: a scenario (carrying on where it was left), or a new blank one. Works from the title screen.",
            Args.Schema(new Arg("scenario", "string", "a scenario's id (list_scenarios)"), new Arg("new_scenario", "string", "a name for a new blank scenario"),
                new Arg("from_start", "boolean", "start the scenario over instead of carrying on")), HostGame);
        yield return new AgentTool("leave_game",
            "Saves the game and leaves the table, back to the title screen (the others at the table are disconnected).",
            Args.Schema(), _ => Task.FromResult(_a.LeaveGame() is { } why ? ToolResult.Fail(why) : new ToolResult("Leaving: the game is saved and the app goes back to the title screen in a moment. Call status before the next step.")));
        yield return new AgentTool("list_packs",
            "The packs installed on this PC: id, name, how many kinds, and whether the open game uses it.",
            Args.Schema(), _ => Task.FromResult(ListPacks()));
        yield return new AgentTool("add_pack",
            "Brings an installed pack into the open game so its kinds can be placed (as the Pieces drawer does; the others at the table receive it).",
            Args.Schema(new Arg("id", "string", "the pack's id (list_packs)", true)), AddPack);
        yield return new AgentTool("check_scripts",
            "Runs every script of the open scenario (host only) or of the piece studio's pack on a copy away from this table: on_load, every button, three seconds of timers, hotkeys and menu rows, on_save. Returns the report.",
            Args.Schema(), _ => Check());
    }

    const int MinAngle = 7;
    const float FrameMargin = 1.1f;

    async Task<ToolResult> Snapshot(JsonElement a)
    {
        if (_a.InStudio || _a.TableHud() is not { } hud || _a.TableSession() is not { } s) return await Shot();
        var rig = hud.T.Rig;
        var before = rig.Now;
        if (Aim(a, s, before, out var why) is not { } view) return why!;
        if (view == before) return await Shot();
        rig.Show(view);
        await _a.Drawn(AgentEndpoint.SettleFrames);
        try { return await Shot(); }
        finally { rig.Show(before); }
    }

    Task<ToolResult> ShowView(JsonElement a)
    {
        if (_a.InStudio || _a.TableHud() is not { } hud || _a.TableSession() is not { } s) return Task.FromResult(ToolResult.Fail("No game is open: host_game first."));
        var rig = hud.T.Rig;
        if (Aim(a, s, rig.Now, out var why) is not { } view) return Task.FromResult(why!);
        rig.Show(view);
        _a.Log("turned the view");
        return Task.FromResult(new ToolResult("The view of the person at this PC now looks there; snapshot shows it."));
    }

    static CameraRig.View? Aim(JsonElement a, Session s, CameraRig.View from, out ToolResult? why)
    {
        why = null;
        var asked = a.Num("distance");
        var view = from;
        if (a.Str("focus") is { Length: > 0 } id)
        {
            if (s.View.Find(Layers.Umpire, id) is not { } p) { why = ToolResult.Fail($"No piece {id} on this table."); return null; }
            float span = asked is > 0 ? (float)asked.Value : (float)Math.Max(4, (s.Def(p.Def) is { } kind ? Math.Max(kind.Size.W, kind.Size.H) : 1) * 3);
            view = view with { Target = Coords.ToWorld(p.Pos), Distance = span * FrameMargin };
        }
        else if (a.Num("x") is { } x && a.Num("z") is { } z) view = view with { Target = Coords.ToWorld(new Vec2(x, z)), Distance = (float)(asked is > 0 ? asked.Value : 20) * FrameMargin };
        else if (asked is > 0) view = view with { Distance = (float)asked.Value * FrameMargin };
        if (a.Num("angle") is { } angle)
        {
            var deg = Math.Clamp(angle, MinAngle, 90);
            view = deg >= 89.5 ? view with { TopDown = true } : view with { TopDown = false, Pitch = -(float)(deg * Math.PI / 180) };
        }
        if (a.Num("heading") is { } heading) view = view with { Yaw = (float)(Math.PI - heading * Math.PI / 180) };
        return view;
    }

    async Task<ToolResult> Shot()
    {
        var shot = await _a.Snapshot();
        return shot == null ? ToolResult.Fail("No picture: the window is not drawing (minimised, or a run without a screen).") : new ToolResult("The window now.", false, new[] { shot });
    }

    static ToolResult ListScenarios()
    {
        var store = new KTable.Core.PackageStore(KTable.Core.PackageStore.DefaultRoot);
        var rows = new JsonArray();
        foreach (var (id, path) in store.ListScenarios())
            rows.Add(new JsonObject { ["id"] = id, ["name"] = KTable.Core.Json.PeekString(path, "name") ?? id });
        return new ToolResult(rows.Count == 0 ? "No scenarios yet: host_game with new_scenario makes one." : rows.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    async Task<ToolResult> OpenStudio(JsonElement a)
    {
        if (_a.InStudio && _a.Studio is { } open)
        {
            if (a.Str("pack") is { } same && same == open.Pack.Id) return new ToolResult($"The studio is already open on {open.Pack.Name}.");
            return ToolResult.Fail($"The studio is open on {open.Pack.Name}: close_studio first.");
        }
        var store = new KTable.Core.PackageStore(KTable.Core.PackageStore.DefaultRoot);
        string id;
        if (a.Str("new_pack") is { Length: > 0 } name) id = PackEdit.CreatePack(store, name.Trim(), string.IsNullOrWhiteSpace(_a.PlayerName) ? TitleScreen.NoName : _a.PlayerName);
        else if (a.Str("pack") is { Length: > 0 } p)
        {
            if (store.Pack(p) is not { } found) return ToolResult.Fail($"No pack {p} on this PC (list_packs).");
            if (StandardPacks.IsStandard(p))
                return ToolResult.Fail($"{found.Name} comes with the app and stays as it is: open a pack of your own (or new_pack) and copy its pieces in with studio_duplicate_piece.");
            id = p;
        }
        else return ToolResult.Fail("Give pack (an id) or new_pack (a name).");
        if (_a.OpenStudio(id) is { } why) return ToolResult.Fail(why);
        for (int i = 0; i < 600 && !_a.InStudio; i++) await _a.Delay(0.1);
        if (!_a.InStudio) return ToolResult.Fail("The studio did not open in a minute (the title screen may say why).");
        await _a.Delay(0.5);
        _a.Log($"opened the piece studio on {_a.Studio!.Pack.Name}");
        return new ToolResult($"The studio is open on {_a.Studio.Pack.Name} ({id}): {_a.Studio.Pack.Pieces.Count} kinds.");
    }

    async Task<ToolResult> CloseStudio()
    {
        if (_a.CloseStudio() is { } why) return ToolResult.Fail(why);
        for (int i = 0; i < 300 && _a.InStudio; i++) await _a.Delay(0.1);
        return _a.InStudio ? ToolResult.Fail("The studio is still open: it is asking the person about unsaved work.") : new ToolResult("The studio is closed and its pack saved.");
    }

    async Task<ToolResult> HostGame(JsonElement a)
    {
        if (_a.TableSession() is { } now) return ToolResult.Fail($"A game is open ({now.View.Name}): leave_game first.");
        var store = new KTable.Core.PackageStore(KTable.Core.PackageStore.DefaultRoot);
        string dir;
        bool resume = !(a.Flag("from_start") ?? false);
        if (a.Str("new_scenario") is { Length: > 0 } name) { dir = ScenarioFiles.CreateBlank(store, name.Trim(), "1812-07-22T06:00", 15); resume = false; }
        else if (a.Str("scenario") is { Length: > 0 } sc) { if (!store.ListScenarios().Any(x => x.id == sc)) return ToolResult.Fail($"No scenario {sc} (list_scenarios)."); dir = sc; }
        else return ToolResult.Fail("Give scenario (an id) or new_scenario (a name).");
        if (_a.Host(dir, resume) is { } why) return ToolResult.Fail(why);
        for (int i = 0; i < 1800 && _a.TableSession() == null; i++) await _a.Delay(0.1);
        if (_a.TableSession() is not { } s) return ToolResult.Fail("The game did not open in three minutes (the title screen may say why).");
        await _a.Delay(0.5);
        _a.Log($"hosted {s.View.Name}");
        return new ToolResult($"Hosting {s.View.Name} ({dir}), turn {s.View.Clock.Turn}, {s.View.Layers.Values.Sum(l => l.Count)} pieces. Packs in it: {string.Join(", ", s.View.Packs)}.");
    }

    ToolResult ListPacks()
    {
        var store = _a.Session?.Store ?? new KTable.Core.PackageStore(KTable.Core.PackageStore.DefaultRoot);
        var inGame = _a.InStudio ? new List<string>() : _a.TableSession()?.View.Packs ?? new List<string>();
        var rows = new JsonArray();
        foreach (var id in store.ListPackIds().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            if (store.Pack(id) is { } pack) rows.Add(new JsonObject
            {
                ["id"] = id, ["name"] = pack.Name, ["kinds"] = pack.Pieces.Count, ["inGame"] = inGame.Contains(id),
                ["authors"] = pack.Source.Authors.Count > 0 ? string.Join(", ", pack.Source.Authors) : null, ["description"] = pack.Description,
            });
        return new ToolResult(rows.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    async Task<ToolResult> AddPack(JsonElement a)
    {
        if (_a.InStudio || _a.TableSession() is not { } s || _a.TableHud() is not { } hud) return ToolResult.Fail("No game is open.");
        if (!s.IsUmpire || s.Reviewing) return ToolResult.Fail("Only umpires add packs, and not while a past turn is shown.");
        var id = a.Str("id") ?? "";
        if (s.Store.Pack(id) is not { } pack) return ToolResult.Fail($"No pack {id} on this PC (list_packs lists them).");
        if (s.View.Packs.Contains(id)) return new ToolResult($"{pack.Name} is already in the game.");
        hud.N.SharePack(id);
        for (int i = 0; i < 80 && !s.View.Packs.Contains(id); i++) await _a.Delay(0.05);
        if (!s.View.Packs.Contains(id)) return ToolResult.Fail($"Sent, but {pack.Name} was not in the game after 4 seconds.");
        _a.Log($"brought the pack {pack.Name} into the game");
        return new ToolResult($"{pack.Name} is in the game: {pack.Pieces.Count} kinds (list_kinds).");
    }

    ToolResult Status()
    {
        var o = new JsonObject();
        if (_a.InStudio && _a.Studio is { } st)
        {
            o["open"] = "piece studio";
            o["pack"] = st.Pack.Name;
            o["packId"] = st.Pack.Id;
            o["pieces"] = st.Pack.Pieces.Count;
            o["selected"] = st.Sel?.Id;
            o["view"] = st.View;
            o["scriptsFolder"] = ScriptFileWatch.PackFolder(st.Pack.Id);
        }
        else if (_a.TableSession() is { } s)
        {
            o["open"] = "game";
            o["game"] = s.View.Name;
            o["role"] = s.Me.Role.ToString();
            o["turn"] = s.View.Clock.Turn;
            o["pieces"] = s.View.Layers.Values.Sum(l => l.Count);
            o["reviewingPastTurn"] = s.Reviewing;
            o["briefing"] = s.View.Briefing is { Length: > 0 } b ? $"{b.Length} characters (read_briefing)" : "none yet (set_briefing)";
            o["scriptsFolder"] = ScriptFileWatch.TableFolder(s);
        }
        else o["open"] = "title screen: host or join a game, or open a pack in the piece studio";
        if (KTable.Client.Clippings.Held is { Pieces.Count: > 0 } clip)
            o["clipboard"] = $"{clip.Pieces.Count} piece{(clip.Pieces.Count > 1 ? "s" : "")} copied{(clip.From is { Length: > 0 } was ? " from " + was : "")} (paste_pieces)";
        o["scriptsRoot"] = ScriptFiles.Root;
        return new ToolResult(o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static ToolResult Reference(string? find)
    {
        var md = ApiDoc.Markdown();
        if (string.IsNullOrWhiteSpace(find)) return new ToolResult(Cap(md));
        var sections = new List<string>();
        var current = new StringBuilder();
        foreach (var line in md.Split('\n'))
        {
            if (line.StartsWith('#') && current.Length > 0) { sections.Add(current.ToString()); current.Clear(); }
            current.Append(line).Append('\n');
        }
        if (current.Length > 0) sections.Add(current.ToString());
        var hits = sections.Where(s => s.Contains(find.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count == 0) return new ToolResult($"Nothing in the reference mentions \"{find}\". Leave find out for the whole reference.");
        return new ToolResult(Cap(string.Join("\n", hits)));
    }

    static string Cap(string s) => s.Length <= ReferenceLimit ? s : s[..ReferenceLimit] + "\n… (cut: pass find to narrow it)";

    async Task<ToolResult> Check()
    {
        string[] args;
        if (_a.InStudio && _a.Studio is { } st)
        {
            if (!st.FlushPack()) return ToolResult.Fail("The pack could not be saved, so its scripts were not checked.");
            args = new[] { "check-scripts", st.Store.Root, st.Pack.Id };
        }
        else if (_a.TableSession() is { } s)
        {
            if (!s.IsHost || s.ScenarioDir is not { } dir) return ToolResult.Fail("Only the host checks the scenario's scripts, and only for a game hosted from a scenario.");
            args = new[] { "check-scripts", s.Store.Root, Path.Combine(s.Store.Root, "scenarios", dir, "scenario.json") };
        }
        else return ToolResult.Fail("Nothing is open to check.");
        var lines = new StringBuilder();
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConverterTool.Run(args, line => { lock (lines) lines.Append(line).Append('\n'); }, code => done.TrySetResult(code));
        var finished = await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromMinutes(3)));
        string text;
        lock (lines) text = lines.ToString();
        if (finished != done.Task) return ToolResult.Fail("The check was still running after three minutes:\n" + Cap(text));
        var code = await done.Task;
        return new ToolResult((code == 0 ? "Done.\n" : $"Finished with problems (exit {code}).\n") + Cap(text), code != 0);
    }
}
