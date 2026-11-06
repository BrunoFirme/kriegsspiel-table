using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Core.Model;

namespace KTable.App;

static class WorkflowTests
{
    static string Call(string tool, string args) => $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";

    static (bool error, string text) Answer(string? json)
    {
        var res = JsonNode.Parse(json!)!["result"]!;
        return ((bool)res["isError"]!, (string?)res["content"]![0]!["text"] ?? "");
    }

    [NodeTest]
    static async Task An_agent_sets_up_a_table_through_its_tools_and_each_change_reaches_the_game(TestRunner r)
    {
        var h = await HudTests.Host(r, packs: new[] { "test" });
        h.S.Store.RegisterDef(new KTable.Core.Model.PackManifest { Id = "test", Name = "Test" }, new KTable.Core.Model.PieceDef { Id = "test.block", Name = "Block" });
        foreach (var packs in new[] { h.S.View.Packs, h.Net.Server!.State.Packs }) if (!packs.Contains("test")) packs.Add("test");
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => h.S, TableHud = () => h.Hud };
        r.AddChild(agent);
        async Task<string> Ok(string tool, string args = "{}")
        {
            var (error, text) = Answer(await agent.Handle(Call(tool, args)));
            Check.That(!error, $"{tool} answered: {text}");
            await r.Frames(2);
            return text;
        }
        async Task Try(string tool, string args = "{}")
        {
            await agent.Handle(Call(tool, args));
            await r.Frames(2);
        }
        await Try("status");
        await Try("list_kinds");
        await Ok("place_pieces", """{"kind":"test.block","count":4,"x":10,"z":10,"formation":"line"}""");
        Check.Equal(6, h.S.View.Layer(Layers.Umpire).Count, "four placed beside the two");
        await Ok("move_piece", """{"id":"p1","x":-5,"z":-5}""");
        Check.Equal(new Vec2(-5, -5), h.S.View.Find(Layers.Umpire, "p1")!.Pos, "moved");
        await Ok("set_piece_values", """{"id":"p1","label":"Grenadiers"}""");
        Check.Equal("Grenadiers", h.S.View.Find(Layers.Umpire, "p1")!.Label, "labelled");
        await Try("form_up", """{"ids":["p1","p2"],"formation":"column","x":0,"z":0}""");
        await Try("draw", """{"points":[[0,0],[5,5],[10,0]]}""");
        await Try("list_drawings");
        await Try("erase_drawings", """{"near":[0,0]}""");
        await Ok("set_briefing", """{"text":"Hold the bridge."}""");
        Check.That((await Ok("read_briefing")).Contains("Hold the bridge"), "the briefing reads back");
        await Try("set_grid", """{"kind":"square","size":5}""");
        await Try("set_light", """{"sun":30}""");
        await Try("roll_dice", """{"count":2,"sides":6}""");
        await Try("say", """{"text":"Turn one begins."}""");
        await Try("set_zone", """{"name":"North","points":[[0,0],[20,0],[20,20],[0,20]]}""");
        int turn = h.S.View.Clock.Turn;
        await Ok("end_turn");
        Check.Equal(turn + 1, h.S.View.Clock.Turn, "the turn ended");
        await Try("list_map_sheets");
        Check.That((await Ok("api_reference", """{"find":"spawn"}""")).Contains("spawn"), "the reference finds a call");
        await Ok("delete_piece", """{"id":"p2"}""");
        Check.That(h.S.View.Find(Layers.Umpire, "p2") == null, "deleted");
        await Ok("undo");
        Check.That(h.S.View.Find(Layers.Umpire, "p2") != null, "undo brings it back");
    }

    [NodeTest]
    static async Task An_agent_carries_pieces_on_the_clipboard_from_one_game_into_the_next(TestRunner r)
    {
        var rig = await HudTests.Host(r, pieces: 2, packs: new[] { "test" });
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => rig.S, TableHud = () => rig.Hud };
        r.AddChild(agent);
        async Task<string> Ok(string tool, string args)
        {
            var (error, text) = Answer(await agent.Handle(Call(tool, args)));
            Check.That(!error, $"{tool} answered: {text}");
            await r.Frames(2);
            return text;
        }
        rig.S.Do(new KTable.Core.Protocol.SetLabel { Layer = Layers.Umpire, Id = "p1", Value = "Grenadiers" });
        await r.Frames(2);
        Check.That((await Ok("copy_pieces", """{"ids":["p1","p2"]}""")).Contains("2 pieces on the clipboard"), "both copied");
        rig = await HudTests.Host(r, pieces: 0, packs: new[] { "test" });
        await r.Frames(2);
        Check.Equal(0, rig.S.View.Layer(Layers.Umpire).Count, "the next game starts empty");
        Check.That((await Ok("status", "{}")).Contains("2 pieces copied"), "the clipboard outlived the first game");
        var said = await Ok("paste_pieces", """{"x":8,"z":-3}""");
        Check.That(said.Contains("2 pieces pasted"), "pasted: " + said);
        var now = rig.S.View.Layer(Layers.Umpire);
        Check.Equal(2, now.Count, "pieces in the next game");
        Check.Equal("Grenadiers", now.Single(p => p.Label == "Grenadiers").Label, "a piece keeps its own name across the games");
        Check.Equal(new Vec2(7.5, -3), now.OrderBy(p => p.Pos.X).First().Pos, "laid out round the place asked for, as they stood");
    }

    [NodeTest]
    static async Task The_studio_paints_shapes_layers_and_a_model_through_its_steps(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var steps = new StudioSteps(h.Studio);
        string[] script =
        {
            "piece:Hussars", "view:paint", "tool:brush", "ink:#c0392b", "size:6", "stroke:4,4,60,4,60,60", "sym:x", "stroke:10,20,30,40", "sym:off",
            "tool:shape", "kind:rect", "fillmode:fill", "drag:10,10,40,30", "tool:text", "click:20,50", "type:7th", "tool:fill", "ink2:#ffffff", "click:5,60",
            "grid:on", "snap:on", "grid:off", "snap:off", "radial:6", "tool:erase", "stroke:20,20,30,30", "zoom:2", "fit", "outline:Thin", "save",
            "view:model", "mtool:paint", "mtool:look", "view:table", "turnpiece:90", "tab:1", "tab:0", "addstate:Formed",
        };
        foreach (var raw in script)
        {
            var (verb, arg) = StudioSteps.Split(raw);
            Check.That(await steps.Run(verb, arg), "the step runs: " + raw);
        }
        await r.Frames(5);
        Check.That(h.Studio.FlushPack(), "the pack saves after the painting");
        Check.That(h.OnDisk().Pieces.Any(p => p.Name == "Hussars"), "the kind is still in pack.json");
    }
}
