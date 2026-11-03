using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using KTable.Core;

namespace KTable.Converter.Tests;

public sealed class AgentBridgeTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ktable-bridge-tests", Guid.NewGuid().ToString("N"));
    readonly List<(string auth, string body)> _posts = new();
    readonly List<AgentBridge.Launch> _launched = new();
    bool _gameUp = true;

    public AgentBridgeTests() { Directory.CreateDirectory(_dir); }

    public void Dispose() => BestEffort.DeleteFolder(_dir);

    sealed class Fake : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, string, HttpResponseMessage> _answer;
        public Fake(Func<HttpRequestMessage, string, HttpResponseMessage> answer) { _answer = answer; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken stop) => _answer(r, await r.Content!.ReadAsStringAsync(stop));
    }

    AgentBridge Bridge(bool startsGame = true) => new(_dir, new Fake((r, body) =>
    {
        if (!_gameUp) throw new HttpRequestException("refused");
        lock (_posts) _posts.Add((r.Headers.Authorization?.ToString() ?? "", body));
        if (!body.Contains("\"id\"")) return new HttpResponseMessage(HttpStatusCode.Accepted);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":{"from":"game"}}""") };
    }), l => { _launched.Add(l); if (startsGame) { Live(); _gameUp = true; } return true; }, TimeSpan.FromSeconds(3));

    void Live() => File.WriteAllText(Path.Combine(_dir, AgentBridge.LiveFile), """{"url":"http://127.0.0.1:7796/mcp","token":"abc"}""");

    void Cache(string exe = "", int? pid = null, string? process = null) => File.WriteAllText(Path.Combine(_dir, AgentBridge.CacheFile), new JsonObject
    {
        ["version"] = "1.2", ["instructions"] = "use status first",
        ["tools"] = new JsonArray(new JsonObject { ["name"] = "status" }), ["prompts"] = new JsonArray(new JsonObject { ["name"] = "make-unit" }),
        ["launch"] = new JsonObject { ["exe"] = exe, ["args"] = new JsonArray("--path", "app"), ["cwd"] = _dir },
        ["pid"] = pid, ["process"] = process,
    }.ToJsonString());

    [Fact]
    public async Task With_the_game_open_every_message_goes_to_it_with_its_token()
    {
        Live();
        var answer = await Bridge().Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        Assert.Contains("\"from\":\"game\"", answer);
        Assert.Equal("Bearer abc", Assert.Single(_posts).auth);
        Assert.Null(await Bridge().Handle("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""));
    }

    [Fact]
    public async Task With_the_game_closed_the_handshake_and_lists_come_from_its_last_run()
    {
        Cache();
        var b = Bridge();
        var init = JsonNode.Parse((await b.Handle("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}"""))!)!;
        Assert.Equal(("2025-03-26", "use status first"), ((string?)init["result"]!["protocolVersion"], (string?)init["result"]!["instructions"]));
        var tools = JsonNode.Parse((await b.Handle("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}"""))!)!;
        Assert.Equal("status", (string?)tools["result"]!["tools"]![0]!["name"]);
        var prompts = JsonNode.Parse((await b.Handle("""{"jsonrpc":"2.0","id":3,"method":"prompts/list"}"""))!)!;
        Assert.Equal("make-unit", (string?)prompts["result"]!["prompts"]![0]!["name"]);
        Assert.Empty(_launched);
    }

    [Fact]
    public async Task A_tool_call_with_the_game_closed_opens_it_with_the_agent_on_and_then_goes_through()
    {
        var exe = Path.Combine(_dir, "game.exe");
        File.WriteAllText(exe, "");
        Cache(exe);
        _gameUp = false;
        var answer = await Bridge().Handle("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"status"}}""");
        var l = Assert.Single(_launched);
        Assert.Equal(exe, l.Exe);
        Assert.Equal(new[] { "--path", "app", "--", "--agent" }, l.Args);
        Assert.Contains("\"from\":\"game\"", answer);
    }

    [Fact]
    public async Task A_game_that_is_open_with_the_agent_switched_off_is_not_opened_twice()
    {
        using var me = Process.GetCurrentProcess();
        Cache(Path.Combine(_dir, "game.exe"), me.Id, me.ProcessName);
        var answer = await Bridge().Handle("""{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"status"}}""");
        Assert.Empty(_launched);
        Assert.Contains("switched off", answer);
        Assert.Contains("\"isError\":true", answer);
    }

    [Fact]
    public async Task With_nothing_known_a_call_says_to_open_the_game()
    {
        var answer = await Bridge().Handle("""{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"status"}}""");
        Assert.Contains("not open", answer);
        Assert.Contains("-32700", (await Bridge().Handle("{nope"))!);
    }

    [Fact]
    public async Task Lines_in_are_answered_one_line_out_each()
    {
        Live();
        var input = new StringReader("""{"jsonrpc":"2.0","id":1,"method":"ping"}""" + "\n\n" + """{"jsonrpc":"2.0","method":"notifications/initialized"}""" + "\n");
        var output = new StringWriter();
        await Bridge().Serve(input, output);
        Assert.Single(output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
