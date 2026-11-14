using System.Globalization;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class PackedModelTests
{
    static readonly string[] Field =
    [
        "o Field",
        "cull back",
        "usemtl tinted",
        "v 0 0.25 0 1 1 1",
        "v 1 4.01418 0 1 1 1",
        "v 1 -1e-05 -1.00098 1 1 1",
        "v 0 0 -1 1 1 1",
        "vn 0 1 0",
        "vn 0.707107 0.707107 0",
        "vn 0 1 0",
        "vn 0 1 0",
        "vt 0.1 0.1",
        "vt 0.963867 0.443848",
        "vt 0.9 0.9",
        "f 3/3/3 2/2/2 1/1/1",
        "f 4/1/4 3/3/3 1/1/1",
        "cull off",
        "usemtl own",
        "v 0 1 0 0.501961 0.25098 0",
        "v 1 1 0 0.501961 0.25098 0",
        "v 1 1 -1 0.501961 0.25098 0",
        "vt 0.6 0.6",
        "vt 0.7 0.6",
        "f 7/5 6/4 5/4",
    ];

    static string Fresh(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-packed-model-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    static string Kept(IEnumerable<string> lines)
    {
        var path = Fresh("model" + PackedModel.Ext);
        File.WriteAllBytes(path, PackedModel.Pack(lines)!);
        return path;
    }

    static List<string> Read(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number.ToString("R", CultureInfo.InvariantCulture) : word).ToList();

    [Fact]
    public void A_packed_model_reads_back_every_statement_with_the_same_numbers_in_the_same_order()
    {
        var back = PackedModel.Lines(Kept(Field)).ToList();

        Assert.Equal(Field.Select(Read), back.Select(Read));
    }

    [Fact]
    public void A_packed_model_reads_back_the_same_whatever_spaces_blank_lines_and_line_ends_the_text_had()
    {
        var loose = Field.SelectMany(line => new[] { "  " + line.Replace(" ", "   ") + "\r", "" });

        Assert.Equal(Field.Select(Read), PackedModel.Lines(Kept(loose)).Select(Read));
    }

    [Fact]
    public void A_model_that_starts_new_points_after_its_faces_keeps_each_run_in_its_place()
    {
        string[] runs = ["v 0 0 0", "v 1 0 0", "v 0 1 0", "f 1 2 3", "v 0 0 1", "vn 0 0 1", "f 1//1 2//1 4//1"];

        Assert.Null(PackedModel.Pack(runs));
        Assert.Equal(runs[..5].Select(Read), PackedModel.Lines(Kept(runs[..5])).Select(Read));
    }

    [Theory]
    [InlineData("mtllib field.mtl")]
    [InlineData("f 1/1/1 2/2/2 3/3/3 4/1/4")]
    [InlineData("f 1//1 2//2 3//3")]
    [InlineData("f -1/1/1 2/2/2 3/3/3")]
    [InlineData("f 1/1/1 2/2 3/3/3")]
    [InlineData("v 0.123456789 0 0 1 1 1")]
    [InlineData("v 0 0 0 1")]
    [InlineData("v 0 NaN 0 1 1 1")]
    [InlineData("vt 0.5 0.5 0")]
    public void A_model_that_would_not_read_back_the_same_is_not_packed(string odd)
    {
        Assert.Null(PackedModel.Pack(Field.Append(odd)));
        Assert.Null(PackedModel.Pack([]));
    }

    [Fact]
    public void A_big_plain_model_packs_to_a_small_part_of_its_text()
    {
        const int Side = 120;
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string> { "o Ground", "usemtl tinted" };
        for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
                lines.Add(string.Create(inv, $"v {x} {Math.Round(Math.Sin(x * 0.1) + Math.Cos(z * 0.1), 5):G6} {-z} 1 1 1"));
        for (int i = 0; i < Side * Side; i++) lines.Add("vn 0 1 0");
        for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
                lines.Add(string.Create(inv, $"vt {x / (double)Side:G6} {z / (double)Side:G6}"));
        for (int z = 0; z + 1 < Side; z++)
            for (int x = 0; x + 1 < Side; x++)
            {
                int a = z * Side + x + 1, b = a + 1, c = a + Side, d = c + 1;
                lines.Add($"f {a}/{a}/{a} {b}/{b}/{b} {d}/{d}/{d}");
                lines.Add($"f {a}/{a}/{a} {d}/{d}/{d} {c}/{c}/{c}");
            }

        var packed = PackedModel.Pack(lines)!;

        long text = lines.Sum(line => line.Length + 1L);
        Assert.True(packed.Length * 8 < text, $"{packed.Length} bytes packed from {text} of text");
        var path = Fresh("ground" + PackedModel.Ext);
        File.WriteAllBytes(path, packed);
        Assert.Equal(lines.Select(Read), PackedModel.Lines(path).Select(Read));
    }

    [Fact]
    public void A_packed_model_that_unpacks_to_more_than_a_model_may_be_is_refused_before_it_is_unpacked()
    {
        var path = Kept(Field);

        Assert.Throws<TooLargeException>(() => PackedModel.Lines(path, most: 256).ToList());
        Assert.NotEmpty(PackedModel.Lines(path, most: 4096));
    }

    [Fact]
    public void A_cut_short_or_scrambled_packed_model_is_refused_as_unreadable_rather_than_read_wrong()
    {
        var whole = PackedModel.Pack(Field)!;
        var cut = Fresh("cut" + PackedModel.Ext);
        File.WriteAllBytes(cut, whole[..(whole.Length / 2)]);
        var other = Fresh("other" + PackedModel.Ext);
        File.WriteAllLines(other, Field);
        var scrambled = Fresh("scrambled" + PackedModel.Ext);
        var bytes = whole.ToArray();
        for (int i = 12; i < bytes.Length; i++) bytes[i] ^= 0x5A;
        File.WriteAllBytes(scrambled, bytes);

        Assert.ThrowsAny<Exception>(() => PackedModel.Lines(cut).ToList());
        Assert.Throws<FormatException>(() => PackedModel.Lines(other).ToList());
        var refused = Assert.ThrowsAny<Exception>(() => PackedModel.Lines(scrambled).ToList());
        Assert.True(refused is FormatException or IOException, refused.GetType().Name);
    }

    [Fact]
    public void A_model_file_that_is_not_packed_is_read_as_its_own_lines()
    {
        var path = Fresh("model.obj");
        File.WriteAllLines(path, Field);

        Assert.Equal(Field, PackedModel.Lines(path));
    }

    [Fact]
    public void A_packed_model_s_middle_is_found_where_its_text_s_is_and_a_damaged_one_has_none()
    {
        string text = Fresh("model.obj"), packed = Kept(Field), cut = Fresh("cut" + PackedModel.Ext);
        File.WriteAllLines(text, Field);
        File.WriteAllBytes(cut, PackedModel.Pack(Field)![..20]);
        var store = new PackageStore(Path.GetDirectoryName(text)!);

        Assert.NotEqual((0d, 0d), store.MeshMiddle(text));
        Assert.Equal(store.MeshMiddle(text), store.MeshMiddle(packed));
        Assert.Equal((0d, 0d), store.MeshMiddle(cut));
    }
}
