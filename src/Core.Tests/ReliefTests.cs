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
}
