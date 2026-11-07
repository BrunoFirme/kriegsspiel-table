using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class TiltTests : ConverterHarness
{
    [Fact]
    public void A_flat_piece_keeps_what_is_left_of_its_turn_once_heading_and_face_down_are_taken_out()
    {
        Assert.Equal(new PieceTilt.Pose(30, false, 0, 90), PieceTilt.Of(0, 30, 90, model: false, text: false));
        Assert.Equal(new PieceTilt.Pose(30, true, 0, 0), PieceTilt.Of(0, 30, 180, model: false, text: false));
        Assert.Equal(new PieceTilt.Pose(210, true, 0, 10), PieceTilt.Of(180, 30, 10, model: false, text: false));
        Assert.Equal(new PieceTilt.Pose(30, false, 0, 0), PieceTilt.Of(0.01, 30, 359.99, model: false, text: false));
    }

    [Fact]
    public void A_model_keeps_its_whole_turn_and_three_d_text_is_measured_from_lying_flat()
    {
        Assert.Equal(new PieceTilt.Pose(30, false, 0, -180), PieceTilt.Of(0, 30, 180, model: true, text: false));
        Assert.Equal(new PieceTilt.Pose(30, false, 15, 0), PieceTilt.Of(105, 30, 0, model: false, text: true));
    }

    [Fact]
    public void A_die_keeps_how_far_it_leans_off_the_face_it_shows()
    {
        Assert.Equal(new PieceTilt.Pose(30, false, 20, 0), PieceTilt.OfDie(30, false, 20, 0, [0, 1, 0]));
        Assert.Equal(new PieceTilt.Pose(30, false, 0, 0), PieceTilt.OfDie(30, false, 0, -90, [1, 0, 0]));
        Assert.Equal(new PieceTilt.Pose(30, false, 15, 0), PieceTilt.OfDie(30, false, 15, -90, [1, 0, 0]));
        Assert.Equal(new PieceTilt.Pose(30, false, 0, 0), PieceTilt.OfDie(30, false, 180, 0, [0, -1, 0]));
        Assert.Equal(new PieceTilt.Pose(30, false, 0, 0), PieceTilt.OfDie(30, false, 20, 0, null));
    }

    [Fact]
    public void A_turned_box_reaches_as_high_and_as_low_as_its_turned_corners()
    {
        var (low, high) = PieceTilt.Span(1, 1, -0.5, 0.5, 0, 45);

        Assert.Equal((-0.7071, 0.7071), (Math.Round(low, 4), Math.Round(high, 4)));
    }

    [Fact]
    public void A_triangle_block_on_its_side_keeps_its_tilt_and_what_rests_on_it_sits_on_its_turned_top()
    {
        TempConfig();
        var side = Obj("BlockTriangle", "Side", 1);
        side["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = 0.5, ["posZ"] = 0.0, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 90.0, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 };
        var cap = Obj("BlockSquare", "Cap", 0.3);
        cap["Transform"] = new JsonObject { ["posX"] = 0.0, ["posY"] = 1.6, ["posZ"] = 0.0, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 0.0, ["scaleX"] = 0.3, ["scaleY"] = 0.3, ["scaleZ"] = 0.3 };
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", Module("tilted", side, cap), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var board = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "tilted", "scenario.json")).UmpireBoard;
        var block = board.Single(p => p.Label == "Side");
        var onTop = board.Single(p => p.Label == "Cap");
        Assert.Equal((0.0, 90.0), (block.TiltX, block.TiltZ));
        Assert.Equal(1, onTop.Stack);
        Assert.Equal(DefFactory.TriangleAcross, onTop.Y, 2);
    }
}
