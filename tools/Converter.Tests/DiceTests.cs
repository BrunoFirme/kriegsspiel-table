using SkiaSharp;
using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    static JsonArray SixFaces() => new(
        Face("1", -90, 0, 0), Face("2", 0, 0, 0), Face("3", 0, 0, -90), Face("4", 0, 0, 90), Face("5", 0, 0, 180), Face("6", 90, 0, 0));

    static readonly List<double[]> D20 = DefFactory.TemplateLayout["d20"].Faces;

    static JsonObject FaceUp(string value, double[] up) =>
        Face(value, Math.Asin(-up[2]) * 180 / Math.PI, 0, Math.Atan2(-up[0], up[1]) * 180 / Math.PI);

    static JsonObject Face(string value, double x, double y, double z) =>
        new() { ["Value"] = value, ["Rotation"] = new JsonObject { ["x"] = x, ["y"] = y, ["z"] = z } };

    [Fact]
    public void The_digital_tabletop_s_own_dice_become_the_app_s_dice_showing_the_face_they_were_saved_on()
    {
        var cfg0 = TempConfig();
        LocalPicture(cfg0, "http://a/face.png", 64, 64, (x, _) => x < 32 ? SKColors.Red : SKColors.White);
        var red = Obj("Die_6", "Red die", 1, new JsonObject { ["RotationValues"] = SixFaces() });
        red["Transform"]!["rotX"] = 0;
        red["Transform"]!["rotY"] = 37;
        red["Transform"]!["rotZ"] = 90;
        var big = Obj("Custom_Dice", "Big die", 2, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/face.png", ["CustomDice"] = new JsonObject { ["Type"] = 5 } },
            ["RotationValues"] = new JsonArray(FaceUp("1", D20[0]), FaceUp("20", D20[1])),
        });
        big["Transform"]!["rotX"] = FaceUp("20", D20[1])["Rotation"]!["x"]!.GetValue<double>();
        big["Transform"]!["rotZ"] = FaceUp("20", D20[1])["Rotation"]!["z"]!.GetValue<double>();
        var module = Module("dice", red, big);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "dice", "scenario.json"));
        PieceInstance Named(string label) => table.UmpireBoard.Single(p => p.Label == label);
        Assert.Contains("std.dice", table.Packs);
        Assert.Equal(("std.dice.d6", 3, 1.0), (Named("Red die").Def, Named("Red die").Face, Named("Red die").Scale));
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.dice", "pack.json"));
        var d20 = pack.Pieces.Single(d => d.Id == Named("Big die").Def);
        Assert.Equal(("model", 1), (d20.Kind, Named("Big die").Face));
        Assert.Equal(20, d20.Faces.Count);
        Assert.Equal(new[] { "1", "20" }, d20.Faces.Take(2).Select(s => s.Value));
        Assert.All(d20.Faces.Skip(2), s => Assert.Contains(s.Value, new[] { "1", "20" }));
        Assert.Contains("d20", d20.Tags);
        Assert.NotNull(d20.Texture);
        Assert.Equal(DefFactory.TemplateLayout["d20"].Size[0] * 2, d20.Size.W * Named("Big die").Scale, 3);
    }

    [Fact]
    public void A_die_s_face_is_read_from_the_rotation_it_was_saved_at()
    {
        var die = new PieceDef { Faces = Enumerable.Range(1, 6).Select(i => new DieFace { Value = i.ToString() }).ToList() };
        var o = new ModuleObj(Obj("Die_6", "", 1, new JsonObject { ["RotationValues"] = SixFaces() }));
        o.J["Transform"]!["rotX"] = 90.0;
        o.J["Transform"]!["rotZ"] = 0.0;
        Assert.Equal(5, DefFactory.FaceShown(o, die));
        o.J["Transform"]!["rotX"] = -90.0;
        Assert.Equal(0, DefFactory.FaceShown(o, die));
    }
}
