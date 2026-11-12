using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class FlightTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void A_piece_takes_off_at_the_default_altitude_and_lands_keeping_the_altitude_it_was_given()
    {
        var g = CoreSamples.State();
        var p = g.Find(U, "p-000001")!;
        Assert.Equal((false, 0.0, false), (p.Flies, p.Aloft, p.PassesThrough));

        Assert.Null(StateMachine.Apply(g, new SetPieceOption { Layer = U, Id = p.Id, Name = PieceOptions.Flies, Value = true }, out var land));
        Assert.Equal((true, PieceInstance.DefaultAltitude, true), (p.Flies, p.Aloft, p.PassesThrough));

        Assert.Null(StateMachine.Apply(g, new SetAltitude { Layer = U, Id = p.Id, Value = 12 }, out var lower));
        Assert.Equal(12, p.Aloft);
        Assert.Null(StateMachine.Apply(g, land!, out _));
        Assert.Equal((false, 0.0, 12.0), (p.Flies, p.Aloft, p.FlightAltitude));
        Assert.Null(StateMachine.Apply(g, new SetPieceOption { Layer = U, Id = p.Id, Name = PieceOptions.Flies, Value = true }, out _));
        Assert.Equal(12, p.Aloft);

        Assert.Null(StateMachine.Apply(g, lower!, out _));
        Assert.Equal(PieceInstance.DefaultAltitude, p.Aloft);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0.49)]
    [InlineData(40.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_altitude_off_the_scale_is_refused_and_none_is_the_default(double value)
    {
        var g = CoreSamples.State();
        Assert.NotNull(StateMachine.Apply(g, new SetAltitude { Layer = U, Id = "p-000001", Value = value }, out _));
        Assert.Equal(0, g.Find(U, "p-000001")!.Altitude);
        Assert.NotNull(StateMachine.Apply(g, new SetAltitude { Layer = U, Id = "nobody", Value = 5 }, out _));
        Assert.Null(StateMachine.Apply(g, new SetAltitude { Layer = U, Id = "p-000001", Value = 0 }, out _));
        Assert.Equal(PieceInstance.DefaultAltitude, g.Find(U, "p-000001")!.FlightAltitude);
    }

    [Fact]
    public void Flight_is_kept_in_a_copy_and_through_a_save()
    {
        var p = new PieceInstance { Id = "a", Def = "d", Flies = true, Altitude = 8 };
        Assert.Equal((true, 8.0), (p.Clone().Flies, p.Clone().Altitude));
        var back = Json.Deserialize<PieceInstance>(Json.Serialize(p))!;
        Assert.Equal((true, 8.0), (back.Flies, back.Altitude));
        Assert.DoesNotContain("flies", Json.Serialize(new PieceInstance { Id = "b", Def = "d" }), System.StringComparison.OrdinalIgnoreCase);
    }
}
