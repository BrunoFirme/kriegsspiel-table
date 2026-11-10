using KTable.Core;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class ScenarioReadTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ktable-scenario-read-tests", Guid.NewGuid().ToString("N"));

    string File(string text)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        System.IO.File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    [Fact]
    public void A_scenario_file_reads_whole_and_a_damaged_one_says_nothing_but_is_not_refused()
    {
        var good = Path.Combine(_root, "good.json");
        Json.Save(good, new Scenario { Id = "scn", Name = "Feud" });
        Assert.Equal(("Feud", (string?)null), (ScenarioRead.ToHost(good).Scenario!.Name, ScenarioRead.ToHost(good).Refused));
        Assert.Equal((null, null), ScenarioRead.ToHost(File("{ damaged")));
    }

    [Fact]
    public void A_scenario_from_a_newer_version_is_refused_not_taken_for_damage()
    {
        var (sc, refused) = ScenarioRead.ToHost(File("{\"format\":9,\"name\":\"Feud\",\"umpireBoard\":[{\"id\":\"p\",\"kind\":\"not-a-kind\"}]}"));
        Assert.Null(sc);
        Assert.Contains("newer version of the app", refused);
    }

    [Fact]
    public void A_scenario_held_by_another_program_is_refused_not_taken_for_damage()
    {
        var path = Path.Combine(_root, "held.json");
        Json.Save(path, new Scenario { Id = "scn", Name = "Feud" });
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var (sc, refused) = ScenarioRead.ToHost(path);
            Assert.Null(sc);
            Assert.Contains("held by another program", refused);
        }
        Assert.Equal("Feud", ScenarioRead.ToHost(path).Scenario!.Name);
    }
}
