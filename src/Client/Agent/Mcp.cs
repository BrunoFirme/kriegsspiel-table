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

public static class Args
{
    public static JsonObject Schema(params Arg[] args)
    {
        var props = new JsonObject();
        foreach (var a in args) props[a.Name] = new JsonObject { ["type"] = a.Type, ["description"] = a.Description };
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
}

public sealed class Mcp
{
    public const string Latest = "2025-06-18";
    public const string BatchName = "batch";
    public const int BatchMost = 50;
    static readonly string[] Versions = { "2025-06-18", "2025-03-26", "2024-11-05" };

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

    public async Task<string?> Handle(string body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException) { return Error(null, -32700, "not JSON"); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error(null, -32600, "send one JSON-RPC request at a time");
            JsonNode? id = root.TryGetProperty("id", out var idEl) ? JsonNode.Parse(idEl.GetRawText()) : null;
            bool notification = !root.TryGetProperty("id", out _);
            var method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            var ps = root.TryGetProperty("params", out var p) ? p : default;
            if (notification) return null;
            try
            {
                return method switch
                {
                    "initialize" => Result(id, Initialize(ps)),
                    "ping" => Result(id, new JsonObject()),
                    "tools/list" => Result(id, new JsonObject { ["tools"] = ToolList() }),
                    "tools/call" => await Call(id, ps),
                    "prompts/list" => Result(id, new JsonObject { ["prompts"] = PromptList() }),
                    "prompts/get" => Prompt(id, ps),
                    _ => Error(id, -32601, "no such method: " + method),
                };
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return Error(id, -32603, e.Message);
            }
        }
    }

    JsonObject Initialize(JsonElement ps)
    {
        var asked = ps.ValueKind == JsonValueKind.Object ? ps.Str("protocolVersion") : null;
        return new JsonObject
        {
            ["protocolVersion"] = asked != null && Versions.Contains(asked) ? asked : Latest,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false }, ["prompts"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "kriegsspiel-table", ["version"] = _version },
            ["instructions"] = _instructions,
        };
    }

    static JsonNode Describe(AgentTool t) => new JsonObject
    {
        ["name"] = t.Name,
        ["description"] = t.Description,
        ["inputSchema"] = JsonNode.Parse(t.Schema.ToJsonString()),
    };

    static JsonNode DescribePrompt(AgentPrompt p) => new JsonObject
    {
        ["name"] = p.Name,
        ["description"] = p.Description,
        ["arguments"] = new JsonArray(p.Args.Select(a => (JsonNode?)new JsonObject { ["name"] = a.Name, ["description"] = a.Description, ["required"] = a.Required }).ToArray()),
    };

    string Prompt(JsonNode? id, JsonElement ps)
    {
        var name = ps.ValueKind == JsonValueKind.Object ? ps.Str("name") : null;
        var prompt = _prompts.FirstOrDefault(p => p.Name == name);
        if (prompt == null) return Error(id, -32602, "no such prompt: " + name);
        var given = new Dictionary<string, string>();
        if (ps.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
            foreach (var kv in a.EnumerateObject()) given[kv.Name] = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.GetRawText();
        var missing = prompt.Args.Where(x => x.Required && !given.ContainsKey(x.Name)).Select(x => x.Name).ToList();
        if (missing.Count > 0) return Error(id, -32602, "missing: " + string.Join(", ", missing));
        return Result(id, new JsonObject
        {
            ["description"] = prompt.Description,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonObject { ["type"] = "text", ["text"] = prompt.Text(given) } }),
        });
    }

    async Task<string> Call(JsonNode? id, JsonElement ps)
    {
        var name = ps.ValueKind == JsonValueKind.Object ? ps.Str("name") : null;
        var tool = _tools.FirstOrDefault(t => t.Name == name);
        if (tool == null) return Error(id, -32602, "no such tool: " + name);
        var args = ps.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object ? a.Clone() : Empty;
        var r = await Run(tool, args);
        return Result(id, new JsonObject
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

    static string Result(JsonNode? id, JsonNode result) => new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToJsonString();

    static string Error(JsonNode? id, int code, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();
}
