using KTable.Client.Hand;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class FormationsTests
{
    static FormationKind K(string id) => Formations.Find(id)!;

    static Vec2 Front(double facing) => new(-Math.Sin(facing * Math.PI / 180), -Math.Cos(facing * Math.PI / 180));

    static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;

    [Fact]
    public void A_line_spreads_across_the_centre_and_turns_with_the_facing()
    {
        var line = Formations.Place(Formations.Slots(K("line"), 3, new Pitch(2, 1)), new Vec2(0, 0), 0);
        Assert.Equal(new[] { -2.0, 0.0, 2.0 }, line.Select(p => Math.Round(p.Pos.X, 6)).OrderBy(x => x));
        Assert.All(line, p => Assert.Equal(0, p.Pos.Z, 6));
        var turned = Formations.Place(Formations.Slots(K("line"), 3, new Pitch(2, 1)), new Vec2(10, 10), 90);
        Assert.All(turned, p => Assert.Equal(10, p.Pos.X, 6));
        Assert.Equal(new[] { 8.0, 10.0, 12.0 }, turned.Select(p => Math.Round(p.Pos.Z, 6)).OrderBy(z => z));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(200)]
    public void A_wedge_points_the_way_its_pieces_face(double facing)
    {
        var placed = Formations.Place(Formations.Slots(K("wedge"), 6, new Pitch(1, 1)), new Vec2(3, -4), facing);
        var centre = new Vec2(placed.Average(p => p.Pos.X), placed.Average(p => p.Pos.Z));
        var tip = placed.MaxBy(p => Dot(p.Pos - centre, Front(facing)));
        var toTip = tip.Pos - centre;
        Assert.True(Dot(toTip, Front(facing)) / toTip.Length > 0.99);
        Assert.All(placed, p => Assert.Equal(facing, p.Rot, 6));
    }

    [Theory]
    [InlineData("square")]
    [InlineData("ring")]
    public void A_square_and_a_ring_look_out_on_every_side(string kind)
    {
        var placed = Formations.Place(Formations.Slots(K(kind), 12, new Pitch(1, 1)), new Vec2(0, 0), 30);
        Assert.All(placed, p => Assert.True(Dot(p.Pos, Front(p.Rot)) > 0.1));
        Assert.True(placed.Select(p => Math.Round(p.Rot)).Distinct().Count() >= 4);
    }

    [Fact]
    public void Pieces_go_the_shortest_way_to_their_places()
    {
        var places = Enumerable.Range(0, 9).Select(i => new Vec2(i % 3 * 2, i / 3 * 2)).ToList();
        var from = places.Select(p => new Vec2(p.X + 0.1, p.Z - 0.1)).Reverse().ToList();
        var match = Formations.Match(from, places);
        for (int i = 0; i < from.Count; i++) Assert.True((from[i] - places[match[i]]).Length < 0.2);
        Assert.Equal(9, match.Distinct().Count());
    }

    [Fact]
    public void A_dragged_front_sets_the_ranks_and_the_front_rank_stands_on_it()
    {
        var slots = Formations.Slots(K("line"), 7, new Pitch(1, 1), front: 3);
        Assert.Equal(3, slots.Select(s => Math.Round(s.Z, 6)).Distinct().Count());
        var placed = Formations.Place(slots, new Vec2(5, 5), 0, frontAt: true);
        Assert.Equal(3, placed.Count(p => Math.Abs(p.Pos.Z - 5) < 1e-9));
        Assert.All(placed, p => Assert.True(p.Pos.Z >= 5 - 1e-9));
    }

    [Fact]
    public void A_stretch_runs_from_one_end_of_the_line_to_the_other_but_never_closer_than_the_spacing()
    {
        var placed = Formations.Place(Formations.Slots(K("stretch"), 5, new Pitch(1, 1), span: 20), new Vec2(10, 0), 0, frontAt: true);
        Assert.Equal(new[] { 0.0, 5.0, 10.0, 15.0, 20.0 }, placed.Select(p => Math.Round(p.Pos.X, 6)).OrderBy(x => x));
        Assert.All(placed, p => Assert.Equal(0, p.Pos.Z, 6));
        var cramped = Formations.Slots(K("stretch"), 5, new Pitch(2, 1), span: 1);
        Assert.Equal(8, cramped.Max(s => s.X) - cramped.Min(s => s.X), 6);
    }

    [Fact]
    public void The_old_names_still_form_up()
    {
        Assert.Equal(("line", 2), Formations.Named("two-lines") is var (k, d) ? (k.Id, d) : default);
        Assert.Equal("block", Formations.Named("grid")?.Kind.Id);
        Assert.Null(Formations.Named("phalanx-of-doom"));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 1.6)]
    public void No_two_places_overlap_in_any_formation(double across, double deep)
    {
        foreach (var kind in Formations.All)
            for (int n = 1; n <= 40; n++)
            {
                var slots = Formations.Slots(kind, n, new Pitch(across, deep));
                Assert.Equal(n, slots.Count);
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        double dx = slots[i].X - slots[j].X, dz = slots[i].Z - slots[j].Z;
                        Assert.True(Math.Sqrt(dx * dx + dz * dz) >= Math.Min(across, deep) - 1e-9, $"{kind.Id} with {n}: places {i} and {j} overlap");
                    }
            }
    }
}
