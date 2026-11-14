using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Core.Model;

namespace KTable.App;

static class AgentStatusTests
{
    static Task<(bool error, string text)> Ask(AgentEndpoint agent, string tool, string args) => AgentRecordTests.Ask(agent, tool, args);

    [NodeTest]
    static async Task Status_says_what_is_selected_where_the_view_looks_and_how_the_work_at_a_table_is_done(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        h.Table.Selection.Set(new[] { "p1" });
        await r.Frames(2);
        var (error, said) = await Ask(agent, "status", "{}");
        var status = JsonNode.Parse(said)!;
        var piece = h.S.View.Find(Layers.Umpire, "p1")!;
        Check.That(!error && (string?)status["open"] == "game", "status answers at the table: " + said);
        Check.That(status["selected"]!.AsArray().Count == 1 && (string?)status["selected"]![0]!["id"] == "p1", "it names what the person has selected");
        Check.That(Math.Abs((double)status["selected"]![0]!["x"]! - piece.Pos.X) < 0.01, "with where it stands");
        var view = status["view"]!;
        Check.That((double)view["across"]! > 0 && (double)view["angle"]! is > 0 and <= 90 && (double)view["heading"]! is >= 0 and <= 360, "and where the view looks from: " + view.ToJsonString());
        Check.That(((string?)status["how_to"]!["table"])!.Contains("place_piece") && ((string?)status["how_to"]!["map"])!.Contains("shape_ground"), "the work of a table comes with it, tool by tool");
        Check.That(status["how_to"]!["studio"] == null, "and not the studio's");
        Check.That(!said.Contains('\n'), "the answer is one close line, not a page of indents");
        h.Table.Selection.Set(Array.Empty<string>());
        var (_, bare) = await Ask(agent, "status", "{}");
        Check.That(JsonNode.Parse(bare)!["selected"]!.AsArray().Count == 0, "with nothing selected it says so");
    }

    [NodeTest]
    static async Task All_that_one_call_of_the_agent_changed_is_taken_back_with_one_undo(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        Vec2 At(string id) => h.S.View.Find(Layers.Umpire, id)!.Pos;
        var was = At("p1");
        int steps = h.S.MyUndoable.Count;
        var (error, said) = await Ask(agent, "batch", """{"calls":[{"tool":"move_piece","arguments":{"id":"p1","x":3,"z":3}},{"tool":"move_piece","arguments":{"id":"p1","x":6,"z":-4}}]}""");
        await r.Frames(4);
        Check.That(!error && Math.Abs(At("p1").X - 6) < 1e-6, "a batch moves a piece twice: " + said);
        Check.That(h.S.MyUndoable.Count == steps + 1, $"and leaves one step to undo, not two ({h.S.MyUndoable.Count - steps})");
        await Ask(agent, "move_piece", """{"id":"p1","x":9,"z":9}""");
        await r.Frames(4);
        Check.That(h.S.MyUndoable.Count == steps + 2, "a second call is a step of its own");
        h.S.UndoLast();
        await r.Frames(4);
        Check.That(Math.Abs(At("p1").X - 6) < 1e-6 && Math.Abs(At("p1").Z + 4) < 1e-6, "the person's undo takes the second call back");
        h.S.UndoLast();
        await r.Frames(4);
        Check.That(Math.Abs(At("p1").X - was.X) < 1e-6 && Math.Abs(At("p1").Z - was.Z) < 1e-6, "and the next one the whole batch, back to where the piece stood");
        h.S.RedoLast();
        await r.Frames(4);
        Check.That(Math.Abs(At("p1").X - 6) < 1e-6, "a redo puts the whole batch back");
    }
}
