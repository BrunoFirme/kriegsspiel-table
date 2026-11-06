using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;

namespace KTable.App;

sealed class AgentSettingsTools
{
    readonly AgentEndpoint _a;
    readonly Settings _cfg;

    public AgentSettingsTools(AgentEndpoint a, Settings cfg) { _a = a; _cfg = cfg; }

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("read_settings",
            "This PC's settings, page by page as the Settings window shows them: each one's value, what it does and what it takes. Keys, and the agent's own connection, are left out.",
            Args.Schema(), _ => Task.FromResult(Read()));
        yield return new AgentTool("set_settings",
            "Changes settings of this PC (read_settings names them), at once and kept, as the Settings window does. Change them only when the person asks. Nothing changes if any value is refused.",
            Args.Schema(new Arg("values", "object", "setting name to its new value, e.g. {\"contact_shading\": true, \"shadows\": \"high\"}", true)), a => Task.FromResult(Set(a)));
    }

    ToolResult Read()
    {
        var pages = new JsonObject();
        foreach (var group in Preferences.All.GroupBy(p => p.Page))
        {
            var rows = new JsonObject();
            foreach (var p in group) rows[p.Name] = new JsonObject { ["value"] = p.Get(_cfg), ["does"] = p.Meaning, ["takes"] = p.Allowed };
            pages[group.Key] = rows;
        }
        return new ToolResult(pages.ToJsonString(Opts));
    }

    ToolResult Set(JsonElement a)
    {
        if (!a.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object) return ToolResult.Fail("values is an object of setting names to values.");
        var asked = values.EnumerateObject().ToList();
        if (asked.Count == 0) return ToolResult.Fail("No settings given.");
        var unknown = asked.Where(v => Preferences.Find(v.Name) == null).Select(v => v.Name).ToList();
        if (unknown.Count > 0) return ToolResult.Fail($"No setting {string.Join(", ", unknown)}: read_settings names them.");
        var trial = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(_cfg))!;
        var refused = asked.Select(v => Preferences.Find(v.Name)!.Set(trial, v.Value)).OfType<string>().ToList();
        if (refused.Count > 0) return ToolResult.Fail(string.Join("; ", refused) + ". Nothing was changed.");
        var changed = asked.Select(v => Preferences.Find(v.Name)!).ToList();
        foreach (var v in asked) Preferences.Find(v.Name)!.Set(_cfg, v.Value);
        _cfg.Save();
        Preferences.Apply(changed.Select(p => p.Effect), _cfg, _a.GetTree().Root, _a.TableHud());
        var now = new JsonObject();
        foreach (var p in changed) now[p.Name] = p.Get(_cfg);
        _a.Log("changed settings: " + string.Join(", ", changed.Select(p => p.Name)));
        return new ToolResult("Set and kept: " + now.ToJsonString());
    }
}
