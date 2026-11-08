using System.Text.Json.Nodes;
using KTable.Client.Effects;
using KTable.Core;
using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter.Tests;

public sealed class ConversionGuardTests : ConverterHarness
{
    const string Name = "guard";

    static string IdsPath => Path.Combine(SourceDir(), "Snapshots", "kind-ids.txt");

    static string SourceDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "Converter.Tests.csproj"))) return d.FullName;
        throw new DirectoryNotFoundException("Converter.Tests.csproj not found above " + AppContext.BaseDirectory);
    }

    static JsonObject At(JsonObject o, string guid, double x)
    {
        o["GUID"] = guid;
        o["Transform"]!["posX"] = x;
        return o;
    }

    static JsonObject Picture(string url) => new() { ["ImageURL"] = url };

    JsonObject[] EveryKind()
    {
        var cfg = TempConfig();
        LocalPicture(cfg, "http://a/unit.png", 64, 32, (x, _) => x < 32 ? SKColors.Navy : SKColors.White);
        LocalPicture(cfg, "http://a/die.png", 64, 64, (x, y) => (x + y) % 2 == 0 ? SKColors.Red : SKColors.White);
        var models = Path.Combine(cfg.ModsPath, "Models");
        Directory.CreateDirectory(models);
        File.WriteAllLines(Path.Combine(models, AssetStore.Key("http://a/tower.obj") + ".obj"),
            ["v 0 0 0", "v 1 0 0", "v 1 2 0", "v 0 2 0", "v 0 0 1", "v 1 0 1", "v 1 2 1", "v 0 2 1", "f 1 2 3 4", "f 5 6 7 8", "f 1 2 6 5", "f 4 3 7 8"]);
        var bag = new PackManifest { Id = EffectKinds.StandardPack, Name = "Firme's Effects and Props" };
        foreach (var (box, w, h, t) in new[] { (BagBoxes.Box, 1.0, 0.62, 0.48), (BagBoxes.Chest, 1.46, 0.96, 0.8), (BagBoxes.Crate, 1.91, 1.3, 1.04) })
            bag.Pieces.Add(new PieceDef { Id = BagBoxes.IdOf(box), Name = box, Kind = "model", Size = new Size2(w, h), Thickness = t, Shape = "box", Tags = ["prop"] });
        Json.Save(Path.Combine(Packages, EffectKinds.StandardPack, "pack.json"), bag);

        var looks = At(Obj("Custom_Token", "Battalion in line", 1, new JsonObject { ["CustomImage"] = Picture("http://a/unit.png") }), "g08", 21);
        looks["States"] = new JsonObject { ["2"] = At(Obj("BlockSquare", "Battalion in column", 1), "g08b", 21) };
        var glued = At(Obj("BlockRectangle", "Base", 1), "g10", 27);
        glued["ChildObjects"] = new JsonArray(At(Obj("Custom_Token", "Flag", 0.5, new JsonObject { ["CustomImage"] = Picture("http://a/unit.png") }), "g10a", 27));
        var held = At(Obj("Bag", "Supplies", 1, new JsonObject
        {
            ["ContainedObjects"] = new JsonArray(At(Obj("Checker_white", "Crate 1", 1), "g11a", 0), At(Obj("BlockSquare", "Crate 2", 1), "g11b", 0)),
        }), "g11", 30);
        var stretched = At(Obj("Custom_Model", "Long hall", 1, new JsonObject { ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/tower.obj" } }), "g09", 24);
        stretched["Transform"]!["scaleZ"] = 2.5;
        return
        [
            At(Obj("Custom_Token", "Gun", 1, new JsonObject { ["CustomImage"] = Picture("http://a/unit.png") }), "g01", 0),
            At(Obj("Custom_Tile", "Village", 1, new JsonObject { ["CustomImage"] = Picture("http://a/unit.png") }), "g02", 3),
            At(Obj("BlockSquare", "Brigade", 1), "g03", 6),
            At(Obj("Checker_red", "Marker", 1), "g04", 9),
            At(Obj("Die_6", "d6", 1), "g05", 12),
            At(Obj("Custom_Dice", "Picture die", 1, new JsonObject { ["CustomImage"] = new JsonObject { ["ImageURL"] = "http://a/die.png", ["CustomDice"] = new JsonObject { ["Type"] = 1 } } }), "g06", 15),
            At(Obj("Custom_Model", "Tower", 1, new JsonObject { ["CustomMesh"] = new JsonObject { ["MeshURL"] = "http://a/tower.obj" } }), "g07", 18),
            looks, stretched, glued, held,
            At(Obj("Figurine_Custom", "Officer", 1, new JsonObject { ["CustomImage"] = Picture("http://a/unit.png") }), "g12", 33),
            At(Obj("Strange_Contraption", "What is this", 1), "g13", 36),
        ];
    }

    (Scenario Table, List<PieceDef> Kinds, string Report) Convert(JsonObject[] objects)
    {
        var main = typeof(Config).Assembly.EntryPoint!;
        main.Invoke(null, new object[] { new[] { "import", Module(Name, objects), "--config", Path.Combine(_root, "converter.json"), "--out", Packages } });
        var scenarioDir = Path.Combine(Packages, "scenarios", Name);
        var table = Json.Load<Scenario>(Path.Combine(scenarioDir, "scenario.json"));
        var kinds = table.Packs.Where(p => p.StartsWith("tts." + Name)).SelectMany(p => Json.Load<PackManifest>(Path.Combine(Packages, p, "pack.json")).Pieces).ToList();
        return (table, kinds, File.ReadAllText(Path.Combine(scenarioDir, "import-report.md")));
    }

    static IEnumerable<PieceInstance> Everything(IEnumerable<PieceInstance> pieces) =>
        pieces.SelectMany(p => new[] { p }.Concat(Everything(p.Stowed ?? [])));

    [Fact]
    public void Every_kind_a_module_can_hold_keeps_the_id_it_was_recorded_with_so_tables_built_on_a_module_still_fit_it_converted_again()
    {
        var (table, kinds, _) = Convert(EveryKind());
        var lines = Everything(table.UmpireBoard).Select(p => $"{p.Guid} {p.Def}").Concat(kinds.Select(k => "kind " + k.Id)).Order(StringComparer.Ordinal).ToList();
        if (!File.Exists(IdsPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IdsPath)!);
            File.WriteAllText(IdsPath, string.Join("\n", lines) + "\n");
            Assert.Fail($"ids recorded, review and commit: {IdsPath}");
        }
        var recorded = File.ReadAllText(IdsPath).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var gone = recorded.Except(lines).ToList();
        var added = lines.Except(recorded).ToList();
        Assert.True(gone.Count == 0 && added.Count == 0,
            "a converter change gave kinds new ids, and a table built on a module loses those pieces when the module is converted again. "
            + $"No longer made: {string.Join(", ", gone)}. Now made: {string.Join(", ", added)}. If the change is meant, delete {IdsPath} and run again to record the new ids.");
    }

    [Fact]
    public void Every_object_a_module_saves_comes_over_as_a_piece_a_held_piece_or_a_glued_part_or_is_named_in_the_import_report()
    {
        var saved = EveryKind();
        var (table, kinds, report) = Convert(saved);
        var guids = Everything(table.UmpireBoard).Select(p => p.Guid).OfType<string>().ToHashSet();
        var kindOf = kinds.ToDictionary(k => k.Id);
        var missing = new List<string>();
        foreach (var o in saved)
        {
            var guid = o["GUID"]!.GetValue<string>();
            var named = report.Contains(o["Nickname"]!.GetValue<string>()) || report.Contains(o["Name"]!.GetValue<string>());
            if (!guids.Contains(guid) && !named) missing.Add($"{o["Name"]} {guid}");
            foreach (var inner in (o["ContainedObjects"] as JsonArray ?? []).OfType<JsonObject>())
                if (!guids.Contains(inner["GUID"]!.GetValue<string>())) missing.Add($"{inner["Name"]} {inner["GUID"]} in {guid}");
            if (o["ChildObjects"] is JsonArray { Count: > 0 } children)
            {
                var piece = Everything(table.UmpireBoard).FirstOrDefault(p => p.Guid == guid);
                var parts = piece != null && kindOf.TryGetValue(piece.Def, out var k) ? k.Parts?.Count ?? 0 : 0;
                if (parts < children.Count) missing.Add($"{children.Count - parts} glued part(s) of {guid}");
            }
        }
        Assert.True(missing.Count == 0, "came over nowhere and are not named in the report: " + string.Join("; ", missing));
    }
}
