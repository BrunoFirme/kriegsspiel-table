using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class AgentPillTests
{
    const int TrialPort = 47961;

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

    [NodeTest]
    static async Task The_agent_pill_stays_hidden_while_on_until_an_agent_calls(TestRunner r)
    {
        var agent = new AgentEndpoint(new Settings { AgentPort = TrialPort });
        r.AddChild(agent);
        var pill = AgentPill.Make(agent, () => { });
        r.AddChild(pill);
        agent.TurnOn(remember: false);
        try
        {
            await r.Frames(2);
            Check.That(agent.On, "the endpoint is on");
            Check.That(!pill.Visible, "no pill while no agent has called");
            using var http = new System.Net.Http.HttpClient();
            using var ask = new HttpRequestMessage(HttpMethod.Post, agent.Address)
            {
                Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", Encoding.UTF8, "application/json"),
            };
            ask.Headers.TryAddWithoutValidation("Authorization", "Bearer " + agent.Token);
            var sent = http.SendAsync(ask);
            for (int i = 0; i < 120 && !sent.IsCompleted; i++) await r.Frame();
            await r.Frames(2);
            Check.That(pill.Visible, "the pill shows once an agent has called");
        }
        finally
        {
            agent.TurnOff(remember: false);
            pill.QueueFree();
            agent.QueueFree();
        }
    }
}
