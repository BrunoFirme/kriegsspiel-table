using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed partial class ConverterTests
{
    static JsonArray SixFaces() => new(
        Face("1", -90, 0, 0), Face("2", 0, 0, 0), Face("3", 0, 0, -90), Face("4", 0, 0, 90), Face("5", 0, 0, 180), Face("6", 90, 0, 0));

    static JsonObject Face(string value, double x, double y, double z) =>
        new() { ["Value"] = value, ["Rotation"] = new JsonObject { ["x"] = x, ["y"] = y, ["z"] = z } };

    [Fact]
    public void The_digital_tabletop_s_own_dice_become_the_app_s_dice_showing_the_face_they_were_saved_on()
    {
        var cfg0 = TempConfig();
        var red = Obj("Die_6", "Red die", 1, new JsonObject { ["RotationValues"] = SixFaces() });
        red["Transform"]!["rotX"] = 0;
        red["Transform"]!["rotY"] = 37;
        red["Transform"]!["rotZ"] = 90;
        var big = Obj("Custom_Dice", "Big die", 2, new JsonObject
        {
            ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/face.png", ["CustomDice"] = new JsonObject { ["Type"] = 5 } },
            ["RotationValues"] = new JsonArray(Face("1", -90, 0, 0), Face("20", 90, 0, 0)),
        });
        big["Transform"]!["rotX"] = 90;
        var module = Module("dice", red, big);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[] { new[] { "import", module, "--config", Path.Combine(_root, "converter.json"), "--out", Packages } })!;

        Assert.Equal(0, code);
        var table = Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "dice", "scenario.json"));
        PieceInstance Named(string label) => table.UmpireBoard.Single(p => p.Label == label);
        Assert.Contains("std.dice", table.Packs);
        Assert.Equal(("std.dice.d6", 3, 1.0), (Named("Red die").Def, Named("Red die").State, Named("Red die").Scale));
        Assert.Equal(("std.dice.d20", 19, 2.0), (Named("Big die").Def, Named("Big die").State, Named("Big die").Scale));
        Assert.Contains("without its pictures", File.ReadAllText(Path.Combine(Packages, "scenarios", "dice", "import-report.md")));
    }

    [Fact]
    public void A_die_s_face_is_read_from_the_rotation_it_was_saved_at()
    {
        var die = new PieceDef { States = Enumerable.Range(1, 6).Select(i => new PieceStateDef { Name = i.ToString() }).ToList() };
        var o = new ModuleObj(Obj("Die_6", "", 1, new JsonObject { ["RotationValues"] = SixFaces() }));
        o.J["Transform"]!["rotX"] = 90.0;
        o.J["Transform"]!["rotZ"] = 0.0;
        Assert.Equal(5, DefFactory.DieFace(o, die));
        o.J["Transform"]!["rotX"] = -90.0;
        Assert.Equal(0, DefFactory.DieFace(o, die));
    }
}
