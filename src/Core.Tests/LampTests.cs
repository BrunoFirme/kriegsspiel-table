using KTable.Core.Model;

namespace KTable.Core.Tests;

public class LampTests
{
    const double Side = 60, MetresToTheUnit = 25, Reach = 9;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = 1, Manner = manner };

    static List<Lamp> Of(int manner, double reach = Reach, double houses = 0.8, double walls = 0) =>
        Lamps.Of(Settlements.Lay(Site(manner), "s", Middle, reach, 4, houses, walls, Plans.Value(Plans.Own(manner, reach * MetresToTheUnit))), Manners.Named(manner).Epoch, 1 / MetresToTheUnit);

    static int Count(List<Lamp> lamps, int kind) => lamps.Count(l => l.Kind == kind);

    [Fact]
    public void Each_age_lights_its_town_its_own_way()
    {
        var modern = Of(Manners.Western);
        var later = Of(Manners.Continental);
        var ancient = Of(Manners.Roman);
        Assert.True(Count(modern, Lamps.Electric) > 2 * Count(later, Lamps.Lantern) && Count(modern, Lamps.Electric) > 4 * Count(modern, Lamps.Lantern), $"{Count(modern, Lamps.Electric)} electric lamps, {Count(later, Lamps.Lantern)} lanterns");
        Assert.True(Count(later, Lamps.Lantern) > 8 && Count(later, Lamps.Electric) == 0);
        Assert.Equal(0, Count(ancient, Lamps.Electric) + Count(ancient, Lamps.Lantern));
        Assert.True(Count(ancient, Lamps.Fire) + Count(ancient, Lamps.Torch) > 2, "an ancient town has its fires and torches");
        Assert.All(new[] { modern, later, ancient }, lamps => Assert.True(Count(lamps, Lamps.Window) > 5, "and lit windows"));
        Assert.True(Count(Of(Manners.Continental, 3, 0.4), Lamps.Lantern) <= 2, "a village has no street lanterns");
    }

    [Fact]
    public void A_walled_town_lights_its_gates_and_a_place_without_a_scale_lights_nothing()
    {
        var walled = Lamps.Of(Settlements.Lay(Site(Manners.Continental), "s", Middle, Reach, 4, 0.8, 0.5, 0), Manners.Later, 1 / MetresToTheUnit);
        Assert.True(walled.Count(l => l.Kind == Lamps.Lantern) > Of(Manners.Continental).Count(l => l.Kind == Lamps.Lantern));
        var town = Settlements.Lay(Site(Manners.Continental), "s", Middle, Reach, 4, 0.8, 0, 0);
        Assert.Empty(Lamps.Of(town, Manners.Later, 0));
        Assert.Equal(Lamps.Of(town, Manners.Later, 1 / MetresToTheUnit), Lamps.Of(town, Manners.Later, 1 / MetresToTheUnit));
    }

    [Fact]
    public void Lamps_glow_from_dusk_and_not_when_turned_off()
    {
        var light = new Lighting();
        Assert.Equal(0, Lamps.Glow(light, 1.2));
        Assert.Equal(1, Lamps.Glow(light, 0.3));
        Assert.InRange(Lamps.Glow(light, 0.5), 0.1, 0.9);
        light.LampsOut = true;
        Assert.Equal(0, Lamps.Glow(light, 0.3));
    }
}
