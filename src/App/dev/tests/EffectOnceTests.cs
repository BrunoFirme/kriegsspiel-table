using System;
using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class EffectOnceTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task A_stopped_effect_played_once_runs_one_round_and_stops_again(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { FirmeEffects.PackId }, prepare: StandardPacks.Ensure);
        await r.Frame();
        var smoke = t.S.Store.Pack(FirmeEffects.PackId)!.Pieces.First(d => d.Tags.Contains("effect") && d.Effect is { Every: <= 0 });
        t.S.Do(new SpawnPiece { Layer = U, Def = smoke.Id, Id = "fx1", Pos = new Vec2(2, 2) });
        await r.Seconds(0.5);
        t.S.Do(new SetPieceOption { Layer = U, Id = "fx1", Name = PieceOptions.EffectOff, Value = true });
        await r.Frames(3);
        Check.Equal(0, t.Node("fx1").Emitters, "stopped, it sends nothing out");
        t.S.Do(new PlayEffect { Layer = U, Id = "fx1" });
        await r.Frames(3);
        var fx = t.Node("fx1");
        Check.That(fx.PlayingOnce && fx.Emitters > 0, "played once, it runs");
        t.S.Do(new PlayEffect { Layer = U, Id = "fx1" });
        await r.Seconds(3.5);
        Check.That(!t.Node("fx1").PlayingOnce && t.Node("fx1").Emitters == 0, "after one round it stops again");
        Check.That(t.S.View.Find(U, "fx1")!.EffectOff, "the piece is still marked stopped");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }
}
