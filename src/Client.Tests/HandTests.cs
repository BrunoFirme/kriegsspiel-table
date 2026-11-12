using KTable.Client.Hand;
using KTable.Core.Model;
using KTable.Core.Protocol;
using static KTable.Client.Tests.HandLayouts;

namespace KTable.Client.Tests;

public class HandTests
{
    public static TheoryData<string, bool, string> RestCases => new()
    {
        { "bare table", true, "3,4 y 0" },
        { "bare table", false, "3,4 y 0" },
        { "on a big block", true, "0.5,0.5 y 0.30000001192092896" },
        { "on a big block", false, "0.5,0.5 y 0.30000001192092896" },
        { "three high", true, "0.2,-0.1 y 0.30000001192092896" },
        { "three high", false, "0.2,-0.1 y 0.30000001192092896" },
        { "over an edge slides off", true, "3.0599999845027925,0 y 0" },
        { "over an edge slides off", false, "2.7,0 y 0.4000000059604645" },
        { "turned over an edge", true, "0,2.45 y 0.4000000059604645" },
        { "turned over an edge", false, "0,2.45 y 0.4000000059604645" },
        { "between two blocks", true, "0.05,0 y 0.4999999701976776" },
        { "between two blocks", false, "0.05,0 y 0.4999999701976776" },
        { "prop is set down as let go", true, "2,0 y 0.4000000059604645" },
        { "prop is set down as let go", false, "2,0 y 0.4000000059604645" },
        { "other layer ignored", true, "0.5,0.5 y 0" },
        { "other layer ignored", false, "0.5,0.5 y 0" },
        { "dice ignored", true, "0.5,0.5 y 0" },
        { "dice ignored", false, "0.5,0.5 y 0" },
        { "on a turned block", true, "1.3,1.2 y 0.20000000298023224" },
        { "on a turned block", false, "1.3,1.2 y 0.20000000298023224" },
    };

    [Theory, MemberData(nameof(RestCases))]
    public void A_piece_let_go_of_rests_where_it_always_has(string layout, bool slide, string expected)
    {
        var (_, table, dropped, at, rot) = Rests().Single(r => r.name == layout);
        var footing = new Footing<FakePiece>((_, _) => table, _ => false);
        var (pos, y) = footing.Rest(dropped, at, rot, new HashSet<FakePiece> { dropped }, slide);
        Assert.Equal(expected, $"{R(pos.X)},{R(pos.Z)} y {R(y)}");
    }

    public static TheoryData<string, string> SurfaceCases => new()
    {
        { "bare table", "0" },
        { "on a big block", "0.30000001192092896" },
        { "three high", "0.30000001192092896" },
        { "over an edge slides off", "0" },
        { "turned over an edge", "0" },
        { "between two blocks", "0" },
        { "prop is set down as let go", "0.4000000059604645" },
        { "other layer ignored", "0" },
        { "dice ignored", "0" },
        { "on a turned block", "0.20000000298023224" },
    };

    [Theory, MemberData(nameof(SurfaceCases))]
    public void The_height_under_a_point_is_the_top_of_the_highest_piece_there(string layout, string expected)
    {
        var (_, table, _, at, _) = Rests().Single(r => r.name == layout);
        Assert.Equal(expected, R(new Footing<FakePiece>((_, _) => table, _ => false).SurfaceY(at, Layers.Umpire)));
    }

    [Fact]
    public void A_piece_that_passes_through_others_holds_nothing_up_and_comes_down_to_the_table()
    {
        var block = new FakePiece("base", 0, 0, 6, 6, thickness: 0.3f);
        var ghost = new FakePiece("ghost", 0, 0, 6, 6, thickness: 0.3f);
        ghost.Piece.NoCollision = true;
        var dropped = new FakePiece("dropped", 0, 0, 1, 1);
        Assert.Equal(0, new Footing<FakePiece>((_, _) => new[] { ghost }, _ => false).SurfaceY(new Vec2(0, 0), Layers.Umpire));
        var footing = new Footing<FakePiece>((_, _) => new[] { block }, _ => false);
        Assert.True(footing.Rest(dropped, new Vec2(0, 0), 0, new HashSet<FakePiece> { dropped }).y > 0.2);
        dropped.Piece.NoCollision = true;
        Assert.Equal((new Vec2(0, 0), 0.0), footing.Rest(dropped, new Vec2(0, 0), 0, new HashSet<FakePiece> { dropped }));
    }

