using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class BoundRulesTests
{
    static BoundLayer Cellar() => new()
    {
        Name = "Cellar", Shapes = { new BoundShape { Polygon = { new(0, 0), new(4, 0), new(4, 4), new(0, 4) }, Floor = -1, Ceiling = 2 } },
    };

    static GameState WithCellar(out string id)
    {
        var g = new GameState();
        var set = new SetBoundLayer { Bound = Cellar() };
        Assert.Null(StateMachine.Apply(g, set, out _));
        id = set.Bound.Id;
        return g;
    }

    [Fact]
    public void A_new_layer_binds_nobody_until_a_rule_turns_it_on()
    {
        var g = WithCellar(out var cellar);
        Assert.Equal(BoundRule.Off, Bounds.RuleOf(g, "Bruno", cellar));
        Assert.False(Bounds.Binds(g, "Bruno"));
        Assert.StartsWith("bs-", g.BoundLayers![0].Shapes[0].Id);
    }

    [Fact]
    public void A_player_follows_everyone_unless_given_their_own_rule_and_undo_puts_it_back()
    {
        var g = WithCellar(out var cellar);
        Assert.Null(StateMachine.Apply(g, new SetBoundRule { LayerId = cellar, Rule = BoundRule.Out }, out _));
        Assert.Null(StateMachine.Apply(g, new SetBoundRule { Player = "Bruno", LayerId = cellar, Rule = BoundRule.In }, out var back));
        Assert.Equal(BoundRule.In, Bounds.RuleOf(g, "bruno", cellar));
        Assert.Equal(BoundRule.Out, Bounds.RuleOf(g, "Ana", cellar));
        Assert.True(Bounds.Binds(g, "Ana"));
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal(BoundRule.Out, Bounds.RuleOf(g, "Bruno", cellar));
        Assert.Null(g.PlayerBounds);
        Assert.Null(StateMachine.Apply(g, new SetBoundRule { Player = "Bruno", LayerId = cellar, Rule = BoundRule.Off }, out _));
        Assert.Equal(BoundRule.Off, Bounds.RuleOf(g, "Bruno", cellar));
        Assert.Equal(BoundRule.Off, Bounds.SetFor(g, "Bruno", cellar));
        Assert.Equal(BoundRule.Same, Bounds.SetFor(g, "Ana", cellar));
    }

    [Fact]
    public void Removing_a_layer_comes_back_in_its_place_with_its_rules_on_undo()
    {
        var g = WithCellar(out var cellar);
        Assert.Null(StateMachine.Apply(g, new SetBoundLayer { Bound = new BoundLayer { Name = "Hall" } }, out _));
        Assert.Null(StateMachine.Apply(g, new SetBoundRule { LayerId = cellar, Rule = BoundRule.In }, out _));
        Assert.Null(StateMachine.Apply(g, new RemoveBoundLayer { Id = cellar }, out var back));
        Assert.False(Bounds.Binds(g, "Bruno"));
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal(cellar, g.BoundLayers![0].Id);
        Assert.True(Bounds.Binds(g, "Bruno"));
    }

    [Fact]
    public void Shapes_without_room_or_with_broken_corners_are_refused()
    {
        var g = new GameState();
        var flat = Cellar();
        flat.Shapes[0].Ceiling = flat.Shapes[0].Floor;
        Assert.NotNull(StateMachine.Apply(g, new SetBoundLayer { Bound = flat }, out _));
        var line = Cellar();
        line.Shapes[0].Polygon.RemoveRange(2, 2);
        Assert.NotNull(StateMachine.Apply(g, new SetBoundLayer { Bound = line }, out _));
        var wild = Cellar();
        wild.Shapes[0].Polygon[1] = new(double.NaN, 0);
        Assert.NotNull(StateMachine.Apply(g, new SetBoundLayer { Bound = wild }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetBoundRule { LayerId = "b-404", Rule = BoundRule.In }, out _));
        Assert.Null(g.BoundLayers);
    }

    [Fact]
    public void A_scenario_keeps_its_layers_and_everyones_rules_and_a_save_keeps_each_player()
    {
        var g = WithCellar(out var cellar);
        Assert.Null(StateMachine.Apply(g, new SetBoundRule { LayerId = cellar, Rule = BoundRule.In }, out _));
        Assert.Null(StateMachine.Apply(g, new SetBoundRule { Player = "Ana", LayerId = cellar, Rule = BoundRule.Out }, out _));
        var started = GameState.FromScenario(g.ToScenario("s", "S"));
        Assert.Equal(BoundRule.In, Bounds.RuleOf(started, "Ana", cellar));
        var back = Json.Deserialize<GameState>(Json.Serialize(g))!;
        Assert.Equal(BoundRule.Out, Bounds.RuleOf(back, "Ana", cellar));
        Assert.Equal(4, back.BoundLayers![0].Shapes[0].Polygon.Count);
    }
}
