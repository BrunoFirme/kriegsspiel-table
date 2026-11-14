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
