using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class RaisedPieceTests
{
    const string U = Layers.Umpire;
    const float Near = 0.005f, Seated = 0.04f;

    static async Task<OfflineTable> Hill(TestRunner r, System.Action<GameState>? arrange = null)
    {
        var t = OfflineTable.Make(r, pieces: 3, arrange: arrange);
        var grid = ReliefGrid.Flat(40, 40);
        ReliefBrush.Stroke(grid, new[] { (0.2, 0.5), (0.8, 0.5) }, 40, new Brush(BrushMode.Raise, 0, 0.6));
        var (sha, _) = ReliefImport.Save(grid, t.S.Store.Root);
        t.S.Relief.Remember(sha, grid);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(40, 40), Relief = sha, Height = 1 } });
        await r.Seconds(0.5);
        return t;
    }

    static Vec2 Steepest(Ground g) => Enumerable.Range(-30, 61).Select(i => new Vec2(0, i * 0.25)).MaxBy(g.SlopeAt);

    [NodeTest]
    static async Task A_piece_kept_above_the_ground_with_nothing_under_it_rests_on_the_ground_unless_locked_and_one_on_another_piece_stays_on_it(TestRunner r)
    {
        var t = await Hill(r, g => g.Layers[U][2].Locked = true);
        var g = t.S.Ground;
        var alone = new Vec2(-5, 15);
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = alone, RotDeg = 0, Y = g.DrawnAt(alone) + 1 });
        var stand = new Vec2(5, 15);
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = stand, RotDeg = 0, Y = 0 });
        var held = new Vec2(-10, 15);
        double heldY = g.DrawnAt(held) + 1;
        t.S.Do(new MovePiece { Layer = U, Id = "p3", Pos = held, RotDeg = 0, Y = heldY });
        await r.Seconds(0.5);

        var p1 = t.Node("p1");
        float ground = (float)g.DrawnAt(alone);
        Check.That(p1.Position.Y - ground < Seated, $"the lone piece rests on the ground at {ground:0.000}, not at {p1.Position.Y:0.000}");
        Check.That(p1.Position.Y >= ground, $"the lone piece does not sink: {p1.Position.Y:0.000} under {ground:0.000}");
        Check.That(t.Node("p3").Position.Y >= heldY, $"the locked piece keeps its height {heldY:0.000}: {t.Node("p3").Position.Y:0.000}");

        float top = t.Node("p2").SurfaceAt(Coords.ToWorld(stand)) ?? throw new TestFailure("the lower piece has a top");
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = stand, RotDeg = 0, Y = top });
        await r.Seconds(0.5);
        Check.That(t.Node("p1").Position.Y > top - Near, $"the piece on the other stays on its top {top:0.000}: {t.Node("p1").Position.Y:0.000}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_stacked_piece_on_a_hillside_sits_on_the_slope_and_a_slight_tilt_of_its_own_does_not_lift_it(TestRunner r)
    {
        var t = await Hill(r);
        var g = t.S.Ground;
        var at = Steepest(g);
        Check.That(g.SlopeAt(at) > 0.2, $"the hillside is steep: {g.SlopeAt(at):0.00}");
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = at, RotDeg = 0, Y = 0, Stack = 1 });
        var beside = new Vec2(at.X + 6, at.Z);
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = beside, RotDeg = 0, Y = 0 });
        await r.Seconds(0.5);

        var stacked = t.Node("p1");
        var seat = g.SeatOn(stacked.MiddleAt(at, 0), 0, stacked.Footprint.X, stacked.Footprint.Y);
        float onSlope = (float)seat.Y + stacked.StackRise(1);
        Check.That(Mathf.Abs(stacked.Position.Y - onSlope) < Seated, $"the stacked piece sits on the slope at {onSlope:0.000}, not {stacked.Position.Y:0.000}");
        float highest = (float)g.Under(stacked.MiddleAt(at, 0), 0, stacked.Footprint.X, stacked.Footprint.Y) + stacked.StackRise(1);
        Check.That(stacked.Position.Y < highest, $"it no longer stands on the highest ground under it, {highest:0.000}: {stacked.Position.Y:0.000}");

        var flat = t.Node("p2");
        float middle = flat.RestBox.Origin.Y;
        t.S.Do(new TiltPiece { Layer = U, Id = "p2", X = 0, Z = 0.08 });
        await r.Seconds(0.5);
        float slopeLift = Mathf.Max(flat.Footprint.X, flat.Footprint.Y) / 2 * Mathf.Sin(Mathf.Atan((float)g.SlopeAt(beside)));
        Check.That(Mathf.Abs(flat.RestBox.Origin.Y - middle) < slopeLift / 4, $"a tilt of 0.08 degrees keeps the piece on the slope, not {slopeLift:0.000} up it: {middle:0.0000} to {flat.RestBox.Origin.Y:0.0000}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
