using KTable.Client.Hand;
using KTable.Server;

namespace KTable.Client.Tests;

public class CarryShareTests
{
    static readonly CarriedPiece A = new("umpire", "p-1", 1, 0, 90, 0);
    static readonly CarriedPiece B = new("umpire", "p-2", -1, 0, 90, 0.5f);

    readonly CarryShare _share = new();
    readonly List<(int Pieces, HandAt At)> _layouts = new();
    readonly List<HandAt> _places = new();

    public CarryShareTests()
    {
        _share.SendLayout += (pieces, at, _) => _layouts.Add((pieces.Count, at));
        _share.SendPlace += (at, _) => _places.Add(at);
    }

    [Fact]
    public void Nothing_is_sent_while_nothing_is_held()
    {
        _share.Tick(0, [], new(0, 0, 0));
        _share.Tick(1, [], new(1, 0, 0));
        Assert.Empty(_layouts);
        Assert.Empty(_places);
    }

    [Fact]
    public void A_carry_starts_with_its_layout_then_sends_only_the_hand_thirty_times_a_second()
    {
        _share.Tick(0, [A, B], new(0, 0, 2));
        for (var i = 1; i <= 9; i++) _share.Tick(i * 0.01, [A, B], new(i, 0, 2));
        _share.Tick(0.1, [A, B], new(10, 0, 2));
        Assert.Equal(new[] { 2 }, _layouts.Select(l => l.Pieces));
        Assert.Equal(new[] { 4f, 8f }, _places.Select(p => p.X));
    }

    [Fact]
    public void A_hand_held_still_sends_nothing_but_the_layout_twice_a_second()
    {
        for (var i = 0; i <= 100; i++) _share.Tick(i * 0.01, [A], new(3, 3, 2));
        Assert.Equal(3, _layouts.Count);
        Assert.Empty(_places);
    }

    [Fact]
    public void A_changed_selection_sends_its_layout_at_once_and_a_moving_one_twenty_times_a_second()
    {
        _share.Tick(0, [A], new(0, 0, 2));
        _share.Tick(0.01, [A, B], new(0, 0, 2));
        _share.Tick(0.02, [A with { Dx = 2 }, B], new(0, 0, 2));
        _share.Tick(0.07, [A with { Dx = 3 }, B], new(0, 0, 2));
        _share.Tick(0.08, [A with { Dx = 4 }, B], new(0, 0, 2));
        _share.Tick(0.13, [A with { Dx = 5 }, B], new(0, 0, 2));
        Assert.Equal(new[] { 1, 2, 2, 2 }, _layouts.Select(l => l.Pieces));
    }

    [Fact]
    public void Letting_go_sends_an_empty_layout_once()
    {
        _share.Tick(0, [A], new(0, 0, 2));
        _share.Tick(0.1, [], new(0, 0, 2));
        _share.Tick(0.2, [], new(0, 0, 2));
        Assert.Equal(new[] { 1, 0 }, _layouts.Select(l => l.Pieces));
    }
}

public class CarryWireTests
{
    [Fact]
    public void A_layout_survives_the_wire()
    {
        List<CarriedPiece> pieces = [new("umpire", "p-1", 1.5f, -2, 270, 0.25f), new("players", "p-2", 0, 0, 0, 0)];
        var (layers, ids, poses) = CarryWire.Pack(pieces);
        Assert.Equal(pieces, CarryWire.Unpack(layers, ids, poses));
    }

    [Fact]
    public void A_broken_layout_is_refused()
    {
        Assert.Null(CarryWire.Unpack(["umpire"], ["p-1", "p-2"], new float[8]));
        Assert.Null(CarryWire.Unpack(["umpire"], ["p-1"], new float[3]));
        Assert.Null(CarryWire.Unpack(["umpire"], ["p-1"], [float.NaN, 0, 0, 0]));
        Assert.Null(CarryWire.Unpack(null, ["p-1"], new float[4]));
        var many = Enumerable.Repeat("x", CarryWire.MaxPieces + 1).ToArray();
        Assert.Null(CarryWire.Unpack(many, many, new float[many.Length * 4]));
        Assert.False(CarryWire.IsFinite(new HandAt(float.PositiveInfinity, 0, 0)));
    }
}
