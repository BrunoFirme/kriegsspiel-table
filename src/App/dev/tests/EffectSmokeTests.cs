using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class EffectSmokeTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task A_placed_effect_takes_its_own_smoke_and_undo_gives_back_its_kind_s(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { FirmeEffects.PackId }, prepare: StandardPacks.Ensure);
        await r.Frame();
        var volley = FirmeEffects.IdOf("Musket volley");
        Check.That(t.S.Store.Pack(FirmeEffects.PackId)!.Pieces.Any(d => d.Id == volley), "the standard pack has the musket volley");
        Check.That(!t.S.Store.Pack(FirmeEffects.PackId)!.Pieces.Any(d => d.Id.EndsWith("-dense-smoke")), "no separate dense pieces");
        t.S.Do(new SpawnPiece { Layer = U, Def = volley, Id = "fx1", Pos = new Vec2(2, 2) });
        await r.Seconds(0.3);
        Check.Equal(SmokeDensity.Standard, t.Node("fx1").PlacedEffect.Density, "placed as its kind is made");
        t.S.Do(new SetSmoke { Layer = U, Id = "fx1", Value = SmokeDensity.VeryFine });
        await r.Frames(3);
        Check.Equal(SmokeDensity.VeryFine, t.Node("fx1").PlacedEffect.Density, "rebuilt with its own smoke");
        Check.That(t.Node("fx1").Emitters > 0, "still firing");
        t.S.UndoLast();
        await r.Frames(3);
        Check.Equal(SmokeDensity.Standard, t.Node("fx1").PlacedEffect.Density, "undo gives back its kind's smoke");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }
}
