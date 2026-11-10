using KTable.Core.Model;

namespace KTable.Core.Tests;

public class NoteCardTests
{
    static (byte r, byte g, byte b, byte a) At(byte[] px, int w, int x, int y)
    {
        int o = (y * w + x) * 4;
        return (px[o], px[o + 1], px[o + 2], px[o + 3]);
    }

    [Fact]
    public void The_note_is_painted_at_its_size_as_a_message_pad_with_a_binding_strip_a_title_band_and_a_rule_under_every_line()
    {
        var (px, w, h) = NoteCard.Paint();
        Assert.Equal(((int)(NoteCard.Width * NoteCard.PixelsPerUnit), (int)(NoteCard.Height * NoteCard.PixelsPerUnit)), (w, h));
        Assert.Equal((26, 25, 26, 255), At(px, w, 0, 0));
        Assert.Equal((186, 180, 156, 255), At(px, w, w - 1, h - 1));
        Assert.Equal((175, 171, 143, 255), At(px, w, w / 2, (int)(NoteCard.TitleTop * h) + 4));
        Assert.Equal((39, 34, 29, 255), At(px, w, w / 2, (int)Math.Round(NoteCard.HeadRule * h)));
        Assert.Equal((186, 180, 156, 255), At(px, w, w / 2, (int)Math.Round((NoteCard.RuleAt(0) + NoteCard.RuleStep / 2) * h)));
        for (int i = 0; i < NoteCard.Lines; i++) Assert.Equal((149, 141, 106, 255), At(px, w, w / 2, (int)Math.Round(NoteCard.RuleAt(i) * h)));
        Assert.True(NoteCard.RuleAt(NoteCard.Lines - 1) <= 1 - NoteCard.BottomMargin, "the last rule stays on the card");
        Assert.True(NoteCard.RuleAt(NoteCard.Lines) > 1 - NoteCard.BottomMargin, "every rule that fits is drawn");
    }

    [Fact]
    public void The_message_pad_keeps_the_ten_lines_the_index_card_had_and_stacks_its_parts_in_order()
    {
        Assert.Equal(10, NoteCard.Lines);
        Assert.True(NoteCard.BindingBottom < NoteCard.Perforation && NoteCard.Perforation < NoteCard.TitleTop, "the perforation runs between the binding and the title band");
        Assert.True(NoteCard.TitleTop < NoteCard.TitleBottom && NoteCard.TitleBottom <= NoteCard.HeadRule && NoteCard.HeadRule < NoteCard.FirstRule - NoteCard.RuleStep / 2, "the title sits in its band above the head rule, clear of the first line");
    }

    [Fact]
    public void A_note_is_any_piece_tagged_note_and_its_two_lines_carry_title_and_body()
    {
        Assert.True(NoteCard.Is(new PieceDef { Tags = new() { NoteCard.Tag } }));
        Assert.False(NoteCard.Is(new PieceDef { Tags = new() { "card" } }));
        Assert.False(NoteCard.Is(null));
        var lines = NoteCard.TextLines("Orders", "Hold");
        Assert.Equal(new[] { (NoteCard.TitleLine, "Orders"), (NoteCard.BodyLine, "Hold") }, lines.Select(l => (l.Name, l.Default)));
    }
}
