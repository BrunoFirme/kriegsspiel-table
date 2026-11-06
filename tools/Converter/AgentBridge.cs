using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KTable.Converter;

public sealed class AgentBridge
{
    public const string LiveFile = "agent.json", CacheFile = "agent-tools.json";
    static readonly string[] Versions = { "2025-06-18", "2025-03-26", "2024-11-05" };
    const string NotRunning = "Kriegsspiel Table is not open and could not be started: open it, then ask again.";
    const string SwitchedOff = "Kriegsspiel Table is open but its AI agent is switched off: in its Settings → AI agent, press Connect an AI agent, then ask again.";

    readonly string _dir;
    readonly HttpClient _http;
    readonly Func<Launch, bool> _launch;
    readonly TimeSpan _wait;

    public sealed record Launch(string Exe, IReadOnlyList<string> Args, string? Cwd);

    public AgentBridge(string dir, HttpMessageHandler? handler = null, Func<Launch, bool>? launch = null, TimeSpan? wait = null)
    {
        _dir = dir;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(180) };
        _launch = launch ?? Start;
        _wait = wait ?? TimeSpan.FromSeconds(90);
    }

    public static async Task<int> Run(IReadOnlyList<string> args)
    {
        if (args.Count == 0) { Console.Error.WriteLine("agent <the app's local data folder>"); return 1; }
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        var bridge = new AgentBridge(args[0]);
        await bridge.Serve(Console.In, Console.Out);
        return 0;
    }

    public async Task Serve(TextReader input, TextWriter output, CancellationToken stop = default)
    {
        string? line;
        while ((line = await input.ReadLineAsync(stop)) != null)
        {
            if (line.Trim().Length == 0) continue;
            string? answer;
            try { answer = await Handle(line, stop); }
            catch (Exception e) when (e is not OperationCanceledException) { Console.Error.WriteLine("agent bridge: " + e.Message); answer = null; }
            if (answer == null) continue;
            await output.WriteLineAsync(answer);
            await output.FlushAsync(stop);
        }
    }

    public async Task<string?> Handle(string line, CancellationToken stop = default)
    {
        JsonNode? id;
        string method;
        JsonNode? ps;
        try
        {
            var msg = JsonNode.Parse(line)?.AsObject() ?? throw new JsonException();
            id = msg.TryGetPropertyValue("id", out var i) ? i?.DeepClone() : null;
            method = (string?)msg["method"] ?? "";
            ps = msg["params"];
            if (!msg.ContainsKey("id") && !msg.ContainsKey("method")) return null;
            bool notification = !msg.ContainsKey("id");
            var (reached, answer) = await Forward(line, stop);
            if (reached) return answer;
            if (notification) return null;
        }
        catch (JsonException) { return Error(null, -32700, "not JSON"); }
        var cache = Read(CacheFile);
        switch (method)
        {
            case "initialize":
            {
                var asked = (string?)ps?["protocolVersion"];
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = asked != null && Versions.Contains(asked) ? asked : Versions[0],
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false }, ["prompts"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = "kriegsspiel-table", ["version"] = (string?)cache?["version"] ?? "" },
                    ["instructions"] = (string?)cache?["instructions"] ?? "Kriegsspiel Table, a tabletop for umpired wargames.",
                });
            }
            case "ping": return Result(id, new JsonObject());
            case "tools/list": return Result(id, new JsonObject { ["tools"] = cache?["tools"]?.DeepClone() ?? new JsonArray() });
            case "prompts/list": return Result(id, new JsonObject { ["prompts"] = cache?["prompts"]?.DeepClone() ?? new JsonArray() });
            case "tools/call":
            case "prompts/get":
            {
                var why = await Open(cache, stop);
                if (why == null)
                {
                    var (reached, answer) = await Forward(line, stop);
                    if (reached && answer != null) return answer;
                    why = NotRunning;
                }
                return method == "tools/call"
                    ? Result(id, new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = why }), ["isError"] = true })
                    : Error(id, -32603, why);
            }
            default: return Error(id, -32601, "no such method: " + method);
        }
    }

    async Task<(bool reached, string? answer)> Forward(string line, CancellationToken stop)
    {
        if (Read(LiveFile) is not { } live || (string?)live["url"] is not { } url || (string?)live["token"] is not { } token) return (false, null);
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(line, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.ParseAdd("application/json");
        try
        {
            using var res = await _http.SendAsync(req, stop);
            if (res.StatusCode == HttpStatusCode.Accepted) return (true, null);
            var body = await res.Content.ReadAsStringAsync(stop);
            if (res.IsSuccessStatusCode) return (true, body);
            return (false, null);
        }
        catch (HttpRequestException) { return (false, null); }
        catch (TaskCanceledException) when (!stop.IsCancellationRequested) { return (false, null); }
    }

    async Task<string?> Open(JsonNode? cache, CancellationToken stop)
    {
        if (Running(cache)) return await Ready(TimeSpan.FromSeconds(3), stop) ? null : SwitchedOff;
        if (cache?["launch"] is not JsonObject l || (string?)l["exe"] is not { Length: > 0 } exe || !File.Exists(exe)) return NotRunning;
        var args = (l["args"] as JsonArray)?.Select(a => (string?)a ?? "").ToList() ?? new List<string>();
        args.Add("--");
        args.Add("--agent");
        if (!_launch(new Launch(exe, args, (string?)l["cwd"]))) return NotRunning;
        return await Ready(_wait, stop) ? null : NotRunning;
    }

    async Task<bool> Ready(TimeSpan within, CancellationToken stop)
    {
        var until = DateTime.UtcNow + within;
        const string Ping = """{"jsonrpc":"2.0","id":"bridge-ping","method":"ping"}""";
        while (true)
        {
            if ((await Forward(Ping, stop)).reached) return true;
            if (DateTime.UtcNow > until) return false;
            await Task.Delay(400, stop);
        }
    }

    static bool Running(JsonNode? cache)
    {
        if (cache?["pid"] is not JsonValue pv || !pv.TryGetValue<int>(out var pid) || (string?)cache["process"] is not { } name) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited && string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    static bool Start(Launch l)
    {
        try
        {
            var psi = new ProcessStartInfo(l.Exe) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal, WorkingDirectory = l.Cwd ?? Path.GetDirectoryName(l.Exe) ?? "" };
            foreach (var a in l.Args) psi.ArgumentList.Add(a);
            return Process.Start(psi) != null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { Console.Error.WriteLine("agent bridge: " + e.Message); return false; }
    }

    JsonNode? Read(string file)
    {
        try { return JsonNode.Parse(File.ReadAllText(Path.Combine(_dir, file))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static string Result(JsonNode? id, JsonNode result) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    static string Error(JsonNode? id, int code, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();
}
