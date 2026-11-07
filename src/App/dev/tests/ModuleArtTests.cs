using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ModuleArtTests
{
    [NodeTest]
    static Task A_tokens_saved_outline_is_walled_along_its_own_points_and_a_loop_of_fewer_than_three_is_left_out(TestRunner r)
    {
        var square = new List<Vec2> { new(0, 0), new(1, 0), new(1, 1), new(0, 1) };
        var walls = PieceArt.OutlineWalls(new List<List<Vec2>> { square, new() { new(0, 0), new(1, 1) } }, 2f, 4f, 0.2f);

        Check.That(walls != null, "the outline is walled");
        var aabb = walls!.GetAabb();
        Check.Equal(6 * 4, walls.SurfaceGetArrayLen(0), "one quad of two triangles for each side of the square, none for the two-point loop");
        Check.That(Mathf.IsEqualApprox(aabb.Size.X, 2f) && Mathf.IsEqualApprox(aabb.Size.Z, 4f) && Mathf.IsEqualApprox(aabb.Size.Y, 0.2f), $"the walls fill the footprint: {aabb.Size}");
        Check.That(PieceArt.OutlineWalls(new List<List<Vec2>>(), 1f, 1f, 0.1f) == null, "no loop, no walls");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_module_line_ends_round_when_asked_and_flat_otherwise(TestRunner r)
    {
        var mat = new StandardMaterial3D();
        var points = new List<Vec2> { new(0, 0), new(2, 0) };
        var round = new ImmediateMesh();
        var flat = new ImmediateMesh();

        Ribbon.Draw(round, mat, points, 0.1, 0, null, round: true);
        Ribbon.Draw(flat, mat, points, 0.1, 0);

        Check.Equal(3, round.GetSurfaceCount(), "the line and a cap at each end");
        Check.Equal(1, flat.GetSurfaceCount(), "the line alone");
        Check.Equal(6, Ribbon.CapSteps(0.1), "the game's cap: four plus seven times the root of the width");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_tint_that_colours_only_a_pieces_edge_never_fades_its_pictures(TestRunner r)
    {
        var halfRed = new Color(1, 0, 0, 0.5f);

        Check.Equal(Colors.White, PieceArt.PictureTint(false, false, halfRed, Colors.White), "a tile's pictures stay whole");
        Check.Equal(halfRed, PieceArt.PictureTint(true, false, halfRed, Colors.White), "a token's picture takes the tint");
        Check.Equal(0f, PieceArt.PictureTint(false, true, halfRed, Colors.White).A, "a piece drawn in live lettering hides its picture");
        return Task.CompletedTask;
    }
}
