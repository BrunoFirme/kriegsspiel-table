using KTable.Core;
using KTable.Core.Model;

namespace KTable.Server.Tests;

public class MovedKindsTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-moved-kinds", Guid.NewGuid().ToString("N"));

    public void Dispose() => BestEffort.DeleteFolder(_root);

    static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "moved", "scenario.json");

    PackageStore Installed()
    {
        var effects = new PackManifest { Id = MovedKinds.Effects, Name = "Effects" };
        effects.Pieces.Add(new PieceDef { Id = "std.effects.bivouac", Name = "Bivouac" });
        foreach (var id in new[] { "std.effects.tavern", MovedKinds.OldTracker, "std.effects.candle-flame", "std.effects.east-door" })
            effects.Pieces.Add(new PieceDef { Id = id, Name = id, Formerly = MovedKinds.Earlier(id) });
        Directory.CreateDirectory(Path.Combine(_root, effects.Id));
        File.WriteAllText(Path.Combine(_root, effects.Id, "pack.json"), Json.Serialize(effects));
        return new PackageStore(_root);
    }

    [Fact]
    public void A_scenario_naming_the_adventuring_packs_opens_with_their_pieces_from_firmes_effects_and_props()
    {
        var store = Installed();
        var srv = GameServer.FromScenario(Json.Load<Scenario>(Fixture), store);
        var pieces = srv.State.Layer(Layers.Umpire).ToDictionary(p => p.Id, p => p.Def);
        Assert.Equal("std.effects.tavern", pieces["p-000001"]);
        Assert.Equal(MovedKinds.OldTracker, pieces["p-000002"]);
        Assert.Equal("std.effects.bivouac", pieces["p-000003"]);
        Assert.Equal("std.effects.candle-flame", pieces["p-000004"]);
        Assert.Equal("std.effects.east-door", pieces["p-000005"]);
        Assert.All(srv.State.Layer(Layers.Umpire), p => Assert.NotNull(store.Def(p.Def)));
        Assert.Equal([MovedKinds.Effects], srv.State.Packs);
        Assert.Empty(srv.MissingPacks);
        Assert.Equal("std.effects.tavern", store.CurrentId("std.adventurer.tavern"));
        Assert.Equal("std.effects.tavern", store.CurrentId("std.adventuring.tavern"));
    }
}
