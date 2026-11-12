using KTable.Core.Model;

namespace KTable.Client.Tests;

public class PartRulesTests
{
    static PieceDef Kind(string id, params string[] parts) => new() { Id = id, Name = id, Parts = parts.Length == 0 ? null : parts.Select(p => new PartDef { Def = p }).ToList() };

    static (List<PieceDef> all, Func<string, PieceDef?> find) Pack(params PieceDef[] kinds) => (kinds.ToList(), id => kinds.FirstOrDefault(k => k.Id == id));

    [Fact]
    public void A_piece_takes_another_kind_but_not_itself_an_effect_or_a_kind_that_holds_it()
    {
        var soldier = Kind("soldier");
        var stand = Kind("stand", "soldier");
        var brigade = Kind("brigade", "stand");
        var smoke = new PieceDef { Id = "smoke", Name = "smoke", Tags = { PieceKinds.EffectTag } };
        var (all, find) = Pack(soldier, stand, brigade, smoke);

        Assert.Null(PartRules.Refusal(stand, soldier, all, find));
        Assert.NotNull(PartRules.Refusal(stand, stand, all, find));
        Assert.NotNull(PartRules.Refusal(stand, smoke, all, find));
        Assert.NotNull(PartRules.Refusal(smoke, soldier, all, find));
        Assert.NotNull(PartRules.Refusal(soldier, stand, all, find));
        Assert.NotNull(PartRules.Refusal(soldier, brigade, all, find));
        Assert.Equal(new[] { "soldier", "stand" }, PartRules.Takes(brigade, all, find).Select(k => k.Id));
        Assert.Empty(PartRules.Takes(soldier, all, find));
    }

    [Fact]
    public void Parts_stop_at_the_depth_the_table_draws()
    {
        var a = Kind("a", "b");
        var b = Kind("b", "c");
        var c = Kind("c", "d");
        var d = Kind("d");
        var e = Kind("e");
        var top = Kind("top");
        var (all, find) = Pack(a, b, c, d, e, top);

        Assert.Equal(3, PartRules.Below(a, find));
        Assert.Equal(3, PartRules.Above(d, all));
        Assert.Null(PartRules.Refusal(d, e, all, find));
        d.Parts = new() { new PartDef { Def = "e" } };
        Assert.Equal(PartRules.Deepest, PartRules.Below(a, find));
        Assert.NotNull(PartRules.Refusal(e, top, all, find));
        Assert.NotNull(PartRules.Refusal(top, a, all, find));
        Assert.Null(PartRules.Refusal(top, b, all, find));
    }

    [Fact]
    public void A_loop_already_in_a_pack_is_measured_without_end_and_refused()
    {
        var a = Kind("a", "b");
        var b = Kind("b", "a");
        var c = Kind("c");
        var (all, find) = Pack(a, b, c);

        Assert.True(PartRules.Below(a, find) > PartRules.Deepest);
        Assert.NotNull(PartRules.Refusal(c, a, all, find));
    }

    [Fact]
    public void Ten_in_two_ranks_fill_the_block_front_rank_first()
    {
        var places = PartRules.InRanks(10, 2, new Size2(5, 2));

        Assert.Equal(10, places.Count);
        Assert.Equal(new Vec2(-2, 0.5), places[0]);
        Assert.Equal(new Vec2(2, 0.5), places[4]);
        Assert.Equal(new Vec2(-2, -0.5), places[5]);
        Assert.Equal(0, places.Sum(p => p.X), 9);
    }

    [Fact]
    public void A_short_last_rank_is_centred()
    {
        var places = PartRules.InRanks(5, 2, new Size2(3, 2));

        Assert.Equal(new[] { -1.0, 0, 1 }, places.Take(3).Select(p => p.X));
        Assert.Equal(new[] { -0.5, 0.5 }, places.Skip(3).Select(p => p.X));
    }

    [Fact]
    public void Repeat_replaces_every_part_of_that_kind_and_leaves_the_others()
    {
        var flag = new PartDef { Def = "flag", X = 9 };
        var first = new PartDef { Def = "soldier", Y = 0.2, Scale = 0.5, Tint = "#112233", State = 1 };
        var list = new List<PartDef> { flag, first, new() { Def = "soldier", X = 3 } };

        PartRules.Repeat(list, first, 4, 2, new Size2(2, 2));

        Assert.Same(flag, list[0]);
        Assert.Equal(5, list.Count);
        Assert.All(list.Skip(1), p => Assert.Equal(("soldier", 0.2, 0.5, "#112233", 1), (p.Def, p.Y, p.Scale, p.Tint, p.State)));
        Assert.Equal(new[] { (-0.5, 0.5), (0.5, 0.5), (-0.5, -0.5), (0.5, -0.5) }, list.Skip(1).Select(p => (p.X, -p.Z)));
    }

    [Fact]
    public void Forgetting_a_kind_clears_it_from_every_list_and_keeps_looks_that_still_differ()
    {
        var plain = Kind("plain", "soldier", "flag");
        var looks = new PieceDef
        {
            Id = "looks", Name = "looks",
            States = { new PieceStateDef { Name = "Formed", Parts = new() { new PartDef { Def = "soldier" } } }, new PieceStateDef { Name = "Routed", Parts = new() { new PartDef { Def = "flag" } } } },
        };
        var only = Kind("only", "soldier");

        Assert.True(PartRules.Forget(new[] { plain, looks, only }, "soldier"));

        Assert.Equal(new[] { "flag" }, plain.Parts!.Select(p => p.Def));
        Assert.Empty(looks.States[0].Parts!);
        Assert.Single(looks.States[1].Parts!);
        Assert.Null(only.Parts);
        Assert.False(PartRules.Forget(new[] { plain, looks, only }, "soldier"));
    }

    [Fact]
    public void The_list_shown_and_the_list_changed_are_the_look_s_own_only_when_looks_differ()
    {
        var plain = new PieceDef { Id = "p", States = { new PieceStateDef { Name = "A" }, new PieceStateDef { Name = "B" } } };
        Assert.Null(PartRules.Shown(plain, 1));
        PartRules.Listed(plain, 1).Add(new PartDef { Def = "x" });
        Assert.Same(plain.Parts, PartRules.Shown(plain, 0));

        var own = new PieceDef { Id = "o", States = { new PieceStateDef { Name = "A", Parts = new() }, new PieceStateDef { Name = "B" } } };
        PartRules.Listed(own, 1).Add(new PartDef { Def = "x" });
        Assert.Null(own.Parts);
        Assert.Empty(PartRules.Shown(own, 0)!);
        Assert.Single(PartRules.Shown(own, 1)!);
    }
}
