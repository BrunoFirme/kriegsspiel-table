using KTable.Core.Model;

namespace KTable.Core.Tests;

public class ModelMiddleTests
{
    static readonly string[] Rank =
    {
        "v -0.5 0 -1.06", "v 0.5 0 -1.06", "v 0.5 0 0.03", "v -0.5 0 0.03", "v 40 0 40",
        "f 1 2 3 4",
    };

    [Fact]
    public void A_model_drawn_behind_its_pivot_has_its_middle_that_share_of_its_footprint_ahead_once_turned_to_face_the_table()
    {
        var (x, z) = ModuleMeshes.Middle(Rank);
        Assert.Equal(0, x, 9);
        Assert.Equal(0.515 / 1.09, z, 9);
    }

    [Fact]
    public void Points_no_face_uses_leave_the_middle_alone_and_a_model_without_faces_takes_all_its_points()
    {
        Assert.Equal(ModuleMeshes.Middle(Rank), ModuleMeshes.Middle(Rank.Take(4).Append("f 1 2 3 4")));
        Assert.Equal((-0.5, -0.5), ModuleMeshes.Middle(new[] { "v 0 0 0", "v 2 0 2" }));
        Assert.Equal((0.0, 0.0), ModuleMeshes.Middle(Array.Empty<string>()));
    }

    [Theory]
    [InlineData(false, 0, 0, 0.3, 0.4)]
    [InlineData(true, 0, 0, -0.3, 0.4)]
    [InlineData(false, 90, 0, 0.3, 0)]
    [InlineData(false, 0, 90, 0, 0.4)]
    public void A_middle_off_the_pivot_lies_mirrored_when_turned_over_and_drawn_in_when_leaning(bool down, double tiltX, double tiltZ, double x, double z)
    {
        var p = new PieceInstance { FaceDown = down, TiltX = tiltX, TiltZ = tiltZ };
        var (lx, lz) = p.Lying(0.3, 0.4);
        Assert.Equal(x, lx, 9);
        Assert.Equal(z, lz, 9);
    }

    [Theory]
    [InlineData(0, -1, 2)]
    [InlineData(90, 2, 1)]
    [InlineData(180, 1, -2)]
    public void An_offset_on_a_piece_turns_onto_the_map_with_the_piece(double rot, double x, double z)
    {
        var at = Vec2.OnPiece(1, 2, rot);
        Assert.Equal(x, at.X, 9);
        Assert.Equal(z, at.Z, 9);
    }
}
