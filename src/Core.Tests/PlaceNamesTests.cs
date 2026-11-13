using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class PlaceNamesTests
{
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
