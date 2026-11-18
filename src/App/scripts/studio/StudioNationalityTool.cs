using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class StudioNationalityTool(AgentEndpoint a)
{
    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("studio_nationality",
            "Crafts a pack nationality (how_to crowds): copy a shipped one in, record sets fields as JSON, remove drops it.",
            Args.Schema(new Arg("copy", "string", "shipped id"), new Arg("id", "string", "this pack's one"), new Arg("record", "string", "JSON fields"),
                new Arg("remove", "boolean", "drop it")), Act);
    }

    Task<ToolResult> Act(JsonElement args) => Task.FromResult(a.Studio is { } s && a.InStudio ? Done(s, args) : ToolResult.Fail("No studio is open: open_studio first."));

    ToolResult Done(PieceStudio s, JsonElement args)
    {
        if (args.Str("copy") is { } shipped)
        {
            if (Nationalities.Named(shipped) == null) return ToolResult.Fail("copy is a shipped one: " + string.Join(", ", Nationalities.All.Select(n => n.Id)));
            var made = StudioNationalities.Copy(s.Pack, shipped);
            s.SavePack();
            a.Log("copied a nationality into the pack");
            return new ToolResult($"{made.Id}: a copy of {shipped}. record changes it.");
        }
        string id = args.Str("id") ?? "";
        var mine = s.Pack.Nationalities?.Select(n => n.Id) ?? [];
        if (s.Pack.Nationalities?.FirstOrDefault(n => n.Id == id) is not { } now) return ToolResult.Fail("id is one of this pack's: " + string.Join(", ", mine));
        if (args.Flag("remove") == true)
        {
            s.Pack.Nationalities!.Remove(now);
            if (s.Pack.Nationalities.Count == 0) s.Pack.Nationalities = null;
            s.SavePack();
            return new ToolResult($"Removed {id}.");
        }
        if (args.Str("record") is not { } record) return new ToolResult(Json.Serialize(now, indented: true));
        Nationality changed;
        try { changed = Merged(now, record); }
        catch (JsonException e) { return ToolResult.Fail("record does not read: " + e.Message); }
        if (StudioNationalities.Problems(s.Pack, changed) is { Count: > 0 } problems) return ToolResult.Fail(string.Join("; ", problems));
        StudioNationalities.Put(s.Pack, changed);
        s.SavePack();
        a.Log("changed a nationality in the pack");
        return new ToolResult($"Saved {changed.Id}.");
    }

    static Nationality Merged(Nationality now, string record)
    {
        var whole = JsonNode.Parse(Json.Serialize(now))!.AsObject();
        foreach (var (key, value) in JsonNode.Parse(record)!.AsObject().ToList()) whole[key] = value?.DeepClone();
        whole["id"] = now.Id;
        return Json.Deserialize<Nationality>(whole.ToJsonString());
    }
}
