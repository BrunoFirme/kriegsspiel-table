using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using KTable.Client.Agent;
using KTable.Core;

namespace KTable.Converter.Tests;

public sealed class AgentBridgeTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ktable-bridge-tests", Guid.NewGuid().ToString("N"));
    readonly List<(string auth, string body)> _posts = new();
    readonly List<AgentBridge.Launch> _launched = new();
    readonly List<string> _named = new();
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
        lock (_named) _named.Add(string.Join(" ", new[] { Mcp.VersionHeader, Mcp.MethodHeader, Mcp.NameHeader }.Select(h => r.Headers.TryGetValues(h, out var v) ? v.First() : "-")));
        if (body.Contains("1900-01-01")) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(Mcp.Unsupported(1, "1900-01-01"), System.Text.Encoding.UTF8, "application/json") };
        if (!body.Contains("\"id\"")) return new HttpResponseMessage(HttpStatusCode.Accepted);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":{"from":"game"}}""") };
    }), l =>
    {
        _launched.Add(l);
        if (startsGame) { Live(); _gameUp = true; } return true;
    }, TimeSpan.FromSeconds(3));

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

    const string Meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";

    static string Stateless(string method, string more = "", string meta = Meta) => "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"" + method + "\",\"params\":{" + meta + more + "}}";

    [Fact]
    public async Task With_the_game_closed_a_stateless_client_is_answered_from_its_last_run()
    {
        Cache();
        var b = Bridge();
        var found = JsonNode.Parse((await b.Handle(Stateless(Mcp.Discover)))!)!["result"]!;
        Assert.Equal(("complete", "use status first", "1.2"), ((string?)found["resultType"], (string?)found["instructions"], (string?)found["_meta"]![Mcp.ServerKey]!["version"]));
        Assert.Contains(Mcp.Stateless, found["supportedVersions"]!.AsArray().Select(v => (string?)v));
        var tools = JsonNode.Parse((await b.Handle(Stateless("tools/list")))!)!["result"]!;
        Assert.Equal(("status", "complete", Mcp.ListsFreshMs), ((string?)tools["tools"]![0]!["name"], (string?)tools["resultType"], (int)tools["ttlMs"]!));
        var old = JsonNode.Parse((await b.Handle(Stateless("tools/list", meta: Meta.Replace("2026-07-28", "1900-01-01"))))!)!;
        Assert.Equal(Mcp.UnsupportedVersion, (int)old["error"]!["code"]!);
        var call = JsonNode.Parse((await b.Handle(Stateless("tools/call", ",\"name\":\"status\"")))!)!["result"]!;
        Assert.Equal(("complete", true), ((string?)call["resultType"], (bool)call["isError"]!));
    }

    [Fact]
    public async Task A_stateless_request_goes_to_the_game_with_its_headers_and_a_refusal_comes_back_as_it_was()
    {
        Live();
        Assert.Contains("\"from\":\"game\"", await Bridge().Handle(Stateless("tools/call", ",\"name\":\"status\"")));
        Assert.Equal("2026-07-28 tools/call status", Assert.Single(_named));
        await Bridge().Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
        Assert.Equal("- - -", _named[1]);
        var refused = JsonNode.Parse((await Bridge().Handle(Stateless("tools/list", meta: Meta.Replace("2026-07-28", "1900-01-01"))))!)!;
        Assert.Equal((Mcp.UnsupportedVersion, "1900-01-01"), ((int)refused["error"]!["code"]!, (string?)refused["error"]!["data"]!["requested"]));
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
