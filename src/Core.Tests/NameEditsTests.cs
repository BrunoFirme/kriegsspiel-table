using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class NameEditsTests
{
    const double Close = 1e-9;

    static Stroke Name(params Vec2[] points) => new() { Id = "d-000004", Points = points.ToList(), Text = "Uru", Width = 0, Letter = 2, Align = Stroke.AlignLeft };

    static void Near(Vec2 want, Vec2 got) => Assert.True((want - got).Length < 1e-6, $"{want} != {got}");

    [Fact]
    public void A_name_at_a_point_turns_about_a_middle_and_keeps_its_turn_clockwise_from_zero_to_360()
    {
        var name = Name(new Vec2(2, 0));
        var turned = NameEdits.Turned(name, new Vec2(0, 0), 90);
        Near(new Vec2(0, -2), turned.Points[0]);
        Assert.Equal(90, turned.Turn!.Value, Close);
        Assert.Equal(300, NameEdits.Turned(name, new Vec2(2, 0), -60).Turn!.Value, Close);
        Assert.Null(NameEdits.Turned(turned, new Vec2(0, 0), 270).Turn);
        Assert.Null(name.Turn);
        Assert.Equal(new Vec2(2, 0), name.Points[0]);
    }

    [Fact]
    public void A_name_along_a_path_turns_its_path_and_takes_no_turn_of_its_own()
    {
        var turned = NameEdits.Turned(Name(new Vec2(-1, 0), new Vec2(1, 0)), new Vec2(0, 0), 90);
        Near(new Vec2(0, 1), turned.Points[0]);
        Near(new Vec2(0, -1), turned.Points[1]);
        Assert.Null(turned.Turn);
    }

    [Fact]
    public void Sizing_grows_the_letters_and_the_path_about_the_middle_within_the_letter_bounds()
    {
        var sized = NameEdits.Sized(Name(new Vec2(1, 1), new Vec2(3, 1)), new Vec2(2, 1), 2, 2);
        Assert.Equal(4, sized.Letter!.Value, Close);
        Near(new Vec2(0, 1), sized.Points[0]);
        Near(new Vec2(4, 1), sized.Points[1]);
        var tiny = NameEdits.Sized(Name(new Vec2(1, 1)), new Vec2(0, 0), 1e-9, 2);
        Assert.Equal(Stroke.LeastLetter, tiny.Letter!.Value, Close);
    }

    [Fact]
    public void Moving_shifts_every_point_and_keeps_the_rest()
    {
        var moved = NameEdits.Moved(Name(new Vec2(1, 1), new Vec2(3, 2)), new Vec2(-1, 4));
        Assert.Equal(new[] { new Vec2(0, 5), new Vec2(2, 6) }, moved.Points);
        Assert.Equal(("Uru", 2.0, Stroke.AlignLeft), (moved.Text, moved.Letter!.Value, moved.Align));
    }

    [Fact]
    public void A_relaid_name_is_erased_and_drawn_again_with_its_id_at_its_place()
    {
        var layer = new List<Stroke> { new() { Id = "d-000001" }, Name(new Vec2(0, 0)), new() { Id = "d-000009" } };
        var changed = NameEdits.Moved(layer[1], new Vec2(1, 0));
        changed.Id = "";
        var batch = NameEdits.Relaid(layer, Layers.Umpire, layer[1], changed);
        var erase = Assert.IsType<EraseStroke>(batch.Items[0]);
        var draw = Assert.IsType<Draw>(batch.Items[1]);
        Assert.Equal(("d-000004", "d-000004", 1), (erase.StrokeId, draw.Stroke.Id, draw.Index!.Value));
        Assert.Equal(new Vec2(1, 0), draw.Stroke.Points[0]);
    }

    [Theory]
    [InlineData("didone", true)]
    [InlineData("capitals", true)]
    [InlineData("comic", false)]
    public void A_name_takes_only_a_known_font(string font, bool kept)
    {
        var name = Name(new Vec2(0, 0));
        name.Font = font;
        Assert.Equal(kept, name.Refused() == null);
    }

    [Fact]
    public void A_name_refuses_a_turn_that_is_not_a_number()
    {
        var name = Name(new Vec2(0, 0));
        name.Turn = double.NaN;
        Assert.Equal("turn out of range", name.Refused());
        name.Turn = 725;
        Assert.Null(name.Refused());
    }

    [Fact]
    public void A_side_handle_widens_a_text_box_keeping_the_edge_its_alignment_holds()
    {
        var name = Name(new Vec2(2, 0));
        var right = NameEdits.Widened(name, 4, 1, 2, 0.5);
        Assert.Equal(6, right.Wrap!.Value, Close);
        Near(new Vec2(2, 0), right.Points[0]);
        Near(new Vec2(0, 0), NameEdits.Widened(name, 4, -1, 2, 0.5).Points[0]);
        name.Align = Stroke.AlignCentre;
        Near(new Vec2(3, 0), NameEdits.Widened(name, 4, 1, 2, 0.5).Points[0]);
        name.Align = Stroke.AlignRight;
        Near(new Vec2(4, 0), NameEdits.Widened(name, 4, 1, 2, 0.5).Points[0]);
        name.Align = Stroke.AlignCentre;
        name.Turn = 90;
        Near(new Vec2(2, -1), NameEdits.Widened(name, 4, 1, 2, 0.5).Points[0]);
        Assert.Equal(0.5, NameEdits.Widened(name, 4, 1, -10, 0.5).Wrap!.Value, Close);
        Assert.Equal(0.5 * Stroke.MostWrapLetters, NameEdits.Widened(name, 4, 1, 1000, 0.5).Wrap!.Value, Close);
        Assert.Null(name.Wrap);
    }

    [Fact]
    public void Sizing_a_text_box_sizes_its_width_with_its_letters()
    {
        var name = Name(new Vec2(1, 1));
        name.Wrap = 5;
        Assert.Equal(10, NameEdits.Sized(name, new Vec2(0, 0), 2, 2).Wrap!.Value, Close);
        Assert.Null(NameEdits.Sized(Name(new Vec2(1, 1)), new Vec2(0, 0), 2, 2).Wrap);
    }

    [Theory]
    [InlineData(3.0, true)]
    [InlineData(0.0, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(2.0 * Stroke.MostWrapLetters, true)]
    [InlineData(2.0 * Stroke.MostWrapLetters + 0.01, false)]
    public void A_text_box_takes_only_a_width_in_range(double wrap, bool kept)
    {
        var name = Name(new Vec2(0, 0));
        name.Wrap = wrap;
        Assert.Equal(kept, name.Refused() == null);
    }
}
