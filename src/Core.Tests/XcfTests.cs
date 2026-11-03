using System.Buffers.Binary;
using System.IO.Compression;
using KTable.Core;

namespace KTable.Core.Tests;

public class XcfTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    static (byte r, byte g, byte b, byte a) Px(XcfImage img, int x, int y)
    {
        int i = (y * img.Width + x) * 4;
        return (img.Rgba[i], img.Rgba[i + 1], img.Rgba[i + 2], img.Rgba[i + 3]);
    }

    static void AssertPx(XcfImage img, int x, int y, int r, int g, int b, int a, int tol = 0)
    {
        var p = Px(img, x, y);
        Assert.True(Math.Abs(p.r - r) <= tol && Math.Abs(p.g - g) <= tol && Math.Abs(p.b - b) <= tol && Math.Abs(p.a - a) <= tol,
            $"pixel ({x},{y}) is {p.r},{p.g},{p.b},{p.a}; expected {r},{g},{b},{a} (±{tol})");
    }

    static byte[] Solid(int w, int h, int channels, params byte[] color)
    {
        var px = new byte[w * h * channels];
        for (int i = 0; i < w * h; i++) for (int c = 0; c < channels; c++) px[i * channels + c] = color[c];
        return px;
    }

    static XcfBuilder Standard(int version, int compression)
    {
        var b = new XcfBuilder { Version = version, Compression = compression, Width = 70, Height = 70 };
        b.Layers.Add(new XcfLayer("corner", 10, 10, -5, -5, 1, Solid(10, 10, 4, 0, 0, 0, 255)));
        b.Layers.Add(new XcfLayer("legacy", 20, 20, 60, 60, 1, Solid(20, 20, 4, 0, 255, 0, 255)) { Opacity = 0.6f, Mode = 0, CompositeSpace = -2 });
        b.Layers.Add(new XcfLayer("hidden", 70, 70, 0, 0, 1, Solid(70, 70, 4, 0, 0, 255, 255)) { Visible = false });
        b.Layers.Add(new XcfLayer("red50", 70, 70, 0, 0, 1, Solid(70, 70, 4, 255, 0, 0, 255)) { Opacity = 0.5f, CompositeSpace = -1 });
        b.Layers.Add(new XcfLayer("white", 70, 70, 0, 0, 0, Solid(70, 70, 3, 255, 255, 255)));
        return b;
    }

    static void CheckStandard(XcfImage img)
    {
        Assert.Equal(70, img.Width); Assert.Equal(70, img.Height);
        Assert.Empty(img.Warnings);
        Assert.Equal(4, img.LayersUsed);
        AssertPx(img, 30, 30, 255, 188, 188, 255);
        AssertPx(img, 66, 30, 255, 188, 188, 255);
        AssertPx(img, 30, 66, 255, 188, 188, 255);
        AssertPx(img, 65, 65, 102, 228, 75, 255);
        AssertPx(img, 69, 69, 102, 228, 75, 255);
        AssertPx(img, 59, 65, 255, 188, 188, 255);
        AssertPx(img, 0, 0, 0, 0, 0, 255);
        AssertPx(img, 4, 4, 0, 0, 0, 255);
        AssertPx(img, 5, 5, 255, 188, 188, 255);
    }

    [Fact]
    public void A_damaged_xcf_is_read_or_refused_never_thrown_out_of_or_hung_on()
    {
        var rng = new Random(2026);
        var seeds = new[] { Standard(3, 0).Build(), Standard(11, 1).Build(), Standard(11, 2).Build() };
        for (int round = 0; round < 1500; round++)
        {
            var bytes = (byte[])seeds[round % seeds.Length].Clone();
            if (round % 5 == 0) Array.Resize(ref bytes, rng.Next(1, bytes.Length));
            for (int k = rng.Next(1, 5); k > 0; k--) bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);
            try { _ = Xcf.Load(new MemoryStream(bytes)); }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException) { }
        }
    }

    [Fact]
    public void Version_3_file_with_32_bit_offsets_and_raw_tiles()
    {
        var img = Xcf.Load(new MemoryStream(Standard(3, 0).Build()));
        Assert.Equal(3, img.Version);
        CheckStandard(img);
    }

    [Fact]
    public void Version_11_file_with_64_bit_offsets_and_rle_tiles()
    {
        var img = Xcf.Load(new MemoryStream(Standard(11, 1).Build()));
        Assert.Equal(11, img.Version);
        CheckStandard(img);
    }

    [Fact]
    public void Version_11_file_with_zlib_tiles()
    {
        CheckStandard(Xcf.Load(new MemoryStream(Standard(11, 2).Build())));
    }

    [Fact]
    public void Raw_rle_and_zlib_tiles_decode_to_the_same_picture_including_noise()
    {
        var rnd = new Random(7);
        var noise = new byte[130 * 70 * 4];
        rnd.NextBytes(noise);
        for (int i = 0; i < noise.Length; i += 4) noise[i + 3] = 255;
        for (int i = 2000; i < 2600; i += 4) { noise[i] = 9; noise[i + 1] = 9; noise[i + 2] = 9; }
        XcfImage? first = null;
        for (int compression = 0; compression <= 2; compression++)
        {
            var b = Standard(11, compression);
            b.Width = 130;
            b.Layers.Insert(0, new XcfLayer("noise", 130, 70, 0, 0, 1, noise));
            var img = Xcf.Load(new MemoryStream(b.Build()));
            Assert.Equal(130, img.Width);
            for (int y = 0; y < 70; y += 7) for (int x = 0; x < 130; x += 11)
                AssertPx(img, x, y, noise[(y * 130 + x) * 4], noise[(y * 130 + x) * 4 + 1], noise[(y * 130 + x) * 4 + 2], 255);
            if (first == null) first = img; else Assert.Equal(first.Rgba, img.Rgba);
        }
    }

    [Fact]
    public void Opacity_from_the_integer_property_when_no_float_one()
    {
        var b = new XcfBuilder { Version = 3, Compression = 1, Width = 8, Height = 8 };
        b.Layers.Add(new XcfLayer("red", 8, 8, 0, 0, 1, Solid(8, 8, 4, 255, 0, 0, 255)) { Opacity = 0.5f, IntegerOpacityOnly = true, CompositeSpace = -2 });
        b.Layers.Add(new XcfLayer("white", 8, 8, 0, 0, 0, Solid(8, 8, 3, 255, 255, 255)));
        var img = Xcf.Load(new MemoryStream(b.Build()));
        AssertPx(img, 3, 3, 255, 128, 128, 255, 1);
    }

    [Fact]
    public void Layer_mask_scales_the_layer_alpha()
    {
        var b = new XcfBuilder { Version = 11, Compression = 1, Width = 70, Height = 20 };
        var mask = new byte[70 * 20];
        for (int y = 0; y < 20; y++) for (int x = 0; x < 70; x++) mask[y * 70 + x] = (byte)(x < 30 ? 0 : x < 60 ? 255 : 100);
        b.Layers.Add(new XcfLayer("blue", 70, 20, 0, 0, 1, Solid(70, 20, 4, 0, 0, 255, 255)) { Mask = mask, ApplyMask = true, CompositeSpace = -1 });
        b.Layers.Add(new XcfLayer("white", 70, 20, 0, 0, 0, Solid(70, 20, 3, 255, 255, 255)));
        var img = Xcf.Load(new MemoryStream(b.Build()));
        AssertPx(img, 10, 10, 255, 255, 255, 255);
        AssertPx(img, 40, 10, 0, 0, 255, 255);
        AssertPx(img, 65, 10, 205, 205, 255, 255, 1);
        b.Layers[0] = b.Layers[0] with { ApplyMask = false };
        img = Xcf.Load(new MemoryStream(b.Build()));
        AssertPx(img, 10, 10, 0, 0, 255, 255);
    }

    [Fact]
    public void Group_is_flattened_first_then_composited_with_its_opacity()
    {
        var b = new XcfBuilder { Version = 11, Compression = 2, Width = 40, Height = 40 };
        b.Layers.Add(new XcfLayer("grp", 40, 40, 0, 0, 1, Solid(40, 40, 4, 0, 0, 0, 0)) { IsGroup = true, Opacity = 0.5f, CompositeSpace = -1 });
        b.Layers.Add(new XcfLayer("gb", 20, 20, 0, 0, 1, Solid(20, 20, 4, 0, 255, 0, 255)) { Path = new[] { 0, 0 }, CompositeSpace = -1 });
        b.Layers.Add(new XcfLayer("ga", 40, 40, 0, 0, 1, Solid(40, 40, 4, 255, 0, 0, 255)) { Path = new[] { 0, 1 }, CompositeSpace = -1 });
        b.Layers.Add(new XcfLayer("white", 40, 40, 0, 0, 0, Solid(40, 40, 3, 255, 255, 255)));
        var img = Xcf.Load(new MemoryStream(b.Build()));
        Assert.Equal(4, img.LayersUsed);
        AssertPx(img, 10, 10, 188, 255, 188, 255);
        AssertPx(img, 30, 30, 255, 188, 188, 255);
        b.Layers[0] = b.Layers[0] with { Visible = false };
        img = Xcf.Load(new MemoryStream(b.Build()));
        AssertPx(img, 10, 10, 255, 255, 255, 255);
        Assert.Equal(1, img.LayersUsed);
    }

    [Fact]
    public void Transparent_canvas_keeps_the_alpha()
    {
        var b = new XcfBuilder { Version = 11, Compression = 1, Width = 20, Height = 20 };
        b.Layers.Add(new XcfLayer("red", 10, 10, 5, 5, 1, Solid(10, 10, 4, 200, 10, 10, 255)) { Opacity = 0.5f });
        var img = Xcf.Load(new MemoryStream(b.Build()));
        AssertPx(img, 0, 0, 0, 0, 0, 0);
        AssertPx(img, 7, 7, 200, 10, 10, 128, 1);
    }

    [Fact]
    public void Sixteen_bit_and_float_precisions_read_like_eight_bit()
    {
        foreach (var (precision, bpc) in new[] { (250, 2), (600, 4), (550, 2) })
        {
            var b = new XcfBuilder { Version = 11, Compression = 1, Width = 70, Height = 10, Precision = precision };
            b.Layers.Add(new XcfLayer("red50", 70, 10, 0, 0, 1, Solid(70, 10, 4, 255, 0, 0, 255)) { Opacity = 0.5f, CompositeSpace = -1 });
            b.Layers.Add(new XcfLayer("white", 70, 10, 0, 0, 0, Solid(70, 10, 3, 255, 255, 255)));
            var img = Xcf.Load(new MemoryStream(b.Build()));
            Assert.Equal(bpc, b.Bpc);
            AssertPx(img, 66, 5, 255, 188, 188, 255, 1);
        }
    }

    [Fact]
    public void Grayscale_and_indexed_pictures()
    {
        var g = new XcfBuilder { Version = 11, Compression = 1, Width = 10, Height = 10, BaseType = 1 };
        var ga = Solid(10, 10, 2, 128, 255);
        for (int i = 0; i < 5 * 10; i++) ga[i * 2 + 1] = 0;
        g.Layers.Add(new XcfLayer("gray", 10, 10, 0, 0, 3, ga));
        g.Layers.Add(new XcfLayer("white", 10, 10, 0, 0, 2, Solid(10, 10, 1, 255)));
        var img = Xcf.Load(new MemoryStream(g.Build()));
        AssertPx(img, 5, 2, 255, 255, 255, 255);
        AssertPx(img, 5, 7, 128, 128, 128, 255);

        var ix = new XcfBuilder { Version = 11, Compression = 1, Width = 10, Height = 10, BaseType = 2, Colormap = new byte[] { 255, 255, 255, 200, 0, 0, 0, 0, 200 } };
        var ia = Solid(10, 10, 2, 1, 255);
        for (int i = 0; i < 5 * 10; i++) ia[i * 2] = 2;
        ix.Layers.Add(new XcfLayer("idx", 10, 10, 0, 0, 5, ia));
        ix.Layers.Add(new XcfLayer("back", 10, 10, 0, 0, 4, Solid(10, 10, 1, 0)));
        img = Xcf.Load(new MemoryStream(ix.Build()));
        AssertPx(img, 5, 2, 0, 0, 200, 255);
        AssertPx(img, 5, 7, 200, 0, 0, 255);
    }

    [Fact]
    public void Unsupported_blend_mode_warns_once_and_shows_as_normal()
    {
        var b = new XcfBuilder { Version = 11, Compression = 1, Width = 10, Height = 10 };
        b.Layers.Add(new XcfLayer("hue", 10, 10, 0, 0, 1, Solid(10, 10, 4, 0, 0, 255, 255)) { Mode = 37 });
        b.Layers.Add(new XcfLayer("hue2", 10, 10, 0, 0, 1, Solid(10, 10, 4, 0, 0, 255, 255)) { Mode = 37 });
        b.Layers.Add(new XcfLayer("white", 10, 10, 0, 0, 0, Solid(10, 10, 3, 255, 255, 255)));
        var img = Xcf.Load(new MemoryStream(b.Build()));
        Assert.Single(img.Warnings);
        Assert.Contains("HSV Hue", img.Warnings[0]);
        AssertPx(img, 5, 5, 0, 0, 255, 255);
    }

    [Fact]
    public void Multiply_blends_in_the_blend_space()
    {
        var b = new XcfBuilder { Version = 11, Compression = 1, Width = 10, Height = 10 };
        b.Layers.Add(new XcfLayer("mul", 10, 10, 0, 0, 1, Solid(10, 10, 4, 128, 128, 128, 255)) { Mode = 3, CompositeSpace = -2, BlendSpace = -2 });
        b.Layers.Add(new XcfLayer("gray", 10, 10, 0, 0, 0, Solid(10, 10, 3, 200, 100, 50)));
        var img = Xcf.Load(new MemoryStream(b.Build()));
        AssertPx(img, 5, 5, 100, 50, 25, 255, 1);
    }

    [Fact]
    public void Blend_modes_other_than_normal_show_only_where_there_is_a_backdrop()
    {
        var b = new XcfBuilder { Version = 11, Compression = 1, Width = 20, Height = 10 };
        b.Layers.Add(new XcfLayer("mul", 20, 10, 0, 0, 1, Solid(20, 10, 4, 128, 128, 128, 255)) { Mode = 30 });
        b.Layers.Add(new XcfLayer("back", 10, 10, 0, 0, 1, Solid(10, 10, 4, 200, 200, 200, 255)));
        var img = Xcf.Load(new MemoryStream(b.Build()));
        AssertPx(img, 15, 5, 0, 0, 0, 0);
        AssertPx(img, 5, 5, 100, 100, 100, 255, 1);
        b.Layers[0] = b.Layers[0] with { Mode = 28 };
        img = Xcf.Load(new MemoryStream(b.Build()));
        AssertPx(img, 15, 5, 128, 128, 128, 255);
    }

    [Fact]
    public void Rendering_in_bands_equals_the_whole_picture()
    {
        foreach (var name in new[] { "layers8", "modes8" })
        {
            var whole = Xcf.Load(Fixture(name + ".xcf"));
            using var f = XcfFile.Open(Fixture(name + ".xcf"));
            foreach (int band in new[] { 1, 37, 100 })
            {
                var got = new byte[whole.Rgba.Length];
                var buf = new byte[f.Width * band * 4];
                for (int y = 0; y < f.Height; y += band)
                {
                    int rows = Math.Min(band, f.Height - y);
                    f.Render(y, rows, buf);
                    Array.Copy(buf, 0, got, y * f.Width * 4, rows * f.Width * 4);
                }
                Assert.True(whole.Rgba.AsSpan().SequenceEqual(got), $"{name} in bands of {band}");
            }
            Assert.Equal(whole.LayersUsed, f.LayersUsed);
        }
        var b = new XcfBuilder { Version = 11, Compression = 1, Width = 90, Height = 130 };
        var mask = new byte[90 * 130];
        for (int i = 0; i < mask.Length; i++) mask[i] = (byte)(i % 90 < 45 ? 40 : 255);
        b.Layers.Add(new XcfLayer("grp", 90, 130, 0, 0, 1, Solid(90, 130, 4, 0, 0, 0, 0)) { IsGroup = true, Opacity = 0.7f, CompositeSpace = -1 });
        b.Layers.Add(new XcfLayer("gb", 50, 50, -10, 70, 1, Solid(50, 50, 4, 0, 255, 0, 255)) { Path = new[] { 0, 0 }, CompositeSpace = -1, Mask = null });
        b.Layers.Add(new XcfLayer("ga", 90, 130, 0, 0, 1, Solid(90, 130, 4, 255, 0, 0, 255)) { Path = new[] { 0, 1 }, CompositeSpace = -1, Mask = mask, ApplyMask = true });
        b.Layers.Add(new XcfLayer("mul", 60, 60, 20, -20, 1, Solid(60, 60, 4, 128, 128, 128, 255)) { Mode = 30, CompositeSpace = -1 });
        b.Layers.Add(new XcfLayer("white", 90, 130, 0, 0, 0, Solid(90, 130, 3, 255, 255, 255)));
        var bytes = b.Build();
        var all = Xcf.Load(new MemoryStream(bytes));
        using var g = XcfFile.Open(new MemoryStream(bytes));
        var got2 = new byte[all.Rgba.Length];
        var buf2 = new byte[g.Width * 29 * 4];
        for (int y = 0; y < g.Height; y += 29)
        {
            int rows = Math.Min(29, g.Height - y);
            g.Render(y, rows, buf2);
            Array.Copy(buf2, 0, got2, y * g.Width * 4, rows * g.Width * 4);
        }
        Assert.True(all.Rgba.AsSpan().SequenceEqual(got2), "hand-built file in bands of 29");
    }

    [Fact]
    public void A_gigapixel_canvas_renders_in_bands_but_not_whole()
    {
        var b = new XcfBuilder { Version = 11, Compression = 1, Width = 60000, Height = 40000 };
        b.Layers.Add(new XcfLayer("dot", 10, 10, 59995, 20000, 1, Solid(10, 10, 4, 10, 200, 30, 255)));
        var bytes = b.Build();
        using var f = XcfFile.Open(new MemoryStream(bytes));
        Assert.Equal(60000, f.Width);
        Assert.Throws<InvalidDataException>(() => f.Flatten());
        var buf = new byte[60000 * 3 * 4];
        f.Render(20004, 3, buf);
        Assert.Equal(new byte[] { 10, 200, 30, 255 }, buf.AsSpan(59996 * 4, 4).ToArray());
        Assert.Equal(0, buf[59994 * 4 + 3]);
        f.Render(0, 3, buf);
        Assert.Equal(0, buf[59996 * 4 + 3]);
    }

    [Fact]
    public void Not_an_xcf_is_refused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Xcf.Load(new MemoryStream(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })));
        Assert.Contains("not a GIMP", ex.Message);
        Assert.Throws<InvalidDataException>(() => Xcf.Load(new MemoryStream(Standard(11, 1).Build().AsSpan(0, 600).ToArray())));
    }

    [Theory]
    [InlineData("layers8", 12)]
    [InlineData("layers16", 12)]
    [InlineData("layersf", 12)]
    [InlineData("gray8", 12)]
    public void Gimp_files_flatten_like_gimp(string name, int layersUsed)
    {
        var img = Xcf.Load(Fixture(name + ".xcf"));
        var (w, h, ppm) = ReadPpm(Fixture(name + ".ppm"));
        Assert.Equal(w, img.Width); Assert.Equal(h, img.Height);
        Assert.Equal(layersUsed, img.LayersUsed);
        Assert.Empty(img.Warnings);
        int worst = 0, worstAt = -1, over1 = 0;
        for (int i = 0; i < w * h; i++)
        {
            Assert.Equal(255, img.Rgba[i * 4 + 3]);
            for (int c = 0; c < 3; c++)
            {
                int d = Math.Abs(img.Rgba[i * 4 + c] - ppm[i * 3 + c]);
                if (d > worst) { worst = d; worstAt = i; }
                if (d > 1) over1++;
            }
        }
        Assert.True(worst <= 2, $"{name}: pixel {worstAt % w},{worstAt / w} differs by {worst}: ours {img.Rgba[worstAt * 4]},{img.Rgba[worstAt * 4 + 1]},{img.Rgba[worstAt * 4 + 2]} vs GIMP {ppm[worstAt * 3]},{ppm[worstAt * 3 + 1]},{ppm[worstAt * 3 + 2]}");
        Assert.True(over1 <= w * h * 3 / 100, $"{name}: {over1} channel values differ from GIMP by 2");
        if (name == "gray8") { var g = Px(img, 10, 40); Assert.True(g.r == g.g && g.g == g.b, "gray"); return; }
        AssertPx(img, 10, 40, 229, 219, 182, 255);
        var pGreen = Px(img, 125, 40); Assert.True(pGreen.g > 150 && pGreen.r < 60, "masked layer shows on its right half");
        var pMasked = Px(img, 60, 40); Assert.True(pMasked.r > 150, "masked layer hidden on its left half");
    }

    [Fact]
    public void Gimp_blend_modes_match_gimp()
    {
        string[] modes = { "screen", "overlay", "hardlight", "softlight", "darken", "lighten", "addition", "subtract", "difference", "grainextract", "grainmerge", "dodge", "burn", "divide", "multiply",
            "screen_legacy", "overlay_legacy", "softlight_legacy", "dodge_legacy", "burn_legacy", "addition_legacy", "hardlight_legacy", "difference_legacy", "divide_legacy" };
        var img = Xcf.Load(Fixture("modes8.xcf"));
        var (w, h, ppm) = ReadPpm(Fixture("modes8.ppm"));
        Assert.Equal(w, img.Width); Assert.Equal(h, img.Height);
        Assert.Empty(img.Warnings);
        var report = new List<string>();
        for (int m = 0; m < modes.Length; m++)
        {
            int worst = 0; string where = "";
            int rows = modes[m].StartsWith("grain") ? 20 : h;
            for (int y = 0; y < rows; y++)
                for (int x = m * 8; x < m * 8 + 8; x++)
                    for (int c = 0; c < 3; c++)
                    {
                        int i = y * w + x;
                        int d = Math.Abs(img.Rgba[i * 4 + c] - ppm[i * 3 + c]);
                        if (d > worst) { worst = d; where = $"({x},{y}) ours {img.Rgba[i * 4]},{img.Rgba[i * 4 + 1]},{img.Rgba[i * 4 + 2]} gimp {ppm[i * 3]},{ppm[i * 3 + 1]},{ppm[i * 3 + 2]}"; }
                    }
            if (worst > 2) report.Add($"{modes[m]}: off by {worst} at {where}");
        }
        Assert.True(report.Count == 0, string.Join("; ", report));
    }

    static (int w, int h, byte[] rgb) ReadPpm(string path)
    {
        var data = File.ReadAllBytes(path);
        int p = 0;
        string Token()
        {
            while (true)
            {
                while (p < data.Length && char.IsWhiteSpace((char)data[p])) p++;
                if (p < data.Length && data[p] == '#') { while (p < data.Length && data[p] != '\n') p++; continue; }
                break;
            }
            int s = p;
            while (p < data.Length && !char.IsWhiteSpace((char)data[p])) p++;
            return System.Text.Encoding.ASCII.GetString(data, s, p - s);
        }
        Assert.Equal("P6", Token());
        int w = int.Parse(Token()), h = int.Parse(Token()), max = int.Parse(Token());
        p++;
        var rgb = new byte[w * h * 3];
        if (max == 255) Array.Copy(data, p, rgb, 0, rgb.Length);
        else for (int i = 0; i < rgb.Length; i++) rgb[i] = (byte)Math.Round(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p + i * 2)) / 257.0);
        return (w, h, rgb);
    }

    record XcfLayer(string Name, int W, int H, int OffX, int OffY, int Type, byte[] Pixels)
    {
        public float Opacity { get; init; } = 1;
        public bool IntegerOpacityOnly { get; init; }
        public bool Visible { get; init; } = true;
        public int Mode { get; init; } = 28;
        public int CompositeSpace { get; init; }
        public int BlendSpace { get; init; }
        public bool IsGroup { get; init; }
        public int[]? Path { get; init; }
        public byte[]? Mask { get; init; }
        public bool ApplyMask { get; init; }
        public int Channels => Type switch { 0 => 3, 1 => 4, 2 => 1, 3 => 2, 4 => 1, _ => 2 };
    }

    sealed class XcfBuilder
    {
        public int Version = 11, Compression = 1, Width, Height, BaseType, Precision = 150;
        public byte[]? Colormap;
        public List<XcfLayer> Layers = new();
        public int Bpc => (Precision / 100) switch { 1 => 1, 2 => 2, 5 => 2, 6 => 4, _ => throw new NotSupportedException() };
        int PtrSize => Version >= 11 ? 8 : 4;

        public byte[] Build()
        {
            var ms = new MemoryStream();
            void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); ms.Write(b); }
            void Ptr(long v) { Span<byte> b = stackalloc byte[8]; if (PtrSize == 8) { BinaryPrimitives.WriteInt64BigEndian(b, v); ms.Write(b); } else { BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v); ms.Write(b[..4]); } }
            ms.Write(System.Text.Encoding.ASCII.GetBytes(Version == 0 ? "gimp xcf file\0" : $"gimp xcf v{Version:000}\0"));
            U32((uint)Width); U32((uint)Height); U32((uint)BaseType);
            if (Version >= 4) U32((uint)Precision);
            U32(17); U32(1); ms.WriteByte((byte)Compression);
            if (Colormap != null) { U32(1); U32((uint)(4 + Colormap.Length)); U32((uint)(Colormap.Length / 3)); ms.Write(Colormap); }
            U32(0); U32(0);
            long tablePos = ms.Position;
            for (int i = 0; i <= Layers.Count; i++) Ptr(0);
            Ptr(0);
            var offsets = new long[Layers.Count];
            for (int i = 0; i < Layers.Count; i++) { offsets[i] = ms.Position; ms.Write(LayerBlob(Layers[i], ms.Position)); }
            ms.Position = tablePos;
            foreach (var o in offsets) Ptr(o);
            return ms.ToArray();
        }

        byte[] LayerBlob(XcfLayer l, long basePos)
        {
            var ms = new MemoryStream();
            void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); ms.Write(b); }
            void Ptr(long v) { Span<byte> b = stackalloc byte[8]; if (PtrSize == 8) { BinaryPrimitives.WriteInt64BigEndian(b, v); ms.Write(b); } else { BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v); ms.Write(b[..4]); } }
            void Prop(uint type, params uint[] words) { U32(type); U32((uint)(4 * words.Length)); foreach (var w in words) U32(w); }
            U32((uint)l.W); U32((uint)l.H); U32((uint)l.Type);
            var name = System.Text.Encoding.UTF8.GetBytes(l.Name);
            U32((uint)(name.Length + 1)); ms.Write(name); ms.WriteByte(0);
            Prop(6, (uint)Math.Round(l.Opacity * 255));
            if (!l.IntegerOpacityOnly) Prop(33, BitConverter.SingleToUInt32Bits(l.Opacity));
            Prop(8, l.Visible ? 1u : 0u);
            Prop(15, (uint)l.OffX, (uint)l.OffY);
            Prop(7, (uint)l.Mode);
            Prop(11, l.ApplyMask ? 1u : 0u);
            Prop(36, (uint)l.CompositeSpace);
            Prop(37, (uint)l.BlendSpace);
            if (l.IsGroup) Prop(29);
            if (l.Path != null) Prop(30, l.Path.Select(x => (uint)x).ToArray());
            U32(0); U32(0);
            int bpc = l.Type >= 4 ? 1 : Bpc;
            long hierPos = basePos + ms.Position + 2 * PtrSize;
            var hier = Hierarchy(hierPos, l.W, l.H, l.Channels, bpc, l.Pixels, l.Channels);
            long maskPos = 0;
            byte[]? mask = null;
            if (l.Mask != null)
            {
                maskPos = hierPos + hier.Length;
                var cm = new MemoryStream();
                void MU32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); cm.Write(b); }
                MU32((uint)l.W); MU32((uint)l.H);
                var mn = "mask"u8.ToArray(); MU32((uint)(mn.Length + 1)); cm.Write(mn); cm.WriteByte(0);
                MU32(0); MU32(0);
                long mh = maskPos + cm.Position + PtrSize;
                Span<byte> pb = stackalloc byte[8];
                if (PtrSize == 8) { BinaryPrimitives.WriteInt64BigEndian(pb, mh); cm.Write(pb); } else { BinaryPrimitives.WriteUInt32BigEndian(pb, (uint)mh); cm.Write(pb[..4]); }
                cm.Write(Hierarchy(mh, l.W, l.H, 1, Bpc, l.Mask, 1));
                mask = cm.ToArray();
            }
            Ptr(hierPos); Ptr(maskPos);
            ms.Write(hier);
            if (mask != null) ms.Write(mask);
            return ms.ToArray();
        }

        byte[] Hierarchy(long basePos, int w, int h, int channels, int bpc, byte[] pixels8, int srcChannels)
        {
            var ms = new MemoryStream();
            void U32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); ms.Write(b); }
            void Ptr(long v) { Span<byte> b = stackalloc byte[8]; if (PtrSize == 8) { BinaryPrimitives.WriteInt64BigEndian(b, v); ms.Write(b); } else { BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v); ms.Write(b[..4]); } }
            int bpp = channels * bpc;
            int cols = (w + 63) / 64, rows = (h + 63) / 64;
            var tiles = new List<byte[]>();
            for (int ty = 0; ty < rows; ty++)
                for (int tx = 0; tx < cols; tx++)
                {
                    int tw = Math.Min(64, w - tx * 64), th = Math.Min(64, h - ty * 64);
                    var raw = new byte[tw * th * bpp];
                    for (int j = 0; j < th; j++)
                        for (int i = 0; i < tw; i++)
                            for (int c = 0; c < channels; c++)
                            {
                                byte v = pixels8[((ty * 64 + j) * w + tx * 64 + i) * srcChannels + c];
                                int at = ((j * tw + i) * channels + c) * bpc;
                                Widen(raw, at, v, bpc);
                            }
                    tiles.Add(Compression switch { 0 => raw, 1 => Rle(raw, tw * th, bpp), _ => Zlib(raw) });
                }
            U32((uint)w); U32((uint)h); U32((uint)bpp);
            long levelPos = basePos + 12 + 2 * PtrSize;
            Ptr(levelPos); Ptr(0);
            U32((uint)w); U32((uint)h);
            long tilePos = levelPos + 8 + (tiles.Count + 1) * PtrSize;
            foreach (var t in tiles) { Ptr(tilePos); tilePos += t.Length; }
            Ptr(0);
            foreach (var t in tiles) ms.Write(t);
            return ms.ToArray();
        }

        void Widen(byte[] raw, int at, byte v, int bpc)
        {
            switch (Precision / 100)
            {
                case 1: raw[at] = v; break;
                case 2: BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(at), (ushort)(v * 257)); break;
                case 5: BinaryPrimitives.WriteInt16BigEndian(raw.AsSpan(at), BitConverter.HalfToInt16Bits((Half)(v / 255f))); break;
                default: BinaryPrimitives.WriteSingleBigEndian(raw.AsSpan(at), Precision % 100 == 0 ? SrgbToLinear(v / 255f) : v / 255f); break;
            }
        }

        static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

        static byte[] Rle(byte[] raw, int pixels, int bpp)
        {
            var o = new MemoryStream();
            var plane = new byte[pixels];
            for (int p = 0; p < bpp; p++)
            {
                for (int i = 0; i < pixels; i++) plane[i] = raw[i * bpp + p];
                int n = 0;
                while (n < pixels)
                {
                    int run = 1;
                    while (n + run < pixels && plane[n + run] == plane[n] && run < 65535) run++;
                    if (run >= 3)
                    {
                        if (run <= 127) o.WriteByte((byte)(run - 1));
                        else { o.WriteByte(127); o.WriteByte((byte)(run >> 8)); o.WriteByte((byte)run); }
                        o.WriteByte(plane[n]);
                        n += run;
                        continue;
                    }
                    int lit = 0;
                    while (n + lit < pixels && lit < 200)
                    {
                        int r = 1;
                        while (n + lit + r < pixels && plane[n + lit + r] == plane[n + lit] && r < 3) r++;
                        if (r >= 3) break;
                        lit++;
                    }
                    if (lit == 0) lit = 1;
                    if (lit <= 127) o.WriteByte((byte)(256 - lit));
                    else { o.WriteByte(128); o.WriteByte((byte)(lit >> 8)); o.WriteByte((byte)lit); }
                    o.Write(plane, n, lit);
                    n += lit;
                }
            }
            return o.ToArray();
        }

        static byte[] Zlib(byte[] raw)
        {
            var o = new MemoryStream();
            using (var z = new ZLibStream(o, CompressionLevel.Optimal, true)) z.Write(raw);
            return o.ToArray();
        }
    }
}
