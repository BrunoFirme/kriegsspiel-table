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
        var effects = new PackManifest { Id = "std.effects", Name = "Effects" };
        effects.Pieces.Add(new PieceDef { Id = "std.effects.bivouac", Name = "Bivouac" });
        var adventurer = new PackManifest { Id = MovedKinds.Adventurer, Name = "Adventurer" };
        adventurer.Pieces.Add(new PieceDef { Id = "std.adventurer.tavern", Name = "Tavern", Formerly = ["std.effects.tavern"] });
        adventurer.Pieces.Add(new PieceDef { Id = "std.adventurer.round-tracker", Name = "Round tracker", Formerly = [MovedKinds.OldTracker] });
        foreach (var pack in new[] { effects, adventurer })
        {
            Directory.CreateDirectory(Path.Combine(_root, pack.Id));
            File.WriteAllText(Path.Combine(_root, pack.Id, "pack.json"), Json.Serialize(pack));
        }
        return new PackageStore(_root);
    }

    [Fact]
    public void A_scenario_naming_the_tavern_and_round_tracker_by_their_old_ids_opens_with_them_from_the_adventurer_pack()
    {
        var store = Installed();
        var srv = GameServer.FromScenario(Json.Load<Scenario>(Fixture), store);
        var pieces = srv.State.Layer(Layers.Umpire).ToDictionary(p => p.Id, p => p.Def);
        Assert.Equal("std.adventurer.tavern", pieces["p-000001"]);
        Assert.Equal("std.adventurer.round-tracker", pieces["p-000002"]);
        Assert.Equal("std.effects.bivouac", pieces["p-000003"]);
        Assert.All(srv.State.Layer(Layers.Umpire), p => Assert.NotNull(store.Def(p.Def)));
        Assert.Contains(MovedKinds.Adventurer, srv.State.Packs);
        Assert.Contains("std.effects", srv.State.Packs);
        Assert.Empty(srv.MissingPacks);
        Assert.Equal("std.adventurer.tavern", store.CurrentId("std.effects.tavern"));
    }
}
