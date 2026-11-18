using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class RoundTrackerTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task The_standard_round_tracker_counts_a_round_and_steps_the_game_clock_when_its_button_is_pressed(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { FirmeAdventurer.PackId }, prepare: StandardPacks.Ensure);
        await r.Frame();
        var tracker = t.S.Store.Pack(FirmeAdventurer.PackId)!.Pieces.Single(RoundTracker.Is);
        Check.That(StandardPacks.Piece(t.S.Store, tracker.Id, "user.pack") != null, "it copies into a pack of one's own");
        long start = t.S.View.Clock.Now;
        t.S.Do(new SpawnPiece { Layer = U, Def = tracker.Id, Id = "rt", Pos = new Vec2(4, 4) });
        await r.Seconds(0.3);
        Check.Equal("Next round", t.S.View.Find(U, "rt")?.Widgets.FirstOrDefault()?.Label, "its button");
        t.S.Do(new ClickWidget { Layer = U, Id = "rt", Index = 1 });
        await r.Seconds(0.3);
        Check.Equal(1, t.S.View.Find(U, "rt")!.Counters.GetValueOrDefault(RoundTracker.RoundCounter), "a round counted");
        Check.Equal(start + RoundTracker.UsualSeconds, t.S.View.Clock.Now, "the clock moved by the round's seconds");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
