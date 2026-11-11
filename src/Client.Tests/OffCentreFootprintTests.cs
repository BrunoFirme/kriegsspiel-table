using System.Numerics;
using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class OffCentreFootprintTests
{
    static readonly Vector2 Behind = new(0, 2.5f);

    static FakePiece Line(string id, double x, double z, double rot) => new(id, x, z, 2, 2, rot: rot, thickness: 0.3f) { GroundCentre = Behind };

    [Theory]
    [InlineData(0)]
    [InlineData(35)]
    [InlineData(180)]
    [InlineData(250)]
    public void An_off_centre_model_rests_on_what_lies_under_its_drawn_part_not_under_its_pivot(double rot)
    {
        var block = new FakePiece("block", 0, 0, 2, 2, thickness: 0.3f);
        var table = new List<FakePiece> { block };
        var footing = new Footing<FakePiece>((_, _) => table, _ => false);
        var line = new FakePiece("line", 0, 0, 0.6f, 0.6f, rot: rot) { GroundCentre = Behind };
        var moving = new HashSet<FakePiece> { line };
        var shift = line.CentreOffset(rot);

        var (_, drawnOver) = footing.Rest(line, new Vec2(-shift.X, -shift.Z), rot, moving, slide: false);
        var (_, pivotOver) = footing.Rest(line, new Vec2(0, 0), rot, moving, slide: false);

        Assert.Equal(0.3, drawnOver, 3);
        Assert.Equal(0, pivotOver, 3);
        Assert.False(footing.OnGround(line, new Vec2(-shift.X, -shift.Z), rot, moving));
        Assert.True(footing.OnGround(line, new Vec2(0, 0), rot, moving));
    }

    [Fact]
    public void Formed_in_rows_off_centre_models_put_their_drawn_middles_where_centred_ones_put_their_pivots()
    {
        const double rot = 70;
        var centred = new List<(FakePiece, Vec2)> { (new FakePiece("a", 0, 0, 2, 2, rot: rot), new Vec2(0, 0)), (new FakePiece("b", 3, 0, 2, 2, rot: rot), new Vec2(3, 0)) };
        var shifted = centred.Select(c => (Line(c.Item1.Piece.Id, c.Item2.X, c.Item2.Z, rot), c.Item2)).ToList();

        var slots = Formation.Rows(centred, rot, 1, new Vec2(5, 5));
        var pivots = Formation.Rows(shifted, rot, 1, new Vec2(5, 5));

        for (int i = 0; i < slots.Count; i++)
        {
            var middle = shifted[i].Item1.MiddleAt(pivots[i], rot);
            Assert.Equal(slots[i].X, middle.X, 6);
            Assert.Equal(slots[i].Z, middle.Z, 6);
        }
    }
}
