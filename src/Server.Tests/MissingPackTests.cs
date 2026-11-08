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
    public void The_retired_red_and_blue_d6_open_as_the_white_d6_in_their_colour()
    {
        var g = new GameState { Packs = { "std.dice" } };
        g.Layers[Layers.Umpire] = new()
        {
            new PieceInstance { Id = "a", Def = "std.dice.d6-red" },
            new PieceInstance { Id = "b", Def = "std.dice.d6-blue", TintOverride = "#00FF00" },
            new PieceInstance { Id = "c", Def = "std.dice.d6" },
        };
        var srv = new GameServer(g, Store());

        Assert.Equal(new[] { ("std.dice.d6", DieColors.Red), ("std.dice.d6", "#00FF00"), ("std.dice.d6", (string?)null) },
            srv.State.Layer(Layers.Umpire).Select(p => (p.Def, p.TintOverride)));
    }

    [Fact]
    public void A_piece_whose_pack_cannot_be_told_keeps_every_missing_pack()
    {
        var srv = new GameServer(Game("inf"), Store());

        Assert.Equal(3, srv.State.Packs.Count);
        Assert.Equal(3, srv.MissingPacks.Count);
    }

    [Fact]
    public void Pieces_whose_pack_the_game_does_not_name_bring_the_installed_pack_that_makes_them()
    {
        var store = Store();
        Install(store, "tts.sleepwalkers-balkan", "sleepwalkers.1-kd");
        Install(store, "tts.other", "other.gun");
        var g = new GameState();
        g.Layers[Layers.Umpire] = new() { new PieceInstance { Id = "p1", Def = "sleepwalkers.1-kd" } };

        var srv = new GameServer(g, store);

        Assert.Equal(new[] { "tts.sleepwalkers-balkan" }, srv.State.Packs);
        Assert.Empty(srv.MissingPacks);
        Assert.NotNull(store.Def("sleepwalkers.1-kd"));
    }

    static void Install(PackageStore store, string id, string def)
    {
        Directory.CreateDirectory(store.PackageDir(id));
        Json.Save(Path.Combine(store.PackageDir(id), "pack.json"), new PackManifest { Id = id, Name = id, Pieces = { new PieceDef { Id = def, Name = def } } });
    }
}
