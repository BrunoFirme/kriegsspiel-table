using KTable.Core.Model;

namespace KTable.Core.Tests;

public class DoorsTests
{
    static readonly Dictionary<string, PieceDef> Kinds = new()
    {
        ["front"] = new PieceDef { Id = "front", Name = "Front door", Tags = new() { Doors.Tag } },
        ["back"] = new PieceDef { Id = "back", Name = "Back door", Tags = new() { Doors.Tag } },
        ["fire"] = new PieceDef { Id = "fire", Name = "Fire", Tags = new() { "effect" } },
    };

    static PieceDef? Find(string id) => Kinds.GetValueOrDefault(id);

    static PieceDef House()
    {
        PartDef Door(string id, bool open) => new() { Def = id, X = open ? 1 : 0, Rot = open ? 90 : 0 };
        PieceStateDef State(bool front, bool back) => new() { Parts = new() { new PartDef { Def = "fire" }, Door("front", front), Door("back", back) } };
        return new PieceDef { Id = "house", Tags = new() { Doors.BuildingTag }, States = new() { State(false, false), State(true, false), State(false, true), State(true, true) } };
    }

    [Fact]
    public void A_building_names_its_doors_and_finds_the_state_that_opens_or_shuts_just_one()
    {
        var house = House();
        Assert.Equal(2, Doors.Count(house, Find));
        Assert.Equal("Back door", Doors.Name(house, 1, Find));
        Assert.Equal(1, Doors.Toggled(house, 0, 0, Find));
        Assert.Equal(3, Doors.Toggled(house, 1, 1, Find));
        Assert.Equal(2, Doors.Toggled(house, 3, 0, Find));
        Assert.True(Doors.Open(house, 3, 1, Find));
        Assert.False(Doors.Open(house, 1, 1, Find));
    }

    [Fact]
    public void Only_a_one_door_change_counts_as_opening_a_door()
    {
        var house = House();
        Assert.True(Doors.IsToggle(house, 0, 2, Find));
        Assert.False(Doors.IsToggle(house, 0, 3, Find));
        Assert.False(Doors.IsToggle(house, 0, 0, Find));
        Assert.Null(Doors.Toggled(house, 0, 5, Find));
    }
}
