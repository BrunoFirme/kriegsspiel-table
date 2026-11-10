using KTable.Core;
using KTable.Core.Model;

namespace KTable.Server.Tests;

public sealed class FormerKindTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-former-kinds", Guid.NewGuid().ToString("N"));

    public void Dispose() => BestEffort.DeleteFolder(_root);

    PackageStore Store(params PieceDef[] defs)
    {
        var store = new PackageStore(_root);
        Directory.CreateDirectory(store.PackageDir("tts.balkan"));
        Json.Save(Path.Combine(store.PackageDir("tts.balkan"), "pack.json"), new PackManifest { Id = "tts.balkan", Name = "Balkan", Pieces = defs.ToList() });
        return store;
    }

    static PieceDef Kind(string id, params string[] formerly) => new() { Id = id, Name = id, Formerly = formerly.Length > 0 ? formerly.ToList() : null };

    [Fact]
    public void Pieces_saved_under_a_former_kind_open_as_the_kind_that_replaced_it_stowed_ones_too()
    {
        var store = Store(Kind("balkan.inf-new", "balkan.inf-old"), Kind("balkan.gun"));
        var g = new GameState { Packs = { "tts.balkan" } };
        g.Layers[Layers.Umpire] = new()
        {
            new PieceInstance { Id = "a", Def = "balkan.inf-old" },
            new PieceInstance { Id = "b", Def = "balkan.gun", Stowed = new() { new PieceInstance { Id = "c", Def = "balkan.inf-old" } } },
        };

        var srv = new GameServer(g, store);

        var pieces = srv.State.Layer(Layers.Umpire);
        Assert.Equal(new[] { "balkan.inf-new", "balkan.gun" }, pieces.Select(p => p.Def));
        Assert.Equal("balkan.inf-new", pieces[1].Stowed![0].Def);
        Assert.Empty(srv.MissingPacks);
    }

    [Fact]
    public void A_piece_of_a_former_kind_brings_the_installed_pack_whose_kind_replaced_it()
    {
        var store = Store(Kind("balkan.inf-new", "balkan.inf-old"));
        var g = new GameState();
        g.Layers[Layers.Umpire] = new() { new PieceInstance { Id = "a", Def = "balkan.inf-old" } };

        var srv = new GameServer(g, store);

        Assert.Equal(new[] { "tts.balkan" }, srv.State.Packs);
        Assert.Equal("balkan.inf-new", srv.State.Layer(Layers.Umpire)[0].Def);
    }

    [Fact]
    public void A_kind_still_in_a_pack_wins_over_a_kind_that_names_it_as_former()
    {
        var store = Store(Kind("balkan.inf-new", "balkan.inf-old"), Kind("balkan.inf-old"));
        store.Pack("tts.balkan");

        Assert.Equal("balkan.inf-old", store.Def("balkan.inf-old")!.Id);
        Assert.Equal("balkan.inf-old", store.CurrentId("balkan.inf-old"));
    }

    [Fact]
    public void A_forgotten_pack_no_longer_answers_for_its_former_kinds()
    {
        var store = Store(Kind("balkan.inf-new", "balkan.inf-old"));
        store.Pack("tts.balkan");
        Assert.Equal("balkan.inf-new", store.Def("balkan.inf-old")!.Id);
        Assert.Equal("tts.balkan", store.PackOfDef("balkan.inf-old")!.Id);

        store.Forget("tts.balkan");

        Assert.Null(store.Def("balkan.inf-old"));
        Assert.Equal("balkan.inf-old", store.CurrentId("balkan.inf-old"));
    }
}
