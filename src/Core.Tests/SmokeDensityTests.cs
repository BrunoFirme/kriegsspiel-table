using System.Globalization;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class SmokeDensityTests
{
    const string U = Layers.Umpire;

    [Theory]
    [InlineData("powder", 1.5, 1.4, 1, 1)]
    [InlineData("cannon", 1.4, 1.4, 1, 1)]
    [InlineData("powder", 1, 1, 1 / 1.5, 1 / 1.4)]
    public void A_lingering_effect_from_an_older_pack_reads_as_dense_smoke_that_looks_the_same(string kind, double amount, double wind, double baseAmount, double baseWind)
    {
        var fx = Json.Deserialize<EffectDef>(string.Create(CultureInfo.InvariantCulture, $$"""{ "kind": "{{kind}}", "amount": {{amount}}, "wind": {{wind}}, "lingers": true }"""));
        Assert.Equal(SmokeDensity.Dense, fx.Density);
        Assert.True(fx.Lingers);
        Assert.Equal(baseAmount, fx.Amount, 9);
        Assert.Equal(baseWind, fx.Wind, 9);
        Assert.Equal(amount, fx.Amount * SmokeDensity.DenseAmount(kind), 9);
        Assert.DoesNotContain("lingers", Json.Serialize(fx));
    }

    [Fact]
    public void A_pack_written_with_a_density_keeps_its_amounts()
    {
        var fx = Json.Deserialize<EffectDef>("""{ "kind": "powder", "amount": 1.2, "density": -1.5 }""");
        Assert.Equal(-1.5, fx.Density);
        Assert.Equal(1.2, fx.Amount);
        Assert.False(fx.Lingers);
        Assert.Equal(fx.Density, Json.Clone(fx).Density);
    }

    [Theory]
    [InlineData("std.effects.musket-volley-dense-smoke", null, "std.effects.musket-volley", SmokeDensity.Dense)]
    [InlineData("std.effects.battery-firing-dense-smoke", null, "std.effects.battery-firing", SmokeDensity.Dense)]
    [InlineData("std.effects.rolling-fire-dense-smoke", -0.5, "std.effects.rolling-fire", -0.5)]
    [InlineData("std.effects.musket-volley", null, "std.effects.musket-volley", null)]
    [InlineData("user.mine.musket-volley-dense-smoke", null, "user.mine.musket-volley-dense-smoke", null)]
    public void A_dense_smoke_piece_from_before_opens_as_the_plain_piece_set_dense(string def, double? smoke, string becomes, double? smokeThen)
    {
        var json = Json.Serialize(new PieceInstance { Id = "p-000001", Def = def, Smoke = smoke });
        var p = Json.Deserialize<PieceInstance>(json);
        Assert.Equal(becomes, p.Def);
        Assert.Equal(smokeThen, p.Smoke);
        Assert.Equal(smokeThen, p.Clone().Smoke);
    }

    [Fact]
    public void Smoke_is_set_on_one_piece_and_undone_back_to_how_it_was_made()
    {
        var g = CoreSamples.State();
        Assert.Null(StateMachine.Apply(g, new SetSmoke { Layer = U, Id = "p-000001", Value = SmokeDensity.VeryFine }, out var undo));
        Assert.Equal(SmokeDensity.VeryFine, g.Find(U, "p-000001")!.Smoke);
        Assert.Null(g.Find(U, "p-000002")!.Smoke);
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.Null(g.Find(U, "p-000001")!.Smoke);
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(-2.01)]
    [InlineData(double.NaN)]
    public void Smoke_beyond_very_fine_or_very_dense_is_refused(double value)
    {
        var g = CoreSamples.State();
        Assert.NotNull(StateMachine.Apply(g, new SetSmoke { Layer = U, Id = "p-000001", Value = value }, out _));
        Assert.Null(g.Find(U, "p-000001")!.Smoke);
        Assert.NotNull(StateMachine.Apply(g, new SetSmoke { Layer = U, Id = "nobody", Value = 0 }, out _));
    }
}
