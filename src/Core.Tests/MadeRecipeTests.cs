using KTable.Core.Model;
using E = KTable.Core.Model.CrowdEntry;

namespace KTable.Core.Tests;

public class MadeRecipeTests
{
    static MadeCrowd Made(string mix, int seed = 9) => new() { Id = "m-1", Count = 200, Mix = mix, Nationality = "continental", Seed = seed, At = new Vec2(1, 1) };

    static Nationality Continental => CrowdDress.Flat(Nationalities.Named("continental")!);

    [Fact]
    public void A_preset_is_a_quick_fill_of_the_recipe_and_gives_the_people_a_crowd_made_before_recipes_had()
    {
        foreach (var mix in MadeMixes.All)
        {
            var old = Made(mix.Id);
            var filled = Made(mix.Id);
            mix.Fill().Into(filled);
            Assert.Null(filled.Problem());
            var a = CrowdMade.Town(old, Continental).Residents.Select(r => KTable.Core.Json.Serialize(r.Entry));
            var b = CrowdMade.Town(filled, Continental).Residents.Select(r => KTable.Core.Json.Serialize(r.Entry));
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void A_recipe_sets_the_callings_sex_ages_and_wealth_of_who_appears()
    {
        var recipe = CrowdMade.Read("smith:3|cleric:1, men 1, children 0, elders 0, rich, edge", MadeMixes.Of(MadeMixes.Mob).Fill())!;
        Assert.True(recipe.Edge);
        var made = Made(MadeMixes.Mob);
        recipe.Into(made);
        Assert.Null(made.Problem());
        var people = CrowdMade.Town(made, Continental).Residents.Select(r => r.Entry).ToList();
        Assert.All(people, e => Assert.True(e.Sex == E.Man && e.Band == E.Adult && e.Wealth == E.Rich && e.Calling is CrowdBaseline.Smith or CrowdBaseline.Cleric));
        Assert.True(people.Count(e => e.Calling == CrowdBaseline.Smith) > people.Count(e => e.Calling == CrowdBaseline.Cleric));
    }

    [Fact]
    public void A_recipe_out_of_bounds_is_refused()
    {
        Assert.Null(CrowdMade.Read("smith:2:3", new MadeRecipe()));
        var made = Made(MadeMixes.Townsfolk);
        made.Men = 1.5;
        Assert.NotNull(made.Problem());
        made.Men = 0.5;
        made.Callings = new() { ["smith"] = -1 };
        Assert.NotNull(made.Problem());
        made.Callings = new() { ["smith"] = 1 };
        (made.Children, made.Elders) = (0.7, 0.6);
        Assert.NotNull(made.Problem());
    }

    [Fact]
    public void A_crowd_that_appears_at_the_edge_walks_in_by_its_first_order()
    {
        var made = Made(MadeMixes.Pilgrims);
        var order = CrowdMade.WalkIn(made, new Vec2(5, 5));
        Assert.Null(order.Problem());
        Assert.Equal(made.Count, order.Count);
        Assert.Equal(new Vec2(5, 5), order.To.Single());
    }
}
