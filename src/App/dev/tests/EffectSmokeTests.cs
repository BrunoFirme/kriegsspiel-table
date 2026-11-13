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

    [NodeTest]
    static async Task Placed_musketry_takes_its_own_intensity_and_dust_its_own_thickness(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { FirmeEffects.PackId }, prepare: StandardPacks.Ensure);
        await r.Frame();
        t.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.IdOf("Musket volley"), Id = "fx1", Pos = new Vec2(2, 2) });
        t.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.IdOf("Battalion volley"), Id = "fx2", Pos = new Vec2(2, 5) });
        t.S.Do(new SpawnPiece { Layer = U, Def = FirmeEffects.IdOf("Cavalry dust"), Id = "fx3", Pos = new Vec2(8, 2) });
        await r.Seconds(0.3);
        double amount = t.Node("fx1").EffectSpec.Amount, every = KTable.Client.Effects.EffectKinds.Interval(t.Node("fx2").EffectSpec);
        t.S.Do(new SetIntensity { Layer = U, Id = "fx1", Value = 0.5 });
        t.S.Do(new SetIntensity { Layer = U, Id = "fx2", Value = 2 });
        t.S.Do(new SetSmoke { Layer = U, Id = "fx3", Value = SmokeDensity.Fine });
        await r.Frames(3);
        Check.Equal(amount / 2, t.Node("fx1").PlacedEffect.Amount, "at will, half the shots");
        Check.Equal(every / 2, KTable.Client.Effects.EffectKinds.Interval(t.Node("fx2").PlacedEffect), "volleys twice as often");
        Check.Equal(SmokeDensity.Fine, t.Node("fx3").PlacedEffect.Density, "dust rebuilt finer");
        Check.That(t.Node("fx1").Emitters > 0 && t.Node("fx3").Emitters > 0, "still playing");
        t.S.UndoLast();
        t.S.UndoLast();
        t.S.UndoLast();
        await r.Frames(3);
        Check.Equal(amount, t.Node("fx1").PlacedEffect.Amount, "undo gives back its kind's fire");
        Check.Equal(SmokeDensity.Standard, t.Node("fx3").PlacedEffect.Density, "undo gives back its kind's dust");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }
}
