using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Client.Agent;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.Client.Tests;

public class McpTests
{
    static Mcp Echo() => new(new[]
    {
        new AgentTool("echo", "says it back", Args.Schema(new Arg("text", "string", "what to say", true)), a => Task.FromResult(new ToolResult("said " + a.Str("text")))),
        new AgentTool("boom", "fails", Args.Schema(), _ => throw new InvalidOperationException("broken")),
        new AgentTool("look", "a picture", Args.Schema(), _ => Task.FromResult(new ToolResult("here", false, new[] { new byte[] { 137, 80, 78, 71 } }))),
    }, "1.0", "for tests", new[]
    {
        new AgentPrompt("make-unit", "a unit", new[] { new Arg("unit", "string", "the unit", true) }, a => "make " + a["unit"]),
    });

    static JsonNode Parse(string? s) => JsonNode.Parse(s!)!;

    [Fact]
    public async Task Initialize_answers_the_asked_version_when_known_and_the_latest_otherwise()
    {
        var mcp = Echo();
        var known = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}"""));
        Assert.Equal("2025-03-26", (string?)known["result"]!["protocolVersion"]);
        Assert.Equal("kriegsspiel-table", (string?)known["result"]!["serverInfo"]!["name"]);
        var unknown = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":"a","method":"initialize","params":{"protocolVersion":"1999-01-01"}}"""));
        Assert.Equal(Mcp.Latest, (string?)unknown["result"]!["protocolVersion"]);
        Assert.Equal("a", (string?)unknown["id"]);
    }

    [Fact]
    public async Task Tools_are_listed_with_their_schemas_and_called_by_name()
    {
        var mcp = Echo();
        var list = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""));
        Assert.Equal("echo", (string?)list["result"]!["tools"]![0]!["name"]);
        Assert.Equal("text", (string?)list["result"]!["tools"]![0]!["inputSchema"]!["required"]![0]);
        var call = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}"""));
        Assert.Equal("said hi", (string?)call["result"]!["content"]![0]!["text"]);
        Assert.False((bool)call["result"]!["isError"]!);
    }

    [Fact]
    public async Task Recipes_are_listed_and_filled_in_with_what_was_given()
    {
        var mcp = Echo();
        var init = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize"}"""));
        Assert.NotNull(init["result"]!["capabilities"]!["prompts"]);
        var list = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":2,"method":"prompts/list"}"""));
        Assert.Equal(("make-unit", "unit", true), ((string?)list["result"]!["prompts"]![0]!["name"], (string?)list["result"]!["prompts"]![0]!["arguments"]![0]!["name"], (bool)list["result"]!["prompts"]![0]!["arguments"]![0]!["required"]!));
        var got = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":3,"method":"prompts/get","params":{"name":"make-unit","arguments":{"unit":"hussars"}}}"""));
        Assert.Equal(("user", "make hussars"), ((string?)got["result"]!["messages"]![0]!["role"], (string?)got["result"]!["messages"]![0]!["content"]!["text"]));
        var missing = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":4,"method":"prompts/get","params":{"name":"make-unit"}}"""));
        Assert.Equal(-32602, (int)missing["error"]!["code"]!);
    }

    [Fact]
    public async Task A_picture_comes_back_as_image_content_after_the_text()
    {
        var r = Parse(await Echo().Handle("""{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"look"}}"""));
        var content = r["result"]!["content"]!.AsArray();
        Assert.Equal(("text", "image", "image/png"), ((string?)content[0]!["type"], (string?)content[1]!["type"], (string?)content[1]!["mimeType"]));
        Assert.Equal(Convert.ToBase64String(new byte[] { 137, 80, 78, 71 }), (string?)content[1]!["data"]);
    }

    [Fact]
    public async Task A_failing_tool_is_an_error_result_and_an_unknown_one_a_protocol_error()
    {
        var mcp = Echo();
        var boom = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"boom"}}"""));
        Assert.True((bool)boom["result"]!["isError"]!);
        Assert.Contains("broken", (string?)boom["result"]!["content"]![0]!["text"]);
        var none = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"nope"}}"""));
        Assert.Equal(-32602, (int)none["error"]!["code"]!);
        var method = Parse(await mcp.Handle("""{"jsonrpc":"2.0","id":6,"method":"resources/list"}"""));
        Assert.Equal(-32601, (int)method["error"]!["code"]!);
    }

    [Fact]
    public async Task Notifications_get_no_answer_and_bad_json_a_parse_error()
    {
        var mcp = Echo();
        Assert.Null(await mcp.Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
        Assert.Equal(-32700, (int)Parse(await mcp.Handle("{nope"))["error"]!["code"]!);
        Assert.Equal(-32600, (int)Parse(await mcp.Handle("[]"))["error"]!["code"]!);
    }
}

