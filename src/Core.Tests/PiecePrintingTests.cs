using KTable.Core.Model;

namespace KTable.Core.Tests;

public class PiecePrintingTests
{
    static PieceDef Block() => new()
    {
        Id = "inf", Name = "Infantry", Image = "front", Back = "back",
        States = { new PieceStateDef { Name = "Fresh", Image = "fresh", Back = "fresh-back" }, new PieceStateDef { Name = "Shaken", Image = "shaken" } },
        Counters = { new CounterDef { Name = "Strength", Max = 12, Default = 12 }, new CounterDef { Name = "Ammo", Max = 3, Default = 3 } },
        TextLines = { new TextLineDef { Name = "Name" }, new TextLineDef { Name = "Unit", Default = "1st" } },
    };

    [Fact]
    public void The_back_card_keeps_its_title_band_and_shares_the_rest_among_the_counters()
    {
        var (title, rows) = PiecePrinting.Card(boxy: true, title: true, rows: 2);
        Assert.NotNull(title);
        Assert.Equal(0.06, title!.X, 9);
        Assert.Equal(0.04, title.Y, 9);
        Assert.Equal(0.8 * 0.24, title.H, 9);
        Assert.Equal(2, rows.Count);
        Assert.Equal(title.Y + title.H, rows[0].Y, 9);
        Assert.Equal(0.04 + 0.8, rows[1].Y + rows[1].H, 9);
        var (none, all) = PiecePrinting.Card(boxy: true, title: false, rows: 1);
        Assert.Null(none);
        Assert.Equal(0.8, all[0].H, 9);
    }

    [Fact]
    public void Only_unplaced_counters_sit_on_the_card_and_a_text_line_on_the_back_takes_the_card_s_title()
    {
        var d = Block();
        d.Counters[0].Place = new PrintPlace { Side = PrintPlace.Front };
        Assert.Equal(new[] { 1 }, PiecePrinting.CardCounters(d));
        Assert.True(PiecePrinting.CardTitle(d));
        d.TextLines[1].Place = new PrintPlace { Side = PrintPlace.Back };
        Assert.False(PiecePrinting.CardTitle(d));
    }

    [Fact]
    public void A_pack_of_the_old_format_gets_what_printed_by_itself_placed_where_it_printed_and_a_new_one_keeps_unplaced_items_unshown()
    {
        static string Pack(int format) => $$"""
            {"format":{{format}},"id":"p","name":"P","pieces":[
              {"id":"blk","name":"Block","shape":"box","nameBox":{"x":0.1,"y":0.3,"w":0.8,"h":0.2},"counters":[{"name":"Strength","max":9}],"textLines":[{"name":"Name"}]},
              {"id":"note","name":"Note","tags":["note"],"textLines":[{"name":"Name"},{"name":"Note"}]}]}
            """;
        var old = Json.Deserialize<PackManifest>(Pack(1))!;
        var blk = old.Pieces[0];
        Assert.Equal((PrintPlace.Front, 0.3, 0.2), (blk.TextLines[0].Place!.Side, blk.TextLines[0].Place!.Y, blk.TextLines[0].Place!.H));
        Assert.Equal(PrintPlace.Back, blk.Counters[0].Place!.Side);
        Assert.Null(blk.NameBox);
        Assert.All(old.Pieces[1].TextLines, t => Assert.Null(t.Place));
        var fresh = Json.Deserialize<PackManifest>(Pack(PackManifest.CurrentFormat))!;
        Assert.Null(fresh.Pieces[0].TextLines[0].Place);
        Assert.Null(fresh.Pieces[0].Counters[0].Place);
    }

    [Fact]
    public void Pinning_puts_every_unplaced_item_where_it_already_prints()
    {
        var d = Block();
        var band = PiecePrinting.Band(true);
        var rows = PiecePrinting.CardRows(d, true);
        PiecePrinting.PinDefaults(d, band, true);
        Assert.All(d.TextLines, t => Assert.Equal(PrintPlace.Front, t.Place!.Side));
        Assert.Equal(band.Y, d.TextLines[0].Place!.Y, 9);
        Assert.Equal(band.Y + band.H / 2, d.TextLines[1].Place!.Y, 9);
        Assert.Equal(band.H / 2, d.TextLines[1].Place!.H, 9);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(PrintPlace.Back, d.Counters[i].Place!.Side);
            Assert.Equal(rows[i].Y, d.Counters[i].Place!.Y, 9);
        }
    }

    [Fact]
    public void A_scripted_piece_s_counters_stay_unplaced_since_its_script_draws_the_back()
    {
        var d = Block();
        d.Script = "function on_load() end";
        PiecePrinting.PinDefaults(d, PiecePrinting.Band(true), true);
        Assert.All(d.Counters, c => Assert.Null(c.Place));
    }

    [Fact]
    public void Swapping_sides_turns_every_picture_and_place_over_leaves_what_is_not_shown_unshown_and_twice_is_where_it_began()
    {
        var d = Block();
        d.Counters[0].Place = new PrintPlace { Side = PrintPlace.Front, X = 0.2, Y = 0.7, W = 0.5, H = 0.2 };
        PiecePrinting.SwapSides(d);
        Assert.Equal(("back", "front"), (d.Image, d.Back));
        Assert.Equal(("fresh-back", "fresh"), (d.States[0].Image, d.States[0].Back));
        Assert.Equal((null, "shaken"), (d.States[1].Image, d.States[1].Back));
        Assert.Equal(PrintPlace.Back, d.Counters[0].Place!.Side);
        Assert.Equal(0.7, d.Counters[0].Place!.Y, 9);
        Assert.Null(d.Counters[1].Place);
        Assert.All(d.TextLines, t => Assert.Null(t.Place));
        var once = Json.Serialize(d);
        PiecePrinting.SwapSides(d);
        PiecePrinting.SwapSides(d);
        Assert.Equal(once, Json.Serialize(d));
    }
}
