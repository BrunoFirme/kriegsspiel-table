using KTable.Core;
using KTable.Core.Model;

namespace KTable.Server.Tests;

public class MissingPackTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-missing-packs", Guid.NewGuid().ToString("N"));

    public void Dispose() => BestEffort.DeleteFolder(_root);

    PackageStore Store()
    {
        Directory.CreateDirectory(_root);
        return new PackageStore(_root);
    }

    static GameState Game(params string[] defs)
    {
        var g = new GameState { Packs = { "gone.blocks", "gone.blocks.extra", "never.used" } };
        g.Layers[Layers.Umpire] = defs.Select((d, i) => new PieceInstance { Id = "p" + i, Def = d }).ToList();
        return g;
    }

    [Fact]
    public void A_missing_pack_no_piece_uses_is_dropped_and_one_still_used_is_kept_and_warned_of()
    {
        var srv = new GameServer(Game("gone.blocks.extra.gun"), Store());

        Assert.Equal(new[] { "gone.blocks.extra" }, srv.State.Packs);
        Assert.Equal(new[] { "gone.blocks.extra" }, srv.MissingPacks);
    }

    [Fact]
    public void Once_its_pieces_are_gone_the_next_open_forgets_the_pack()
    {
        Assert.Empty(new GameServer(Game(), Store()).MissingPacks);
    }

    [Fact]
    public void A_piece_whose_pack_cannot_be_told_keeps_every_missing_pack()
    {
        var srv = new GameServer(Game("inf"), Store());

        Assert.Equal(3, srv.State.Packs.Count);
        Assert.Equal(3, srv.MissingPacks.Count);
    }
}
