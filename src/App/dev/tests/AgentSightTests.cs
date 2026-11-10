using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class AgentSightTests
{
    static Task<(bool error, string text)> Ask(AgentEndpoint agent, string tool, string args) => AgentRecordTests.Ask(agent, tool, args);

    static async Task<JsonNode> Rpc(AgentEndpoint agent, string method) =>
        JsonNode.Parse((await agent.Handle($$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}"}"""))!)!["result"]!;

    [NodeTest]
    static async Task The_instructions_and_every_tool_s_description_stay_short_enough_for_any_agent_to_read_whole(TestRunner r)
    {
        var (_, agent) = await AgentRecordTests.Open(r);
        var instructions = (string?)(await Rpc(agent, "initialize"))["instructions"] ?? "";
        Check.That(instructions.Length is > 0 and <= AgentGuide.InstructionsMost, $"the instructions are {instructions.Length} characters, at most {AgentGuide.InstructionsMost}");
        var tools = (await Rpc(agent, "tools/list"))["tools"]!.AsArray();
        var longest = tools.Select(t => ((string?)t!["name"], ((string?)t["description"] ?? "").Length)).OrderByDescending(x => x.Item2).First();
        Check.That(longest.Item2 <= AgentGuide.DescriptionMost, $"{longest.Item1}'s description is {longest.Item2} characters, at most {AgentGuide.DescriptionMost}");
        var names = tools.Select(t => (string?)t!["name"]).ToHashSet();
        var (_, guide) = await Ask(agent, "how_to", "{}");
        var named = System.Text.RegularExpressions.Regex.Matches(guide, @"\b[a-z]+(?:_[a-z]+)+\b").Select(m => m.Value).Where(w => !w.StartsWith("on_")).Distinct().ToList();
        var unknown = named.Where(n => !names.Contains(n)).ToList();
        Check.That(unknown.Count == 0, "how_to names only tools there are: " + string.Join(", ", unknown));
    }

    [NodeTest]
    static async Task How_to_gives_a_topic_finds_a_tool_and_the_reference_comes_as_contents_and_pages(TestRunner r)
    {
        var (_, agent) = await AgentRecordTests.Open(r);
        var (_, table) = await Ask(agent, "how_to", """{"topic":"table"}""");
        Check.That(table.Contains("place_pieces") && !table.Contains("studio_make_block"), "the table topic: " + table);
        var (_, found) = await Ask(agent, "how_to", """{"find":"make_unit"}""");
        Check.That(found.StartsWith("units:"), "find names the topic: " + found);
        var (_, contents) = await Ask(agent, "api_reference", """{"contents":true}""");
        Check.That(contents.Split('\n').All(l => l.StartsWith('#') || l.Length == 0), "contents are headings only");
        var (_, page) = await Ask(agent, "api_reference", """{"offset":100}""");
        Check.That(page.StartsWith("characters 100–"), "a page says where it is: " + page[..Math.Min(120, page.Length)]);
    }

    [NodeTest]
    static async Task What_is_at_a_point_names_the_piece_its_zone_and_its_ground_and_pointers_are_read(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        h.S.Do(new KTable.Core.Protocol.SetNote { Layer = Layers.Umpire, Id = "p1", Value = "Holds the ford" });
        h.S.Do(new KTable.Core.Protocol.SetZone { Zone = new Zone { Name = "West", Owner = "Ann", Polygon = { new Vec2(-5, -5), new Vec2(5, -5), new Vec2(5, 5), new Vec2(-5, 5) } } });
        await r.Frames(2);
        var (error, at) = await Ask(agent, "what_is_at", """{"x":1,"z":0}""");
        Check.That(!error && at.Contains("\"p1\"") && at.Contains("Holds the ford") && at.Contains("West") && at.Contains("\"ground\""), "the point is described: " + at);
        Check.That(!at.Contains("\"p2\""), "a piece two units off is not there");
        var (_, none) = await Ask(agent, "read_pointers", "{}");
        Check.That(none.StartsWith("No other umpire"), "no pointer seen yet: " + none);
        h.Table.Pointers.ShowRemote(7, "Ann", new Vec2(3, 4));
        var (_, seen) = await Ask(agent, "read_pointers", "{}");
        Check.That(seen.Contains("Ann") && seen.Contains("\"x\": 3"), "Ann's pointer is read: " + seen);
    }

    static int PictureWidth(string response)
    {
        var content = JsonNode.Parse(response)!["result"]!["content"]!.AsArray();
        var img = new Image();
        img.LoadPngFromBuffer(Convert.FromBase64String((string)content[1]!["data"]!));
        return img.GetWidth();
    }

    [ScreenTest]
    static async Task A_snapshot_comes_at_the_size_asked_zooms_into_a_region_and_leaves_the_panels_out_and_back(TestRunner r)
    {
        var (h, agent) = await AgentRecordTests.Open(r);
        await r.Frames(5);
        string Call(string args) => $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"snapshot","arguments":{{{args}}}}}""";
        int small = PictureWidth((await agent.Handle(Call("""{"pixels":400}""")))!);
        Check.That(small is > 0 and <= 400, $"a small picture: {small}");
        int window = (int)h.Hud.GetViewport().GetVisibleRect().Size.X;
        int zoomed = PictureWidth((await agent.Handle(Call("""{"region":[0,0,0.5,0.5],"pixels":3840}""")))!);
        Check.That(Math.Abs(zoomed - window / 2) <= 2, $"the region is half the window at full sharpness: {zoomed} of {window}");
        PictureWidth((await agent.Handle(Call("""{"table_only":true}""")))!);
        Check.That(h.Hud.Visible, "the panels come back after a table-only picture");
        var (refused, _) = await Ask(agent, "snapshot", """{"region":[0.5,0,0.2,1]}""");
        Check.That(refused, "a region turned inside out is refused");
    }
}
