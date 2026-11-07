using System.Text;
using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed class SaveFieldTests : ConverterHarness
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

    [Fact]
    public void A_line_turns_by_its_saved_rotation_ends_round_unless_saved_square_and_a_line_of_no_thickness_draws_nothing()
    {
        static JsonObject Line(double thickness, bool? square, double turnY) => new()
        {
            ["points3"] = new JsonArray(new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0 }, new JsonObject { ["x"] = 1.0, ["y"] = 0.0, ["z"] = 0.0 }),
            ["color"] = new JsonObject { ["r"] = 1.0, ["g"] = 1.0, ["b"] = 1.0 }, ["thickness"] = thickness,
            ["rotation"] = new JsonObject { ["x"] = 0.0, ["y"] = turnY, ["z"] = 0.0 }, ["square"] = square is { } s ? JsonValue.Create(s) : null,
        };

        var lines = ModuleObj.LinesOf(new JsonArray(Line(0.01, null, 90), Line(0.2, true, 0), Line(0, false, 0)));

        Assert.Equal(2, lines.Count);
        Assert.Equal((0.0, 0.0, -1.0), (Math.Round(lines[0].Points[1].X, 6), Math.Round(lines[0].Points[1].Y, 6), Math.Round(lines[0].Points[1].Z, 6)));
        Assert.Equal((0.01, true), (lines[0].Thickness, lines[0].Round));
        Assert.Equal((1.0, false), (lines[1].Points[1].X, lines[1].Round));
    }

    [Fact]
    public void Three_d_text_is_drawn_at_the_game_s_three_d_ui_scale_whatever_scale_the_module_saved_and_keeps_its_colour_s_alpha()
    {
        var (defs, pack) = Factory();
        JsonObject Text(double scale) => Obj("3DText", "", scale, new JsonObject
        {
            ["Text"] = new JsonObject { ["Text"] = "Ford", ["fontSize"] = 64, ["colorstate"] = new JsonObject { ["r"] = 1.0, ["g"] = 0.0, ["b"] = 0.0, ["a"] = 0.5 } },
        });

        var one = defs.DefFor(new ModuleObj(Text(1)), pack)!;
        var big = defs.DefFor(new ModuleObj(Text(3)), pack)!;

        Assert.Equal(DefFactory.TextUnitsPerFontSize, one.Lettering!.UnitsPerPixel, 6);
        Assert.Equal((one.Def.Size, 1.0), (big.Def.Size, big.Scale));
        Assert.Equal("#FF000080", one.Lettering.Color);
    }

    [Theory]
    [InlineData(0, 10, 7, 2.1437, "rounded")]
    [InlineData(1, 10, 7, 2.1437, "box")]
    [InlineData(3, 10, 7, 3.0625, "hex")]
    [InlineData(4, 10, 7, 2.1437, "circle")]
    [InlineData(0, 5, 7, 4.2875, "rounded")]
    public void A_card_takes_its_cells_shape_at_the_game_s_card_height_and_the_outline_its_card_type_names(int type, int across, int down, double width, string shape)
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/sheet.png", 700, 700, (_, _) => SKColors.White);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var card = Obj("CardCustom", "", 1, new JsonObject
        {
            ["CardID"] = 100,
            ["CustomDeck"] = new JsonObject { ["1"] = new JsonObject { ["FaceURL"] = "http://a/sheet.png", ["BackURL"] = "", ["NumWidth"] = across, ["NumHeight"] = down, ["Type"] = type } },
        });

        var def = defs.DefFor(new ModuleObj(card), pack)!.Def;

        Assert.Equal((width, DefFactory.CardHeight, shape), (Math.Round(def.Size.W, 4), def.Size.H, def.Shape));
    }

    [Fact]
    public void A_card_sheet_saved_without_its_size_is_ten_by_seven_a_card_past_the_sheet_is_its_last_cell_and_a_back_marked_unique_is_cut_per_card()
    {
        var o = new ModuleObj(new JsonObject
        {
            ["Name"] = "CardCustom", ["CardID"] = 199,
            ["CustomDeck"] = new JsonObject { ["1"] = new JsonObject { ["FaceURL"] = "http://a/f.png", ["BackURL"] = "http://a/b.png{Unique}" } },
        });

        Assert.Equal((10, 7, true, 69), (o.Sheet!.Across, o.Sheet.Down, o.Sheet.UniqueBack, o.SheetCell));
    }

    [Fact]
    public void A_piece_keeps_its_name_and_description_as_saved_its_script_state_without_a_script_and_one_tag_per_spelling_the_game_counts_as_one()
    {
        TempConfig();
        var block = Obj("BlockSquare", " 1st Brigade ", 1, new JsonObject
        {
            ["Description"] = "  Holds the ford ", ["LuaScriptState"] = "{\"n\":3}", ["Tags"] = new JsonArray("Red Army", "red_army", "Guard"),
        });
        var module = Module("named", block);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var piece = Assert.Single(Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "named", "scenario.json")).UmpireBoard);
        Assert.Equal((" 1st Brigade ", "  Holds the ford ", "{\"n\":3}"), (piece.Label, piece.Note, piece.ScriptState));
        Assert.Equal(new[] { "Red Army", "Guard" }, piece.Tags);
    }

    [Fact]
    public void A_model_takes_its_material_from_its_mesh_settings_plastic_when_none_is_saved_and_never_from_the_objects_own_index()
    {
        static string? Finish(JsonObject mesh, int? objectIndex) => new ModuleObj(new JsonObject
        {
            ["Name"] = "Custom_Model", ["CustomMesh"] = mesh, ["MaterialIndex"] = objectIndex is { } i ? JsonValue.Create(i) : null,
        }).Finish;

        Assert.Equal(KTable.Core.Model.Finishes.Plastic, Finish(new JsonObject { ["MeshURL"] = "http://a/m.obj" }, 2));
        Assert.Equal(KTable.Core.Model.Finishes.Metal, Finish(new JsonObject { ["MeshURL"] = "http://a/m.obj", ["MaterialIndex"] = 2 }, 0));
        Assert.Null(new ModuleObj(new JsonObject { ["Name"] = "BlockSquare", ["MaterialIndex"] = 1 }).Finish);
    }

    [Fact]
    public void Only_a_stack_object_counts_its_saved_number_and_it_comes_in_as_its_own_kind_of_piece()
    {
        var stack = new ModuleObj(new JsonObject { ["Name"] = "Custom_Tile_Stack", ["Number"] = 5 });
        var single = new ModuleObj(new JsonObject { ["Name"] = "Custom_Tile", ["Number"] = 5 });

        Assert.Equal(("Custom_Tile", 5, true), (stack.Name, stack.StackCount, stack.IsStack));
        Assert.Equal((1, false), (single.StackCount, single.IsStack));
        Assert.Equal("Custom_Tile", new ModuleObj(stack.J).Name);
    }

    static SKBitmap Picture(int w, int h, Func<int, int, byte> alpha)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) bmp.SetPixel(x, y, new SKColor(200, 50, 50, alpha(x, y)));
        return bmp;
    }

    [Fact]
    public void A_tokens_outline_runs_through_the_centres_of_its_fully_opaque_pixels_as_the_game_cuts_it()
    {
        using var square = Picture(20, 10, (x, _) => (byte)(x < 10 ? 255 : 254));

        var loop = Assert.Single(TokenOutline.Of(square, 0)!);

        Assert.InRange(loop.Count, 4, 5);
        Assert.Equal((0.025, 0.475), (loop.Min(p => p.X), loop.Max(p => p.X)));
        Assert.Equal((0.05, 0.95), (loop.Min(p => p.Z), loop.Max(p => p.Z)));
    }

    [Fact]
    public void A_tokens_outline_merges_points_closer_than_its_merge_distance_and_keeps_each_island()
    {
        using var round = Picture(64, 64, (x, y) => (byte)((x - 31.5) * (x - 31.5) + (y - 31.5) * (y - 31.5) < 900 ? 255 : 0));
        using var two = Picture(40, 10, (x, _) => (byte)(x < 10 || x >= 30 ? 255 : 0));

        int fine = TokenOutline.Of(round, 0)!.Single().Count, coarse = TokenOutline.Of(round, 15)!.Single().Count;

        Assert.True(coarse < fine && coarse >= 3, $"{coarse} points merged from {fine}");
        Assert.Equal(2, TokenOutline.Of(two, 2)!.Count);
    }

    [Fact]
    public void A_token_carries_the_games_outline_of_its_picture_cut_at_its_own_merge_distance()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/round.png", 64, 64, (x, y) => (x - 31.5) * (x - 31.5) + (y - 31.5) * (y - 31.5) < 900 ? SKColors.Red : SKColors.Transparent);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        JsonObject Token(double merge) => Obj("Custom_Token", "", 1, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/round.png", ["CustomToken"] = new JsonObject { ["Thickness"] = 0.2, ["MergeDistancePixels"] = merge } },
        });

        var usual = defs.DefFor(new ModuleObj(Token(15)), pack)!.Def;
        var fine = defs.DefFor(new ModuleObj(Token(1)), pack)!.Def;

        Assert.NotEqual(usual.Id, fine.Id);
        Assert.True(fine.Outline!.Single().Count > usual.Outline!.Single().Count);
    }

    [Fact]
    public void A_token_saved_standing_up_stands_on_its_edge_as_tall_as_its_picture_and_as_deep_as_its_thickness()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/man.png", 20, 40, (_, _) => SKColors.Navy);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        JsonObject Token(bool up) => Obj("Custom_Token", "", 1, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/man.png", ["CustomToken"] = new JsonObject { ["Thickness"] = 0.3, ["StandUp"] = up } },
        });

        var flat = defs.DefFor(new ModuleObj(Token(false)), pack)!.Def;
        var standing = defs.DefFor(new ModuleObj(Token(true)), pack)!.Def;

        Assert.Equal(("flat", "model"), (flat.Kind, standing.Kind));
        Assert.Equal((flat.Size.W, 0.3, Math.Round(flat.Size.H, 3)), (standing.Size.W, standing.Size.H, standing.Thickness));
        Assert.Equal(flat.Image, standing.Texture);
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