    [Fact]
    public void A_flying_piece_is_never_stood_on_and_lands_on_nothing_but_the_ground()
    {
        var plane = new FakePiece("plane", 0, 0, 6, 6, thickness: 0.3f);
        plane.Piece.Flies = true;
        var dropped = new FakePiece("dropped", 0, 0, 1, 1);
        var footing = new Footing<FakePiece>((_, _) => new[] { plane }, _ => false);
        Assert.Equal(0, footing.SurfaceY(new Vec2(0, 0), Layers.Umpire));
        Assert.Equal(0.0, footing.Rest(dropped, new Vec2(0, 0), 0, new HashSet<FakePiece> { dropped }).y);
        var block = new FakePiece("base", 0, 0, 6, 6, thickness: 0.3f);
        var flyer = new FakePiece("flyer", 0, 0, 1, 1);
        flyer.Piece.Flies = true;
        Assert.Equal((new Vec2(0, 0), 0.0), new Footing<FakePiece>((_, _) => new[] { block }, _ => false).Rest(flyer, new Vec2(0, 0), 0, new HashSet<FakePiece> { flyer }));
    }

    [Fact]
    public void A_piece_in_the_hand_is_under_nothing()
    {
        var block = new FakePiece("base", 0, 0, 6, 6, thickness: 0.3f);
        var footing = new Footing<FakePiece>((_, _) => new[] { block }, n => n == block);
        Assert.Equal(0, footing.SurfaceY(new Vec2(0, 0), Layers.Umpire));
    }

    [Fact]
    public void What_rests_on_a_piece_is_what_sits_on_its_top_and_is_free_to_move()
    {
        var under = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var on = new FakePiece("on", 0.5, 0.5, 1, 1, y: 0.3);
        var floating = new FakePiece("high", -0.5, 0.5, 1, 1, y: 0.6);
        var off = new FakePiece("off", 5, 5, 1, 1, y: 0.3);
        var pinned = new FakePiece("pinned", 1, -1, 1, 1, y: 0.3);
        pinned.Piece.Locked = true;
        var held = new FakePiece("held", -1, -1, 1, 1, y: 0.3);
        var footing = new Footing<FakePiece>((_, _) => Array.Empty<FakePiece>(), n => n == held);
        var found = footing.RestingOn(under, new[] { under, on, floating, off, pinned, held }, _ => true);
        Assert.Equal(new[] { "on" }, found.Select(f => f.Piece.Id));
        Assert.Empty(footing.RestingOn(under, new[] { on }, _ => false));
    }

    public static TheoryData<string, Vec2, Vec2, string> DropCases => new()
    {
        { "onto the base", new Vec2(0.3, 0.2), new Vec2(2.5, 0), "move blk 0.3,0.2 rot 200 y 0.30000001192092896\nmove loose 2.8,0.2 rot 90 y 0.30000001192092896\nmove mk 0.5,0.2 rot 210 y 0.4300000071525574\nattach blk to base\nattach loose to base\n2 pieces stuck to the base" },
        { "half off the base", new Vec2(3.1, 0), new Vec2(-1, 0.4), "move blk 3.1,0 rot 200 y 0.30000001192092896\nmove loose 2.1,0.4 rot 90 y 0.30000001192092896\nmove mk 3.3000000000000003,0 rot 210 y 0.4300000071525574\nattach loose to base\nStuck to the base" },
        { "on the bare table", new Vec2(9, 9), new Vec2(0.4, 0.3), "move blk 9,9 rot 200 y 0\nmove loose 9.4,9.3 rot 90 y 0\nmove mk 9.2,9 rot 210 y 0.12999999523162842\n" },
    };

