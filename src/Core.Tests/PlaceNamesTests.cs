using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class PlaceNamesTests
{
    const int Seeds = 200, LeastDifferent = 25;

    [Fact]
    public void A_seed_always_makes_the_same_name_and_seeds_make_many()
    {
        foreach (var tongue in PlaceNames.Tongues)
            foreach (var kind in new[] { NameKind.Place, NameKind.Water, NameKind.Area })
            {
                var names = Enumerable.Range(1, Seeds).Select(seed => PlaceNames.Make(tongue.Key, kind, seed)).ToList();
                Assert.Equal(names, Enumerable.Range(1, Seeds).Select(seed => PlaceNames.Make(tongue.Key, kind, seed)).ToList());
                Assert.True(names.Distinct().Count() >= LeastDifferent, $"{tongue.Name} {kind}: {names.Distinct().Count()} different names");
                Assert.All(names, name =>
                {
                    Assert.True(name.Length is >= 4 and <= PlaceNames.MostLetters, name);
                    Assert.True(char.IsUpper(name[0]), name);
                    Assert.Equal(name.Trim(), name);
                    Assert.DoesNotContain("*", name);
                    Assert.DoesNotContain("@", name);
                    Assert.DoesNotContain("  ", name);
                });
            }
    }

    [Fact]
    public void A_stem_never_meets_itself_at_the_join()
    {
        foreach (var seed in Enumerable.Range(1, Seeds * 5))
        {
            Assert.DoesNotContain("bergberg", PlaceNames.Make("german", NameKind.Area, seed).ToLowerInvariant());
            Assert.DoesNotContain("steinstein", PlaceNames.Make("german", NameKind.Place, seed).ToLowerInvariant());
            Assert.DoesNotContain("montmont", PlaceNames.Make("french", NameKind.Place, seed).ToLowerInvariant());
            Assert.DoesNotContain("novovo", PlaceNames.Make("russian", NameKind.Place, seed).ToLowerInvariant());
        }
    }

    [Fact]
    public void An_unknown_tongue_falls_back_to_the_first() =>
        Assert.Equal(PlaceNames.Make(PlaceNames.Tongues[0].Key, NameKind.Place, 7), PlaceNames.Make("elvish", NameKind.Place, 7));

    [Fact]
    public void A_name_rides_with_its_place_tidied()
    {
        var g = CoreSamples.State();
        g.Sheets.Add(new SheetDef { Id = "s-000040", Size = new Size2(40, 20) });
        g.NextId = 60;
        var thing = CoreSamples.Built("", "s-000040");
        thing.Name = "  Aubach ";
        Assert.Null(StateMachine.Apply(g, new LayBuilt { Thing = thing }, out _));
        Assert.Equal("Aubach", g.Built[0].Name);
        Assert.Equal("Aubach", BuiltGeometry.Moved(g.Built[0], 500, 0).Name);
        var renamed = g.Built[0].Clone();
        renamed.Name = new string('n', PlaceNames.MostLetters * 2);
        Assert.Null(StateMachine.Apply(g, new SetBuilt { Thing = renamed }, out var undo));
        Assert.Equal(PlaceNames.MostLetters, g.Built[0].Name!.Length);
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.Equal("Aubach", g.Built[0].Name);
        var blank = g.Built[0].Clone();
        blank.Name = "   ";
        Assert.Null(StateMachine.Apply(g, new SetBuilt { Thing = blank }, out _));
        Assert.Null(g.Built[0].Name);
        Assert.DoesNotContain("Name", Json.Serialize(g.Built[0]));
    }

    [Fact]
    public void The_name_is_lettered_east_of_everything_the_place_holds()
    {
        var thing = CoreSamples.Built("w-1", "s-000040");
        var (low, high) = BuiltGeometry.Box(thing)!.Value;
        Assert.Equal(new SheetPoint(high.X, (low.Y + high.Y) / 2), BuiltGeometry.NameSpot(thing));
        Assert.Null(BuiltGeometry.NameSpot(new BuiltThing()));
    }
}
