using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class FlightTests
{
    const string U = Layers.Umpire;
    const float Near = 0.01f;

    [NodeTest]
    static async Task A_flying_piece_is_drawn_at_its_altitude_with_a_line_and_its_shadow_to_the_ground_and_lands_where_it_was(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frames(2);
        var n = t.Node("p1");
        float grounded = n.Position.Y;
        t.S.Do(new SetPieceOption { Layer = U, Id = "p1", Name = PieceOptions.Flies, Value = true });
        await r.Frames(3);
        Check.That(Mathf.Abs(n.Position.Y - grounded - (float)PieceInstance.DefaultAltitude) < Near, $"it flies {PieceInstance.DefaultAltitude} up: {grounded} to {n.Position.Y}");
        var mark = n.GetChildren().OfType<LandingMark>().SingleOrDefault() ?? throw new TestFailure("the flying piece has no mark on the ground");
        Check.That(mark.DrawsLine, "a line runs down from it to the ground");

        t.S.Do(new SetAltitude { Layer = U, Id = "p1", Value = 10 });
        await r.Frames(3);
        Check.That(Mathf.Abs(n.Position.Y - grounded - 10) < Near, $"it climbs to 10: {n.Position.Y - grounded}");

        t.Table.Selection.Set(new[] { "p2" });
        t.Table.Hand.Start(t.S.View.Find(U, "p2")!.Pos);
        t.Table.Hand.Move(t.S.View.Find(U, "p1")!.Pos);
        t.Table.Hand.Drop();
        await r.Frames(10);
        Check.Equal(0.0, t.S.View.Find(U, "p2")!.Y, "a piece dropped below it lands on the ground, not on the flyer");

        t.S.Do(new SetPieceOption { Layer = U, Id = "p1", Name = PieceOptions.Flies, Value = false });
        await r.Frames(3);
        Check.That(Mathf.Abs(n.Position.Y - grounded) < Near, $"landed, it is back on the ground: {n.Position.Y} against {grounded}");
        Check.Equal(10.0, t.S.View.Find(U, "p1")!.FlightAltitude, "it keeps its altitude for the next take-off");
        await r.Frames(2);
        Check.That(!n.GetChildren().OfType<LandingMark>().Any(m => !m.IsQueuedForDeletion()), "the mark goes when it lands");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
