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

    static List<Vec2> Arc(Vec2 centre, double radius, double fromDeg, double toDeg, int points = 240) =>
        Enumerable.Range(0, points + 1).Select(i =>
        {
            double a = (fromDeg + (toDeg - fromDeg) * i / points) * Math.PI / 180;
            return new Vec2(centre.X + radius * Math.Cos(a), centre.Z + radius * Math.Sin(a));
        }).ToList();

    static double ArcAt(List<Vec2> path, Vec2 p)
    {
        double s = 0;
        for (int i = 1; i < path.Count; s += (path[i] - path[i - 1]).Length, i++)
        {
            var seg = path[i] - path[i - 1];
            double t = Math.Clamp(Dot(p - path[i - 1], seg) / Dot(seg, seg), 0, 1);
            var near = new Vec2(path[i - 1].X + seg.X * t, path[i - 1].Z + seg.Z * t);
            if ((p - near).Length < 1e-9) return s + seg.Length * t;
        }
        throw new InvalidOperationException($"{p} is not on the path");
    }

    [Fact]
    public void A_stretch_along_a_curve_is_spaced_evenly_by_the_length_along_it_from_its_start_to_its_end()
    {
        var arc = Arc(new Vec2(0, 0), 10, 0, 90);
        var placed = Formations.Along(arc, 5, 1);
        Assert.Equal(arc[0], placed[0].Pos);
        Assert.True((placed[^1].Pos - arc[^1]).Length < 1e-9);
        var along = placed.Select(p => ArcAt(arc, p.Pos)).ToList();
        double length = Formations.Length(arc);
        for (int j = 0; j < placed.Count; j++) Assert.Equal(length * j / 4, along[j], 9);
        Assert.All(placed, p => Assert.True(Dot(Front(p.Rot), new Vec2(-p.Pos.X, -p.Pos.Z)) / p.Pos.Length > 0.999, $"{p} faces the arc's inside"));
        Assert.Equal(0, Formations.PastTheEnd(arc, 5, 1));
    }

    [Fact]
    public void An_s_shaped_stretch_reaches_both_ends_and_each_piece_faces_across_its_own_bend()
    {
        var s = Arc(new Vec2(5, 0), 5, 180, 0).Concat(Arc(new Vec2(15, 0), 5, 180, 360).Skip(1)).ToList();
        var placed = Formations.Along(s, 9, 1);
        Assert.True((placed[0].Pos - new Vec2(0, 0)).Length < 1e-9);
        Assert.True((placed[^1].Pos - new Vec2(20, 0)).Length < 1e-9);
        var along = placed.Select(p => ArcAt(s, p.Pos)).ToList();
        for (int j = 1; j < along.Count; j++) Assert.Equal(Formations.Length(s) / 8, along[j] - along[j - 1], 9);
        Assert.True(Dot(Front(placed[2].Rot), new Vec2(0, 1)) > 0.99, "on the first bend's crest the front looks out of the bend, to the top");
        Assert.True(Dot(Front(placed[6].Rot), new Vec2(0, 1)) > 0.99, "and in the second's trough into it, on the same side of the line");
        Assert.True(Dot(Front(placed[0].Rot), new Vec2(-1, 0)) > 0.99, "where the line sets off upwards, the front is on its left");
        Assert.Equal(180, Formations.FacingOf(s), 9);
    }

    [Fact]
    public void A_straight_stretch_drawn_left_to_right_faces_the_top_as_a_dragged_line_does()
    {
        var placed = Formations.Along(new[] { new Vec2(0, 0), new Vec2(9, 0) }, 4, 1);
        Assert.Equal(new[] { 0.0, 3.0, 6.0, 9.0 }, placed.Select(p => Math.Round(p.Pos.X, 9)));
        Assert.All(placed, p => Assert.Equal(180, p.Rot, 9));
    }

    [Fact]
    public void A_stretch_that_keeps_its_spacing_stands_the_pieces_the_spacing_apart_from_the_line_s_start()
    {
        var line = new[] { new Vec2(0, 0), new Vec2(9, 0) };
        var placed = Formations.Along(line, 4, 1, keepSpacing: true);
        Assert.Equal(new[] { 0.0, 1.0, 2.0, 3.0 }, placed.Select(p => Math.Round(p.Pos.X, 9)));
        Assert.Equal(0, Formations.PastTheEnd(line, 4, 1, keepSpacing: true));
        Assert.Equal(2, Formations.PastTheEnd(line, 12, 1, keepSpacing: true));
        Assert.Equal(0, Formations.PastTheEnd(line, 12, 0.5));
    }

    [Fact]
    public void A_line_too_short_for_the_spacing_runs_on_past_its_end_the_way_it_last_went()
    {
        var bent = new[] { new Vec2(0, 0), new Vec2(2, 0), new Vec2(2, 1) };
        var placed = Formations.Along(bent, 5, 1.5);
        var want = new[] { new Vec2(0, 0), new Vec2(1.5, 0), new Vec2(2, 1), new Vec2(2, 2.5), new Vec2(2, 4) };
        for (int j = 0; j < want.Length; j++) Assert.True((placed[j].Pos - want[j]).Length < 1e-9, $"{j}: {placed[j].Pos}");
        Assert.Equal(90, placed[^1].Rot, 9);
        Assert.Equal(2, Formations.PastTheEnd(bent, 5, 1.5));
    }

    [Fact]
    public void A_line_drawn_round_on_itself_spaces_them_evenly_all_round_with_no_two_on_one_spot()
    {
        var ring = Arc(new Vec2(0, 0), 6, 0, 360);
        var placed = Formations.Along(ring, 6, 1);
        for (int i = 0; i < placed.Count; i++)
            for (int j = i + 1; j < placed.Count; j++) Assert.True((placed[i].Pos - placed[j].Pos).Length > 5.9, $"{i} and {j}");
    }

    [Theory]
    [InlineData(2, 0.5)]
    [InlineData(7, 1)]
    [InlineData(12, 2.5)]
    public void Along_any_drawn_line_no_two_neighbours_are_closer_along_it_than_the_spacing(int n, double spacing)
    {
        var rng = new Random(n * 31 + (int)(spacing * 10));
        var wander = new List<Vec2> { new(0, 0) };
        for (int i = 0; i < 40; i++) wander.Add(new Vec2(wander[^1].X + rng.NextDouble() * 0.6, wander[^1].Z + rng.NextDouble() * 0.8 - 0.4));
        var placed = Formations.Along(wander, n, spacing);
        double length = Formations.Length(wander), step = Math.Max(spacing, length / (n - 1));
        Assert.Equal(n, placed.Count);
        Assert.Equal(wander[0], placed[0].Pos);
        for (int j = 0; j < n; j++)
        {
            double s = j * step;
            double at = s <= length + 1e-9 ? ArcAt(wander, placed[j].Pos) : length + (placed[j].Pos - wander[^1]).Length;
            Assert.Equal(s, at, 9);
        }
        Assert.Equal(Enumerable.Range(0, n).Count(j => j * step > length + 1e-9), Formations.PastTheEnd(wander, n, spacing));
    }

    [Fact]
    public void A_path_with_no_length_strings_nothing()
    {
        Assert.Empty(Formations.Along(new[] { new Vec2(1, 1), new Vec2(1, 1) }, 3, 1));
        Assert.Empty(Formations.Along(new[] { new Vec2(1, 1) }, 3, 1));
    }

    [Fact]
    public void The_old_names_still_form_up()
    {
        Assert.Equal(("line", 2), Formations.Named("two-lines") is var (k, d) ? (k.Id, d) : default);
        Assert.Equal(("column", Formations.Mass), Formations.Named("block") is var (bk, bd) ? (bk.Id, bd) : default);
        Assert.Equal("column", Formations.Named("grid")?.Kind.Id);
        Assert.Equal("wedge", Formations.Named("arrowhead")?.Kind.Id);
        Assert.Null(Formations.Named("phalanx-of-doom"));
    }

    [Fact]
    public void An_old_block_is_a_column_about_as_deep_as_it_is_wide()
    {
        var slots = Formations.Slots(K("column"), 16, new Pitch(1, 1), Formations.Mass);
        Assert.Equal(4, slots.Select(s => Math.Round(s.X, 6)).Distinct().Count());
        Assert.Equal(4, slots.Select(s => Math.Round(s.Z, 6)).Distinct().Count());
    }

    [Fact]
    public void Every_formation_belongs_to_one_group_and_the_columns_march()
    {
        Assert.Equal(new[] { "column", "staggered-column", "herringbone" }, Formations.All.Where(k => k.Marches).Select(k => k.Id));
        Assert.All(Formations.All, k => Assert.False(string.IsNullOrWhiteSpace(k.What)));
        Assert.Equal(Formations.All.Length, Formations.All.Select(k => k.Id).Distinct().Count());
    }

    [Fact]
    public void A_staggered_column_goes_by_turns_left_and_right_one_behind_another()
    {
        var slots = Formations.Slots(K("staggered-column"), 6, new Pitch(2, 1));
        Assert.Equal(6, slots.Select(s => Math.Round(s.Z, 6)).Distinct().Count());
        for (int i = 1; i < slots.Count; i++) Assert.True(Math.Sign(slots[i].X) == -Math.Sign(slots[i - 1].X), $"{i} on the other side");
        Assert.All(slots, s => Assert.Equal(0, s.Turn));
    }

    [Fact]
    public void A_herringbone_turns_each_piece_out_to_its_own_side()
    {
        var slots = Formations.Slots(K("herringbone"), 6, new Pitch(2, 1));
        Assert.All(slots, s => Assert.Equal(45, Math.Abs(s.Turn), 6));
        Assert.All(slots, s => Assert.True(Math.Sign(s.Turn) == Math.Sign(s.X), $"{s} turns out to the side it stands on"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(225)]
    public void A_column_dragged_faces_along_the_drag(double deg)
    {
        var from = new Vec2(3, 4);
        var to = new Vec2(from.X + 10 * Math.Cos(deg * Math.PI / 180), from.Z + 10 * Math.Sin(deg * Math.PI / 180));
        var way = to - from;
        Assert.True(Dot(Front(Formations.FacingDown(from, to)), way) / way.Length > 0.999);
    }

    static List<Vec2> Drawn(IEnumerable<Vec2> pointer, double pitch = 1)
    {
        var path = new List<Vec2>();
        foreach (var p in pointer) Formations.Extend(path, p, pitch, pitch * 0.1);
        return path;
    }

    static double SharpestTurnOverAPitch(List<Vec2> path, double pitch)
    {
        double worst = 0;
        var cum = new List<double> { 0 };
        for (int i = 1; i < path.Count; i++) cum.Add(cum[^1] + (path[i] - path[i - 1]).Length);
        for (int i = 0; i + 1 < path.Count; i++)
        {
            int j = i;
            while (j + 2 < path.Count && cum[j + 1] - cum[i] <= pitch) j++;
            var a = path[i + 1] - path[i];
            var b = path[j + 1] - path[j];
            double turn = Math.Abs(Math.Atan2(a.X * b.Z - a.Z * b.X, Dot(a, b)) * 180 / Math.PI);
            worst = Math.Max(worst, turn);
        }
        return worst;
    }

    [Fact]
    public void Drawing_back_over_a_line_pulls_it_back_instead_of_folding_it()
    {
        var out_ = Enumerable.Range(0, 101).Select(i => new Vec2(i * 0.1, 0));
        var back = Enumerable.Range(0, 41).Select(i => new Vec2(10 - i * 0.1, 0.05));
        var path = Drawn(out_.Concat(back));
        Assert.True(Math.Abs(Formations.Length(path) - 6) < 0.2, $"pulled back to 6 long: {Formations.Length(path)}");
        Assert.All(path, p => Assert.True(p.X <= 10 + 1e-9 && p.X >= -1e-9));
        Assert.True(Math.Abs(path[^1].X - 6) < 0.2, $"its end where the pointer stopped: {path[^1]}");
        var again = Drawn(out_.Concat(back).Concat(Enumerable.Range(1, 20).Select(i => new Vec2(6 + i * 0.1, 0))));
        Assert.True(Math.Abs(Formations.Length(again) - 8) < 0.2, "and drawn on again from there");
    }

    [Fact]
    public void A_sharp_corner_is_rounded_so_the_line_never_bends_sideways_within_a_piece()
    {
        var corner = Enumerable.Range(0, 51).Select(i => new Vec2(i * 0.1, 0)).Concat(Enumerable.Range(1, 50).Select(i => new Vec2(5, i * 0.1)));
        var path = Drawn(corner);
        Assert.True(SharpestTurnOverAPitch(path, 1) < 90, $"no turn of a quarter or more within one piece: {SharpestTurnOverAPitch(path, 1)}");
        Assert.True(path[^1].Z > 3, $"it still heads off the new way: {path[^1]}");
    }

    [Fact]
    public void Gentle_curves_are_drawn_as_they_go_and_a_closing_ring_is_not_cut_at_its_start()
    {
        var ring = Arc(new Vec2(0, 0), 4, 0, 350, 700);
        var path = Drawn(ring);
        Assert.True(Math.Abs(Formations.Length(path) - Formations.Length(ring)) < 0.3, $"{Formations.Length(path)} of {Formations.Length(ring)}");
        Assert.True((path[0] - ring[0]).Length < 1e-9);
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
