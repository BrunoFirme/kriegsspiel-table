using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    const double Edge = 18, TopY = 10.48;

    static string TableObj((double u, double v) nearLeft, (double u, double v) nearRight, (double u, double v) farRight, (double u, double v) farLeft) =>
        string.Join('\n',
            Inv($"v {Edge} 7.5 {Edge}"), Inv($"v {-Edge} 7.5 {Edge}"), Inv($"v {-Edge} 7.5 {-Edge}"), Inv($"v {Edge} 7.5 {-Edge}"),
            Inv($"v {Edge} {TopY} {-Edge}"), Inv($"v {-Edge} {TopY} {-Edge}"), Inv($"v {-Edge} {TopY} {Edge}"), Inv($"v {Edge} {TopY} {Edge}"),
            Inv($"vt {nearLeft.u} {nearLeft.v}"), Inv($"vt {nearRight.u} {nearRight.v}"), Inv($"vt {farRight.u} {farRight.v}"), Inv($"vt {farLeft.u} {farLeft.v}"),
            "f 5/1 6/2 7/3", "f 5/1 7/3 8/4", "f 1 2 3", "f 1 3 4") + "\n";

    static string Inv(FormattableString s) => FormattableString.Invariant(s);

    static readonly string StockTable = TableObj((0, 0), (1, 0), (1, 1), (0, 1));

    string WriteObj(string text)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"top-{Guid.NewGuid():N}.obj");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_table_tops_flat_face_and_which_way_its_picture_faces_are_read_from_its_model()
    {
        var stock = TableTop.Read(WriteObj(StockTable));
        Assert.Equal(new TableTop(-Edge, Edge, -Edge, Edge, TopY, 180), stock);
        Assert.Equal(90, TableTop.Read(WriteObj(TableObj((0, 1), (0, 0), (1, 0), (1, 1))))!.Turn);
        Assert.Equal(270, TableTop.Read(WriteObj(TableObj((1, 0), (1, 1), (0, 1), (0, 0))))!.Turn);
        Assert.Equal(0, TableTop.Read(WriteObj(TableObj((1, 1), (0, 1), (0, 0), (1, 0))))!.Turn);
        Assert.Null(TableTop.Read(WriteObj(TableObj((1, 0), (0, 0), (0, 1), (1, 1)))));
        Assert.Null(TableTop.Read(WriteObj(TableObj((0, 0), (0.5, 0), (0.5, 1), (0, 1)))));
    }

    string WritePicture(int w, int h, Func<int, int, SKColor> colour)
    {
        Directory.CreateDirectory(_root);
        using var bmp = new SKBitmap(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) bmp.SetPixel(x, y, colour(x, y));
        var path = Path.Combine(_root, $"cloth-{Guid.NewGuid():N}.png");
        using var png = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
        return path;
    }

    [Fact]
    public void A_felt_with_a_weave_is_a_plain_cloth_and_a_map_is_not()
    {
        var felt = WritePicture(400, 260, (x, y) => (x + y) % 2 == 0 ? new SKColor(38, 88, 57) : new SKColor(40, 91, 59));
        var map = WritePicture(400, 260, (x, y) => x < 200 ? new SKColor(171, 185, 123) : y < 40 ? new SKColor(120, 220, 230) : new SKColor(150, 170, 110));

        Assert.Equal((true, false), (TableTop.IsCloth(felt), TableTop.IsCloth(map)));
    }

    [Fact]
    public void A_flex_tables_top_comes_over_as_the_map_sheet_with_its_picture_and_no_piece_of_it()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/battlefield.png", 12, 10, (x, _) => x < 6 ? SKColors.Green : SKColors.Tan);
        var models = Path.Combine(cfg.ModsPath, "Models");
        Directory.CreateDirectory(models);
        File.WriteAllText(Path.Combine(models, AssetStore.Key("http://a/flex-top.obj") + ".obj"), StockTable);
        var top = Obj("Custom_Model", "", 3.6, new JsonObject
        {
            ["Locked"] = true,
            ["Transform"] = new JsonObject { ["posX"] = 0, ["posY"] = -9, ["posZ"] = 0, ["rotX"] = 0, ["rotY"] = 0, ["rotZ"] = 0, ["scaleX"] = 3.6, ["scaleY"] = 1, ["scaleZ"] = 3.6 },
            ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/flex-top.obj", ["DiffuseURL"] = "http://a/battlefield.png", ["TypeIndex"] = 4 },
        });
        var legs = Obj("Custom_Model", "", 3.6, new JsonObject
        {
            ["Locked"] = true,
            ["Transform"] = new JsonObject { ["posX"] = 46.8, ["posY"] = -9, ["posZ"] = 0, ["rotX"] = 0, ["rotY"] = 0, ["rotZ"] = 0, ["scaleX"] = 3.6, ["scaleY"] = 1, ["scaleZ"] = 1 },
            ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/gone-leg.obj" },
        });
        var module = Module("flex", top, legs);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "flex", "scenario.json"));
        var sheet = Assert.Single(table.Sheets);
        Assert.Equal((new Vec2(0, 0), 180.0, new Size2(129.6, 129.6)), (sheet.Pos, sheet.RotDeg, sheet.Size));
        Assert.Equal("http://a/battlefield.png", sheet.Url);
        Assert.Empty(table.UmpireBoard);
    }
}
