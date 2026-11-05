using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class ReliefTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ktable-relief-tests", Guid.NewGuid().ToString("N"));

    public void Dispose() => BestEffort.DeleteFolder(_dir);

    static ReliefGrid Slope(int cols, int rows, float rise)
    {
        var g = new ReliefGrid(cols, rows);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++) g.H[y * cols + x] = rise * x / (cols - 1);
        return g;
    }

    static SheetDef Sheet(string id, double x = 0, double z = 0, double w = 100, double h = 50, double rot = 180, int layer = 0, double height = 1) =>
        new() { Id = id, Name = id, Image = "pic", Package = "sheets", Pos = new Vec2(x, z), Size = new Size2(w, h), RotDeg = rot, Z = layer, Relief = new string('a', 64), Height = height };

    [Fact]
    public void A_relief_saved_in_chunks_reads_back_the_same()
    {
        var g = Slope(130, 70, 0.1f);
        g.Kind[5 * 130 + 7] = GroundKinds.Of("wood")!.Value;
        var (sha, written) = ReliefFiles.Save(_dir, g);
        var index = ReliefFiles.LoadIndex(Path.Combine(_dir, ReliefFiles.HeaderRel(sha)));
        var back = ReliefFiles.Load(index, rel => Path.Combine(_dir, rel));
        Assert.Equal(g.H, back.H);
        Assert.Equal(g.Kind, back.Kind);
        Assert.Contains(ReliefFiles.HeaderRel(sha), written);
        Assert.Equal(sha, ReliefFiles.Sha(File.ReadAllBytes(Path.Combine(_dir, ReliefFiles.HeaderRel(sha)))));
        Assert.All(ReliefFiles.Files(sha, index), rel => Assert.True(ReliefFiles.IsReliefPath(rel), rel));
    }

    [Fact]
    public void Saving_the_same_relief_again_writes_nothing_and_a_change_writes_only_its_chunks()
    {
        var g = Slope(200, 200, 0.05f);
        ReliefFiles.Save(_dir, g);
        Assert.Empty(ReliefFiles.Save(_dir, g).written);
        g.H[10] += 0.01f;
        var (_, written) = ReliefFiles.Save(_dir, g);
        Assert.Equal(2, written.Count);
    }

    [Fact]
    public void Flat_and_unpainted_chunks_take_no_file()
    {
        var (sha, written) = ReliefFiles.Save(_dir, new ReliefGrid(100, 100));
        Assert.Equal(new[] { ReliefFiles.HeaderRel(sha) }, written);
    }

    [Fact]
    public void A_damaged_header_is_refused()
    {
        var path = Path.Combine(_dir, "bad.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "cols": 100, "rows": 100, "heights": ["", ""], "ground": ["", ""] }""");
        Assert.Throws<InvalidDataException>(() => ReliefFiles.LoadIndex(path));
        File.WriteAllText(path, """{ "cols": 10, "rows": 10, "heights": ["../../evil"], "ground": [""] }""");
        Assert.Throws<InvalidDataException>(() => ReliefFiles.LoadIndex(path));
    }

    [Fact]
    public void The_ground_is_flat_without_relief()
    {
        Assert.False(Ground.Flat.Any);
        Assert.Equal(0, Ground.Flat.At(new Vec2(3, 4)));
        Assert.Equal(GroundKinds.None, Ground.Flat.KindAt(new Vec2(3, 4)));
    }

    [Fact]
    public void The_ground_follows_the_relief_scaled_by_the_sheet_and_its_height()
    {
        var s = Sheet("s1", height: 2);
        var ground = new Ground(new[] { s }, _ => Slope(11, 6, 0.1f));
        Assert.True(ground.Any);
        Assert.Equal(0, ground.At(new Vec2(-50, 0)), 6);
        Assert.Equal(0.1 * 100 * 2, ground.At(new Vec2(50, 0)), 4);
        Assert.Equal(0.05 * 100 * 2, ground.At(new Vec2(0, 0)), 4);
        Assert.Equal(0, ground.At(new Vec2(80, 0)));
        Assert.Equal(20, ground.Highest, 4);
    }

    [Fact]
    public void A_turned_and_cropped_sheet_maps_points_both_ways()
    {
        var s = Sheet("s1", 10, -5, 40, 30, rot: 63);
        s.Crop = new FaceBox { X = 0.1, Y = 0.2, W = 0.6, H = 0.5 };
        foreach (var (u, v) in new[] { (0.15, 0.25), (0.5, 0.45), (0.69, 0.69) })
        {
            var p = Ground.FromUv(s, u, v);
            var back = Ground.Uv(s, p);
            Assert.NotNull(back);
            Assert.Equal(u, back!.Value.u, 9);
            Assert.Equal(v, back.Value.v, 9);
        }
        Assert.Null(Ground.Uv(s, Ground.FromUv(s, 0.05, 0.5)));
    }

    [Fact]
    public void The_top_sheet_with_a_relief_wins()
    {
        var low = Sheet("low", layer: 1);
        var high = Sheet("high", layer: 2);
        var lowGrid = Slope(5, 5, 0.1f);
        var highGrid = new ReliefGrid(5, 5);
        Array.Fill(highGrid.H, 0.02f);
        var ground = new Ground(new[] { low, high }, s => s.Id == "low" ? lowGrid : highGrid);
        Assert.Equal(2, ground.At(new Vec2(40, 0)), 4);
        Assert.Equal("high", ground.ReliefAt(new Vec2(40, 0))?.Id);
    }

    [Fact]
    public void A_piece_rests_on_the_highest_ground_under_it()
    {
        var ground = new Ground(new[] { Sheet("s1") }, _ => Slope(101, 51, 0.1f));
        double centre = ground.At(new Vec2(0, 0));
        double under = ground.Under(new Vec2(0, 0), 180, 10, 4);
        Assert.True(under > centre);
        Assert.Equal(ground.At(new Vec2(5.1, 0)), under, 3);
    }

    [Fact]
    public void A_piece_on_flat_ground_sits_level()
    {
        var ground = new Ground(new[] { Sheet("s1") }, _ => new ReliefGrid(11, 6));
        var seat = ground.SeatOn(new Vec2(3, 2), 180, 4, 2);
        Assert.Equal(0, seat.Y, 9);
        Assert.Equal(1, seat.UpY, 9);
        Assert.Equal(0, seat.TiltDeg, 6);
    }

    [Fact]
    public void A_piece_on_a_slope_leans_with_it_and_its_heading_does_not_matter()
    {
        var ground = new Ground(new[] { Sheet("s1") }, _ => Slope(101, 51, 0.1f));
        double rise = 0.1 * 100 / 100;
        foreach (var rot in new[] { 0.0, 37, 90, 180, 300 })
        {
            var seat = ground.SeatOn(new Vec2(0, 0), rot, 4, 2);
            Assert.Equal(Math.Atan(rise) * 180 / Math.PI, seat.TiltDeg, 3);
            Assert.True(seat.UpX < 0, "the top leans away from the rising side");
            Assert.Equal(0, seat.UpZ, 6);
            Assert.Equal(ground.At(new Vec2(0, 0)), seat.Y, 3);
        }
    }

    [Fact]
    public void A_piece_on_a_steep_or_bumpy_spot_leans_no_more_than_the_most_and_never_sinks()
    {
        var steep = new Ground(new[] { Sheet("s1") }, _ => Slope(101, 51, 3f));
        var seat = steep.SeatOn(new Vec2(0, 0), 180, 4, 2);
        Assert.Equal(Ground.MostTiltDeg, seat.TiltDeg, 3);
        var bump = new ReliefGrid(101, 51);
        bump.H[25 * 101 + 50] = 0.05f;
        var ground = new Ground(new[] { Sheet("s1") }, _ => bump);
        var on = ground.SeatOn(new Vec2(0, 0), 180, 4, 4);
        double a = on.UpX / on.UpY, b = on.UpZ / on.UpY;
        for (double x = -2; x <= 2; x += 1)
            for (double z = -2; z <= 2; z += 1)
                Assert.True(on.Y - a * x - b * z >= ground.At(new Vec2(x, z)) - 1e-6, $"sinks at {x},{z}");
    }

    [Fact]
    public void Raise_lower_flatten_and_paint_change_only_under_the_brush()
    {
        var g = new ReliefGrid(65, 65);
        var box = ReliefBrush.Dab(g, 0.5, 0.5, 6, new Brush(BrushMode.Raise, 0, 1));
        Assert.False(box.Empty);
        Assert.True(g.H[32 * 65 + 32] > 0);
        Assert.Equal(0, g.H[0]);
        ReliefBrush.Dab(g, 0.5, 0.5, 6, new Brush(BrushMode.Lower, 0, 1));
        ReliefBrush.Dab(g, 0.5, 0.5, 6, new Brush(BrushMode.Lower, 0, 1));
        Assert.All(g.H, h => Assert.True(h >= 0));
        var hill = Slope(65, 65, 0.2f);
        ReliefBrush.Dab(hill, 0.5, 0.5, 4, new Brush(BrushMode.Flatten, 0, 1, Level: 0.05));
        Assert.Equal(0.05, hill.H[32 * 65 + 32], 4);
        ReliefBrush.Stroke(g, new List<(double, double)> { (0.2, 0.2), (0.8, 0.2) }, 3, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Of("road")!.Value));
        Assert.Equal(GroundKinds.Of("road"), g.KindAt(0.5, 0.2));
        Assert.Equal(GroundKinds.None, g.KindAt(0.5, 0.8));
    }

    [Fact]
    public void Smoothing_softens_a_spike()
    {
        var g = new ReliefGrid(33, 33);
        g.H[16 * 33 + 16] = 1;
        ReliefBrush.Dab(g, 0.5, 0.5, 4, new Brush(BrushMode.Smooth, 0, 1));
        Assert.True(g.H[16 * 33 + 16] < 1);
        Assert.True(g.H[16 * 33 + 17] > 0);
    }

    [Fact]
    public void Ground_kinds_are_named_both_ways()
    {
        for (byte i = 0; i < GroundKinds.All.Count; i++) Assert.Equal(i, GroundKinds.Of(GroundKinds.Name(i)));
        Assert.Null(GroundKinds.Of("lava"));
    }

    static GameState Table() => new() { Sheets = { Sheet("s1") } };

    [Fact]
    public void A_sheet_takes_a_relief_and_undo_gives_the_old_one_back()
    {
        var g = Table();
        var sha = new string('b', 64);
        Assert.Null(StateMachine.Apply(g, new SetSheetRelief { Id = "s1", Relief = sha, Height = 2.5 }, out var inverse));
        Assert.Equal(sha, g.Sheets[0].Relief);
        Assert.Equal(2.5, g.Sheets[0].Height);
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.Equal(new string('a', 64), g.Sheets[0].Relief);
        Assert.Equal(1, g.Sheets[0].Height);
    }

    [Theory]
    [InlineData("../tiles", 1)]
    [InlineData(null, -1)]
    [InlineData(null, 999)]
    [InlineData(null, double.NaN)]
    public void A_bad_relief_or_height_is_refused(string? relief, double height)
    {
        var g = Table();
        Assert.NotNull(StateMachine.Apply(g, new SetSheetRelief { Id = "s1", Relief = relief, Height = height }, out _));
        Assert.Equal(new string('a', 64), g.Sheets[0].Relief);
    }

    [Fact]
    public void A_sheet_keeps_a_picture_or_a_relief()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, new SetSheetRelief { Id = "s1", Relief = null }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetSheetPicture { Id = "s1", Image = "" }, out _));
        Assert.Equal("pic", g.Sheets[0].Image);
    }

    [Fact]
    public void A_new_picture_comes_off_with_undo_and_brings_the_crop_back()
    {
        var g = Table();
        g.Sheets[0].Crop = new FaceBox { X = 0.1, Y = 0.1, W = 0.5, H = 0.5 };
        Assert.Null(StateMachine.Apply(g, new SetSheetPicture { Id = "s1", Image = "new", Package = "sheets", Size = new Size2(10, 5) }, out var inverse));
        Assert.Null(g.Sheets[0].Crop);
        Assert.Equal("new", g.Sheets[0].Image);
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.Equal("pic", g.Sheets[0].Image);
        Assert.Equal(0.5, g.Sheets[0].Crop!.W);
        Assert.Equal(new Size2(100, 50), g.Sheets[0].Size);
    }

    [Fact]
    public void Only_hash_named_relief_files_in_the_sheets_folder_may_be_uploaded()
    {
        var sha = new string('c', 64);
        Assert.True(UploadAsset.PathAllowed("sheets", ReliefFiles.HeaderRel(sha)));
        Assert.True(UploadAsset.PathAllowed("sheets", ReliefFiles.ChunkRel(sha)));
        Assert.False(UploadAsset.PathAllowed("sheets", "relief/../../x.json"));
        Assert.False(UploadAsset.PathAllowed("sheets", "tiles/abc/index.json"));
        Assert.False(UploadAsset.PathAllowed("scenarios/x", ReliefFiles.HeaderRel(sha)));
    }
    static int Count(ReliefGrid g, byte kind) => g.Kind.Count(k => k == kind);

    [Fact]
    public void Sparse_paint_covers_about_its_density_in_clumps_and_adds_nothing_on_a_second_pass()
    {
        var g = new ReliefGrid(201, 201);
        byte wood = GroundKinds.Of("wood")!.Value;
        var brush = new Brush(BrushMode.Paint, 0, 1, wood, Density: 0.4);
        ReliefBrush.Stroke(g, new List<(double, double)> { (0.5, 0.5) }, 400, brush);
        double share = Count(g, wood) / (double)g.Kind.Length;
        Assert.InRange(share, 0.3, 0.5);
        int alike = 0, pairs = 0;
        for (int i = 0; i + 1 < g.Kind.Length; i++)
            if ((i + 1) % g.Cols != 0) { pairs++; if ((g.Kind[i] == wood) == (g.Kind[i + 1] == wood)) alike++; }
        Assert.True(alike / (double)pairs > 0.7, "neighbours mostly agree, so the wood comes in clumps");
        var once = (byte[])g.Kind.Clone();
        ReliefBrush.Stroke(g, new List<(double, double)> { (0.5, 0.5) }, 400, brush);
        Assert.Equal(once, g.Kind);
        ReliefBrush.Stroke(g, new List<(double, double)> { (0.5, 0.5) }, 400, brush with { Density = 0.7 });
        Assert.True(Count(g, wood) > once.Count(k => k == wood));
        Assert.All(Enumerable.Range(0, once.Length).Where(i => once[i] == wood), i => Assert.Equal(wood, g.Kind[i]));
    }

    [Fact]
    public void Fill_paints_the_patch_of_like_ground_up_to_other_types()
    {
        var g = new ReliefGrid(40, 40);
        byte road = GroundKinds.Of("road")!.Value, grass = GroundKinds.Of("grass")!.Value;
        for (int y = 0; y < g.Rows; y++) g.Kind[y * g.Cols + 20] = road;
        var box = ReliefBrush.Fill(g, 0.1, 0.5, grass, 1);
        Assert.Equal(new CellBox(0, 0, 19, 39), box);
        Assert.Equal(20 * 40, Count(g, grass));
        Assert.Equal(40, Count(g, road));
        Assert.True(ReliefBrush.Fill(g, 0.1, 0.5, grass, 1).Empty);
        ReliefBrush.Fill(g, 0.9, 0.5, grass, 1);
        Assert.Equal(39 * 40, Count(g, grass));
    }

    [Fact]
    public void A_road_across_water_is_a_bridge_until_water_is_painted_back()
    {
        var g = new ReliefGrid(40, 40);
        for (int y = 0; y < g.Rows; y++)
            for (int x = 18; x <= 22; x++) g.Kind[y * g.Cols + x] = GroundKinds.Water;
        var road = new Brush(BrushMode.Paint, 0, 1, GroundKinds.Road);
        ReliefBrush.Stroke(g, new List<(double, double)> { (0.1, 0.5), (0.9, 0.5) }, 2, road);
        Assert.Equal(GroundKinds.Bridge, g.KindAt(0.5, 0.5));
        Assert.Equal(GroundKinds.Road, g.KindAt(0.2, 0.5));
        Assert.Equal(GroundKinds.Water, g.KindAt(0.5, 0.2));
        ReliefBrush.Stroke(g, new List<(double, double)> { (0.5, 0.5) }, 2, road);
        Assert.Equal(GroundKinds.Bridge, g.KindAt(0.5, 0.5));
        ReliefBrush.Dab(g, 0.5, 0.5, 2, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Water));
        Assert.Equal(GroundKinds.Water, g.KindAt(0.5, 0.5));
        Assert.False(ReliefBrush.Fill(g, 0.5, 0.2, GroundKinds.Road, 1).Empty);
        Assert.Equal(5 * 40, Count(g, GroundKinds.Bridge));
        Assert.Equal(GroundKinds.Road, GroundKinds.BrushOf(GroundKinds.Bridge));
        Assert.DoesNotContain(GroundKinds.Bridge, GroundKinds.Brushes);
        Assert.Contains(GroundKinds.Road, GroundKinds.Brushes);
    }

    [Fact]
    public void Fill_on_bare_ground_covers_the_whole_sheet()
    {
        var g = new ReliefGrid(30, 20);
        ReliefBrush.Fill(g, 0.5, 0.5, GroundKinds.Of("field")!.Value, 1);
        Assert.All(g.Kind, k => Assert.Equal(GroundKinds.Of("field")!.Value, k));
    }

    static List<(double, double)> Cells(ReliefGrid g, params (double x, double y)[] corners) =>
        corners.Select(c => (c.x / (g.Cols - 1), c.y / (g.Rows - 1))).ToList();

    [Fact]
    public void A_shape_paints_every_cell_inside_it_and_on_its_edges_and_nothing_else()
    {
        var g = new ReliefGrid(40, 40);
        byte field = GroundKinds.Of("field")!.Value;
        var box = ReliefBrush.Shape(g, Cells(g, (2, 3), (5, 3), (5, 6), (2, 6)), field, 1);
        Assert.Equal(new CellBox(2, 3, 5, 6), box);
        Assert.Equal(16, Count(g, field));
        Assert.True(ReliefBrush.Shape(g, Cells(g, (2, 3), (5, 3), (5, 6), (2, 6)), field, 1).Empty);
        Assert.True(ReliefBrush.Shape(g, Cells(g, (2, 3), (5, 3)), field, 1).Empty);
        Assert.True(ReliefBrush.Shape(g, Cells(g, (8, 3), (12, 3), (10, 3)), field, 1).Empty);
        ReliefBrush.Shape(g, Cells(g, (-10, -10), (50, -10), (50, 50), (-10, 50)), field, 1);
        Assert.All(g.Kind, k => Assert.Equal(field, k));
    }

    [Fact]
    public void A_turned_shape_paints_its_own_outline_and_a_notched_polygon_leaves_its_notch()
    {
        var g = new ReliefGrid(41, 41);
        byte wood = GroundKinds.Of("wood")!.Value;
        ReliefBrush.Shape(g, Cells(g, (20, 4), (36, 20), (20, 36), (4, 20)), wood, 1);
        Assert.Equal(wood, g.Kind[20 * 41 + 20]);
        Assert.Equal(wood, g.Kind[20 * 41 + 5]);
        Assert.Equal(GroundKinds.None, g.Kind[5 * 41 + 5]);
        Assert.Equal(GroundKinds.None, g.Kind[35 * 41 + 35]);
        Assert.InRange(Count(g, wood), 16 * 16 * 2 * 0.95, 16 * 16 * 2 * 1.1);
        var h = new ReliefGrid(41, 41);
        ReliefBrush.Shape(h, Cells(h, (0, 0), (40, 0), (40, 40), (20, 10), (0, 40)), wood, 1);
        Assert.Equal(wood, h.Kind[5 * 41 + 20]);
        Assert.Equal(GroundKinds.None, h.Kind[30 * 41 + 20]);
        Assert.Equal(wood, h.Kind[30 * 41 + 2]);
    }

    [Fact]
    public void A_shape_drawn_on_a_turned_sheet_lands_where_it_was_drawn()
    {
        var s = Sheet("s1", 10, -5, 40, 40, rot: 63);
        var g = new ReliefGrid(41, 41);
        byte field = GroundKinds.Of("field")!.Value;
        var world = new[] { (0.25, 0.25), (0.75, 0.25), (0.75, 0.75), (0.25, 0.75) }.Select(c => Ground.FromUv(s, c.Item1, c.Item2)).ToList();
        ReliefBrush.Shape(g, world.Select(p => Ground.UvOf(s, p)).ToList(), field, 1);
        Assert.Equal(21 * 21, Count(g, field));
        Assert.Equal(field, g.KindAt(0.5, 0.5));
        Assert.Equal(GroundKinds.None, g.KindAt(0.2, 0.5));
        var beyond = Ground.UvOf(s, Ground.FromUv(s, 1.5, -0.5));
        Assert.Equal(1.5, beyond.u, 9);
        Assert.Equal(-0.5, beyond.v, 9);
        Assert.Null(Ground.Uv(s, Ground.FromUv(s, 1.5, -0.5)));
    }

    [Fact]
    public void A_shape_keeps_the_paint_rules_bridge_density_and_rubbing_out()
    {
        var g = new ReliefGrid(40, 40);
        for (int y = 0; y < g.Rows; y++)
            for (int x = 18; x <= 22; x++) g.Kind[y * g.Cols + x] = GroundKinds.Water;
        ReliefBrush.Shape(g, Cells(g, (5, 10), (35, 10), (35, 12), (5, 12)), GroundKinds.Road, 1);
        Assert.Equal(5 * 3, Count(g, GroundKinds.Bridge));
        Assert.Equal(26 * 3, Count(g, GroundKinds.Road));
        ReliefBrush.Shape(g, Cells(g, (0, 0), (39, 0), (39, 39), (0, 39)), GroundKinds.None, 1);
        Assert.All(g.Kind, k => Assert.Equal(GroundKinds.None, k));
        var big = new ReliefGrid(201, 201);
        byte rock = GroundKinds.Of("rock")!.Value;
        ReliefBrush.Shape(big, Cells(big, (0, 0), (200, 0), (200, 200), (0, 200)), rock, 0.3);
        Assert.InRange(Count(big, rock) / (double)big.Kind.Length, 0.2, 0.4);
    }

    [Fact]
    public void Every_map_style_colours_every_ground_type_and_unknown_styles_read_as_the_survey()
    {
        Assert.Equal(MapStyles.Survey, MapStyles.Of(null));
        Assert.Equal(MapStyles.Survey, MapStyles.Of("baroque"));
        Assert.Equal(MapStyles.Modern, MapStyles.Of(" Modern "));
        Assert.Equal(MapStyles.All.Count, MapStyles.ShaderOrder.Distinct().Count());
        foreach (var (id, _) in MapStyles.All)
            for (byte k = 1; k < GroundKinds.All.Count; k++)
                Assert.Matches("^#[0-9A-F]{6}$", GroundKinds.Color(k, id));
        Assert.Equal(GroundKinds.All[3].Color, GroundKinds.Color(3, MapStyles.Survey));
        Assert.NotEqual(GroundKinds.Color(6, MapStyles.Ancient), GroundKinds.Color(6, MapStyles.Modern));
    }

    [Fact]
    public void Border_distances_grow_away_from_where_ground_types_meet()
    {
        var g = new ReliefGrid(20, 4);
        Assert.All(GroundEdges.Distances(g), d => Assert.Equal(GroundEdges.Far, d));
        for (int y = 0; y < g.Rows; y++)
            for (int x = 10; x < g.Cols; x++) g.Kind[y * g.Cols + x] = GroundKinds.Of("water")!.Value;
        var d = GroundEdges.Distances(g);
        Assert.Equal(GroundEdges.StepsPerCell / 2, d[1 * g.Cols + 10]);
        Assert.Equal(GroundEdges.StepsPerCell / 2, d[1 * g.Cols + 9]);
        Assert.Equal(GroundEdges.StepsPerCell * 5 / 2, d[1 * g.Cols + 12]);
        Assert.True(d[1 * g.Cols + 19] > d[1 * g.Cols + 15]);
    }

    [Fact]
    public void Contours_step_in_round_heights_and_in_real_units_once_the_map_is_calibrated()
    {
        Assert.Equal(0, Contours.Interval(0));
        Assert.Equal(2, Contours.Interval(17));
        Assert.Equal(50, Contours.Interval(390));
        var plain = Contours.Plan(19.5, 1, new Calibration())!;
        Assert.Equal(2, plain.Step);
        Assert.False(plain.Numbered);
        var metres = Contours.Plan(19.5, 1, new Calibration { UnitsPerMeter = 0.05 })!;
        Assert.Equal(50, metres.Every);
        Assert.Equal(2.5, metres.Step, 9);
        Assert.Equal("m", metres.Unit);
        Assert.Equal(5, Contours.Plan(39, 2, new Calibration { UnitsPerMeter = 0.05 })!.Step, 9);
        Assert.Null(Contours.Plan(19.5, 0, new Calibration()));
        Assert.Null(Contours.Plan(0, 1, new Calibration()));
    }

    [Fact]
    public void A_stroke_lays_its_salt_and_levels_with_the_kind_and_a_repaint_changes_the_salt_alone()
    {
        var g = new ReliefGrid(40, 40);
        byte grass = GroundKinds.Of("grass")!.Value;
        ReliefBrush.Dab(g, 0.5, 0.5, 6, new Brush(BrushMode.Paint, 0, 1, grass, Salt: 17, Detail: 0x52));
        int mid = 20 * 40 + 20;
        Assert.Equal(grass, g.Kind[mid]);
        Assert.Equal(17, g.Salt[mid]);
        Assert.Equal(0x52, g.Detail[mid]);
        Assert.Equal(0, g.Salt[0]);
        var box = ReliefBrush.Fill(g, 0.5, 0.5, new Brush(BrushMode.Paint, 0, 1, grass, Salt: 99, Detail: 0x00));
        Assert.False(box.Empty);
        Assert.Equal(grass, g.Kind[mid]);
        Assert.Equal(99, g.Salt[mid]);
        Assert.Equal(0, g.Detail[mid]);
        ReliefBrush.Dab(g, 0.5, 0.5, 2, new Brush(BrushMode.Paint, 0, 1, GroundKinds.None, Salt: 5, Detail: 0x11));
        Assert.Equal(GroundKinds.None, g.Kind[mid]);
        Assert.Equal(0, g.Salt[mid]);
        Assert.Equal(0, g.Detail[mid]);
    }

    [Fact]
    public void Salt_and_levels_round_trip_in_chunks_and_a_header_without_them_loads_with_zeros_and_its_old_sha()
    {
        var g = Slope(130, 70, 0.1f);
        var plain = ReliefFiles.Save(_dir, g);
        g.Kind[7] = GroundKinds.Of("wood")!.Value;
        g.Salt[7] = 200;
        g.Detail[7] = 0x3C;
        var (sha, written) = ReliefFiles.Save(_dir, g);
        Assert.NotEqual(plain.sha, sha);
        var index = ReliefFiles.LoadIndex(Path.Combine(_dir, ReliefFiles.HeaderRel(sha)));
        Assert.NotNull(index.Seeds);
        Assert.NotNull(index.Details);
        var back = ReliefFiles.Load(index, rel => Path.Combine(_dir, rel));
        Assert.Equal(g.Salt, back.Salt);
        Assert.Equal(g.Detail, back.Detail);
        Assert.All(ReliefFiles.Files(sha, index), rel => Assert.True(File.Exists(Path.Combine(_dir, rel)), rel));
        var old = ReliefFiles.LoadIndex(Path.Combine(_dir, ReliefFiles.HeaderRel(plain.sha)));
        Assert.Null(old.Seeds);
        Assert.DoesNotContain("seeds", File.ReadAllText(Path.Combine(_dir, ReliefFiles.HeaderRel(plain.sha))));
        var unseeded = ReliefFiles.Load(old, rel => Path.Combine(_dir, rel));
        Assert.All(unseeded.Salt, s => Assert.Equal(0, s));
        Assert.Equal(plain.sha, ReliefFiles.Save(_dir, unseeded).sha);
    }

    [Fact]
    public void A_town_keeps_the_roads_under_it_and_lays_nothing_on_water_and_its_distance_field_grows_away_from_it()
    {
        Assert.Equal(GroundKinds.Road, GroundKinds.Laid(GroundKinds.Road, GroundKinds.Town));
        Assert.Equal(GroundKinds.Bridge, GroundKinds.Laid(GroundKinds.Bridge, GroundKinds.Town));
        Assert.Equal(GroundKinds.Water, GroundKinds.Laid(GroundKinds.Water, GroundKinds.Town));
        Assert.Equal(GroundKinds.Town, GroundKinds.Laid(GroundKinds.Of("grass")!.Value, GroundKinds.Town));
        Assert.Equal(GroundKinds.Road, GroundKinds.Laid(GroundKinds.Town, GroundKinds.Road));
        Assert.Contains(GroundKinds.Town, GroundKinds.Brushes);
        Assert.Equal(new[] { "Houses", "Walls" }, GroundKinds.Parameters(GroundKinds.Town).Select(p => p.Name));
        var g = new ReliefGrid(64, 64);
        for (int y = 20; y < 30; y++)
            for (int x = 20; x < 30; x++) g.Kind[y * 64 + x] = x == 24 ? GroundKinds.Road : GroundKinds.Town;
        g.Kind[5 * 64 + 5] = GroundKinds.Water;
        var fields = GroundEdges.Fields(g);
        int At(int x, int y, int channel) => fields[(y * 64 + x) * GroundEdges.Channels + channel];
        Assert.Equal(0, At(25, 25, 1) / GroundEdges.FieldStepsPerCell);
        Assert.True(At(35, 25, 1) > At(31, 25, 1), "the town field grows away from the town");
        Assert.True(At(63, 63, 1) > At(35, 25, 1) && At(63, 63, 1) < GroundEdges.Far, "the far corner is far but not off the field");
        Assert.True(At(10, 5, 2) > At(6, 5, 2) && At(6, 5, 2) > 0, "the water field grows away from the water");
        Assert.Equal(GroundEdges.Distances(g)[25 * 64 + 25], At(25, 25, 0));
        Assert.True(At(25, 25, 3) > At(21, 25, 3) && At(24, 25, 3) > At(21, 25, 3), "the town's depth grows inward and a road through it is no edge");
        Assert.Equal(GroundEdges.TownStepsPerCell / 2, At(10, 10, 3));
    }

    [Fact]
    public void Each_kind_names_its_own_parameters_and_their_levels_fit_a_byte()
    {
        byte field = GroundKinds.Of("field")!.Value, wood = GroundKinds.Of("wood")!.Value;
        Assert.Equal(new[] { "Plots", "Buildings" }, GroundKinds.Parameters(field).Select(p => p.Name));
        Assert.Equal(new[] { "Trees" }, GroundKinds.Parameters(wood).Select(p => p.Name));
        Assert.Empty(GroundKinds.Parameters(GroundKinds.Water));
        Assert.NotNull(GroundKinds.Parameter(field, "plots"));
        Assert.Null(GroundKinds.Parameter(wood, "plots"));
        Assert.Null(GroundKinds.Parameter(GroundKinds.Road, "trees"));
        Assert.Equal((100, 0), GroundKinds.LevelsOf(GroundKinds.Levels(100, 0)));
        Assert.Equal((0, 100), GroundKinds.LevelsOf(GroundKinds.Levels(0, 100)));
        var (first, second) = GroundKinds.LevelsOf(GroundKinds.Levels(40, 15));
        Assert.InRange(first, 37, 43);
        Assert.InRange(second, 12, 18);
        Assert.Equal(0, GroundKinds.Levels(-5, 0));
    }

    [Fact]
    public void Blurred_heights_keep_flat_ground_soften_a_step_and_hold_the_mean()
    {
        var flat = new ReliefGrid(30, 20);
        Array.Fill(flat.H, 2.5f);
        Assert.All(GroundHeights.Blurred(flat, 3), h => Assert.Equal(2.5f, h, 5));
        var step = new ReliefGrid(40, 10);
        for (int y = 0; y < 10; y++)
            for (int x = 20; x < 40; x++) step.H[y * 40 + x] = 1;
        Assert.Equal(step.H, GroundHeights.Blurred(step, 0));
        var soft = GroundHeights.Blurred(step, 2);
        for (int x = 1; x < 40; x++) Assert.True(soft[5 * 40 + x] >= soft[5 * 40 + x - 1], "rises across the step");
        Assert.Equal(0, soft[5 * 40 + 10], 5);
        Assert.Equal(1, soft[5 * 40 + 30], 5);
        Assert.InRange(soft[5 * 40 + 19], 0.3f, 0.5f);
        Assert.InRange(soft[5 * 40 + 20], 0.5f, 0.7f);
        Assert.Equal(step.H.Sum(), soft.Sum(), 3);
    }

    [Fact]
    public void Heights_read_in_roman_numerals_for_the_ancient_style_only_when_whole()
    {
        Assert.Equal("CCL", Roman.Of(250));
        Assert.Equal("MCMXCIV", Roman.Of(1994));
        Assert.Equal("XLIX", Roman.Of(49));
        Assert.Null(Roman.Of(2.5));
        Assert.Null(Roman.Of(0));
        Assert.Null(Roman.Of(4000));
    }
}
