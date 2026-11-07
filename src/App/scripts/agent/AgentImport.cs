using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Client.Agent;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

sealed class AgentImport
{
    const double ListMinutes = 3, ImportMinutes = 60;
    const int ListedMost = 300;
    const string MeasuredKind = "size";
    static readonly JsonSerializerOptions Listed = new() { PropertyNameCaseInsensitive = true };
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    readonly AgentEndpoint _a;

    public AgentImport(AgentEndpoint a) { _a = a; }

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("list_modules",
            "The digital tabletop modules and saves on this PC that import_module can convert: name, Workshop id, kind and file. Filter by a word in the name.",
            Args.Schema(new Arg("filter", "string", "a word the name contains")), ListModules);
        yield return new AgentTool("import_module",
            "Converts a digital tabletop module or save into a pack and a scenario, as Convert from… does on the title screen (works from there). " +
            "source is a file, a Workshop link or id, or a name from list_modules. A pack it replaces goes to the trash first; one changed in the piece studio is replaced only with replace_changed. " +
            "Returns what it made, what was left out or not taken exactly, and the report's path. A big module takes minutes.",
            Args.Schema(new Arg("source", "string", "a file, a Workshop link or id, or a module's name (list_modules)", true), new Arg("name", "string", "a name for the scenario instead of the module's"),
                new Arg("skip_3d_text", "boolean", "leave out the module's 3D text objects"), new Arg("replace_changed", "boolean", "replace packs changed in the piece studio (they go to the trash whole)")),
            ImportModule);
    }

    static Task<(int code, List<string> lines)> Converter(IEnumerable<string> args, string? writesTo = null, IDisposable? hold = null)
    {
        var done = new TaskCompletionSource<(int, List<string>)>();
        var lines = new List<string>();
        ConverterTool.Run(args, lines.Add, code => done.TrySetResult((code, lines)), writesTo, hold);
        return done.Task;
    }

    static async Task<(int code, List<string> lines)?> Within(Task<(int code, List<string> lines)> work, double minutes) =>
        await Task.WhenAny(work, Task.Delay(TimeSpan.FromMinutes(minutes))) == work ? await work : null;

    async Task<List<ModuleEntry>?> Modules()
    {
        if (await Within(Converter(new[] { "list-modules" }), ListMinutes) is not var (code, lines) || code != 0) return null;
        return lines.Where(l => l.StartsWith('{')).Select(Entry).OfType<ModuleEntry>().Where(e => e.Kind != MeasuredKind).ToList();
    }

    static ModuleEntry? Entry(string line)
    {
        try { return JsonSerializer.Deserialize<ModuleEntry>(line, Listed); }
        catch (JsonException) { return null; }
    }

    async Task<ToolResult> ListModules(JsonElement a)
    {
        if (await Modules() is not { } all) return ToolResult.Fail("The modules on this PC could not be listed; a file or a Workshop link still converts.");
        var word = a.Str("filter")?.Trim() ?? "";
        var found = all.Where(e => word.Length == 0 || e.Name.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
        var rows = new JsonArray(found.Take(ListedMost).Select(e => (JsonNode?)new JsonObject { ["name"] = e.Name, ["id"] = e.Id, ["kind"] = e.Kind, ["file"] = e.Path }).ToArray());
        return new ToolResult((found.Count > ListedMost ? $"{found.Count} found, the first {ListedMost} shown; narrow with filter.\n" : "") + rows.ToJsonString(Opts));
    }

    async Task<ToolResult> ImportModule(JsonElement a)
    {
        if (!_a.AtTitle()) return ToolResult.Fail("Converting works from the title screen: close_studio or leave_game first.");
        if (ConverterTool.Writer != null) return ToolResult.Fail("Another conversion is still running; wait for it to finish.");
        var source = a.Str("source")?.Trim() ?? "";
        if (source.Length == 0) return ToolResult.Fail("source is needed.");
        if (!LooksLikeFileOrLink(source) && await Modules() is { } known
            && known.FirstOrDefault(e => string.Equals(e.Name, source, StringComparison.OrdinalIgnoreCase)) is { } named) source = named.Path;
        var root = PackageStore.DefaultRoot;
        var name = a.Str("name")?.Trim() ?? "";
        var planArgs = new List<string> { "plan", source, "--out", root };
        if (name.Length > 0) planArgs.AddRange(new[] { "--name", name });
        if (await Within(Converter(planArgs), ImportMinutes) is not var (planCode, planLines)) return ToolResult.Fail("Reading the module took too long.");
        var plan = new ImportPlan();
        var said = planLines.Where(plan.Read).ToList();
        if (planCode != 0 || plan.Packs.Count == 0) return ToolResult.Fail($"The module could not be read, so nothing was converted (exit {planCode}).\n" + string.Join("\n", said.TakeLast(20)));
        var worked = ImportDialog.WorkedOn(root, plan.Packs);
        if (worked.Count > 0 && a.Flag("replace_changed") != true)
            return ToolResult.Fail("Not converted: it would replace packs changed in the piece studio, which would go to the trash whole: "
                + string.Join("; ", worked.Select(w => $"'{w.name}' ({w.id})")) + ". Ask the person; replace_changed goes ahead.");
        InUse.Hold hold;
        try
        {
            var folders = plan.Packs.Select(p => (InUse.Pack(root, p), $"the pack {p}")).ToList();
            folders.AddRange(plan.Scenarios.Select(s => (InUse.Scenario(root, s), $"the scenario {s}")));
            hold = InUse.Claim(folders, "being converted");
        }
        catch (InUseException e) { return ToolResult.Fail("Not converted: " + e.Message); }
        var args = new List<string> { "import", plan.File ?? source, "--out", root };
        if (name.Length > 0) args.AddRange(new[] { "--name", name });
        if (a.Flag("skip_3d_text") == true) args.AddRange(new[] { "--skip", "3DText" });
        _a.Log("converting " + source);
        if (await Within(Converter(args, root, hold), ImportMinutes) is not var (code, lines)) return ToolResult.Fail($"The conversion is still running after {ImportMinutes} minutes; it carries on.");
        return Outcome(code, lines);
    }

    static bool LooksLikeFileOrLink(string s) => s.Contains('/') || s.Contains('\\') || s.All(char.IsDigit);

    ToolResult Outcome(int code, List<string> lines)
    {
        string? Value(string head) => lines.LastOrDefault(l => l.StartsWith(head))?[head.Length..].Trim();
        string? scenario = null, pack = null;
        if (Value("IMPORTED ") is { } imported)
            foreach (var kv in imported.Split(' ').Select(p => p.Split('=', 2)).Where(kv => kv.Length == 2))
            {
                if (kv[0] == "scenario") scenario = kv[1];
                if (kv[0] == "pack") pack = kv[1];
            }
        var trashed = lines.Where(l => l.StartsWith("TRASHED ")).Select(l => l[8..].Trim()).ToList();
        if (code == ConverterTool.StoppedCode) return ToolResult.Fail("Stopped; nothing of this conversion was kept.");
        if (pack == null) return ToolResult.Fail($"Conversion failed (exit {code}).\n" + string.Join("\n", lines.Where(l => !l.StartsWith('{')).TakeLast(20)));
        _a.Refresh();
        _a.Log($"converted {scenario ?? pack}");
        var o = new JsonObject
        {
            ["scenario"] = scenario, ["pack"] = pack,
            ["also_scenarios"] = new JsonArray(lines.Where(l => l.StartsWith("ALSO ")).SelectMany(l => l[5..].Split(' ')).Where(p => p.StartsWith("scenario=")).Select(p => (JsonNode?)JsonValue.Create(p[9..])).ToArray()),
            ["left_out"] = Value("LEFTOUT "), ["hostile_script_removed"] = Value("HOSTILE "), ["not_taken_exactly"] = Value("NOTEXACT "),
            ["replaced_into_trash"] = new JsonArray(trashed.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()), ["report"] = Value("REPORT "),
        };
        return new ToolResult((scenario != null ? "Converted: host_game opens the scenario.\n" : "Installed the pack.\n") + o.ToJsonString(Opts));
    }
}
