using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using KTable.Client.Hand;
using KTable.Client.Relief;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class ReliefBuilderTests
{
    readonly string _dir = TestFolders.New();

    string Write(string name, string text)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, text);
        return p;
    }

    static ModelMesh Grid(int n, Func<float, float, float> height, bool zUp = false, bool flipV = false)
    {
        var m = new ModelMesh { Materials = { new ModelMaterial(Rgba.White, null) } };
        Vector3 P(float x, float z) => zUp ? new Vector3(x, -z, height(x, z)) : new Vector3(x, height(x, z), z);
        Vector2 Uv(float x, float z) => new(x / n, flipV ? 1 - z / n : z / n);
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                foreach (var (x, z) in new[] { (i, j), (i + 1, j), (i, j + 1), (i + 1, j), (i + 1, j + 1), (i, j + 1) })
                {
                    m.Add(P(x, z), Vector3.UnitY, Uv(x, z), Rgba.White);
                    if (m.V.Count % 3 == 0) m.Mat.Add(0);
                }
        return m;
    }

    [Fact]
    public void A_sloping_grid_becomes_a_relief_rising_the_same_way()
    {
        var src = ReliefBuilder.FromMesh(Grid(40, (x, _) => x * 0.25f), "slope");
        Assert.Equal(40, src.Width, 3);
        var g = src.ToGrid(src.Low);
        Assert.True(g.HeightAt(1, 0.5) > g.HeightAt(0, 0.5));
        Assert.Equal(10 / 40.0, g.HeightAt(1, 0.5), 2);
        Assert.Equal(0, g.HeightAt(0, 0.5), 3);
    }

    [Fact]
    public void A_z_up_model_is_read_from_above_the_same_as_a_y_up_one()
    {
        var y = ReliefBuilder.FromMesh(Grid(20, (x, z) => x * 0.1f + z * 0.05f), "y").ToGrid(0);
        var z = ReliefBuilder.FromMesh(Grid(20, (x, z) => x * 0.1f + z * 0.05f, zUp: true), "z").ToGrid(0);
        foreach (var (u, v) in new[] { (0.2, 0.3), (0.8, 0.9), (0.5, 0.1) })
            Assert.Equal(y.HeightAt(u, v), z.HeightAt(u, v), 3);
    }

    [Fact]
    public void A_top_down_texture_is_used_as_it_is_or_flipped()
    {
        var plain = ReliefBuilder.FromMesh(Grid(10, (_, _) => 0.5f), "plain");
        Assert.Equal(PictureFit.Direct, plain.Fit);
        Assert.False(plain.FlipU || plain.FlipV || plain.Swap);
        var flipped = ReliefBuilder.FromMesh(Grid(10, (_, _) => 0.5f, flipV: true), "flipped");
        Assert.Equal(PictureFit.Direct, flipped.Fit);
        Assert.True(flipped.FlipV);
    }

    [Fact]
    public void Two_tiles_of_one_map_meet_without_a_step_when_they_share_a_zero()
    {
        var left = ReliefBuilder.FromMesh(Grid(20, (x, _) => 5 + x * 0.1f), "left");
        var right = ReliefBuilder.FromMesh(Grid(20, (x, _) => 7 + x * 0.1f), "right");
        double zero = ReliefSource.SharedZero(new[] { left, right });
        Assert.Equal(left.ToGrid(zero).HeightAt(1, 0.5), right.ToGrid(zero).HeightAt(0, 0.5), 3);
    }

    [Fact]
    public void Holes_in_a_model_are_filled_from_around_them()
    {
        var m = Grid(10, (_, _) => 2);
        var keep = new ModelMesh { Materials = { m.Materials[0] } };
        for (int t = 0; t + 2 < m.V.Count; t += 3)
        {
            if (t / 6 == 44) continue;
            for (int k = 0; k < 3; k++) keep.Add(m.V[t + k], m.N[t + k], m.UV[t + k], m.C[t + k]);
            keep.Mat.Add(0);
        }
        var g = ReliefBuilder.FromMesh(keep, "holed").ToGrid(0);
        Assert.All(g.H, h => Assert.Equal(2f / 10, h, 3));
    }

    [Fact]
    public void An_esri_grid_reads_with_its_cell_size()
    {
        var path = Write("dem.asc", "ncols 3\nnrows 2\nxllcorner 1000\nyllcorner 2000\ncellsize 30\nNODATA_value -9999\n1 2 3\n4 -9999 6\n");
        var src = HeightFiles.Read(path);
        Assert.Equal(3, src.Cols);
        Assert.Equal(2, src.Rows);
        Assert.Equal(60, src.Width, 6);
        Assert.True(src.RealUnits);
        Assert.InRange(src.Raw[4], 1, 6);
    }

    [Fact]
    public void An_xyz_point_grid_reads_north_up()
    {
        var sb = new StringBuilder();
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++) sb.AppendLine($"{x * 10} {y * 10} {y * 5}");
        var src = HeightFiles.Read(Write("pts.xyz", sb.ToString()));
        Assert.Equal(4, src.Cols);
        Assert.Equal(3, src.Rows);
        Assert.True(src.Raw[0] > src.Raw[^1]);
    }

    [Fact]
    public void A_file_that_is_not_an_srtm_tile_is_refused()
    {
        var p = Path.Combine(_dir, "N45E007.hgt");
        File.WriteAllBytes(p, new byte[100]);
        Assert.Throws<InvalidDataException>(() => HeightFiles.Read(p));
    }

    static byte[] Png16(int w, int h, Func<int, int, ushort> value)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(y % 2 == 0 ? (byte)0 : (byte)2);
            for (int x = 0; x < w; x++)
            {
                ushort v = value(x, y);
                ushort above = y > 0 ? value(x, y - 1) : (ushort)0;
                if (y % 2 == 1) { raw.WriteByte((byte)((v >> 8) - (above >> 8))); raw.WriteByte((byte)((v & 0xFF) - (above & 0xFF))); }
                else { raw.WriteByte((byte)(v >> 8)); raw.WriteByte((byte)v); }
            }
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) raw.WriteTo(zs);
        using var o = new MemoryStream();
        o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        void Chunk(string kind, byte[] data)
        {
            var len = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
            o.Write(len); o.Write(Encoding.ASCII.GetBytes(kind)); o.Write(data); o.Write(new byte[4]);
        }
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w); BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 16; ihdr[9] = 0;
        Chunk("IHDR", ihdr); Chunk("IDAT", z.ToArray()); Chunk("IEND", Array.Empty<byte>());
        return o.ToArray();
    }

    [Fact]
    public void A_sixteen_bit_heightmap_keeps_its_fine_steps()
    {
        var p = Path.Combine(_dir, "h.png");
        File.WriteAllBytes(p, Png16(16, 8, (x, y) => (ushort)(x * 4000 + y * 3)));
        Assert.True(HeightFiles.IsDeepPng(p));
        var src = HeightFiles.Png(p);
        Assert.Equal(16, src.Cols);
        Assert.False(src.RealUnits);
        Assert.True(src.Raw[1 * 16 + 0] > src.Raw[0]);
        Assert.True(src.Raw[15] > src.Raw[0]);
        Assert.Equal(ReliefSource.UnitlessRise, src.ToGrid(src.Low).H.Max(), 3);
    }

    [Fact]
    public void A_collada_triangle_reads_with_its_texture_coordinates()
    {
        var p = Write("tri.dae", """
            <?xml version="1.0"?>
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
              <asset><up_axis>Z_UP</up_axis></asset>
              <library_geometries><geometry id="g"><mesh>
                <source id="p"><float_array count="9">0 0 1  4 0 1  0 4 3</float_array><technique_common><accessor stride="3"/></technique_common></source>
                <source id="t"><float_array count="6">0 0 1 0 0 1</float_array><technique_common><accessor stride="2"/></technique_common></source>
                <vertices id="v"><input semantic="POSITION" source="#p"/></vertices>
                <triangles count="1"><input semantic="VERTEX" source="#v" offset="0"/><input semantic="TEXCOORD" source="#t" offset="1"/><p>0 0 1 1 2 2</p></triangles>
              </mesh></geometry></library_geometries>
              <library_visual_scenes><visual_scene id="s"><node><instance_geometry url="#g"/></node></visual_scene></library_visual_scenes>
            </COLLADA>
            """);
        var m = ColladaFile.Read(p);
        Assert.Equal(3, m.V.Count);
        Assert.Contains(m.V, v => Math.Abs(v.Y - 3) < 1e-4);
        Assert.Single(m.Mat);
    }

    static Ground Hill() => new(new[] { new SheetDef { Id = "s", Image = "x", Size = new Size2(100, 100), RotDeg = 180, Relief = new string('a', 64) } },
        _ => { var g = new ReliefGrid(101, 101); for (int y = 0; y < 101; y++) for (int x = 0; x < 101; x++) g.H[y * 101 + x] = 0.1f * x / 100; return g; });

    [Fact]
    public void A_piece_let_go_of_on_a_hillside_stands_on_it_and_is_kept_as_on_the_ground()
    {
        var ground = Hill();
        var piece = new FakePiece("a", 10, 0, 4, 2);
        var footing = new Footing<FakePiece>(_ => Array.Empty<FakePiece>(), _ => false, () => ground);
        var (pos, y) = footing.Rest(piece, new Vec2(10, 0), 180, new HashSet<FakePiece> { piece });
        Assert.Equal(ground.Under(pos, 180, 4, 2), y, 4);
        Assert.Equal(0, footing.Stored(piece, pos, 180, y));
        Assert.Equal(ground.At(new Vec2(10, 0)), footing.SurfaceY(new Vec2(10, 0), Layers.Umpire), 4);
    }

    [Fact]
    public void A_piece_set_on_a_block_on_a_hillside_keeps_its_height()
    {
        var ground = Hill();
        float top = (float)ground.Under(new Vec2(10, 0), 180, 6, 6);
        var block = new FakePiece("b", 10, 0, 6, 6, y: top, thickness: 0.5f);
        var small = new FakePiece("s", 10, 0, 1, 1);
        var footing = new Footing<FakePiece>(_ => new[] { block }, _ => false, () => ground);
        var (_, y) = footing.Rest(small, new Vec2(10, 0), 180, new HashSet<FakePiece> { small });
        Assert.Equal(block.TopY - 0.03, y, 3);
        Assert.Equal(y, footing.Stored(small, new Vec2(10, 0), 180, y));
    }

    static byte[] Numbered(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = (byte)i; px[i * 4 + 3] = 255; }
        return px;
    }

    static int[] Cells(byte[] px) => Enumerable.Range(0, px.Length / 4).Select(i => (int)px[i * 4]).ToArray();

    [Fact]
    public void A_terrain_whose_picture_runs_upside_down_gets_its_picture_turned_to_lie_as_on_the_model()
    {
        var src = ReliefBuilder.FromMesh(Grid(4, (_, _) => 0, flipV: true), "t");
        Assert.Equal((PictureFit.Direct, false, true, false), (src.Fit, src.FlipU, src.FlipV, src.Swap));
        Assert.False(ReliefPicture.AsItIs(src));
        var (px, w, h) = ReliefPicture.Fitted(src, Numbered(2, 3), 2, 3);
        Assert.Equal((2, 3), (w, h));
        Assert.Equal(new[] { 4, 5, 2, 3, 0, 1 }, Cells(px));
    }

    [Fact]
    public void A_terrain_block_with_a_floor_under_it_shows_its_ground_from_above_and_not_its_floor()
    {
        var m = Grid(4, (_, _) => 0.5f);
        foreach (var (x, z) in new[] { (0, 0), (4, 0), (0, 4), (4, 0), (4, 4), (0, 4) })
        {
            m.Add(new Vector3(x, -1, z), -Vector3.UnitY, new Vector2(x / 32f, z / 32f), Rgba.White);
            if (m.V.Count % 3 == 0) m.Mat.Add(0);
        }
        var src = ReliefBuilder.FromMesh(m, "block");
        Assert.Equal(0.5, src.Raw.Min(), 3);
        var (px, w, h) = ReliefPicture.Fitted(src, Numbered(2, 2), 2, 2);
        Assert.Equal((2, 2), (w, h));
        Assert.Equal(new[] { 0, 1, 2, 3 }, Cells(px));
    }

    [Fact]
    public void A_terrain_picture_laid_across_is_transposed_and_then_mirrored_as_its_flips_say()
    {
        ReliefSource Src(bool u, bool v) => new() { Name = "t", Cols = 2, Rows = 2, Raw = new float[4], Width = 1, Depth = 1, Fit = PictureFit.Direct, Swap = true, FlipU = u, FlipV = v };
        var (px, w, h) = ReliefPicture.Fitted(Src(false, false), Numbered(2, 3), 2, 3);
        Assert.Equal((3, 2), (w, h));
        Assert.Equal(new[] { 0, 2, 4, 1, 3, 5 }, Cells(px));
        Assert.Equal(new[] { 4, 2, 0, 5, 3, 1 }, Cells(ReliefPicture.Fitted(Src(true, false), Numbered(2, 3), 2, 3).Rgba));
        Assert.Equal(new[] { 1, 3, 5, 0, 2, 4 }, Cells(ReliefPicture.Fitted(Src(false, true), Numbered(2, 3), 2, 3).Rgba));
    }
}
