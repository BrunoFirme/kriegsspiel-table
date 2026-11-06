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
    public void The_card_is_painted_at_its_size_with_a_red_title_rule_and_a_blue_rule_under_every_line()
    {
        var (px, w, h) = NoteCard.Paint();
        Assert.Equal(((int)(NoteCard.Width * NoteCard.PixelsPerUnit), (int)(NoteCard.Height * NoteCard.PixelsPerUnit)), (w, h));
        Assert.Equal(0, At(px, w, 0, 0).a);
        Assert.Equal((251, 250, 244, 255), At(px, w, w / 2, (int)(NoteCard.TitleTop * h) + 4));
        Assert.Equal((215, 90, 90, 255), At(px, w, w / 2, (int)Math.Round(NoteCard.RedRule * h)));
        for (int i = 0; i < NoteCard.Lines; i++) Assert.Equal((120, 150, 215, 255), At(px, w, w / 2, (int)Math.Round(NoteCard.RuleAt(i) * h)));
        Assert.True(NoteCard.RuleAt(NoteCard.Lines - 1) <= 1 - NoteCard.BottomMargin, "the last rule stays on the card");
        Assert.True(NoteCard.RuleAt(NoteCard.Lines) > 1 - NoteCard.BottomMargin, "every rule that fits is drawn");
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