    [Theory, MemberData(nameof(DropCases))]
    public void A_drop_sends_the_moves_and_attachments_it_always_has(string gesture, Vec2 cursor, Vec2 looseOffset, string expected)
    {
        _ = gesture;
        var bigBlock = new FakePiece("base", 0, 0, 6, 6, thickness: 0.3f);
        var blk = new FakePiece("blk", 5, 5, 2, 1, rot: 200);
        var mk = new FakePiece("mk", 5.2, 5, 0.4f, 0.4f, y: 0.13, thickness: 0.02f);
        var loose = new FakePiece("loose", 7, 7, 0.4f, 0.4f, thickness: 0.02f);
        blk.Piece.Attached.Add("old-mark");
        blk.Piece.Attached.Add("mk");
        var all = new List<FakePiece> { bigBlock, blk, mk, loose };
        var footing = new Footing<FakePiece>((_, _) => all, _ => false);
        var sticking = new Sticking<FakePiece>(_ => all, (l, id) => all.FirstOrDefault(n => n.Layer == l && n.Piece.Id == id), l => all.Where(n => n.Layer == l).Select(n => n.Piece).ToList());
        var hb = new HeldPiece<FakePiece> { Node = blk, Layer = blk.Layer, Id = "blk", Offset = new Vec2(0, 0), Rot = 200 };
        var hm = new HeldPiece<FakePiece> { Node = mk, Layer = mk.Layer, Id = "mk", Offset = new Vec2(0.2, 0), Rot = 210, Lift = 0.13f, Parent = hb };
        var hl = new HeldPiece<FakePiece> { Node = loose, Layer = loose.Layer, Id = "loose", Offset = looseOffset, Rot = 90 };
        var held = new List<HeldPiece<FakePiece>> { hm, hb, hl };

        var landing = new Landing<FakePiece>(footing, held, cursor, 64);
        var moved = held.Select(h => (h.Node, landing.Move(h))).ToList();
        var cmds = Landing<FakePiece>.ParentsFirst(moved, all.Select(n => n.Piece));
        var nodes = moved.Select(x => x.Node).ToList();
        var sticky = sticking.Stickiness(nodes, moved, nodes.ToHashSet());
        cmds.AddRange(sticky.cmds);
        var said = sticky.stuck > 0 ? (sticky.stuck == 1 ? $"Stuck to {sticky.to}" : $"{sticky.stuck} pieces stuck to {sticky.to}") : sticky.freed > 0 ? "Dropped; no longer stuck" : "";
        Assert.Equal(expected, Describe(cmds) + "\n" + said);
    }

