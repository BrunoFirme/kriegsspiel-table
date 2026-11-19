using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class CrowdScriptTests
{
    const string Town = "w-000001", Vagrants = "m-000002", Hans = Town + ":3", Ulrich = Vagrants + ":0";

    sealed class Watch : ICrowdWatch
    {
        readonly List<CrowdSeen> _people =
        [
            new(Hans, "Hans", "baker", CrowdEntry.Man, CrowdEntry.Adult, CrowdEntry.Middle, Town, "well", new(0, 0), 0, true),
            new(Town + ":4", "Greta", "spinner", CrowdEntry.Woman, CrowdEntry.Elder, CrowdEntry.Poor, Town, null, new(30, 0), 0, false),
            new(Ulrich, "Ulrich", "beggar", CrowdEntry.Man, CrowdEntry.Youth, CrowdEntry.Poor, Vagrants, "square", new(100, 0), 0, true),
        ];

        public IReadOnlyList<CrowdOwner> Owners() => [new(Town, "Lindau", false, 2, 2), new(Vagrants, "Vagrants", true, 1, 1)];
        public IReadOnlyList<CrowdSeen> OutNow(string? owner) => _people.Where(p => owner == null || p.Owner == owner).ToList();
        public CrowdSeen? Find(string id) => _people.FirstOrDefault(p => p.Id == id);
    }

    static Table Peopled()
    {
        var t = new Table();
        t.Server.State.Built.Add(new BuiltThing { Id = Town, Kind = BuiltKinds.Settlement, Name = "Lindau" });
        (t.Server.State.Crowds ??= new CrowdState()).Made = [new MadeCrowd { Id = Vagrants, Name = "Vagrants", Count = 1 }];
        t.Server.Crowds = new Watch();
        return t;
    }

    static string Lua(Table t, string target, string code) =>
        t.Server.Request(Table.Host, new RunLua { Target = target, Code = code }).Where(o => o.PeerId == Table.Host && o.Message is ScriptConsole)
            .SelectMany(o => ((ScriptConsole)o.Message).Lines).LastOrDefault(l => l.Kind is "result" or "error")?.Text ?? "";

    static string Refused(List<Outgoing> res) => Assert.IsType<ErrorMessage>(Assert.Single(res).Message).Text;

    [Fact]
    public void A_town_s_script_runs_as_its_town_lists_its_own_people_keeps_its_state_and_goes_with_the_town()
    {
        using var t = Peopled();
        int before = t.Server.Scripts.Loaded;
        t.Ok(Table.Lead, new SetCrowdScript { Id = Town, Text = "seen = #self.people() function on_load(s) was = s end function on_save() return 'counted ' .. seen end" });
        Assert.Equal(before + 1, t.Server.Scripts.Loaded);
        Assert.Equal("Lindau 2 false", Lua(t, CrowdScript.Of(Town), "return self.name .. ' ' .. seen .. ' ' .. tostring(self.made)"));
        t.Server.Scripts.SaveAll();
        Assert.Equal("counted 2", t.Server.State.Crowds!.Scripts![Town].State);
        t.Ok(Table.Lead, new SetCrowdScript { Id = Town, Text = "seen = #self.people() function on_load(s) was = s end" });
        Assert.Equal("counted 2", Lua(t, CrowdScript.Of(Town), "return was"));
        Assert.Contains(t.Server.Scripts.Describe(), i => i.Id == CrowdScript.Of(Town) && i.Name == "Lindau" && i.Status == "running");
        t.Ok(Table.Host, new RemoveBuilt { Id = Town });
        Assert.Equal(before, t.Server.Scripts.Loaded);
    }

    [Fact]
    public void A_made_crowd_s_script_hears_the_turn_and_its_people_speak()
    {
        using var t = Peopled();
        t.Ok(Table.Lead, new SetCrowdScript { Id = Vagrants, Text = "function on_turn() self.people()[1].speak('Alms!') end" });
        t.Ok(Table.Host, new AdvanceClock());
        var said = t.Server.Log.OfType<Speak>().Last();
        Assert.Equal((CrowdPiece.Layer, Ulrich, "Alms!"), (said.Layer, said.Id, said.Text));
        Assert.Equal("true", Lua(t, CrowdScript.Of(Vagrants), "return tostring(self.made)"));
    }

    [Fact]
    public void Only_a_lead_writes_a_town_s_script_and_only_for_a_town_or_crowd_on_the_table()
    {
        using var t = Peopled();
        Assert.Contains("not allowed", Refused(t.Server.Handle(Table.Ump3, new SetCrowdScript { Id = Town, Text = "x = 1" })));
        Assert.Contains("no such town or crowd", Refused(t.Server.Handle(Table.Lead, new SetCrowdScript { Id = "w-404", Text = "x = 1" })));
        t.Ok(Table.Lead, new SetCrowdScript { Id = Town, Text = "x = 1" });
        t.Ok(Table.Lead, new SetCrowdScript { Id = Town, Text = "" });
        Assert.Null(t.Server.State.Crowds?.Scripts);
    }
}
