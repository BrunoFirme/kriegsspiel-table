using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Core.Model;

namespace KTable.App;

static class AgentRecordTests
{
    internal static string Call(string tool, string args) => $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";

    internal static async Task<(bool error, string text)> Ask(AgentEndpoint agent, string tool, string args)
    {
        var res = JsonNode.Parse((await agent.Handle(Call(tool, args)))!)!["result"]!;
        return ((bool)res["isError"]!, (string?)res["content"]![0]!["text"] ?? "");
    }

    internal static async Task<(HudTests.Rig h, AgentEndpoint agent)> Open(TestRunner r, string[]? packs = null, System.Action<KTable.Core.PackageStore>? prepare = null)
    {
        var h = await HudTests.Host(r, packs: packs, prepare: prepare);
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
        Check.That(all.EndsWith("\n" + KTable.Client.Agent.Hearsay.Mark), "the lines are marked as what others wrote: " + all);
        var (_, none) = await Ask(agent, "read_log", $$"""{"since":{{last}}}""");
        Check.That(!none.Contains(KTable.Client.Agent.Hearsay.Key), "an answer with no lines carries no mark: " + none);
        h.Hud.Log("Third line for the agent");
        var (_, fresh) = await Ask(agent, "read_log", $$"""{"since":{{last}}}""");
        Check.That(fresh.Contains("Third line") && !fresh.Contains("Second line"), "since gives only what is new: " + fresh);
    }

    [NodeTest]
    static async Task The_agent_reads_the_saved_record_by_kind_person_piece_place_turn_and_number(TestRunner r)
    {
        var (h, agent) = await Open(r);
        var server = h.Net.Server!;
        long now = h.S.View.Clock.Now;
        int turn = h.S.View.Clock.Turn;
        server.Keep(new RecordEntry { Kind = "talk", Who = "Ann", Text = "Meet me at the mill", At = 1000, Seconds = now, Turn = turn });
        server.Keep(new RecordEntry { Kind = "speech", Who = "Bob", Text = "Halt there", Piece = "p1", PieceName = "Block", Pos = new Vec2(100, 100), At = 2000, Seconds = now, Turn = turn });
        server.Keep(new RecordEntry { Kind = "note", Who = "Bob", Text = "The mill burns", Pos = new Vec2(500, 500), Reach = "u:", At = 3000, Seconds = now, Turn = turn + 1 });
        var (error, all) = await Ask(agent, "read_messages", "{}");
        Check.That(!error && all.Contains("Meet me at the mill") && all.Contains("Halt there") && all.Contains("The mill burns"), "the record comes back: " + all);
        Check.That(all.Contains("2000  ") && all.EndsWith("\n" + KTable.Client.Agent.Hearsay.Mark), "numbered lines, marked as what others wrote: " + all);
        var (_, speech) = await Ask(agent, "read_messages", """{"kind":"speech"}""");
        Check.That(speech.Contains("Halt there") && !speech.Contains("Meet me"), "by kind: " + speech);
        var (_, ann) = await Ask(agent, "read_messages", """{"who":"ann"}""");
        Check.That(ann.Contains("Meet me") && !ann.Contains("Halt there"), "by person: " + ann);
        var (_, block) = await Ask(agent, "read_messages", """{"piece":"p1"}""");
        Check.That(block.Contains("Halt there") && !block.Contains("The mill burns"), "by piece: " + block);
        var (_, near) = await Ask(agent, "read_messages", """{"near":[490,510,30]}""");
        Check.That(near.Contains("The mill burns") && !near.Contains("Halt there"), "within a radius: " + near);
        var (_, later) = await Ask(agent, "read_messages", $$"""{"turn":{{turn + 1}}}""");
        Check.That(later.Contains("The mill burns") && !later.Contains("Meet me"), "by turn: " + later);
        var (_, fresh) = await Ask(agent, "read_messages", """{"since":2000}""");
        Check.That(fresh.Contains("The mill burns") && !fresh.Contains("Halt there"), "since a number: " + fresh);
        var (_, one) = await Ask(agent, "read_messages", """{"last":1,"kind":"talk"}""");
        Check.That(one.Contains("Meet me") && !one.Contains("left out"), "the last few: " + one);
        var (_, none) = await Ask(agent, "read_messages", """{"who":"Nobody"}""");
        Check.That(!none.Contains(KTable.Client.Agent.Hearsay.Key), "no lines, no mark: " + none);
        var (refused, _) = await Ask(agent, "read_messages", """{"near":[1]}""");
        Check.That(refused, "near needs x, z and radius");
        string shown = h.Hud.LogPanel.Text.GetParsedText();
        Check.Equal(1, shown.Split("Meet me at the mill").Length - 1, "the Chat log takes the record once, however often it is asked");
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
        bool plates = before.Contains("\"label_plates\":true");
        var (_, after) = await Ask(agent, "set_view", $$"""{"label_plates":{{(!plates).ToString().ToLowerInvariant()}},"piece_ids":true}""");
        Check.That(after.Contains($"\"label_plates\":{(!plates).ToString().ToLowerInvariant()}") && after.Contains("\"piece_ids\":true"), "the toggles took: " + after);
    }
}
