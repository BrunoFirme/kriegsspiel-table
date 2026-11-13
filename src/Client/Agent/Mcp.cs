using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KTable.Client.Agent;

public sealed record ToolResult(string Text, bool IsError = false, IReadOnlyList<byte[]>? Images = null)
{
    public static ToolResult Fail(string text) => new(text, true);
}

public sealed record AgentTool(string Name, string Description, JsonObject Schema, Func<JsonElement, Task<ToolResult>> Run);

public sealed record Arg(string Name, string Type, string Description, bool Required = false);

public sealed record AgentPrompt(string Name, string Description, IReadOnlyList<Arg> Args, Func<IReadOnlyDictionary<string, string>, string> Text);

public sealed record Reply(string? Json, int Status = Reply.Ok)
{
    public const int Ok = 200, Refused = 400;
}

public static class Args
{
    public const string Names = "array", Numbers = "numbers", Points = "points", Objects = "objects", Mixed = "mixed";

    static JsonObject? Items(string type) => type switch
    {
        Names => new JsonObject { ["type"] = "string" },
        Numbers => new JsonObject { ["type"] = "number" },
        Points => new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "number" } },
        Objects => new JsonObject { ["type"] = "object" },
        Mixed => new JsonObject(),
        _ => null,
    };

    public static JsonObject Schema(params Arg[] args)
    {
        var props = new JsonObject();
        foreach (var a in args)
        {
            var items = Items(a.Type);
            var prop = new JsonObject { ["type"] = items == null ? a.Type : "array", ["description"] = a.Description };
            if (items != null) prop["items"] = items;
            props[a.Name] = prop;
        }
        var schema = new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
        var required = args.Where(a => a.Required).Select(a => (JsonNode?)JsonValue.Create(a.Name)).ToArray();
        if (required.Length > 0) schema["required"] = new JsonArray(required);
        return schema;
    }

    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static double? Num(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    public static bool? Flag(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public static JsonElement Part(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;

    public static List<string>? Words(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).Distinct().ToList()
            : null;
}

public sealed class Mcp
{
    public const string Stateless = "2026-07-28", Latest = "2025-11-25";
    public const string VersionKey = "io.modelcontextprotocol/protocolVersion", CapabilitiesKey = "io.modelcontextprotocol/clientCapabilities", ServerKey = "io.modelcontextprotocol/serverInfo";
    public const string VersionHeader = "MCP-Protocol-Version", MethodHeader = "Mcp-Method", NameHeader = "Mcp-Name";
    public const string Discover = "server/discover", ServerName = "kriegsspiel-table", Complete = "complete", ListsAreFor = "public";
    public const int HeaderMismatch = -32020, UnsupportedVersion = -32022, InvalidParams = -32602, NoSuchMethod = -32601;
    public const int ListsFreshMs = 300_000;
    public const string BatchName = "batch";
    public const int BatchMost = 50;
    const string EncodedFrom = "=?base64?", EncodedTo = "?=";
    static readonly string[] Handshaken = { Latest, "2025-06-18", "2025-03-26", "2024-11-05" };
    public static readonly IReadOnlyList<string> Versions = Handshaken.Prepend(Stateless).ToArray();

    readonly IReadOnlyList<AgentTool> _tools;
    readonly IReadOnlyList<AgentPrompt> _prompts;
    readonly string _version;
    readonly string _instructions;

    public event Action<string>? Called;

    public Mcp(IReadOnlyList<AgentTool> tools, string version, string instructions, IReadOnlyList<AgentPrompt>? prompts = null)
    {
        _tools = tools.Append(Batch()).ToList();
        _version = version;
        _instructions = instructions;
        _prompts = prompts ?? Array.Empty<AgentPrompt>();
    }

    public IReadOnlyList<AgentTool> Tools => _tools;
    public string Instructions => _instructions;

    public JsonArray ToolList() => new(_tools.Select(Describe).ToArray());

    public JsonArray PromptList() => new(_prompts.Select(DescribePrompt).ToArray());

    readonly record struct Ask(JsonNode? Id, bool Stateless, string Version);

    public async Task<string?> Handle(string body) => (await Answer(body)).Json;

    public async Task<Reply> Answer(string body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return new Reply(Error(null, -32700, "not JSON")); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new Reply(Error(null, -32600, "send one JSON-RPC request at a time"));
            JsonNode? id = root.TryGetProperty("id", out var idEl) ? JsonNode.Parse(idEl.GetRawText()) : null;
            bool notification = !root.TryGetProperty("id", out _);
            var method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            var ps = root.TryGetProperty("params", out var p) ? p : default;
            if (notification) return new Reply(null);
            var meta = ps.Part("_meta");
            var ask = new Ask(id, meta.Str(VersionKey) != null, _version);
            if (ask.Stateless && meta.Str(VersionKey) != Stateless) return new Reply(Unsupported(id, meta.Str(VersionKey)!), Reply.Refused);
            if (ask.Stateless && meta.Part(CapabilitiesKey).ValueKind != JsonValueKind.Object) return new Reply(Error(id, InvalidParams, "params._meta needs " + CapabilitiesKey), Reply.Refused);
            try
            {
                return method switch
                {
                    "initialize" when !ask.Stateless => Done(ask, Initialize(ps)),
                    "ping" when !ask.Stateless => Done(ask, new JsonObject()),
                    Discover => Done(ask with { Stateless = true }, Fresh(Discovered(_instructions))),
                    "tools/list" => Done(ask, Listed(ask, "tools", ToolList())),
                    "tools/call" => await Call(ask, ps),
                    "prompts/list" => Done(ask, Listed(ask, "prompts", PromptList())),
                    "prompts/get" => Prompt(ask, ps),
                    _ => new Reply(Error(id, NoSuchMethod, "no such method: " + method)),
                };
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return new Reply(Error(id, -32603, e.Message));
            }
        }
    }

    public static string Unsupported(JsonNode? id, string requested) => Error(id, UnsupportedVersion, "Unsupported protocol version",
        new JsonObject { ["supported"] = new JsonArray(Versions.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()), ["requested"] = requested });

    public static string? HeaderFault(string body, Func<string, string?> header)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.Part("params").Part("_meta").Str(VersionKey) is not { } stated) return null;
            JsonNode? id = root.TryGetProperty("id", out var idEl) ? JsonNode.Parse(idEl.GetRawText()) : null;
            var method = root.Str("method") ?? "";
            string? Differs(string name, string inBody) => header(name) is not { } sent ? $"Header mismatch: {name} is missing"
                : Plain(sent) != inBody ? $"Header mismatch: {name} header value '{Plain(sent)}' does not match body value '{inBody}'" : null;
            var fault = Differs(VersionHeader, stated) ?? Differs(MethodHeader, method);
            if (fault == null && method is "tools/call" or "prompts/get") fault = Differs(NameHeader, root.Part("params").Str("name") ?? "");
            return fault == null ? null : Error(id, HeaderMismatch, fault);
        }
    }

    public static IReadOnlyList<(string Name, string Value)> HeadersFor(string body)
    {
        var headers = new List<(string, string)>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return headers; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.Part("params").Part("_meta").Str(VersionKey) is not { } stated) return headers;
            var method = root.Str("method") ?? "";
            headers.Add((VersionHeader, stated));
            headers.Add((MethodHeader, method));
            if (method is "tools/call" or "prompts/get") headers.Add((NameHeader, Sendable(root.Part("params").Str("name") ?? "")));
            return headers;
        }
    }

    static string Sendable(string value) =>
        value.All(c => c is >= ' ' and <= '~') && !value.StartsWith(EncodedFrom, StringComparison.Ordinal) ? value : EncodedFrom + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + EncodedTo;

    static string Plain(string value)
    {
        if (!value.StartsWith(EncodedFrom, StringComparison.Ordinal) || !value.EndsWith(EncodedTo, StringComparison.Ordinal) || value.Length < EncodedFrom.Length + EncodedTo.Length) return value;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value[EncodedFrom.Length..^EncodedTo.Length])); }
        catch (FormatException) { return value; }
    }

    public static JsonObject Capabilities() => new() { ["tools"] = new JsonObject { ["listChanged"] = false }, ["prompts"] = new JsonObject { ["listChanged"] = false } };

    public static JsonObject Server(string version) => new() { ["name"] = ServerName, ["version"] = version };

    public static JsonObject Fresh(JsonObject result)
    {
        result["ttlMs"] = ListsFreshMs;
        result["cacheScope"] = ListsAreFor;
        return result;
    }

    public static JsonObject Stamped(JsonObject result, string version)
    {
        result["resultType"] = Complete;
        var meta = result["_meta"] as JsonObject ?? new JsonObject();
        meta[ServerKey] = Server(version);
        result["_meta"] = meta;
        return result;
    }

    public static JsonObject Discovered(string instructions) => new()
    {
        ["supportedVersions"] = new JsonArray(Versions.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()),
        ["capabilities"] = Capabilities(),
        ["instructions"] = instructions,
    };

    public static JsonObject Initialized(string? asked, string version, string instructions) => new()
    {
        ["protocolVersion"] = asked != null && Handshaken.Contains(asked) ? asked : Latest,
        ["capabilities"] = Capabilities(),
        ["serverInfo"] = Server(version),
        ["instructions"] = instructions,
    };

    static Reply Done(Ask ask, JsonObject result) => new(Result(ask.Id, ask.Stateless ? Stamped(result, ask.Version) : result));

    static JsonObject Listed(Ask ask, string what, JsonArray list)
    {
        var result = new JsonObject { [what] = list };
        return ask.Stateless ? Fresh(result) : result;
    }

    JsonObject Initialize(JsonElement ps) => Initialized(ps.ValueKind == JsonValueKind.Object ? ps.Str("protocolVersion") : null, _version, _instructions);

    static JsonNode Describe(AgentTool t) => new JsonObject
    {
        ["name"] = t.Name,
        ["description"] = t.Description,
        ["inputSchema"] = JsonNode.Parse(t.Schema.ToJsonString()),
        ["annotations"] = ToolEffects.Marks(t.Name),
    };

    static JsonNode DescribePrompt(AgentPrompt p) => new JsonObject
    {
        ["name"] = p.Name,
        ["description"] = p.Description,
        ["arguments"] = new JsonArray(p.Args.Select(a => (JsonNode?)new JsonObject { ["name"] = a.Name, ["description"] = a.Description, ["required"] = a.Required }).ToArray()),
    };

    Reply Prompt(Ask ask, JsonElement ps)
    {
        var name = ps.ValueKind == JsonValueKind.Object ? ps.Str("name") : null;
        var prompt = _prompts.FirstOrDefault(p => p.Name == name);
        if (prompt == null) return new Reply(Error(ask.Id, InvalidParams, "no such prompt: " + name));
        var given = new Dictionary<string, string>();
        if (ps.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
            foreach (var kv in a.EnumerateObject()) given[kv.Name] = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.GetRawText();
        var missing = prompt.Args.Where(x => x.Required && !given.ContainsKey(x.Name)).Select(x => x.Name).ToList();
        if (missing.Count > 0) return new Reply(Error(ask.Id, InvalidParams, "missing: " + string.Join(", ", missing)));
        return Done(ask, new JsonObject
        {
            ["description"] = prompt.Description,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonObject { ["type"] = "text", ["text"] = prompt.Text(given) } }),
        });
    }

    async Task<Reply> Call(Ask ask, JsonElement ps)
    {
        var name = ps.ValueKind == JsonValueKind.Object ? ps.Str("name") : null;
        var tool = _tools.FirstOrDefault(t => t.Name == name);
        if (tool == null) return new Reply(Error(ask.Id, InvalidParams, "no such tool: " + name));
        var args = ps.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object ? a.Clone() : Empty;
        var r = await Run(tool, args);
        return Done(ask, new JsonObject
        {
            ["content"] = Content(r),
            ["isError"] = r.IsError,
        });
    }

    static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();

    async Task<ToolResult> Run(AgentTool tool, JsonElement args)
    {
        Called?.Invoke(tool.Name);
        try { return await tool.Run(args); }
        catch (Exception e) when (e is not OutOfMemoryException) { return ToolResult.Fail($"{tool.Name} failed: {e.Message}"); }
    }

    AgentTool Batch() => new(BatchName,
        $"Runs up to {BatchMost} of these tools in one call, in order, each as if called alone, and answers with every answer and picture. " +
        "Put in it the steps whose arguments do not wait on an earlier step's answer. It stops at the first step that fails unless keep_going.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["calls"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "the steps, in order",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["tool"] = new JsonObject { ["type"] = "string", ["description"] = "a tool's name" },
                            ["arguments"] = new JsonObject { ["type"] = "object", ["description"] = "its arguments, as that tool takes them" },
                        },
                        ["required"] = new JsonArray("tool"),
                    },
                },
                ["keep_going"] = new JsonObject { ["type"] = "boolean", ["description"] = "run the rest after a step fails" },
            },
            ["required"] = new JsonArray("calls"),
            ["additionalProperties"] = false,
        }, RunBatch);

    async Task<ToolResult> RunBatch(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("calls", out var calls) || calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() == 0)
            return ToolResult.Fail("calls is [{tool, arguments}, …].");
        int total = calls.GetArrayLength();
        if (total > BatchMost) return ToolResult.Fail($"At most {BatchMost} calls at once.");
        bool keepGoing = a.Flag("keep_going") ?? false;
        var parts = new List<string>();
        var images = new List<byte[]>();
        bool failed = false;
        int n = 0;
        foreach (var c in calls.EnumerateArray())
        {
            n++;
            var name = c.Str("tool") ?? "";
            var args = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("arguments", out var given) && given.ValueKind == JsonValueKind.Object ? given : Empty;
            var r = name == BatchName ? ToolResult.Fail("A batch does not hold another batch.")
                : _tools.FirstOrDefault(t => t.Name == name) is { } tool ? await Run(tool, args) : ToolResult.Fail("No such tool: " + name);
            parts.Add($"{n}. {name}{(r.IsError ? " (failed)" : "")}: {r.Text}");
            if (r.Images != null) images.AddRange(r.Images);
            if (!r.IsError) continue;
            failed = true;
            if (keepGoing) continue;
            if (n < total) parts.Add(n + 1 == total ? $"Stopped: step {total} was not run." : $"Stopped: steps {n + 1} to {total} were not run.");
            break;
        }
        return new ToolResult(string.Join("\n\n", parts), failed, images.Count > 0 ? images : null);
    }

    static JsonArray Content(ToolResult r)
    {
        var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = r.Text });
        foreach (var png in r.Images ?? Array.Empty<byte[]>())
            content.Add(new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" });
        return content;
    }

    public static string Result(JsonNode? id, JsonNode result) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    public static string Error(JsonNode? id, int code, string message, JsonObject? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data != null) error["data"] = data;
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }.ToJsonString();
    }
}
