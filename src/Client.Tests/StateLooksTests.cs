using KTable.Core.Model;

namespace KTable.Client.Tests;

public class StateLooksTests
{
    static PieceDef Block(int states)
    {
        var d = new PieceDef { Image = "f1", Size = new Size2(2, 1), Shape = "box", Thickness = 0.2, Tint = "#ffffff" };
        for (int i = 0; i < states; i++) d.States.Add(i == 0 ? StateLooks.FirstOf(d) : new PieceStateDef { Name = $"S{i + 1}", Image = $"f{i + 1}" });
        return d;
    }

    [Fact]
    public void Changing_the_first_state_leaves_the_others_as_they_looked()
    {
        var d = Block(3);
        StateLooks.SetSize(d, 0, new Size2(3, 3));
        StateLooks.SetShape(d, 0, "circle");
        StateLooks.SetThickness(d, 0, 0.5);
        StateLooks.SetTint(d, 0, "#ff0000");
        StateLooks.SetTintsPictures(d, 0, true);
        Assert.Equal(new Size2(3, 3), StateLooks.SizeOf(d, 0));
        Assert.Equal("circle", StateLooks.ShapeOf(d, 0));
        for (int i = 1; i < 3; i++)
        {
            Assert.Equal(new Size2(2, 1), StateLooks.SizeOf(d, i));
            Assert.Equal("box", StateLooks.ShapeOf(d, i));
            Assert.Equal(0.2, StateLooks.ThicknessOf(d, i));
            Assert.Equal("#ffffff", StateLooks.TintOf(d, i));
            Assert.False(StateLooks.TintsPicturesOf(d, i));
        }
    }

    [Fact]
    public void Changing_a_later_state_leaves_the_first_alone()
    {
        var d = Block(2);
        StateLooks.SetSize(d, 1, new Size2(5, 5));
        StateLooks.SetTint(d, 1, "#00ff00");
        Assert.Equal(new Size2(2, 1), StateLooks.SizeOf(d, 0));
        Assert.Equal("#ffffff", StateLooks.TintOf(d, 0));
        Assert.Equal(new Size2(5, 5), StateLooks.SizeOf(d, 1));
    }

