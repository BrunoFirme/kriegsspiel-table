using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class ScenarioFolderTests
{
    static string Folder(Scenario s)
    {
        var dir = TestFolders.New();
        Json.SaveBig(Path.Combine(dir, ScenarioFolder.ScenarioFile), s);
        return dir;
    }

    [Fact]
    public void Renaming_a_scenario_changes_its_name_and_nothing_else()
    {
        var dir = Folder(new Scenario { Id = "local.feud", Name = "The Feud", Packs = { "p" }, Briefing = "Hold the ford", UmpireBoard = { new PieceInstance { Id = "a", Def = "x" } } });
        Assert.Equal("Königgrätz 1866", ScenarioFolder.Rename(dir, "  Königgrätz 1866 "));
        var back = Json.Load<Scenario>(Path.Combine(dir, ScenarioFolder.ScenarioFile));
        Assert.Equal(("Königgrätz 1866", "local.feud", "Hold the ford"), (back.Name, back.Id, back.Briefing));
        Assert.Equal(new[] { "p" }, back.Packs);
        Assert.Single(back.UmpireBoard);
        Assert.Equal("Königgrätz 1866", CardReader.Card(Path.Combine(dir, ScenarioFolder.ScenarioFile)).s!.Name);
        Assert.Throws<ArgumentException>(() => ScenarioFolder.Rename(dir, "   "));
        Assert.Equal("Königgrätz 1866", Json.Load<Scenario>(Path.Combine(dir, ScenarioFolder.ScenarioFile)).Name);
    }

    static Scenario Feud(string id = "tts.feud") => new()
    {
        Id = id, Name = "The Feud", Briefing = "Hold the ford",
        Sheets = { new SheetDef { Id = "s1", Image = "map", Package = "scenarios/feud" }, new SheetDef { Id = "s2", Image = "std", Package = "std.ground" } },
        UmpireBoard = { new PieceInstance { Id = "a", Def = "x" } },
    };

    static string Scenarios(out string feud)
    {
        var scenarios = Path.Combine(TestFolders.New(), "scenarios");
        feud = Path.Combine(scenarios, "feud");
        Directory.CreateDirectory(Path.Combine(feud, "tiles", "map"));
        File.WriteAllText(Path.Combine(feud, "tiles", "map", "index.json"), "{}");
        Json.SaveBig(Path.Combine(feud, ScenarioFolder.ScenarioFile), Feud());
        Json.SaveBig(Path.Combine(feud, ScenarioFolder.StartFile), Feud());
        return scenarios;
    }

    [Fact]
    public void A_duplicate_is_a_scenario_of_its_own_with_the_map_files_its_sheets_point_at_and_its_own_setup()
    {
        var scenarios = Scenarios(out var feud);
        var dir = ScenarioFolder.Duplicate(feud, " The Feud copy ");
        var copy = Path.Combine(scenarios, dir);
        Assert.NotEqual("feud", dir);
        foreach (var file in new[] { ScenarioFolder.ScenarioFile, ScenarioFolder.StartFile })
        {
            var sc = Json.Load<Scenario>(Path.Combine(copy, file));
            Assert.Equal(("The Feud copy", ScenarioFolder.OwnPrefix + dir, "Hold the ford"), (sc.Name, sc.Id, sc.Briefing));
            Assert.Equal(new[] { "scenarios/" + dir, "std.ground" }, sc.Sheets.Select(s => s.Package));
            Assert.Single(sc.UmpireBoard);
        }
        Assert.True(File.Exists(Path.Combine(copy, "tiles", "map", "index.json")), "the sheet's tiles come along");
        var original = Json.Load<Scenario>(Path.Combine(feud, ScenarioFolder.ScenarioFile));
        Assert.Equal(("The Feud", "tts.feud", "scenarios/feud"), (original.Name, original.Id, original.Sheets[0].Package));
        Assert.NotEqual(dir, ScenarioFolder.Duplicate(feud, "The Feud copy"));
        Assert.Throws<ArgumentException>(() => ScenarioFolder.Duplicate(feud, "  "));
        Assert.Equal(3, Directory.GetDirectories(scenarios).Length);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_duplicate_shares_the_map_s_tiles_with_the_original_where_the_drive_can_and_has_every_other_file_as_its_own(bool linking)
    {
        var scenarios = Scenarios(out var feud);
        Directory.CreateDirectory(Path.Combine(feud, "tiles", "map", "0"));
        Directory.CreateDirectory(Path.Combine(feud, "assets"));
        File.WriteAllText(Path.Combine(feud, "tiles", "map", "0", "0_0.webp"), "tile");
        File.WriteAllText(Path.Combine(feud, "assets", "map.jpg"), "picture");
        if (!linking) FileLinks.Maker.Value = (_, _) => false;

        var copy = Path.Combine(scenarios, ScenarioFolder.Duplicate(feud, "The Feud copy"));

        Assert.Equal("tile", File.ReadAllText(Path.Combine(copy, "tiles", "map", "0", "0_0.webp")));
        Assert.Equal("picture", File.ReadAllText(Path.Combine(copy, "assets", "map.jpg")));
        Assert.Equal(linking, OneCopy.Holds(Path.Combine(feud, "tiles", "map", "0", "0_0.webp"), Path.Combine(copy, "tiles", "map", "0", "0_0.webp")));
        Assert.False(OneCopy.Holds(Path.Combine(feud, "assets", "map.jpg"), Path.Combine(copy, "assets", "map.jpg")));
        Assert.False(OneCopy.Holds(Path.Combine(feud, ScenarioFolder.StartFile), Path.Combine(copy, ScenarioFolder.StartFile)));
    }

    [Fact]
    public void A_renamed_scenario_keeps_its_new_name_when_it_starts_over_from_its_setup()
    {
        Scenarios(out var feud);
        ScenarioFolder.Rename(feud, "Pogue against Boggs");
        Assert.Equal("Pogue against Boggs", Json.Load<Scenario>(Path.Combine(feud, ScenarioFolder.StartFile)).Name);
        Assert.Equal("Pogue against Boggs", Json.Load<Scenario>(Path.Combine(feud, ScenarioFolder.ScenarioFile)).Name);
    }

    [Fact]
    public void A_scenario_shows_its_own_picture_once_given_one_and_its_map_again_once_it_is_taken_away()
    {
        var dir = Folder(new Scenario { Id = "local.feud", Name = "The Feud" });
        Assert.Null(ScenarioFolder.Picture(dir));
        ScenarioFolder.SetPicture(dir, new byte[] { 1, 2, 3 });
        Assert.Equal(Path.Combine(dir, ScenarioFolder.PictureFile), ScenarioFolder.Picture(dir));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(ScenarioFolder.Picture(dir)!));
        ScenarioFolder.DropPicture(dir);
        Assert.Null(ScenarioFolder.Picture(dir));
        ScenarioFolder.DropPicture(dir);
        Assert.True(File.Exists(Path.Combine(dir, ScenarioFolder.ScenarioFile)));
    }
}
