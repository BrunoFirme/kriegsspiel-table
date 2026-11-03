using System.Numerics;
using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class ShakeTests
{
    const double Frame = 1 / 60.0;

    static List<double> Run(Shake s, double from, double to, Func<double, Vector2> path)
    {
        var made = new List<double>();
        for (double t = from; t <= to; t += Frame)
            if (s.Add(t, path(t))) made.Add(t);
        return made;
    }

    static Func<double, Vector2> SideToSide(float amp = 80, double hz = 4) => t => new Vector2(500 + amp * (float)Math.Sin(2 * Math.PI * hz * t), 400);

    [Fact]
    public void Three_quick_shakes_in_place_make_one_at_once()
    {
        var made = Run(new Shake(), 0, 0.7, SideToSide());
        Assert.Single(made);
        Assert.True(made[0] < 0.5, $"made at {made[0]} s");
    }

    [Fact]
    public void Up_and_down_counts_too()
        => Assert.Single(Run(new Shake(), 0, 0.7, t => new Vector2(500, 400 + 70 * (float)Math.Sin(2 * Math.PI * 4 * t))));

    [Fact]
    public void Carrying_steadily_or_unsteadily_and_looking_about_is_never_a_shake()
    {
        var s = new Shake();
        Assert.Empty(Run(s, 0, 3, t => new Vector2(100 + 400 * (float)t, 400 + 15 * (float)Math.Sin(2 * Math.PI * 3 * t))));
        Assert.Empty(Run(s, 3, 7, t => new Vector2(500 + 450 * (float)Math.Sin(2 * Math.PI * 0.6 * t), 400)));
        Assert.Empty(Run(s, 7, 11, SideToSide(amp: 60, hz: 1)));
    }

    [Fact]
    public void A_tremble_too_small_to_be_a_stroke_is_never_a_shake()
        => Assert.Empty(Run(new Shake(), 0, 3, SideToSide(amp: 15, hz: 6)));

    [Fact]
    public void A_shake_while_sweeping_across_the_screen_is_not_in_place()
        => Assert.Empty(Run(new Shake(), 0, 1, t => new Vector2(100 + 900 * (float)t + 80 * (float)Math.Sin(2 * Math.PI * 4 * t), 400)));

    [Fact]
    public void Shaking_on_shakes_again_every_three_turns_and_stops_when_the_hand_is_still()
    {
        var s = new Shake();
        var made = Run(s, 0, 3, SideToSide());
        Assert.InRange(made.Count, 6, 10);
        Assert.Empty(Run(s, 3, 3.4, _ => new Vector2(500, 400)));
        Assert.Single(Run(s, 3.4, 4, SideToSide()));
    }

    static FakePiece Block(string id, float w = 1, float d = 0.5f, bool die = false) => new(id, 0, 0, w, d) { IsDie = die };

    static double Reach(HeldPiece<FakePiece> h) => 0.5 * Math.Sqrt(h.Node.Footprint.X * h.Node.Footprint.X + h.Node.Footprint.Y * h.Node.Footprint.Y);

    static List<HeldPiece<FakePiece>> Line(int n, bool dice = false) => Enumerable.Range(0, n).Select(i => new HeldPiece<FakePiece>
    {
        Node = Block("p" + i, w: dice ? 0.6f : 1 + 0.1f * (i % 3), d: dice ? 0.6f : 0.5f, die: dice),
        Offset = new Vec2(i * 1.5 - 10, 0), Rot = 180,
    }).ToList();

    static IEnumerable<Vec2> Edge(HeldPiece<FakePiece> h)
    {
        double rad = h.Rot * Math.PI / 180, w = h.Node.Footprint.X / 2, d = h.Node.Footprint.Y / 2;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad)); var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        for (int k = 0; k <= 8; k++)
        {
            double f = -1 + 2 * k / 8.0;
            foreach (var (x, z) in new[] { (w * f, d), (w * f, -d), (w, d * f), (-w, d * f) })
                yield return h.Offset + new Vec2(along.X * x + back.X * z, along.Z * x + back.Z * z);
        }
    }

    static bool Overlap(HeldPiece<FakePiece> a, HeldPiece<FakePiece> b)
        => (a.Offset - b.Offset).Length < Reach(a) + Reach(b)
           && (Edge(a).Any(p => b.Node.ContainsAt(p, b.Offset, b.Rot)) || Edge(b).Any(p => a.Node.ContainsAt(p, a.Offset, a.Rot)));

    [Theory]
    [InlineData(6, false)]
    [InlineData(12, true)]
    [InlineData(40, false)]
    public void A_shaken_group_is_a_jumbled_clump_round_the_pointer_with_none_on_another(int n, bool dice)
    {
        var held = Line(n, dice);
        Formation.Gather(held, new Random(1));
        Assert.All(held, h => Assert.Equal(0, h.Lift));
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                Assert.False(Overlap(held[i], held[j]), $"{i} and {j} lie in each other");
        double area = held.Sum(h => h.Node.Footprint.X * (double)h.Node.Footprint.Y);
        double furthest = held.Max(h => h.Offset.Length + Reach(h));
        Assert.True(Math.PI * furthest * furthest < area * 4, $"close round the pointer: reaching {furthest} for {area} of pieces");
        Assert.True(Math.Abs(held.Average(h => h.Offset.X)) < furthest / 3 && Math.Abs(held.Average(h => h.Offset.Z)) < furthest / 3, "round the pointer");
        Assert.Contains(held, h => Math.Abs(h.Rot - 180) > 10);
    }

    [Fact]
    public void A_crowd_of_six_hundred_is_clumped_at_once()
    {
        var held = Line(600);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Formation.Gather(held, new Random(2));
        Assert.True(clock.ElapsedMilliseconds < 1500, $"{clock.ElapsedMilliseconds} ms");
        double area = held.Sum(h => h.Node.Footprint.X * (double)h.Node.Footprint.Y);
        double furthest = held.Max(h => h.Offset.Length + Reach(h));
        Assert.True(Math.PI * furthest * furthest < area * 4, $"close round the pointer: reaching {furthest} for {area} of pieces");
    }

    [Fact]
    public void The_order_is_shuffled_and_a_marker_stays_on_its_unit()
    {
        var held = Line(6);
        var marker = new HeldPiece<FakePiece> { Node = Block("m", 0.3f, 0.3f), Offset = held[2].Offset + new Vec2(0.1, 0.1), Rot = 180, Lift = 0.1f, Parent = held[2] };
        held.Add(marker);
        Formation.Gather(held, new Random(3));
        var rel = marker.Offset - held[2].Offset;
        Assert.True(rel.Length < 0.15, $"the marker rides on its unit: {rel.X},{rel.Z} off");
        Assert.Equal(0.1f, marker.Lift, 5);
        var byX = held.Take(6).OrderBy(h => h.Offset.X).Select(h => h.Node.Piece.Id).ToList();
        Assert.NotEqual(new[] { "p0", "p1", "p2", "p3", "p4", "p5" }, byX);
    }
}
