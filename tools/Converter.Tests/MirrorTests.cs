using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed class MirrorTests : ConverterHarness
{
    static JsonObject Turned(JsonObject o, double sx, double sy, double sz)
    {
        o["Transform"]!["scaleX"] = sx;
        o["Transform"]!["scaleY"] = sy;
        o["Transform"]!["scaleZ"] = sz;
        return o;
    }

    [Fact]
    public void A_piece_scaled_negative_across_shows_its_picture_mirrored_across_and_one_scaled_negative_along_shows_it_upside_down()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/half.png", 20, 10, (x, y) => x < 10 ? SKColors.Red : y < 5 ? SKColors.Blue : SKColors.Green);
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        JsonObject Tile() => Obj("Custom_Tile", "", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/half.png" } });
        SKColor At(PieceDef d, int x, int y)
        {
            using var bmp = SKBitmap.Decode(Path.Combine(pack.Dir, pack.M.Assets[d.Image!].File));
            return bmp.GetPixel(x, y);
        }

        var plain = defs.DefFor(new ModuleObj(Tile()), pack)!.Def;
        var across = defs.DefFor(new ModuleObj(Turned(Tile(), -1, 1, 1)), pack)!.Def;
        var along = defs.DefFor(new ModuleObj(Turned(Tile(), 1, 1, -1)), pack)!.Def;

        Assert.Equal(SKColors.Red, At(plain, 2, 2));
        Assert.Equal((SKColors.Blue, SKColors.Red), (At(across, 2, 2), At(across, 18, 2)));
        Assert.Equal((SKColors.Green, SKColors.Blue), (At(along, 18, 2), At(along, 18, 8)));
        Assert.Equal(3, new[] { plain.Id, across.Id, along.Id }.Distinct().Count());
    }

    [Fact]
    public void A_models_mesh_is_mirrored_on_every_axis_its_scale_is_negative_and_its_faces_turned_the_right_way_out()
    {
        var obj = new[] { "v 1 2 3", "vn 0 0 1", "vt 0.5 0.5", "f 1/1/1 2/2/2 3/3/3" };

        var once = PieceMirror.MirroredObj(obj, new Mirror(true, false, false)).ToList();
        var twice = PieceMirror.MirroredObj(obj, new Mirror(true, false, true)).ToList();

        Assert.Equal(new[] { "v -1 2 3", "vn -0 0 1", "vt 0.5 0.5", "f 3/3/3 2/2/2 1/1/1" }, once);
        Assert.Equal(new[] { "v -1 2 -3", "vn -0 0 -1", "vt 0.5 0.5", "f 1/1/1 2/2/2 3/3/3" }, twice);
    }

    [Fact]
    public void Decals_on_a_mirrored_piece_are_mirrored_with_it()
    {
        var o = new ModuleObj(Turned(Obj("Custom_Tile", "", 1, new JsonObject
        {
            ["AttachedDecals"] = new JsonArray(new JsonObject
            {
                ["Transform"] = new JsonObject { ["posX"] = 0.5, ["posY"] = 0.2, ["posZ"] = 0.25, ["rotX"] = 90.0, ["rotY"] = 210.0, ["rotZ"] = 0.0, ["scaleX"] = 0.2, ["scaleY"] = 0.2, ["scaleZ"] = 1.0 },
                ["CustomDecal"] = new JsonObject { ["Name"] = "Flag", ["ImageURL"] = "http://a/flag.png", ["Size"] = 1.0 },
            }),
        }), -1, 1, 1));
        var plain = new ModuleObj(Turned((JsonObject)o.J.DeepClone(), 1, 1, 1));

        var mirrored = ScenarioBuilder.DecalsOf(o, new Size2(2, 2), false, 14, _ => "pic", _ => "flipped").Single();
        var straight = ScenarioBuilder.DecalsOf(plain, new Size2(2, 2), false, 14, _ => "pic", _ => "flipped").Single();

        Assert.Equal(("flipped", -straight.X, straight.Z, -straight.Rot), (mirrored.Picture, mirrored.X, mirrored.Z, mirrored.Rot));
        Assert.Equal("pic", straight.Picture);
    }

    [Fact]
    public void Lines_drawn_on_a_mirrored_piece_are_mirrored_with_it()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/tile.png", 8, 8, (_, _) => SKColors.White);
        JsonObject Tile(string nick, double sx) => Turned(Obj("Custom_Tile", nick, 1, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/tile.png" },
            ["AttachedVectorLines"] = new JsonArray(new JsonObject
            {
                ["points3"] = new JsonArray(new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = 0.0 }, new JsonObject { ["x"] = 0.5, ["y"] = 0.0, ["z"] = 0.25 }),
                ["color"] = new JsonObject { ["r"] = 1.0, ["g"] = 0.0, ["b"] = 0.0 }, ["thickness"] = 0.1,
            }),
        }), sx, 1, 1);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", Module("lined", Tile("Plain", 1), Tile("Mirrored", -1)), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var board = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "lined", "scenario.json")).UmpireBoard;
        var plain = board.Single(p => p.Label == "Plain").Lines.Single().Points[1];
        var mirrored = board.Single(p => p.Label == "Mirrored").Lines.Single().Points[1];
        Assert.Equal((-plain.X, plain.Z), (mirrored.X, mirrored.Z));
    }
}
