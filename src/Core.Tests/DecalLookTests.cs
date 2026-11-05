using KTable.Core.Model;

namespace KTable.Core.Tests;

public class DecalLookTests
{
    static DecalDef Mark(string picture) => new() { Picture = picture };

    [Fact]
    public void A_piece_shows_the_decals_of_the_look_it_is_in_when_its_kind_gives_its_looks_decals()
    {
        var def = new PieceDef
        {
            Id = "t.block",
            States =
            {
                new PieceStateDef { Name = "Line", Decals = new() { Mark("line") } },
                new PieceStateDef { Name = "Column", Decals = new() { Mark("column") } },
                new PieceStateDef { Name = "Square" },
            },
        };
        var p = new PieceInstance { Def = def.Id, Decals = { Mark("set by a script") } };

        Assert.Equal("line", Assert.Single(DecalDef.ShownOn(p, def)).Picture);
        p.State = 1;
        Assert.Equal("column", Assert.Single(DecalDef.ShownOn(p, def)).Picture);
        p.State = 2;
        Assert.Empty(DecalDef.ShownOn(p, def));
        p.State = 7;
        Assert.Empty(DecalDef.ShownOn(p, def));
    }

    [Fact]
    public void A_piece_whose_kind_gives_its_looks_no_decals_shows_its_own()
    {
        var def = new PieceDef { Id = "t.block", States = { new PieceStateDef { Name = "Fresh" }, new PieceStateDef { Name = "Spent" } } };
        var p = new PieceInstance { Def = def.Id, State = 1, Decals = { Mark("set by a script") } };

        Assert.Equal("set by a script", Assert.Single(DecalDef.ShownOn(p, def)).Picture);
        Assert.Equal("set by a script", Assert.Single(DecalDef.ShownOn(p, null)).Picture);
    }
}
