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
    public void The_brush_puts_out_the_lamps_under_it_and_lights_them_again()
    {
        var town = Settlements.Lay(Site(Manners.Western), "s", Middle, Reach, 4, 0.8, 0, Plans.Value(Plans.Own(Manners.Western, Reach * MetresToTheUnit)));
        double perMetre = 1 / MetresToTheUnit, radius = 2;
        var all = Lamps.Of(town, Manners.Modern, perMetre);
        var stroke = new List<Spot> { Middle - new Spot(3, 0), Middle + new Spot(3, 0) };
        var dark = Lamps.Switched(town, Manners.Modern, perMetre, stroke, radius, on: false)!;
        var after = Lamps.Of(dark, Manners.Modern, perMetre);
        Assert.Equal(all.Count, after.Count);
        Assert.Contains(after, l => l.Dark);
        Assert.All(after, l => Assert.Equal(l.At.FromSegment(stroke[0], stroke[1]) <= radius, l.Dark));
        Assert.Null(Lamps.Switched(dark, Manners.Modern, perMetre, stroke, radius, on: false));
        Assert.All(Lamps.Of(dark.Clone(), Manners.Modern, perMetre), l => Assert.Equal(l.At.FromSegment(stroke[0], stroke[1]) <= radius, l.Dark));
        var lit = Lamps.Switched(dark, Manners.Modern, perMetre, stroke, radius, on: true)!;
        Assert.DoesNotContain(Lamps.Of(lit, Manners.Modern, perMetre), l => l.Dark);
        Assert.All(lit.Parts, p => Assert.Null(p.Dark));
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
