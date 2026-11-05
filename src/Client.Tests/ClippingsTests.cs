using KTable.Core.Model;

namespace KTable.Client.Tests;

public class ClippingsTests
{
    readonly string _file;

    public ClippingsTests()
    {
        _file = Path.Combine(TestFolders.New(), "clipboard.json");
        Clippings.FileAt = () => _file;
    }

    static Clipping OnePiece(string def) => new()
    {
        From = "Feud at Boggs Creek",
        Packs = new() { "user.blocks" },
        Center = new Vec2(2, 3),
        Pieces = new() { new ClippedPiece { Piece = new PieceInstance { Id = "p1", Def = def, Pos = new Vec2(2, 3) }, Above = 0.5 } },
    };

    [Fact]
    public void What_was_copied_is_read_back_from_the_file_a_later_game_opens()
    {
        Clippings.Hold(OnePiece("user.blocks.foot"));
        Assert.True(File.Exists(_file));
        var back = Clippings.Held;
        Assert.NotNull(back);
        Assert.Equal("Feud at Boggs Creek", back!.From);
        Assert.Equal(new[] { "user.blocks" }, back.Packs);
        Assert.Equal(new Vec2(2, 3), back.Center);
        Assert.Equal("user.blocks.foot", back.Pieces.Single().Piece.Def);
        Assert.Equal(0.5, back.Pieces.Single().Above);
    }

    [Fact]
    public void The_newest_copy_is_the_one_held_whichever_window_wrote_it()
    {
        Clippings.Hold(OnePiece("user.blocks.foot"));
        File.WriteAllText(_file, KTable.Core.Json.Serialize(OnePiece("user.blocks.horse")));
        Assert.Equal("user.blocks.horse", Clippings.Held!.Pieces.Single().Piece.Def);
    }

    [Fact]
    public void A_damaged_or_newer_clipboard_file_leaves_the_copy_this_run_made()
    {
        Clippings.Hold(OnePiece("user.blocks.foot"));
        File.WriteAllText(_file, "{ not json");
        Assert.Equal("user.blocks.foot", Clippings.Held!.Pieces.Single().Piece.Def);
        var ahead = OnePiece("user.blocks.horse");
        ahead.Format = Clipping.CurrentFormat + 1;
        File.WriteAllText(_file, KTable.Core.Json.Serialize(ahead));
        Assert.Equal("user.blocks.foot", Clippings.Held!.Pieces.Single().Piece.Def);
    }
}