public sealed class McpServerTests : IDisposable
{
    const string Token = "secret-token";
    readonly McpServer _server;
    readonly List<string> _bodies = new();

    public McpServerTests()
    {
        _server = new McpServer(0, Token, body => { lock (_bodies) _bodies.Add(body); return Task.FromResult<string?>(body.Contains("\"id\"") ? """{"ok":true}""" : null); });
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    async Task<(int code, string body)> Send(string method, string path, string? body, string? token = Token, string? host = null, string? origin = null, int? length = null)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", _server.Port);
        var stream = tcp.GetStream();
        var bytes = Encoding.UTF8.GetBytes(body ?? "");
        var head = new StringBuilder($"{method} {path} HTTP/1.1\r\nHost: {host ?? $"127.0.0.1:{_server.Port}"}\r\n");
        if (token != null) head.Append($"Authorization: Bearer {token}\r\n");
        if (origin != null) head.Append($"Origin: {origin}\r\n");
        if (body != null || length != null) head.Append($"Content-Type: application/json\r\nContent-Length: {length ?? bytes.Length}\r\n");
        head.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        if (length == null) await stream.WriteAsync(bytes);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var all = await reader.ReadToEndAsync();
        var code = int.Parse(all.Split(' ')[1]);
        var split = all.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        return (code, split < 0 ? "" : all[(split + 4)..]);
    }

    [Fact]
    public async Task A_request_with_the_token_is_answered()
    {
        var (code, body) = await Send("POST", "/mcp", """{"jsonrpc":"2.0","id":1,"method":"ping"}""");
        Assert.Equal(200, code);
        Assert.Equal("""{"ok":true}""", body);
        Assert.Equal(1, _server.Calls);
    }

