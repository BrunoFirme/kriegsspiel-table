using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class CrowdedLandingTests
{
    const int Side = 40;
    const double Gap = 0.6;

    static List<FakePiece> Crowd() =>
        Enumerable.Range(0, Side * Side).Select(i => new FakePiece("p" + i, i % Side * Gap, i / Side * Gap, 1, 1)).ToList();

    [Theory, InlineData(true), InlineData(false)]
    public void A_piece_set_down_in_a_crowd_asks_only_the_pieces_it_could_rest_on(bool slide)
    {
        var table = Crowd();
        var dropped = new FakePiece("a", 0, 0, 1, 1);
        var footing = new Footing<FakePiece>((_, _) => table, _ => false);
        var at = new Vec2(Side * Gap / 2 + 0.17, Side * Gap / 2 + 0.29);
        var moving = new HashSet<FakePiece> { dropped };

        var (pos, y) = footing.Rest(dropped, at, 180, moving, slide);
        footing.Stored(dropped, pos, 180, y, moving);

        int asked = table.Sum(p => p.Asked);
        Assert.True(asked < table.Count, $"{asked} surface checks for a table of {table.Count}: each probe walked the whole crowd");
    }

    [Fact]
    public void Carried_pieces_find_their_floor_with_one_look_round_the_crowd()
    {
        var table = Crowd();
        var footing = new Footing<FakePiece>((_, _) => table, _ => false);
        var points = Enumerable.Range(0, 30).Select(i => new Vec2(10 + i % 6 * 0.7, 10 + i / 6 * 0.7)).ToList();

        footing.HighestSurface(points, Layers.Umpire);

        int asked = table.Sum(p => p.Asked);
        Assert.True(asked < table.Count, $"{asked} surface checks for {points.Count} carried pieces over a table of {table.Count}");
    }

    [Fact]
    public void Looking_only_within_reach_lands_every_piece_where_a_look_at_every_piece_would()
    {
        var rng = new Random(20261107);
        for (int trial = 0; trial < 300; trial++)
        {
            var specs = Enumerable.Range(0, 40).Select(i => (id: "p" + i, x: rng.NextDouble() * 8, z: rng.NextDouble() * 8, w: 0.3f + (float)rng.NextDouble() * 2.7f,
                d: 0.3f + (float)rng.NextDouble() * 2.7f, y: rng.Next(4) * 0.12, rot: rng.NextDouble() * 360, t: 0.05f + (float)rng.NextDouble() * 0.3f)).ToList();
            List<FakePiece> Table(bool unbounded) => specs.Select(s => new FakePiece(s.id, s.x, s.z, s.w, s.d, s.y, s.rot, s.t) { Unbounded = unbounded }).ToList();
            var near = Table(false);
            var all = Table(true);
            var dropped = new FakePiece("a", 0, 0, 0.3f + (float)rng.NextDouble() * 2, 0.3f + (float)rng.NextDouble() * 2);
            var at = new Vec2(rng.NextDouble() * 8, rng.NextDouble() * 8);
            double rot = rng.NextDouble() * 360;
            bool slide = rng.Next(2) == 0;
            var moving = new HashSet<FakePiece> { dropped };
            var withinReach = new Footing<FakePiece>((_, _) => near, _ => false);
            var everyPiece = new Footing<FakePiece>((_, _) => all, _ => false);

            var landed = withinReach.Rest(dropped, at, rot, moving, slide);
            Assert.Equal(everyPiece.Rest(dropped, at, rot, moving, slide), landed);
            Assert.Equal(everyPiece.Stored(dropped, landed.pos, rot, landed.y, moving), withinReach.Stored(dropped, landed.pos, rot, landed.y, moving));
            var points = new List<Vec2> { at, landed.pos, at + new Vec2(1.3, -0.4) };
            Assert.Equal(points.Max(p => everyPiece.SurfaceY(p, Layers.Umpire)), withinReach.HighestSurface(points, Layers.Umpire));
        }
    }
}
