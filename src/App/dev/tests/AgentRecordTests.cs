using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace KTable.App;

static class AgentRecordTests
{
    static string Call(string tool, string args) => $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";

    static async Task<(bool error, string text)> Ask(AgentEndpoint agent, string tool, string args)
    {
        var res = JsonNode.Parse((await agent.Handle(Call(tool, args)))!)!["result"]!;
        return ((bool)res["isError"]!, (string?)res["content"]![0]!["text"] ?? "");
    }

    static async Task<(HudTests.Rig h, AgentEndpoint agent)> Open(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => h.S, TableHud = () => h.Hud };
        r.AddChild(agent);
        return (h, agent);
    }

    [NodeTest]
    static async Task The_agent_reads_the_log_by_line_number(TestRunner r)
    {
        var (h, agent) = await Open(r);
        h.Hud.Log("First line for the agent");
        h.Hud.Log("Second line for the agent");
        var (error, all) = await Ask(agent, "read_log", "{}");
        Check.That(!error && all.Contains("Second line for the agent"), "the log comes back: " + all);
        long last = h.Hud.LogPanel.Written;
        Check.That(all.Contains($"{last}  "), "lines carry their numbers: " + all);
        h.Hud.Log("Third line for the agent");
        var (_, fresh) = await Ask(agent, "read_log", $$"""{"since":{{last}}}""");
        Check.That(fresh.Contains("Third line") && !fresh.Contains("Second line"), "since gives only what is new: " + fresh);
    }

    [NodeTest]
    static async Task The_agent_adds_renames_hides_picks_and_removes_drawing_layers(TestRunner r)
    {
        var (h, agent) = await Open(r);
        var (addError, added) = await Ask(agent, "drawing_layers", """{"action":"add","name":"Orders"}""");
        Check.That(!addError && added.Contains("Orders"), "a layer is added: " + added);
        await Ask(agent, "drawing_layers", """{"action":"rename","layer":"Orders","name":"Orders of the day"}""");
        Check.That(h.S.View.Boards.Any(b => b.Name == "Orders of the day"), "it is renamed");
        var id = h.S.View.Boards.First(b => b.Name == "Orders of the day").Id;
        await Ask(agent, "drawing_layers", """{"action":"pick","layer":"Orders of the day"}""");
        Check.Equal(id, h.Table.DrawBoard(), "the pen draws on it");
        await Ask(agent, "drawing_layers", """{"action":"hide","layer":"Orders of the day"}""");
        Check.That(h.Table.HiddenBoards.Contains(id) && h.Table.DrawingLocked, "hidden on this screen, the pen is locked");
        await Ask(agent, "drawing_layers", """{"action":"show","layer":"Orders of the day"}""");
        Check.That(!h.Table.DrawingLocked, "shown again, the pen is free");
        await Ask(agent, "drawing_layers", """{"action":"remove","layer":"Orders of the day"}""");
        Check.That(h.S.View.Boards.All(b => b.Id != id), "it is removed");
        var (refused, _) = await Ask(agent, "drawing_layers", """{"action":"hide","layer":"nobody"}""");
        Check.That(refused, "a layer that is not there is refused");
    }

    [NodeTest]
    static async Task The_agent_turns_label_plates_and_piece_ids_and_says_how_the_view_stands(TestRunner r)
    {
        var (_, agent) = await Open(r);
        var (_, before) = await Ask(agent, "set_view", "{}");
        bool plates = before.Contains("\"label_plates\": true");
        var (_, after) = await Ask(agent, "set_view", $$"""{"label_plates":{{(!plates).ToString().ToLowerInvariant()}},"piece_ids":true}""");
        Check.That(after.Contains($"\"label_plates\": {(!plates).ToString().ToLowerInvariant()}") && after.Contains("\"piece_ids\": true"), "the toggles took: " + after);
    }
}