    [Fact]
    public void A_state_without_a_picture_keeps_the_first_states_picture_when_that_changes()
    {
        var d = Block(2);
        d.States[1].Image = null;
        d.Outline = [[new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1)]];
        StateLooks.PinOthers(d);
        Assert.Equal("f1", d.States[1].Image);
        Assert.Same(d.Outline, d.States[1].Outline);
    }

    [Fact]
    public void Shapes_follow_the_whole_piece_until_one_state_gets_its_own()
    {
        var d = Block(2);
        d.AddOnMesh = "old";
        Assert.Equal("old", StateLooks.AddOnOf(d, 1).Mesh);
        StateLooks.SetAddOn(d, 1, new StateLooks.AddOn("tent", "tex", null));
        Assert.Equal("old", StateLooks.AddOnOf(d, 0).Mesh);
        Assert.Equal("tent", StateLooks.AddOnOf(d, 1).Mesh);
        Assert.Equal("old", d.AddOnMesh);
    }

    [Fact]
    public void Shapes_added_to_one_state_stay_off_the_others()
    {
        var d = Block(3);
        StateLooks.SetAddOn(d, 2, new StateLooks.AddOn("flag", "tex", "fin"));
        Assert.Null(StateLooks.AddOnOf(d, 0).Mesh);
        Assert.Null(StateLooks.AddOnOf(d, 1).Mesh);
        Assert.Equal(new StateLooks.AddOn("flag", "tex", "fin"), StateLooks.AddOnOf(d, 2));
        Assert.Null(d.AddOnMesh);
        StateLooks.SetAddOn(d, 2, default);
        Assert.False(StateLooks.OwnAddOns(d));
        Assert.Null(StateLooks.AddOnOf(d, 2).Mesh);
    }

    [Fact]
    public void A_copy_of_the_first_state_carries_its_whole_look()
    {
        var d = Block(1);
        d.Back = "b1";
        d.TintsPictures = true;
        var copy = StateLooks.Copy(d, 0);
        Assert.Equal("f1", copy.Image);
        Assert.Equal("b1", copy.Back);
        Assert.Equal(new Size2(2, 1), copy.Size);
        Assert.Equal("box", copy.Shape);
        Assert.Equal(0.2, copy.Thickness);
        Assert.True(copy.TintsPictures);
        Assert.EndsWith(" copy", copy.Name);
    }

    [Fact]
    public void Taking_away_the_first_state_keeps_the_next_ones_shapes()
    {
        var d = Block(3);
        StateLooks.SetAddOn(d, 0, new StateLooks.AddOn("cyl", "t", null));
        StateLooks.Remove(d, 0);
        Assert.Equal(2, d.States.Count);
        Assert.Null(StateLooks.AddOnOf(d, 0).Mesh);
        Assert.Null(d.AddOnMesh);
    }

    [Fact]
    public void The_last_state_left_becomes_the_piece()
    {
        var d = Block(2);
        StateLooks.SetSize(d, 1, new Size2(4, 4));
        StateLooks.SetAddOn(d, 1, new StateLooks.AddOn("tent", "t", null));
        StateLooks.Remove(d, 0);
        Assert.Empty(d.States);
        Assert.Equal("f2", d.Image);
        Assert.Equal(new Size2(4, 4), d.Size);
        Assert.Equal("tent", d.AddOnMesh);
    }

    [Fact]
    public void A_model_state_keeps_its_model_when_the_first_one_is_rebuilt()
    {
        var d = new PieceDef { Kind = "model", Mesh = "m1", Texture = "t1", FinishTexture = "fin1" };
        d.States.Add(StateLooks.FirstOf(d));
        d.States.Add(new PieceStateDef { Name = "Two" });
        Assert.Equal("fin1", StateLooks.FinishOf(d, 1));
        StateLooks.PinOthers(d);
        d.Mesh = d.States[0].Mesh = "m2";
        d.Texture = d.States[0].Texture = "t2";
        d.FinishTexture = null;
        Assert.Equal("m1", StateLooks.MeshOf(d, 1));
        Assert.Equal("t1", StateLooks.TextureOf(d, 1));
        Assert.Equal("fin1", StateLooks.FinishOf(d, 1));
        Assert.Null(StateLooks.FinishOf(d, 0));
    }

    static PrintPlace At(string side, double y) => new() { Side = side, X = 0.1, Y = y, W = 0.8, H = 0.15 };

    [Fact]
    public void A_counter_prints_where_its_state_puts_it()
    {
        var d = Block(3);
        var c = new CounterDef { Name = "Strength", Place = At(PrintPlace.Front, 0.1) };
        d.Counters.Add(c);
        Assert.Equal(0.1, StateLooks.PrintOf(d, 2, c)!.Y);
        StateLooks.SetPrint(d, 1, c, null);
        StateLooks.SetPrint(d, 2, c, At(PrintPlace.Back, 0.5));
        Assert.Equal(0.1, StateLooks.PrintOf(d, 0, c)!.Y);
        Assert.Null(StateLooks.PrintOf(d, 1, c));
        Assert.Equal(PrintPlace.Back, StateLooks.PrintOf(d, 2, c)!.Side);
    }

    [Fact]
    public void Moving_a_print_on_the_first_state_leaves_the_others_where_they_were()
    {
        var d = Block(2);
        var t = new TextLineDef { Name = "Name", Place = At(PrintPlace.Front, 0.1) };
        d.TextLines.Add(t);
        StateLooks.SetPrint(d, 0, t, At(PrintPlace.Front, 0.7));
        Assert.Equal(0.7, StateLooks.PrintOf(d, 0, t)!.Y);
        Assert.Equal(0.1, StateLooks.PrintOf(d, 1, t)!.Y);
    }

    [Fact]
    public void A_renamed_or_removed_counter_takes_its_state_places_along()
    {
        var d = Block(2);
        var c = new CounterDef { Name = "Strength", Place = At(PrintPlace.Front, 0.1) };
        d.Counters.Add(c);
        StateLooks.SetPrint(d, 1, c, At(PrintPlace.Back, 0.4));
        StateLooks.Rename(d, c, "Men");
        Assert.Equal(("Men", 0.4), (c.Name, StateLooks.PrintOf(d, 1, c)!.Y));
        StateLooks.Forget(d, c);
        Assert.Null(d.States[1].Prints);
    }

    [Fact]
    public void Taking_the_first_state_away_hands_its_successor_s_prints_to_the_piece()
    {
        var d = Block(3);
        var c = new CounterDef { Name = "Strength", Place = At(PrintPlace.Front, 0.1) };
        d.Counters.Add(c);
        StateLooks.SetPrint(d, 1, c, At(PrintPlace.Back, 0.6));
        StateLooks.Remove(d, 0);
        Assert.Equal((PrintPlace.Back, 0.6), (StateLooks.PrintOf(d, 0, c)!.Side, StateLooks.PrintOf(d, 0, c)!.Y));
        Assert.Equal((PrintPlace.Front, 0.1), (StateLooks.PrintOf(d, 1, c)!.Side, StateLooks.PrintOf(d, 1, c)!.Y));
    }

    [Fact]
    public void Swapping_sides_turns_every_state_s_own_prints()
    {
        var d = Block(2);
        var c = new CounterDef { Name = "Strength", Place = At(PrintPlace.Front, 0.1) };
        d.Counters.Add(c);
        StateLooks.SetPrint(d, 1, c, At(PrintPlace.Back, 0.6));
        PiecePrinting.SwapSides(d);
        Assert.Equal((PrintPlace.Back, PrintPlace.Front), (StateLooks.PrintOf(d, 0, c)!.Side, StateLooks.PrintOf(d, 1, c)!.Side));
    }

    [Fact]
    public void A_model_shared_with_another_state_or_piece_is_found()
    {
        var d = Block(2);
        d.AddOnMesh = "a";
        var other = new PieceDef { Kind = "model", Mesh = "m" };
        Assert.True(StateLooks.MeshShared([d, other], d, 1, "a"));
        Assert.True(StateLooks.MeshShared([d, other], d, 0, "m"));
        Assert.False(StateLooks.MeshShared([d, other], d, 0, "z"));
        var alone = new PieceDef { AddOnMesh = "a" };
        Assert.False(StateLooks.MeshShared([alone], alone, 0, "a"));
    }
}