    [Fact]
    public async Task A_notification_is_accepted_with_no_body()
    {
        var (code, body) = await Send("POST", "/mcp", """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        Assert.Equal(202, code);
        Assert.Equal("", body);
    }

    [Fact]
    public async Task No_token_or_a_wrong_one_is_refused_before_the_app_sees_it()
    {
        Assert.Equal(401, (await Send("POST", "/mcp", "{}", token: null)).code);
        Assert.Equal(401, (await Send("POST", "/mcp", "{}", token: "secret-tokem")).code);
        Assert.Empty(_bodies);
    }

    [Fact]
    public async Task Another_host_name_or_a_web_page_origin_is_refused()
    {
        Assert.Equal(403, (await Send("POST", "/mcp", "{}", host: $"evil.example:{_server.Port}")).code);
        Assert.Equal(403, (await Send("POST", "/mcp", "{}", origin: "https://evil.example")).code);
        Assert.Equal(200, (await Send("POST", "/mcp", """{"id":1}""", host: $"localhost:{_server.Port}", origin: $"http://localhost:{_server.Port}")).code);
        Assert.Single(_bodies);
    }

    [Fact]
    public async Task Only_post_to_the_endpoint_is_served()
    {
        Assert.Equal(405, (await Send("GET", "/mcp", null)).code);
        Assert.Equal(404, (await Send("POST", "/other", "{}")).code);
        Assert.Empty(_bodies);
    }

    [Fact]
    public async Task A_body_over_the_limit_is_refused_unread()
    {
        Assert.Equal(400, (await Send("POST", "/mcp", null, length: McpServer.BodyLimit + 1)).code);
        Assert.Empty(_bodies);
    }

    [Fact]
    public void A_second_server_on_a_port_in_use_fails_to_start()
    {
        using var other = new McpServer(_server.Port, Token, _ => Task.FromResult<string?>(null));
        Assert.ThrowsAny<SocketException>(other.Start);
    }
}

public class TableToolsTests
{
    readonly ClientSession _s;
    readonly GameServer _server;
    readonly List<string> _log = new();
    readonly Dictionary<string, AgentTool> _tools;
    bool _studio;

    public TableToolsTests()
    {
        var root = TestFolders.New();
        Directory.CreateDirectory(Path.Combine(root, "user.t"));
        File.WriteAllText(Path.Combine(root, "user.t", "pack.json"), Json.Serialize(new PackManifest
        {
            Id = "user.t", Name = "T",
            Pieces = { new PieceDef { Id = "user.t.bn", Name = "Battalion", Size = new Size2(2, 1), Counters = { new CounterDef { Name = "Strength", Max = 12, Default = 10 } }, TextLines = { new TextLineDef { Name = "Commander" } }, States = { new PieceStateDef { Name = "Formed" }, new PieceStateDef { Name = "Disordered" } } } },
        }));
        var store = new PackageStore(root);
        var g = new GameState { Name = "Test", Packs = { "user.t" } };
        g.Layers[Layers.Umpire] = new List<PieceInstance>
        {
            new() { Id = "p-1", Def = "pack.gun", Label = "Gun A", Pos = new Vec2(1, 2) },
            new() { Id = "p-2", Def = "pack.horse", Label = "Hussars" },
        };
        _server = new GameServer(g, store);
        _s = new ClientSession(store);
        _s.Send = c => Deliver(_server.Handle(1, c));
        _s.Request = m => Deliver(_server.Request(1, m));
        Deliver(_server.Join(1, new JoinRequest { Name = "Host" }));
        _tools = new TableTools(() => _s, () => _studio, _ => Task.CompletedTask, _log.Add).Tools().ToDictionary(t => t.Name);
    }

    void Deliver(List<Outgoing> outs)
    {
        foreach (var o in outs)
            switch (o.Message)
            {
                case CommandMessage cm: _s.OnCommand(cm.Command); break;
                case JoinResult jr: _s.OnJoined(jr); break;
                case ScriptConsole or ScriptList: _s.OnScriptMessage(o.Message); break;
            }
        if (_server.ConsoleOut() is { Count: > 0 } said) Deliver(said);
    }

    async Task<ToolResult> Call(string tool, object args) => await _tools[tool].Run(JsonSerializer.SerializeToElement(args));

    [Fact]
    public async Task Pieces_are_listed_and_filtered()
    {
        var all = await Call("list_pieces", new { });
        Assert.StartsWith("2 pieces", all.Text);
        var one = await Call("list_pieces", new { filter = "huss" });
        Assert.StartsWith("1 pieces", one.Text);
        Assert.Contains("\"p-2\"", one.Text);
    }

    [Fact]
    public async Task A_script_written_runs_and_its_prints_come_back()
    {
        var r = await Call("write_script", new { id = "p-1", text = "function on_load() print('ready ' .. self.name) end\n" });
        Assert.False(r.IsError, r.Text);
        Assert.Contains("ready", r.Text);
        Assert.Equal("function on_load() print('ready ' .. self.name) end\n", _s.View.Find(Layers.Umpire, "p-1")!.Script);
        Assert.Contains(_log, l => l.Contains("Gun A"));
        Assert.Contains(_s.MyUndoable, _ => true);
        var read = await Call("read_script", new { id = "p-1" });
        Assert.Contains("own script", read.Text);
        Assert.Contains("print('ready '", read.Text);
    }

    [Fact]
    public async Task A_hostile_script_is_refused_and_not_sent()
    {
        var r = await Call("write_script", new { id = "p-1", text = "WebRequest.get('http://x', function() end)" });
        Assert.True(r.IsError);
        Assert.Contains("calls the web", r.Text);
        Assert.Null(_s.View.Find(Layers.Umpire, "p-1")!.Script);
    }

    [Fact]
    public async Task A_script_error_is_reported_with_its_line()
    {
        var r = await Call("write_script", new { id = "p-2", text = "function on_load()\n  local x = nil\n  x.y = 1\nend\n" });
        Assert.Contains("error", r.Text);
        Assert.Contains("line 3", r.Text);
        var console = await Call("read_console", new { errors_only = true });
        Assert.Contains("Hussars", console.Text);
    }

    [Fact]
    public async Task Lua_runs_in_the_global_script_and_returns_its_value()
    {
        var r = await Call("run_lua", new { code = "return 6 * 7" });
        Assert.False(r.IsError, r.Text);
        Assert.Contains("= 42", r.Text);
    }

    [Fact]
    public async Task The_console_reads_from_a_seq_on()
    {
        await Call("run_lua", new { code = "print('one')" });
        var first = await Call("read_console", new { });
        var last = long.Parse(first.Text.Split(' ')[2].TrimEnd(';'));
        await Call("run_lua", new { code = "print('two')" });
        var fresh = await Call("read_console", new { since = last });
        Assert.Contains("two", fresh.Text);
        Assert.DoesNotContain("one", fresh.Text);
    }

    [Fact]
    public async Task In_the_piece_studio_writing_a_piece_s_script_points_at_the_studio_tool()
    {
        _studio = true;
        var r = await Call("write_script", new { id = "p-1", text = "x = 1" });
        Assert.True(r.IsError);
        Assert.Contains("studio_write_script", r.Text);
    }

    [Fact]
    public async Task A_kind_from_the_game_s_packs_is_placed_set_moved_taken_off_and_put_back_by_undo()
    {
        Assert.Contains("Battalion", (await Call("list_kinds", new { })).Text);
        var placed = await Call("place_piece", new { kind = "user.t.bn", x = 2.5, z = 3, turn = 0, label = "I/IR 14" });
        Assert.False(placed.IsError, placed.Text);
        Assert.Contains("at 2.5, 3", placed.Text);
        var id = _s.View.Layer(Layers.Umpire).Single(p => p.Def == "user.t.bn").Id;
        var set = await Call("set_piece_values", new { id, state = 2, counters = new Dictionary<string, int> { ["strength"] = 40 }, text = new Dictionary<string, string> { ["Commander"] = "Maj. Krafft" } });
        Assert.False(set.IsError, set.Text);
        var p = _s.View.Find(Layers.Umpire, id)!;
        Assert.Equal((1, 12, "Maj. Krafft"), (p.State, p.Counters["Strength"], p.Text["Commander"]));
        Assert.True((await Call("set_piece_values", new { id, state = 3 })).IsError);
        await Call("move_piece", new { id, x = 4.0, turn = 30.0 });
        Assert.Equal((4.0, 3.0, 30.0), (_s.View.Find(Layers.Umpire, id)!.Pos.X, _s.View.Find(Layers.Umpire, id)!.Pos.Z, _s.View.Find(Layers.Umpire, id)!.RotDeg));
        await Call("delete_piece", new { id });
        Assert.Null(_s.View.Find(Layers.Umpire, id));
        await Call("undo", new { });
        Assert.NotNull(_s.View.Find(Layers.Umpire, id));
        Assert.Contains(_log, l => l.StartsWith("placed Battalion"));
    }

    [Fact]
    public async Task Many_pieces_go_down_in_a_formation_facing_the_way_asked()
    {
        var r = await Call("place_pieces", new { kind = "user.t.bn", count = 4, x = 10.0, z = 5.0, formation = "line", facing = 90.0, labels = new[] { "A", "B", "C", "D" } });
        Assert.False(r.IsError, r.Text);
        var placed = _s.View.Layer(Layers.Umpire).Where(p => p.Def == "user.t.bn").ToList();
        Assert.Equal(4, placed.Count);
        Assert.All(placed, p => Assert.Equal(10.0, p.Pos.X, 3));
        Assert.Equal(new[] { "A", "B", "C", "D" }, placed.OrderBy(p => p.Pos.Z).Select(p => p.Label));
        Assert.All(placed, p => Assert.Equal(90.0, p.RotDeg));
    }

    [Fact]
    public async Task A_button_and_a_menu_row_a_script_made_are_pressed_as_a_player_does()
    {
        await Call("write_script", new { id = "p-1", text = "function on_load()\n  self.button{ face = 'front', x = 0, z = 0, w = 0.4, h = 0.2, label = 'Fire', on_click = 'fire' }\n  self.menu_add('Rally', 'rally')\nend\nfunction fire(who) print('bang') end\nfunction rally(who) print('rallied') end\n" });
        var list = await Call("list_buttons", new { id = "p-1" });
        Assert.Contains("Fire", list.Text);
        Assert.Contains("Rally", list.Text);
        var fired = await Call("press_button", new { id = "p-1", label = "fire" });
        Assert.False(fired.IsError, fired.Text);
        Assert.Contains("bang", fired.Text);
        var rallied = await Call("pick_menu_row", new { id = "p-1", label = "Rally" });
        Assert.Contains("rallied", rallied.Text);
        Assert.True((await Call("press_button", new { id = "p-1", label = "nope" })).IsError);
    }

    [Fact]
    public async Task A_piece_is_locked_tinted_sized_and_turned_over()
    {
        var r = await Call("set_piece_options", new { id = "p-1", locked = true, tint = "#336699", scale = 1.5, face_down = true });
        Assert.False(r.IsError, r.Text);
        var p = _s.View.Find(Layers.Umpire, "p-1")!;
        Assert.Equal((true, "#336699", 1.5, true), (p.Locked, p.TintOverride, p.Scale, p.FaceDown));
    }

    [Fact]
    public async Task The_turn_ends_and_the_clock_is_set()
    {
        var set = await Call("set_clock", new { turn = 3, time = "1813-10-16T08:00", minutes_per_turn = 30 });
        Assert.False(set.IsError, set.Text);
        Assert.Equal(("1813-10-16T08:00", 30), (_s.View.Clock.GameTime, _s.View.Clock.IncrementMinutes));
        var ended = await Call("end_turn", new { });
        Assert.False(ended.IsError, ended.Text);
        Assert.Equal(4, _s.View.Clock.Turn);
        Assert.True((await Call("set_clock", new { time = "tomorrow" })).IsError);
    }

    [Fact]
    public async Task Dice_come_back_with_their_results_and_the_log_gets_a_line()
    {
        var r = await Call("roll_dice", new { count = 3, sides = 6, label = "volley" });
        Assert.False(r.IsError, r.Text);
        Assert.StartsWith("3d6:", r.Text);
        Assert.False((await Call("say", new { text = "The guns open fire." })).IsError);
    }

    [Fact]
    public async Task Lines_zones_light_wind_and_the_map_scale_are_set()
    {
        Assert.False((await Call("draw", new { points = new[] { new[] { 0.0, 0.0 }, new[] { 5.0, 5.0 } }, arrow = true })).IsError);
        Assert.Equal(2, _s.View.DrawLayer(Layers.Umpire).Count);
        Assert.False((await Call("set_zone", new { name = "North", owner = "Ann", polygon = new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 10.0, 10.0 } } })).IsError);
        Assert.Equal("North", Assert.Single(_s.View.Zones).Name);
        Assert.False((await Call("set_light", new { sun = 0.2, wind_from = 90.0, wind_strength = 3.0 })).IsError);
        Assert.Equal((0.2, 90.0, 3.0), (_s.View.Lighting.SunEnergy, _s.View.Wind.From, _s.View.Wind.Strength));
        Assert.False((await Call("set_map_scale", new { metres_per_unit = 50.0, units = "pace" })).IsError);
        Assert.Equal((1 / 50.0, "pace"), (_s.View.Calibration.UnitsPerMeter!.Value, _s.View.Calibration.Units));
    }

    [Fact]
    public async Task A_grid_is_laid_a_zone_removed_and_a_piece_noted_and_turned_into_another_kind()
    {
        Assert.False((await Call("set_grid", new { kind = "square", cell = 25.0, divisions = 5 })).IsError);
        Assert.Equal(("square", 25.0, 5), (_s.View.Grid!.Kind, _s.View.Grid.Pitch, _s.View.Grid.Subdivisions));
        Assert.True((await Call("set_grid", new { kind = "octagon" })).IsError);
        Assert.True((await Call("set_grid", new { kind = "hex", cell = 0.0 })).IsError);
        Assert.Equal("square", _s.View.Grid!.Kind);
        await Call("set_zone", new { name = "North", owner = "Ann", polygon = new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 10.0, 10.0 } } });
        Assert.False((await Call("set_zone", new { name = "north", remove = true })).IsError);
        Assert.Empty(_s.View.Zones);
        Assert.True((await Call("set_zone", new { name = "North", remove = true })).IsError);
        var set = await Call("set_piece_values", new { id = "p-1", note = "Two guns short", kind = "user.t.bn" });
        Assert.False(set.IsError, set.Text);
        var p = _s.View.Find(Layers.Umpire, "p-1")!;
        Assert.Equal(("Two guns short", "user.t.bn", 1.0, 2.0), (p.Note, p.Def, p.Pos.X, p.Pos.Z));
        Assert.True((await Call("set_piece_values", new { id = "p-1", kind = "user.t.nothing" })).IsError);
    }

    [Fact]
    public async Task Drawn_lines_are_listed_near_a_point_and_erased_by_id_or_by_place()
    {
        var arrow = await Call("draw", new { points = new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 } }, arrow = true });
        Assert.False(arrow.IsError, arrow.Text);
        await Call("draw", new { points = new[] { new[] { 50.0, 50.0 }, new[] { 60.0, 50.0 } } });
        var ids = _s.View.DrawLayer(Layers.Umpire).Select(x => x.Id).ToList();
        Assert.Equal(3, ids.Count);
        Assert.Contains(ids[0], arrow.Text);
        var near = await Call("list_drawings", new { near = new[] { 55.0, 51.0 }, radius = 2.0 });
        Assert.Contains(ids[2], near.Text);
        Assert.DoesNotContain(ids[0], near.Text);
        Assert.Contains("No drawn line", (await Call("list_drawings", new { near = new[] { -40.0, -40.0 } })).Text);
        var byPlace = await Call("erase_drawings", new { near = new[] { 10.0, 0.0 }, radius = 0.5 });
        Assert.False(byPlace.IsError, byPlace.Text);
        Assert.Equal(new[] { ids[2] }, _s.View.DrawLayer(Layers.Umpire).Select(x => x.Id));
        var byId = await Call("erase_drawings", new { ids = new[] { ids[2], "d-nope" } });
        Assert.Contains("Not found: d-nope", byId.Text);
        Assert.Empty(_s.View.DrawLayer(Layers.Umpire));
        Assert.True((await Call("erase_drawings", new { })).IsError);
    }

    [Fact]
    public async Task The_briefing_is_written_read_back_and_cleared()
    {
        Assert.Contains("No briefing", (await Call("read_briefing", new { })).Text);
        var set = await Call("set_briefing", new { text = "# 18:00\r\nBülow holds Plancenoit." });
        Assert.False(set.IsError, set.Text);
        Assert.Equal("# 18:00\nBülow holds Plancenoit.", _s.View.Briefing);
        Assert.Contains("Plancenoit", (await Call("read_briefing", new { })).Text);
        await Call("set_briefing", new { text = "" });
        Assert.Null(_s.View.Briefing);
        _studio = true;
        Assert.True((await Call("set_briefing", new { text = "x" })).IsError);
    }

    [Fact]
    public async Task A_kind_from_a_pack_the_game_does_not_use_is_refused()
    {
        var r = await Call("place_piece", new { kind = "pack.nowhere", x = 0, z = 0 });
        Assert.True(r.IsError);
        Assert.Contains("list_kinds", r.Text);
    }

    [Fact]
    public async Task In_the_piece_studio_the_table_tools_do_not_change_the_table()
    {
        _studio = true;
        var r = await Call("place_piece", new { kind = "user.t.bn", x = 0, z = 0 });
        Assert.True(r.IsError);
        Assert.Contains("studio_", r.Text);
    }

    [Fact]
    public async Task With_nothing_open_every_tool_says_so()
    {
        var none = new TableTools(() => null, () => false, _ => Task.CompletedTask, _ => { }).Tools().First(t => t.Name == "list_pieces");
        var r = await none.Run(JsonSerializer.SerializeToElement(new { }));
        Assert.True(r.IsError);
        Assert.Contains("No game is open", r.Text);
    }
}
