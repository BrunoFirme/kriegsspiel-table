using KTable.Core.Model;
using KTable.Core.Protocol;
using C = KTable.Core.Model.CrowdCommands;

namespace KTable.Core.Tests;

public class CrowdMadeTests
{
    const double Upm = 0.1;

    static GameState Table(string picture = "")
    {
        var g = new GameState();
        g.Calibration.UnitsPerMeter = Upm;
        g.Sheets.Add(new SheetDef { Id = "s-1", Image = picture, Size = new Size2(200, 200) });
        return g;
    }

    static MadeCrowd Made(int count = 40, string mix = MadeMixes.Mob) =>
        new() { Id = "m-1", Name = "The mob", Count = count, Mix = mix, Nationality = CrowdBaseline.Id, At = new Vec2(10, 10), Seed = 7, Start = 1000 };

    static Nationality Flat => CrowdDress.Flat(Nationalities.Baseline);

    [Fact]
    public void A_made_crowd_is_its_count_of_people_of_its_mix_the_same_each_time_standing_close_together()
    {
        var mob = CrowdMade.Town(Made(200), Flat);
        Assert.Equal(200, mob.Residents.Count);
        Assert.True(mob.Made);
        Assert.InRange(mob.Residents.Count(r => r.Entry.Sex == CrowdEntry.Man), 130, 190);
        Assert.Equal(mob.Residents.Select(r => r.Entry.Seed), CrowdMade.Town(Made(200), Flat).Residents.Select(r => r.Entry.Seed));
        var soldiers = CrowdMade.Town(Made(30, MadeMixes.Soldiers), Flat);
        Assert.All(soldiers.Residents, r => Assert.Equal(CrowdBaseline.Soldier, r.Entry.Calling));
        var spots = Enumerable.Range(0, mob.Residents.Count).Select(i => CrowdMade.Stand(mob, i, mob.Residents[i].Entry.Seed, Upm).At).ToList();
        Assert.All(spots, s => Assert.InRange((s - mob.Places[0].At).Length / Upm, 0, 12));
        Assert.Equal(spots.Count, spots.Distinct().Count());
    }

    [Fact]
    public void A_crowd_is_made_changed_and_taken_back_in_one_step_each_and_only_on_laid_ground_within_the_cap()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, new SetMadeCrowd { Crowd = Made() }, out var take));
        Assert.Single(g.Crowds!.Made!);
        var bigger = Made(80);
        Assert.Null(StateMachine.Apply(g, new SetMadeCrowd { Crowd = bigger }, out var back));
        Assert.Equal(80, g.Crowds!.Made![0].Count);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal(40, g.Crowds!.Made![0].Count);
        Assert.Null(StateMachine.Apply(g, take!, out _));
        Assert.Null(g.Crowds);
        Assert.NotNull(StateMachine.Apply(Table("map.png"), new SetMadeCrowd { Crowd = Made() }, out _));
        var off = Made();
        off.At = new Vec2(500, 500);
        Assert.NotNull(StateMachine.Apply(g, new SetMadeCrowd { Crowd = off }, out _));
        g.Lighting.CrowdMadeMost = 100;
        Assert.NotNull(StateMachine.Apply(g, new SetMadeCrowd { Crowd = Made(101) }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetMadeCrowd { Crowd = Made(0) }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetMadeCrowd { Crowd = Made(10, "circus") }, out _));
    }

    [Fact]
    public void Leaving_walks_to_the_nearest_edge_of_the_map_and_then_they_are_gone()
    {
        var sheet = Table().Sheets[0];
        var edge = CrowdMade.EdgeOf(sheet, new Vec2(90, 10));
        Assert.Equal(100, Math.Abs(edge.X), 6);
        Assert.Equal(10, edge.Z, 6);
        var leave = new CrowdOrder { Id = "o-1", Members = new() { ["m-1"] = [0] }, Do = C.Leave, Start = 1000 };
        var track = CrowdOrders.Compile(leave, 0, 1, new Vec2(90, 10), 0, Upm, edge, (a, b) => new CrowdPath([a, b]));
        long off = track.Steps[0].End;
        Assert.True(track.At(off - 1, Upm, out var going, out _) && going.Moving);
        Assert.True(track.Covers(off + 10));
        Assert.False(track.At(off + 10, Upm, out _, out _));
    }
}
