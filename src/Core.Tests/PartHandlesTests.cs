using KTable.Core.Model;

namespace KTable.Core.Tests;

public class PartHandlesTests
{
    const double Least = 0.1, Near = 0.01;

    static BuiltThing House(double turn = 0) => new()
    {
        Kind = BuiltKinds.Building,
        Parts = { BuiltSite.Block("house", new Spot(10, 10), Spot.Toward(turn), 4, 2), BuiltSite.Sign("wheel", new Spot(10, 10)) },
    };

    static (double Long, double Deep, Spot Middle) Shape(BuiltPart part) => (BuiltThings.Sides(part).Long, BuiltThings.Sides(part).Deep, BuiltSite.Round(part).At);

    [Fact]
    public void A_side_handle_moves_that_side_alone_and_a_corner_moves_two()
    {
        var house = House();
        Assert.Equal(0, PartHandles.Lone(house));
        var handles = PartHandles.Of(house.Parts[0]);
        Assert.Equal(PartHandles.Count, handles.Count);
        int end = PartHandles.Near(house.Parts[0], new Spot(12, 10), 0.2);
        Assert.Equal(1, end);
        var longer = PartHandles.WithPulled(house, 0, end, new Spot(15, 10.7), Least);
        var (length, depth, middle) = Shape(longer.Parts[0]);
        Assert.Equal(7, length, 2);
        Assert.Equal(2, depth, 2);
        Assert.Equal(11.5, middle.X, 2);
        Assert.True(longer.Parts[0].Kept);

        var corner = PartHandles.WithPulled(house, 0, 6, new Spot(13, 7), Least);
        (length, depth, middle) = Shape(corner.Parts[0]);
        Assert.Equal(5, length, 2);
        Assert.Equal(4, depth, 2);
        Assert.InRange((middle - new Spot(10.5, 9)).Length, 0, Near);
    }

    [Fact]
    public void A_side_never_passes_the_one_opposite_and_a_turned_building_is_pulled_along_its_own_lines()
    {
        var squeezed = PartHandles.WithPulled(House(), 0, 1, new Spot(0, 10), Least);
        Assert.Equal(Least, Shape(squeezed.Parts[0]).Long, 2);
        var turned = House(Math.PI / 2);
        var pulled = PartHandles.WithPulled(turned, 0, 1, new Spot(13, 14), Least);
        var (length, depth, middle) = Shape(pulled.Parts[0]);
        Assert.Equal(6, length, 2);
        Assert.Equal(2, depth, 2);
        Assert.InRange((middle - new Spot(10, 11)).Length, 0, Near);
    }

    [Fact]
    public void A_wing_stands_out_from_the_side_it_was_drawn_from_and_belongs_to_the_building()
    {
        var house = House();
        house.Parts[0].Roof = "hip";
        var winged = PartHandles.WithWing(house, 0, new Spot(11, 11), new Spot(11.2, 14), Least)!;
        Assert.Equal(3, winged.Parts.Count);
        var (body, wing) = (winged.Parts[0], winged.Parts[1]);
        Assert.Equal("house", wing.Class);
        Assert.Equal("hip", wing.Roof);
        Assert.True(body.Group > 0 && body.Group == wing.Group);
        Assert.Equal([0, 1], Groups.Of(winged, 0));
        var at = BuiltSite.Round(wing).At;
        Assert.InRange(at.X, 10.4, 11.6);
        Assert.True(at.Y > 11 && BuiltThings.Sides(wing).Long > 3);
        Assert.Null(PartHandles.WithWing(house, 0, new Spot(11, 11), new Spot(11, 10.5), Least));
        Assert.Equal(-1, PartHandles.Lone(winged));
    }
}