    [Fact]
    public void A_piece_carried_with_what_it_is_attached_to_stays_attached_wherever_it_hangs()
    {
        var blk = new FakePiece("blk", 0, 0, 2, 1);
        var flag = new FakePiece("flag", 3, 0, 2, 1, y: 0.5, thickness: 0.02f);
        blk.Piece.Attached.Add("flag");
        var all = new List<FakePiece> { blk, flag };
        var footing = new Footing<FakePiece>((_, _) => all, _ => false);
        var sticking = new Sticking<FakePiece>(_ => all, (l, id) => all.FirstOrDefault(n => n.Piece.Id == id), _ => all.Select(n => n.Piece).ToList(), footing);
        var hb = new HeldPiece<FakePiece> { Node = blk, Layer = blk.Layer, Id = "blk", Rot = 180 };
        var hf = new HeldPiece<FakePiece> { Node = flag, Layer = flag.Layer, Id = "flag", Offset = new Vec2(3, 0), Rot = 180, Lift = 0.5f, Parent = hb };
        var held = new List<HeldPiece<FakePiece>> { hb, hf };
        var landing = new Landing<FakePiece>(footing, held, new Vec2(8, 8), 64);
        var moved = held.Select(h => (h.Node, landing.Move(h))).ToList();
        var nodes = moved.Select(x => x.Node).ToList();
        var (cmds, stuck, freed, _) = sticking.Stickiness(nodes, moved, nodes.ToHashSet());
        Assert.Equal("", Describe(cmds));
        Assert.Equal((0, 0), (stuck, freed));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_marker_carried_in_one_selection_with_the_flag_under_it_lands_on_top_of_the_flag(bool markerFirst, bool stuck)
    {
        var flag = new FakePiece("flag", 0, 0, 2, 1) { Placed = true };
        var disc = new FakePiece("disc", 0.3, 0, 0.4f, 0.4f, y: 0.13, thickness: 0.02f) { Placed = true };
        if (stuck) flag.Piece.Attached.Add("disc");
        var all = new List<FakePiece> { flag, disc };
        var hf = new HeldPiece<FakePiece> { Node = flag, Layer = flag.Layer, Id = "flag", Rot = 180 };
        var hd = new HeldPiece<FakePiece> { Node = disc, Layer = disc.Layer, Id = "disc", Offset = new Vec2(0.3, 0), Rot = 180 };
        var held = markerFirst ? new List<HeldPiece<FakePiece>> { hd, hf } : new List<HeldPiece<FakePiece>> { hf, hd };
        var footing = new Footing<FakePiece>((_, _) => all, _ => true);
        Riders.Seat(held, footing);
        var landing = new Landing<FakePiece>(footing, held, new Vec2(5, 5), 64);
        Assert.Equal("move flag 5,5 rot 180 y 0\nmove disc 5.3,5 rot 180 y 0.12999999523162842", Describe(new[] { landing.Move(hf), landing.Move(hd) }));
        Assert.Equal((hf, (HeldPiece<FakePiece>?)null), (hd.Parent, hf.Parent));
    }

    static (string cmds, int stuck) DropOn(List<FakePiece> all, FakePiece dropped)
    {
        var sticking = new Sticking<FakePiece>(_ => all, (l, id) => all.FirstOrDefault(n => n.Piece.Id == id), _ => all.Select(n => n.Piece).ToList());
        var move = new MovePiece { Layer = dropped.Layer, Id = dropped.Piece.Id, Pos = dropped.Piece.Pos, RotDeg = dropped.Piece.RotDeg, Y = dropped.Piece.Y };
        var (cmds, stuck, _, _) = sticking.Stickiness(new List<FakePiece> { dropped }, new List<(FakePiece, MovePiece)> { (dropped, move) }, new HashSet<FakePiece> { dropped });
        return (Describe(cmds), stuck);
    }

    [Fact]
    public void A_piece_set_down_on_one_its_own_size_that_already_rides_another_rides_it_three_deep()
    {
        var a = new FakePiece("a", 0, 0, 3.771f, 0.5729f);
        var b = new FakePiece("b", 0, 0, 3.7708f, 0.5728f, y: 0.13);
        var c = new FakePiece("c", 0, 0, 3.771f, 0.5729f, y: 0.26);
        a.Piece.Attached.Add("b");
        Assert.Equal(("attach c to b", 1), DropOn(new List<FakePiece> { a, b, c }, c));
    }

    [Fact]
    public void A_piece_set_down_on_a_smaller_one_or_on_one_it_carries_rides_neither()
    {
        var small = new FakePiece("small", 0, 0, 1, 1);
        var big = new FakePiece("big", 0, 0, 2, 1, y: 0.13);
        Assert.Equal(("", 0), DropOn(new List<FakePiece> { small, big }, big));

        var under = new FakePiece("under", 0, 0, 2, 1);
        var over = new FakePiece("over", 0, 0, 2, 1, y: 0.13);
        var top = new FakePiece("top", 0, 0, 2, 1, y: 0.26);
        over.Piece.Attached.Add("under");
        under.Piece.Attached.Add("top");
        Assert.Equal(("", 0), DropOn(new List<FakePiece> { under, over, top }, over));
    }

    [Fact]
    public void A_stuck_marker_dropped_on_the_bare_table_comes_loose()
    {
        var blk = new FakePiece("blk", 0, 0, 2, 1);
        var mk = new FakePiece("mk", 0, 0, 0.4f, 0.4f, y: 0.13, thickness: 0.02f);
        blk.Piece.Attached.Add("mk");
        var all = new List<FakePiece> { blk, mk };
        var sticking = new Sticking<FakePiece>(_ => all, (l, id) => all.FirstOrDefault(n => n.Piece.Id == id), _ => all.Select(n => n.Piece).ToList());
        var move = new MovePiece { Layer = mk.Layer, Id = "mk", Pos = new Vec2(8, 8), RotDeg = 0, Y = 0 };
        var (cmds, stuck, freed, _) = sticking.Stickiness(new List<FakePiece> { mk }, new List<(FakePiece, MovePiece)> { (mk, move) }, new HashSet<FakePiece> { mk });
        Assert.Equal("detach mk", Describe(cmds));
        Assert.Equal((0, 1), (stuck, freed));
    }

    [Fact]
    public void Past_the_slide_limit_a_drop_is_set_down_where_it_was_let_go_of()
    {
        var bigBlock = new FakePiece("base", 0, 0, 4, 4, thickness: 0.4f);
        var a = new FakePiece("a", 9, 9, 2, 1);
        var footing = new Footing<FakePiece>((_, _) => new[] { bigBlock, a }, _ => false);
        var held = new List<HeldPiece<FakePiece>> { new() { Node = a, Layer = a.Layer, Id = "a", Rot = 180 } };
        Assert.Equal("move a 3.0599999845027925,0 rot 180 y 0", Describe(new[] { new Landing<FakePiece>(footing, held, new Vec2(2.7, 0), 64).Move(held[0]) }));
        Assert.Equal("move a 2.7,0 rot 180 y 0.4000000059604645", Describe(new[] { new Landing<FakePiece>(footing, held, new Vec2(2.7, 0), 0).Move(held[0]) }));
    }

    static readonly FakePiece[] Five = { new("a", 0, 0, 2, 1), new("b", 0, 0, 2.2f, 1), new("c", 0, 0, 2, 1.3f), new("d", 0, 0, 1, 1), new("e", 0, 0, 2, 1) };
    static readonly Vec2[] Offsets = { new(-3, 1), new(2, -0.5), new(0.1, 0.2), new(4, 3), new(-1, -2) };

    [Theory]
    [InlineData(1, "-1.0576764039907607,3.1880000257492065 3.0576764039907607,0.8119999742507935 1,2 5.115352807981521,-0.376000051498413 -3.1153528079815214,4.376000051498413")]
    [InlineData(2, "1.6225882168965415,0.7023543723601087 1.4062499850988388,2.7036456147652883 -0.6514264188919219,3.891645640514495 3.4639263890895995,1.5156455890160818 -0.43508818709421915,1.8903543981093152")]
    [InlineData(3, "-0.02883820199538034,2.594000012874603 0.7836617682022972,4.00129124240518 2.0288382019953803,1.4059999871253968 2.841338172193058,2.813291216655973 0.1875000298023225,0.5927087704694236")]
    [InlineData(9, "0.1875000298023225,0.5927087704694236 1.8124999701976776,3.4072912295305766 1,2 2.6249999403953552,4.814582459061153 -0.624999940395355,-0.8145824590611528")]
    public void Pieces_form_rows_where_they_always_have(int rows, string expected)
    {
        var slots = Formation.Rows(Five.Select((f, i) => (f, Offsets[i])).ToList(), 30, rows, new Vec2(1, 2));
        Assert.Equal(expected, string.Join(" ", slots.Select(v => R(v.X) + "," + R(v.Z))));
    }

    [Fact]
    public void A_held_group_turns_and_forms_rows_with_its_marker_kept_on_its_unit()
    {
        var held = Enumerable.Range(0, 4).Select(i => new HeldPiece<FakePiece> { Node = Five[i], Offset = Offsets[i], Rot = 170 + i * 10 }).ToList();
        held.Add(new HeldPiece<FakePiece> { Node = Five[4], Offset = Offsets[4], Rot = 15, Parent = held[0] });
        Formation.Turn(held, 25);
        Formation.FormHeld(held, 2);
        Assert.Equal("-0.937229440070848,1.0922917373192145@195 -1.3578103729356263,-0.4773376728268559@195 0.937229440070848,-1.0922917373192145@195 1.3578103729356263,0.4773376728268559@195 -0.39246865121964636,-2.471868147272134@40",
            string.Join(" ", held.Select(h => R(h.Offset.X) + "," + R(h.Offset.Z) + "@" + R(h.Rot))));
    }

    [Theory]
    [InlineData(0.74, "13.201865 2.8000002 -4.6594815")]
    [InlineData(0.76, "13.229805 2.8 -4.579548")]
    [InlineData(0.8, "13.108607 2.8 -4.9157276")]
    [InlineData(0.9, "0 0 0")]
    public void A_flick_throws_at_the_speed_it_always_has(double now, string expected)
    {
        var trail = new ThrowTrail();
        foreach (var (t, at) in new[] { (0.0, new Vec2(0, 0)), (0.3, new Vec2(0.1, 0)), (0.62, new Vec2(0.5, 0.2)), (0.66, new Vec2(0.9, 0.35)), (0.70, new Vec2(1.4, 0.5)),
            (0.74, new Vec2(2.2, 0.8)) })
            trail.Add(t, at);
        var v = trail.Velocity(now);
        Assert.Equal(expected, FormattableString.Invariant($"{v.X:R} {v.Y:R} {v.Z:R}"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-90, 270)]
    [InlineData(725, 5)]
    [InlineData(-360, 0)]
    public void Angles_are_kept_in_one_turn(double deg, double expected) => Assert.Equal(expected, Angles.Norm360(deg));
}
