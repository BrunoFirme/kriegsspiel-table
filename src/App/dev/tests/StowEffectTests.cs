using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class StowEffectTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task An_effect_held_over_a_piece_that_holds_goes_inside_it_like_any_piece(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, packs: new[] { FirmeEffects.PackId }, prepare: StandardPacks.Ensure);
        await r.Frame();
        var smoke = t.S.Store.Pack(FirmeEffects.PackId)!.Pieces.First(d => d.Tags.Contains("effect"));
        t.S.Do(new SetScale { Layer = U, Id = "p1", Scale = 4 });
        t.S.Do(new SetPieceOption { Layer = U, Id = "p1", Name = PieceOptions.Holds, Value = true });
        t.S.Do(new SpawnPiece { Layer = U, Def = smoke.Id, Id = "fx1", Pos = new Vec2(8, 0) });
        await r.Seconds(0.3);
        t.Table.Selection.Set(new[] { "fx1" });
        t.Table.Hand.Start(new Vec2(8, 0));
        t.Table.Hand.Move(new Vec2(1, 0));
        await r.Frames(2);
        Check.Equal("p1", t.Table.Hand.HolderUnder(new Vec2(1, 0))?.Piece.Id, "the piece under the held smoke offers to hold it");
        Check.That(t.Table.Hand.PutInto(U, "p1"), "put into it");
        await r.Seconds(0.5);
        Check.That(t.S.View.Find(U, "p1")!.Stowed?.Any(p => p.Id == "fx1") == true, "the smoke is inside the piece");
        Check.That(t.S.View.Layer(U).All(p => p.Id != "fx1"), "and off the table");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
