using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class CrowdWardrobeTests
{
    [Fact]
    public void The_fresh_body_is_a_plain_whole_look_that_builds_a_figure()
    {
        var fresh = CrowdWardrobe.Fresh();
        Assert.Null(CrowdWardrobe.Problem(fresh));
        Assert.Null(fresh.Part(Wardrobe.Coat));
        Assert.NotNull(fresh.Part(Wardrobe.Torso));
        var (rig, shape) = CrowdFigure.Whole(fresh);
        Assert.NotEmpty(rig.Points);
        Assert.True(CrowdWardrobe.Fits(fresh, CrowdWardrobe.Offer(null, null)));
    }

    [Fact]
    public void A_nationality_and_a_wealth_narrow_what_a_look_may_wear_and_dye()
    {
        var gothic = CrowdDress.Flat(Nationalities.Named("gothic")!);
        var poor = CrowdWardrobe.Offer(gothic, CrowdEntry.Poor);
        var rich = CrowdWardrobe.Offer(gothic, CrowdEntry.Rich);
        Assert.False(poor.Free);
        Assert.True(rich.Colours![CrowdSlots.Outer].Count > poor.Colours![CrowdSlots.Outer].Count);
        var look = CrowdWardrobe.Fresh();
        look.Wear(new Garment { Part = Wardrobe.Hat, Kind = "tricorne" });
        Assert.False(CrowdWardrobe.Fits(look, poor));
        Assert.True(CrowdWardrobe.Fits(look, CrowdWardrobe.Offer(null, null)));
        look.Colours[CrowdSlots.Outer] = "#ff00ff";
        Assert.False(CrowdWardrobe.Fits(look, rich));
    }

    [Fact]
    public void A_look_is_set_on_a_piece_or_a_crowd_person_in_one_undo_and_a_broken_one_is_refused()
    {
        var g = new GameState();
        g.Layers[Layers.Umpire] = [new PieceInstance { Id = "p-1", Def = "std.adventurer.fresh-body" }];
        var look = CrowdWardrobe.Fresh();
        look.Wear(new Garment { Part = Wardrobe.Hat, Kind = "cap", Slot = CrowdSlots.Hat });
        Assert.Null(StateMachine.Apply(g, new SetLook { Layer = Layers.Umpire, Id = "p-1", Look = look }, out var back));
        Assert.Equal("cap", g.Find(Layers.Umpire, "p-1")!.Look!.Part(Wardrobe.Hat)!.Kind);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Null(g.Find(Layers.Umpire, "p-1")!.Look);
        Assert.Null(StateMachine.Apply(g, new SetLook { Person = "w-1:4", Look = look }, out var undress));
        Assert.True(g.Crowds!.Dressed!.ContainsKey("w-1:4"));
        Assert.Null(StateMachine.Apply(g, undress!, out _));
        Assert.Null(g.Crowds);
        var broken = CrowdWardrobe.Fresh();
        broken.Worn.Add(new Garment { Part = Wardrobe.Hat, Kind = "crown" });
        Assert.NotNull(StateMachine.Apply(g, new SetLook { Layer = Layers.Umpire, Id = "p-1", Look = broken }, out _));
        broken = CrowdWardrobe.Fresh();
        broken.HeightM = 9;
        Assert.NotNull(StateMachine.Apply(g, new SetLook { Person = "w-1:4", Look = broken }, out _));
    }
}
