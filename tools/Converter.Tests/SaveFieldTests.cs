using System.Text;
using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    (DefFactory defs, PackOut pack) Factory()
    {
        var cfg = TempConfig();
        var rep = new Report();
        return (new DefFactory(cfg, new AssetStore(cfg, rep), rep), new PackOut(cfg.Output, "t", "T", "t", null));
    }

    static JsonObject At(double x, double y, double z, double sx, double sy, double sz) =>
        new() { ["posX"] = x, ["posY"] = y, ["posZ"] = z, ["rotX"] = 0.0, ["rotY"] = 0.0, ["rotZ"] = 0.0, ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sz };

    [Fact]
    public void Module_values_written_as_text_numbers_or_flags_read_as_the_digital_tabletop_reads_them()
    {
        var o = new ModuleObj(new JsonObject
        {
            ["Name"] = "Custom_Tile", ["Nickname"] = 5, ["Locked"] = "true", ["Sticky"] = 0, ["CardID"] = "307",
            ["Transform"] = new JsonObject { ["posX"] = "1.5", ["scaleX"] = "2", ["scaleZ"] = 2 },
            ["CustomImage"] = new JsonObject { ["CustomTile"] = new JsonObject { ["Type"] = "1", ["Thickness"] = "0.4", ["Stretch"] = 1 } },
        });

        Assert.Equal(("5", true, false, 307), (o.Nickname, o.Locked, o.Sticky, o.CardId));
        Assert.Equal((1.5, 2.0, 1, 0.4, true), (o.PosX, o.ScaleX, o.TileType, o.Thickness, o.TileStretch));
    }

    [Fact]
    public void An_object_placed_where_no_number_is_or_at_the_centre_with_no_size_is_left_out_as_the_digital_tabletop_leaves_it()
    {
        var nan = Obj("Custom_Tile", "Lost", 1);
        nan["Transform"]!["posX"] = "NaN";
        var garbage = Obj("Custom_Tile", "Nothing", 1);
        garbage["Transform"] = At(0, 0, 0, 0, 0, 0);
        var small = Obj("Custom_Tile", "Tiny", 1);
        small["Transform"] = At(0, 0, 0, 0, 0, 0.5);

        Assert.NotNull(DefFactory.NotTranslated(new ModuleObj(nan)));
        Assert.NotNull(DefFactory.NotTranslated(new ModuleObj(garbage)));
        Assert.Null(DefFactory.NotTranslated(new ModuleObj(small)));
    }

    [Fact]
    public void A_tile_saved_without_a_stretch_stays_square_and_one_without_tile_settings_stretches_as_the_prefab_does_whatever_its_shape()
    {
        var cfg = TempConfig();
        const string Wide = "http://a/wide.png";
        LocalPicture(cfg, Wide, 40, 20, (_, _) => SKColors.Olive);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        Size2 SizeOf(JsonObject? tile) => defs.BaseSize(new ModuleObj(Obj("Custom_Tile", "", 1, new JsonObject
        {
            ["CustomImage"] = tile == null ? new JsonObject { ["ImageURL"] = Wide } : new JsonObject { ["ImageURL"] = Wide, ["CustomTile"] = tile },
        })));

        Assert.Equal(new Size2(2, 2), SizeOf(new JsonObject { ["Type"] = 0, ["Thickness"] = 0.2 }));
        Assert.Equal(new Size2(4, 2), SizeOf(null));
        Assert.Equal(new Size2(4, 2), SizeOf(new JsonObject { ["Type"] = 1, ["Stretch"] = true }));
        Assert.Equal(new Size2(4, 2), SizeOf(new JsonObject { ["Type"] = 2, ["Stretch"] = true }));
    }

    [Fact]
    public void A_tile_takes_only_its_own_thickness_and_ignores_its_height_scale_while_a_token_grows_with_its_height_scale()
    {
        var (defs, pack) = Factory();
        var tile = Obj("Custom_Tile", "Tile", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["CustomToken"] = new JsonObject { ["Thickness"] = 0.9 } } });
        tile["Transform"] = At(0, 1, 0, 1, 3, 1);
        var token = Obj("Custom_Token", "Token", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["CustomToken"] = new JsonObject { ["Thickness"] = 0.1 } } });
        token["Transform"] = At(0, 1, 0, 1, 3, 1);
        var bare = Obj("Custom_Tile", "Bare", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["CustomTile"] = new JsonObject { ["Type"] = 0 } } });

        Assert.Equal(0.2, defs.DefFor(new ModuleObj(tile), pack)!.Def.Thickness, 3);
        Assert.Equal(0.3, defs.DefFor(new ModuleObj(token), pack)!.Def.Thickness, 3);
        Assert.Equal(0.0, new ModuleObj(bare).Thickness);
    }

    [Fact]
    public void An_object_saved_without_a_colour_keeps_the_colour_of_the_digital_tabletops_own_piece()
    {
        static string? Colour(string type) => new ModuleObj(new JsonObject { ["Name"] = type }).Color;

        Assert.Equal(("#EC0000", "#0033D2", "#01C500", "#000000"), (Colour("BlockSquare"), Colour("BlockRectangle"), Colour("BlockTriangle"), Colour("Figurine_Custom")));
        Assert.Equal(("#B6B6B6", "#841B9E"), (Colour("CardCustom"), Colour("Die_20")));
        Assert.Null(Colour("Custom_Tile"));
    }

    [Fact]
    public void The_old_rules_text_comes_in_only_when_the_module_keeps_no_notebook_tabs()
    {
        var cfg = TempConfig();
        string Rules(JsonObject? tabs)
        {
            var path = Module("rules", Obj("BlockSquare", "Marker", 1));
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            json["Rules"] = "Old rules";
            if (tabs != null) json["TabStates"] = tabs;
            File.WriteAllText(path, json.ToJsonString());
            return ModuleSave.Load(cfg, path).Rules;
        }

        Assert.Equal("Old rules", Rules(null));
        Assert.Equal("Old rules", Rules(new JsonObject()));
        Assert.Equal("", Rules(new JsonObject { ["0"] = new JsonObject { ["title"] = "Rules", ["body"] = "" } }));
    }

    [Theory]
    [InlineData("Die_4", 0.7719)]
    [InlineData("Die_6", 0.5426)]
    [InlineData("Die_8", 0.5861)]
    [InlineData("Die_20", 0.5261)]
    public void A_stock_die_is_drawn_as_big_as_the_digital_tabletops_own_die(string type, double radius)
    {
        var (defs, pack) = Factory();
        var r = defs.DefFor(new ModuleObj(Obj(type, "", 1)), pack)!;
        using var mesh = typeof(DefFactory).Assembly.GetManifestResourceStream($"std.dice/assets/{r.Def.Mesh}.obj")!;
        double drawn = DefFactory.DrawnRadius(new StreamReader(mesh).ReadToEnd(), r.Def.Size) * r.Fit;

        Assert.Equal(radius, drawn, 3);
    }

    [Fact]
    public void The_first_object_loaded_keeps_a_guid_two_objects_share_and_the_later_one_gets_a_fresh_one()
    {
        var pieces = new List<PieceInstance>
        {
            new() { Id = "p-000001", Guid = "aaaaaa" }, new() { Id = "p-000002", Guid = "bbbbbb" }, new() { Id = "p-000003", Guid = "aaaaaa" }, new() { Id = "p-000004" },
        };

        ScenarioBuilder.UniqueGuids(pieces);

        Assert.Equal(("aaaaaa", "bbbbbb", (string?)null), (pieces[0].Guid, pieces[1].Guid, pieces[3].Guid));
        Assert.DoesNotContain(pieces[2].Guid, new[] { "aaaaaa", "bbbbbb" });
        Assert.Matches("^[0-9a-f]{6}$", pieces[2].Guid!);
    }

    [Fact]
    public void A_piece_turned_end_over_end_faces_the_other_way_and_one_rolled_over_keeps_its_heading()
    {
        static ModuleObj Turned(double rx, double ry, double rz) => new(new JsonObject
        {
            ["Name"] = "Custom_Tile", ["Transform"] = new JsonObject { ["rotX"] = rx, ["rotY"] = ry, ["rotZ"] = rz, ["scaleX"] = 1.0, ["scaleY"] = 1.0, ["scaleZ"] = 1.0 },
        });

        Assert.Equal((true, 30.0), (Turned(0, 30, 180).Flipped, Turned(0, 30, 180).Heading));
        Assert.Equal((true, 210.0), (Turned(180, 30, 0).Flipped, Turned(180, 30, 0).Heading));
        Assert.Equal((false, 210.0), (Turned(180, 30, 180).Flipped, Turned(180, 30, 180).Heading));
    }

    [Fact]
    public void A_figurine_wears_its_colour_on_its_stand_only_and_its_card_grows_with_its_image_scalar_each_side_as_wide_as_its_own_picture()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/front.png", 20, 40, (_, _) => SKColors.White);
        LocalPicture(cfg, "http://a/back.png", 40, 40, (_, _) => SKColors.White);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var figure = Obj(DefFactory.Figurine, "Ney", 1, new JsonObject
        {
            ["ColorDiffuse"] = new JsonObject { ["r"] = 0.0, ["g"] = 0.0, ["b"] = 1.0 },
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/front.png", ["ImageSecondaryURL"] = "http://a/back.png", ["ImageScalar"] = 2.0 },
        });

        var def = defs.DefFor(new ModuleObj(figure), pack)!.Def;

        Assert.Equal("#FFFFFF", def.Tint);
        Assert.Equal(DefFactory.FigurineTop * 2, defs.BaseThickness(new ModuleObj(figure)), 3);
        Assert.Equal(DefFactory.FigurineCard * 2, def.Size.W, 3);
        using var atlas = SKBitmap.Decode(Path.Combine(pack.Dir, pack.M.Assets[def.Texture!].File));
        Assert.Equal(SKColors.Blue, atlas.GetPixel(1, atlas.Height - 1));
        var xs = File.ReadAllLines(Path.Combine(pack.Dir, pack.M.Assets[def.Mesh!].File)).Where(l => l.StartsWith("v ", StringComparison.Ordinal))
            .Select(l => double.Parse(l.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(DefFactory.FigurineCard, xs.Max(), 3);
    }

    static byte[] TwoPagePdf(double w1, double h1, double w2, double h2)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            string.Format(inv, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0} {1}] >>", w1, h1),
            string.Format(inv, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0} {1}] >>", w2, h2),
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append(inv, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append(inv, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets) sb.Append(inv, $"{off:D10} 00000 n \n");
        sb.Append(inv, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public void A_pdf_book_lies_four_units_deep_and_as_wide_as_its_page_and_a_page_of_another_shape_reshapes_it()
    {
        var cfg = TempConfig();
        const string Url = "http://a/book.pdf";
        var pdfs = Path.Combine(cfg.ModsPath, "PDF");
        Directory.CreateDirectory(pdfs);
        File.WriteAllBytes(Path.Combine(pdfs, AssetStore.Key(Url) + ".pdf"), TwoPagePdf(100, 200, 200, 100));
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);

        var def = defs.DefFor(new ModuleObj(Obj("Custom_PDF", "Orders", 1, new JsonObject { ["CustomPDF"] = new JsonObject { ["PDFUrl"] = Url } })), pack)!.Def;

        Assert.Equal((2.0, 4.0, DefFactory.BookThickness), (def.Size.W, def.Size.H, def.Thickness));
        Assert.Equal(2, def.States.Count);
        Assert.Null(def.States[0].Size);
        Assert.Equal(8.0, def.States[1].Size!.Value.W, 1);
    }
}
