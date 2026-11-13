using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class EffectIntensityTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void Intensity_is_set_on_one_piece_and_undone_back_to_how_it_was_made()
    {
        var g = CoreSamples.State();
        Assert.Null(StateMachine.Apply(g, new SetIntensity { Layer = U, Id = "p-000001", Value = 0.5 }, out var undo));
        Assert.Equal(0.5, g.Find(U, "p-000001")!.Intensity);
        Assert.Null(g.Find(U, "p-000002")!.Intensity);
        Assert.Equal(0.5, g.Find(U, "p-000001")!.Clone().Intensity);
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.Null(g.Find(U, "p-000001")!.Intensity);
    }

    [Theory]
    [InlineData(4.5)]
    [InlineData(0.05)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Intensity_beyond_its_range_is_refused(double value)
    {
        var g = CoreSamples.State();
        Assert.NotNull(StateMachine.Apply(g, new SetIntensity { Layer = U, Id = "p-000001", Value = value }, out _));
        Assert.Null(g.Find(U, "p-000001")!.Intensity);
        Assert.NotNull(StateMachine.Apply(g, new SetIntensity { Layer = U, Id = "nobody", Value = 1 }, out _));
    }

    [Fact]
    public void A_piece_s_intensity_is_saved_only_when_it_has_one()
    {
        var p = new PieceInstance { Id = "p-1", Def = "d", Intensity = 2 };
        Assert.Contains("\"intensity\":2", Json.Serialize(p));
        Assert.DoesNotContain("intensity", Json.Serialize(new PieceInstance { Id = "p-2", Def = "d" }));
        Assert.Equal(2, Json.Deserialize<PieceInstance>(Json.Serialize(p)).Intensity);
    }
}
