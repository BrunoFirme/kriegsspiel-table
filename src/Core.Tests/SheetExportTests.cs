using System.Buffers.Binary;
using System.IO.Compression;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class SheetExportTests
{
    const int TileSize = 64;

    static byte[] Colour(int sheet, int x, int y) => [(byte)(x * 7 + sheet * 50), (byte)(y * 5 + 3), (byte)(x + y), 255];

    static SheetExport.Source Sheet(int w, int h, double x = 0, double z = 0, double rot = 180, double perUnit = 10, int stack = 0, FaceBox? crop = null)
    {
        var index = new TileIndex { TileSize = TileSize, SourceW = w, SourceH = h, Ext = "png" };
        index.Levels.Add(new TileLevel { Level = 0, W = w, H = h, Cols = (w + TileSize - 1) / TileSize, Rows = (h + TileSize - 1) / TileSize });
        var c = crop ?? SheetGeometry.Whole;
        var size = new Size2(w * c.W / perUnit, h * c.H / perUnit);
        return new SheetExport.Source(new SheetDef { Id = "s" + stack, Name = "s" + stack, Image = "pic", Pos = new Vec2(x, z), RotDeg = rot, Size = size, Z = stack, Crop = crop }, index);
    }

    static byte[] Tile(SheetExport.Source s, int tx, int ty)
    {
        int w = Math.Min(TileSize, s.Full.W - tx * TileSize), h = Math.Min(TileSize, s.Full.H - ty * TileSize);
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Colour(s.Sheet.Z, tx * TileSize + x, ty * TileSize + y).CopyTo(rgba, (y * w + x) * 4);
        return rgba;
    }

    static (SheetExport.Frame Frame, byte[] Rgba) Rendered(params SheetExport.Source[] sources)
    {
        var frame = SheetExport.FrameOf(sources);
        var all = new MemoryStream();
        SheetExport.Render(sources, frame, Tile, (rows, count) => all.Write(rows, 0, count * frame.W * 4), () => false);
        return (frame, all.ToArray());
    }

    static byte[] At(SheetExport.Frame f, byte[] rgba, int x, int y) => rgba.AsSpan((y * f.W + x) * 4, 4).ToArray();

    [Fact]
    public void One_sheet_comes_out_as_its_own_picture_pixel_for_pixel()
    {
        var (frame, rgba) = Rendered(Sheet(150, 100));

        Assert.Equal((150, 100), (frame.W, frame.H));
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 150; x++)
                Assert.Equal(Colour(0, x, y), At(frame, rgba, x, y));
    }

    [Fact]
    public void A_picture_taller_than_one_band_is_written_whole()
    {
        var (frame, rgba) = Rendered(Sheet(40, SheetExport.BandRows * 2 + 30));

        Assert.Equal(SheetExport.BandRows * 2 + 30, frame.H);
        Assert.Equal(Colour(0, 39, frame.H - 1), At(frame, rgba, 39, frame.H - 1));
    }

    [Fact]
    public void A_cropped_sheet_gives_only_the_part_kept()
    {
        var (frame, rgba) = Rendered(Sheet(200, 100, crop: new FaceBox { X = 0.25, Y = 0.5, W = 0.5, H = 0.5 }));

        Assert.Equal((100, 50), (frame.W, frame.H));
        Assert.Equal(Colour(0, 50, 50), At(frame, rgba, 0, 0));
        Assert.Equal(Colour(0, 149, 99), At(frame, rgba, 99, 49));
    }

    [Fact]
    public void Sheets_side_by_side_come_out_as_one_picture()
    {
        var (frame, rgba) = Rendered(Sheet(100, 80, x: 5), Sheet(60, 80, x: 13, stack: 1));

        Assert.Equal((160, 80), (frame.W, frame.H));
        Assert.Equal(Colour(0, 99, 40), At(frame, rgba, 99, 40));
        Assert.Equal(Colour(1, 0, 40), At(frame, rgba, 100, 40));
    }

    [Fact]
    public void The_upper_sheet_hides_the_lower_where_they_overlap_and_the_table_between_stays_clear()
    {
        var (frame, rgba) = Rendered(Sheet(100, 100, stack: 1), Sheet(40, 40, x: 5, z: 4, stack: 2), Sheet(20, 20, x: 9, z: -9, stack: 0));

        Assert.Equal((150, 160), (frame.W, frame.H));
        Assert.Equal(Colour(2, 0, 0), At(frame, rgba, 80, 0));
        Assert.Equal(Colour(2, 10, 20), At(frame, rgba, 90, 20));
        Assert.Equal(Colour(1, 60, 50), At(frame, rgba, 60, 60));
        Assert.Equal(Colour(0, 19, 19), At(frame, rgba, 149, 159));
        Assert.Equal(0, At(frame, rgba, 149, 0)[3]);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    [InlineData(0)]
    public void A_turned_sheet_is_written_as_it_lies_on_the_table(double rot)
    {
        var sheet = Sheet(120, 80, x: 3, z: -2, rot: rot);
        var (frame, rgba) = Rendered(sheet);

        Assert.Equal(rot == 0 ? (120, 80) : (80, 120), (frame.W, frame.H));
        foreach (var (x, y) in new[] { (0, 0), (119, 0), (0, 79), (119, 79), (37, 61) })
        {
            var onTable = sheet.Sheet.Pos + SheetGeometry.Offset((x + 0.5) / 10 - 6, (y + 0.5) / 10 - 4, rot);
            int px = (int)Math.Floor((onTable.X - frame.Left) / frame.StepX), py = (int)Math.Floor((frame.Top - onTable.Z) / frame.StepZ);
            Assert.Equal(Colour(0, x, y), At(frame, rgba, px, py));
        }
    }

    [Fact]
    public void The_picture_is_as_fine_as_the_sharpest_sheet()
    {
        var (frame, rgba) = Rendered(Sheet(100, 100, x: 5), Sheet(50, 50, x: 15, perUnit: 5, stack: 1));

        Assert.Equal((200, 100), (frame.W, frame.H));
        Assert.Equal(Colour(1, 0, 0), At(frame, rgba, 100, 0));
        Assert.Equal(Colour(1, 49, 49), At(frame, rgba, 199, 99));
    }

    [Fact]
    public void A_sheet_at_an_odd_angle_is_drawn_inside_its_corners_only()
    {
        var (frame, rgba) = Rendered(Sheet(100, 100, rot: 45));

        Assert.InRange(frame.W, 140, 142);
        Assert.Equal(0, At(frame, rgba, 2, 2)[3]);
        Assert.Equal(255, At(frame, rgba, frame.W / 2, frame.H / 2)[3]);
    }

    [Fact]
    public void A_tile_of_the_wrong_size_stops_the_export()
    {
        var sheet = Sheet(100, 100);
        var frame = SheetExport.FrameOf([sheet]);

        var thrown = Record.Exception(() => SheetExport.Render([sheet], frame, (_, _, _) => new byte[16], (_, _) => { }, () => false));

        Assert.Contains(Flattened(thrown), e => e is InvalidDataException);
    }

    static IEnumerable<Exception> Flattened(Exception? e) => e is AggregateException all ? all.Flatten().InnerExceptions : e == null ? [] : [e];

    [Fact]
    public void A_png_written_in_bands_reads_back_as_the_rows_given()
    {
        const int w = 37, h = 300;
        var rgba = new byte[w * h * 4];
        new Random(7).NextBytes(rgba);
        var file = new MemoryStream();
        using (var png = new PngWriter(file, w, h))
        {
            for (int top = 0; top < h; top += 128)
            {
                int rows = Math.Min(128, h - top);
                png.Rows(rgba.AsSpan(top * w * 4, rows * w * 4).ToArray(), rows);
            }
            png.Finish();
        }

        var (width, height, read) = ReadPng(file.ToArray());

        Assert.Equal((w, h), (width, height));
        Assert.Equal(rgba, read);
    }

    [Fact]
    public void A_png_short_of_rows_is_not_finished()
    {
        using var png = new PngWriter(new MemoryStream(), 4, 4);
        png.Rows(new byte[32], 2);

        Assert.Throws<InvalidOperationException>(png.Finish);
    }

    static uint Crc(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    static (int W, int H, byte[] Rgba) ReadPng(byte[] file)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, file[..8]);
        int at = 8, w = 0, h = 0;
        var packed = new MemoryStream();
        var seen = new List<string>();
        while (at < file.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at));
            string type = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
            var data = file.AsSpan(at + 8, length);
            Assert.Equal(Crc(file.AsSpan(at + 4, length + 4)), BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at + 8 + length)));
            if (type == "IHDR") (w, h) = (BinaryPrimitives.ReadInt32BigEndian(data), BinaryPrimitives.ReadInt32BigEndian(data[4..]));
            if (type == "IHDR") Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, data[8..].ToArray());
            if (type == "IDAT") packed.Write(data);
            seen.Add(type);
            at += 12 + length;
        }
        Assert.Equal("IHDR", seen[0]);
        Assert.Equal("IEND", seen[^1]);
        packed.Position = 0;
        var lines = new MemoryStream();
        using (var unpack = new ZLibStream(packed, CompressionMode.Decompress)) unpack.CopyTo(lines);
        var raw = lines.ToArray();
        int wide = w * 4;
        Assert.Equal((wide + 1) * h, raw.Length);
        var rgba = new byte[wide * h];
        for (int y = 0; y < h; y++)
        {
            Assert.Equal(1, raw[y * (wide + 1)]);
            for (int i = 0; i < wide; i++) rgba[y * wide + i] = (byte)(raw[y * (wide + 1) + 1 + i] + (i < 4 ? 0 : rgba[y * wide + i - 4]));
        }
        return (w, h, rgba);
    }
}
