using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class AgentPillTests
{
    [NodeTest]
    static async Task The_agent_pill_hides_while_off_and_keeps_the_last_six_actions_newest_first(TestRunner r)
    {
        var agent = new AgentEndpoint(new Settings());
        r.AddChild(agent);
        bool opened = false;
        var pill = AgentPill.Make(agent, () => opened = true);
        r.AddChild(pill);
        await r.Frames(1);
        Check.That(!pill.Visible, "the pill is hidden while the agent is off");
        for (int i = 1; i <= 8; i++) agent.Log("step " + i);
        Check.Equal(6, agent.Recent.Count, "actions kept");
        Check.Equal("step 8", agent.Recent[0].What, "the newest action");
        Check.Equal("step 3", agent.Recent[^1].What, "the oldest action kept");
        Check.That(!opened, "nothing opens settings by itself");
        pill.QueueFree();
        agent.QueueFree();
    }
}
