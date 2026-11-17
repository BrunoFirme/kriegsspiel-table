using KTable.Core.Model;

namespace KTable.Core.Tests;

public class WealthLooksTests
{
    const double PerMetre = 1, HouseLongM = 9, HouseDeepM = 7, BlockM = 18;

    static BuiltPart Part(string cls, WealthLevel? wealth = null, double longM = HouseLongM, double deepM = HouseDeepM)
    {
        var part = BuiltSite.Block(cls, new Spot(50, 50), new Spot(1, 0), longM, deepM);
        part.Wealth = wealth is { } level ? PlaceZones.Stored(level) : null;
        return part;
    }

    static string Dressed(int manner, WealthLevel level, string cls = "house") => WealthLooks.ClassOf(Part(cls, level), manner, PerMetre);

    static readonly int[] Plain = [Manners.Tribal, Manners.Mongol, Manners.Pacific];

    [Fact]
    public void An_untouched_house_resolves_to_its_own_class_in_every_culture()
    {
        foreach (var culture in Manners.All)
            foreach (string cls in new[] { "house", "townhouse", "cottage", "villa", "domus", "insula", "church", "barn" })
                Assert.Equal(cls, WealthLooks.ClassOf(Part(cls), culture.Number, PerMetre));
    }

    [Theory]
    [InlineData(Manners.Gothic, "townhouse", "cottage")]
    [InlineData(Manners.Continental, "townhouse", "cottage")]
    [InlineData(Manners.Western, "townhouse", "cottage")]
    [InlineData(Manners.Rus, "townhouse", "cottage")]
    [InlineData(Manners.American, "townhouse", "cottage")]
    [InlineData(Manners.Saracen, "villa", "house")]
    [InlineData(Manners.NorthAfrican, "villa", "house")]
    [InlineData(Manners.Nippon, "villa", "house")]
    [InlineData(Manners.Roman, "domus", "taberna")]
    public void A_rich_and_a_poor_house_take_the_classes_the_generator_gives_them(int manner, string rich, string poor)
    {
        Assert.Equal(rich, Dressed(manner, WealthLevel.Rich));
        Assert.Equal(poor, Dressed(manner, WealthLevel.Poor));
    }

    [Fact]
    public void Rich_and_poor_differ_in_every_culture_that_has_its_own_rich_and_poor_houses()
    {
        foreach (var culture in Manners.All.Where(c => !Plain.Contains(c.Number)))
            Assert.NotEqual(Dressed(culture.Number, WealthLevel.Rich), Dressed(culture.Number, WealthLevel.Poor));
        foreach (int manner in Plain)
        {
            Assert.Equal("house", Dressed(manner, WealthLevel.Rich));
            Assert.Equal("house", Dressed(manner, WealthLevel.Poor));
        }
        Assert.Equal("house", Dressed(Manners.Soviet, WealthLevel.Rich));
        Assert.Equal("cottage", Dressed(Manners.Soviet, WealthLevel.Poor));
    }

    [Fact]
    public void A_poor_roman_house_is_a_block_where_its_footprint_holds_one_and_a_booth_where_it_does_not()
    {
        Assert.Equal("insula", WealthLooks.ClassOf(Part("house", WealthLevel.Poor, BlockM, BlockM), Manners.Roman, PerMetre));
        Assert.Equal("taberna", WealthLooks.ClassOf(Part("house", WealthLevel.Poor), Manners.Roman, PerMetre));
    }

    [Fact]
    public void A_middling_rich_or_poor_house_becomes_a_plain_house_and_other_buildings_keep_their_class()
    {
        Assert.Equal("house", Dressed(Manners.Gothic, WealthLevel.Middling, "townhouse"));
        Assert.Equal("house", Dressed(Manners.Roman, WealthLevel.Middling, "insula"));
        Assert.Equal("villa", Dressed(Manners.Saracen, WealthLevel.Middling, "villa"));
        Assert.Equal("church", Dressed(Manners.Gothic, WealthLevel.Rich, "church"));
        Assert.Equal("barn", Dressed(Manners.Gothic, WealthLevel.Poor, "barn"));
    }

    [Fact]
    public void An_override_set_and_taken_back_redresses_the_house_and_restores_it()
    {
        var town = new BuiltThing { Id = "t", Kind = BuiltKinds.Settlement, Parts = [Part("house")] };
        var rich = ZoneOverrides.WithWealth(town, [0], WealthLevel.Rich);
        Assert.Equal("townhouse", WealthLooks.ClassOf(rich.Parts[0], Manners.Gothic, PerMetre));
        var poorer = ZoneOverrides.Brushed(rich, [BuiltSite.Round(rich.Parts[0]).At], 1, false)!;
        Assert.Null(poorer.Parts[0].Wealth);
        Assert.Equal("house", WealthLooks.ClassOf(poorer.Parts[0], Manners.Gothic, PerMetre));
        var poor = ZoneOverrides.Brushed(poorer, [BuiltSite.Round(poorer.Parts[0]).At], 1, false)!;
        Assert.Equal("cottage", WealthLooks.ClassOf(poor.Parts[0], Manners.Gothic, PerMetre));
        Assert.Equal("house", WealthLooks.ClassOf(town.Parts[0], Manners.Gothic, PerMetre));
    }
}
