using System.Buffers.Binary;
using System.IO.Compression;
using KTable.Core;

namespace KTable.Core.Tests;

public sealed class ContentLimitsTests
{
    static string Fresh()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-limits-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    static byte[] Png(uint w, uint h)
    {
        var b = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), 13);
        "IHDR"u8.CopyTo(b.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), w);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), h);
        return b;
    }

    static byte[] Jpeg(ushort w, ushort h)
    {
        var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x06, 1, 2, 3, 4, 0xFF, 0xFF, 0xC2, 0x00, 0x11, 8 };
        b.AddRange(new[] { (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w });
        b.AddRange(new byte[12]);
        return b.ToArray();
    }

    static byte[] Webp(string chunk, Action<byte[]> fill)
    {
        var b = new byte[40];
        "RIFF"u8.CopyTo(b);
        "WEBP"u8.CopyTo(b.AsSpan(8));
        System.Text.Encoding.ASCII.GetBytes(chunk).CopyTo(b, 12);
        fill(b);
        return b;
    }

    [Fact]
    public void Picture_sides_are_read_from_each_format_header()
    {
        Assert.Equal((640, 480), PictureHeader.Read(Png(640, 480)));
        Assert.Equal((1920, 1080), PictureHeader.Read(Jpeg(1920, 1080)));
        var gif = new byte[16];
        "GIF89a"u8.CopyTo(gif);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6), 300);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8), 200);
        Assert.Equal((300, 200), PictureHeader.Read(gif));
        var bmp = new byte[32];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 800);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), -600);
        Assert.Equal((800, 600), PictureHeader.Read(bmp));
        Assert.Equal((500, 400), PictureHeader.Read(Webp("VP8 ", b => { BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(26), 500); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(28), 400); })));
        Assert.Equal((1000, 2000), PictureHeader.Read(Webp("VP8L", b => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(21), 999u | 1999u << 14))));
        Assert.Equal((70000, 3), PictureHeader.Read(Webp("VP8X", b =>
        {
            b[24] = 0x6F;
            b[25] = 0x11;
            b[26] = 0x01;
            b[27] = 2;
        })));
        var tga = new byte[18];
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), 32);
        Assert.Equal((64, 32), PictureHeader.Read(tga, ".tga"));
        Assert.Null(PictureHeader.Read(tga));
    }

    [Fact]
    public void A_picture_that_asks_for_more_than_memory_holds_is_refused_before_decoding()
    {
        Assert.True(ContentLimits.PictureAllowed(Png(4096, 4096)));
        Assert.False(ContentLimits.PictureAllowed(Png(30000, 30000)));
        Assert.False(ContentLimits.PictureAllowed(Png(16384, 16384)));
        Assert.False(ContentLimits.PictureAllowed(Png(0, 10)));
        Assert.False(ContentLimits.PictureAllowed(Jpeg(65000, 65000)));
        Assert.False(ContentLimits.PictureAllowed(Png(uint.MaxValue, 1)));
        Assert.True(ContentLimits.PictureAllowed(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Random_bytes_never_throw_out_of_the_header_reader()
    {
        var r = new Random(5);
        foreach (var seed in new[] { Png(10, 10), Jpeg(10, 10), Webp("VP8X", _ => { }) })
            for (int i = 0; i < 4000; i++)
            {
                var b = (byte[])seed.Clone();
                Array.Resize(ref b, r.Next(0, b.Length + 8));
                for (int k = 0; k < 4 && b.Length > 0; k++) b[r.Next(b.Length)] = (byte)r.Next(256);
                _ = PictureHeader.Read(b, ".tga");
            }
    }

    [Fact]
    public void A_zip_bomb_is_refused_from_its_directory_before_anything_unpacks()
    {
        var dir = Fresh();
        var file = Path.Combine(dir, "bomb.ktpack");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        using (var s = zip.CreateEntry("assets/zero.png", CompressionLevel.Optimal).Open())
            s.Write(new byte[64 << 20]);
        using var read = ZipFile.OpenRead(file);
        var e = Assert.Throws<TooLargeException>(() => ContentLimits.CheckArchive(read.Entries, _ => true));
        Assert.Contains("assets/zero.png", e.Message);
        ContentLimits.CheckArchive(read.Entries, _ => false);
    }

    [Fact]
    public void An_entry_that_unpacks_to_more_than_it_declares_stops_at_its_declared_size()
    {
        var dir = Fresh();
        var file = Path.Combine(dir, "pack.ktpack");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        using (var s = zip.CreateEntry("pack.json").Open())
            s.Write("{\"id\":\"x\"}"u8);
        using var read = ZipFile.OpenRead(file);
        var entry = read.GetEntry("pack.json")!;
        Assert.Equal("{\"id\":\"x\"}", System.Text.Encoding.UTF8.GetString(ContentLimits.Read(entry)));
        Assert.Throws<TooLargeException>(() => ContentLimits.Read(entry, max: 4));
        var to = Path.Combine(dir, "out.json");
        ContentLimits.Extract(entry, to);
        Assert.Equal(entry.Length, new FileInfo(to).Length);
        Assert.False(File.Exists(to + ".part"));
    }

    [Fact]
    public void A_folder_is_counted_and_one_too_large_file_is_named()
    {
        var dir = Fresh();
        File.WriteAllBytes(Path.Combine(dir, "a.png"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(dir, "tiles"));
        File.WriteAllBytes(Path.Combine(dir, "tiles", "b.png"), new byte[50]);
        Assert.Equal(150, ContentLimits.CheckFolder(dir));
        Assert.Throws<IOException>(() => ContentLimits.CheckFreeSpace(dir, long.MaxValue / 2));
        ContentLimits.CheckFreeSpace(dir, 1);
    }

    [Fact]
    public void A_model_file_over_the_limit_is_refused_by_its_size()
    {
        var dir = Fresh();
        var obj = Path.Combine(dir, "m.obj");
        File.WriteAllText(obj, "v 0 0 0\n");
        Assert.True(ContentLimits.ModelAllowed(obj));
        Assert.False(ContentLimits.ModelAllowed(Path.Combine(dir, "missing.obj")));
        Assert.False(ContentLimits.ModelAllowed(obj, most: 4));
        Assert.True(ContentLimits.ModelAllowed(obj, most: 8));
    }
}
