using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class NorthScriptTests
{
    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    [Fact]
    public void A_script_reads_north_as_the_scenario_sets_it()
    {
        using var t = new Table();
        t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_chat(m) game.say('north ' .. game.north) end" });
        t.Server.Scripts.Tick(0.02);
        t.Server.Handle(Table.Ump3, new Say { Text = "where" });
        t.Server.Handle(Table.Lead, new SetCalibration { North = -45 });
        t.Server.Handle(Table.Ump3, new Say { Text = "where" });
        var said = Flat(t.Server.Log).OfType<Say>().Select(s => s.Text).Where(s => s.StartsWith("north ")).ToList();
        Assert.Equal(new[] { "north 0", "north 315" }, said);
    }
}
