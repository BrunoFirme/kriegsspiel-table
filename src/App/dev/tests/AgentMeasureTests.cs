using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Core.Protocol;

namespace KTable.App;

static class AgentMeasureTests
{
    static string Call(string tool, string args) => $$$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";

    static async Task<(JsonNode? answer, bool error, string text)> Ask(AgentEndpoint agent, string tool, string args)
    {
        var res = JsonNode.Parse((await agent.Handle(Call(tool, args)))!)!["result"]!;
        var text = (string?)res["content"]![0]!["text"] ?? "";
        bool error = (bool)res["isError"]!;
        return (error ? null : JsonNode.Parse(text), error, text);
    }

    static async Task<(HudTests.Rig h, AgentEndpoint agent)> Open(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var agent = new AgentEndpoint(new Settings()) { TableSession = () => h.S, TableHud = () => h.Hud };
        r.AddChild(agent);
        return (h, agent);
    }

    [NodeTest]
    static async Task The_agent_measures_leg_by_leg_with_compass_bearings(TestRunner r)
    {
        var (_, agent) = await Open(r);
        var (m, error, text) = await Ask(agent, "measure", """{"stops":["p1",[1,3],[4,3]]}""");
        Check.That(!error, "it measures: " + text);
        var legs = m!["legs"]!.AsArray();
        Check.Equal((3.0, 0.0), ((double)legs[0]!["map_units"]!, (double)legs[0]!["bearing"]!), "up the map is north, 0°");
        Check.Equal((3.0, 90.0), ((double)legs[1]!["map_units"]!, (double)legs[1]!["bearing"]!), "right is east, 90°");
        Check.Equal(6.0, (double)m["total_map_units"]!, "the legs add up");
        var (_, refused, _) = await Ask(agent, "measure", """{"stops":["p1"]}""");
        Check.That(refused, "one stop is not a measure");
    }

    [NodeTest]
    static async Task In_range_lists_the_pieces_within_reach_in_the_map_s_scale(TestRunner r)
    {
        var (h, agent) = await Open(r);
        var (m, error, text) = await Ask(agent, "in_range", """{"id":"p1","reach":1.5}""");
        Check.That(!error, "it answers: " + text);
        Check.Equal("p2", string.Join(",", m!["pieces"]!.AsArray().Select(p => (string?)p!["id"])), "only the piece within reach");
        h.S.Do(new SetCalibration { UnitsPerMeter = 0.01 });
        await r.Frames(2);
        var (scaled, _, scaledText) = await Ask(agent, "in_range", """{"id":"p1","reach":50}""");
        Check.Equal(0, (int)scaled!["within"]!, "50 m is half a map unit here: " + scaledText);
        var (far, _, farText) = await Ask(agent, "in_range", """{"id":"p1","reach":150}""");
        Check.That((int)far!["within"]! == 1 && ((string?)far["pieces"]![0]!["distance"])!.StartsWith("100 m"), "150 m reaches the piece 100 m off: " + farText);
        var (_, noReach, _) = await Ask(agent, "in_range", """{"id":"p1"}""");
        Check.That(noReach, "without a range of its own it needs a reach");
    }
}
