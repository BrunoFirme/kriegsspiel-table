using KTable.Core.Model;

namespace KTable.Core.Tests;

public class CrowdOutcomeTests
{
    [Fact]
    public void An_outcome_falls_on_the_nearest_first_spread_over_the_time_given_and_the_seized_are_held_by_their_nearest_captor()
    {
        var people = Enumerable.Range(0, 10).Select(k => ($"m-1:{k}", new Vec2(k, 0))).ToList();
        var dead = CrowdOutcomes.Spread(people, new Vec2(-1, 0), 3, CrowdFates.Dead, 1000, 300);
        Assert.Equal(["m-1:0", "m-1:1", "m-1:2"], dead.Keys.Order());
        Assert.Equal([1000L, 1100, 1200], dead.Values.Select(f => f!.At).Order());
        var held = CrowdOutcomes.Spread(people, new Vec2(10, 0), 2, CrowdFates.Held, 0, 0, [("w-1:0", new Vec2(9, 0)), ("w-1:1", new Vec2(0, 0))]);
        Assert.All(held.Values, f => Assert.Equal("w-1:0", f!.By));
        var crowds = new CrowdState { Fates = held.ToDictionary(f => f.Key, f => f.Value!) };
        Assert.Equal(2, crowds.HeldBy(new HashSet<string> { "w-1:0" }).Count());
        Assert.Empty(crowds.HeldBy(new HashSet<string> { "w-1:1" }));
        Assert.Empty(CrowdOutcomes.Spread(people, default, 0, CrowdFates.Down, 0, 60));
        var healed = new CrowdState { Fates = new() { ["m-1:0"] = new CrowdFate { State = CrowdFates.Down, At = 100, Until = 400 } } };
        Assert.Equal(CrowdFates.Down, healed.FateOf("m-1:0", 399));
        Assert.Null(healed.FateOf("m-1:0", 400));
    }
}
