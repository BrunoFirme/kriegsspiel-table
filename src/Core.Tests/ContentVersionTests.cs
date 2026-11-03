using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class ContentVersionTests
{
    static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-version-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void A_pack_or_scenario_that_names_no_version_is_this_build_s()
    {
        var pack = Json.Deserialize<PackManifest>("""{ "id": "old.pack", "name": "Old pack", "pieces": [], "assets": {} }""");
        Assert.Equal(PackManifest.CurrentFormat, pack.Format);
        Assert.Equal(ScriptApi.Version, pack.ApiVersion);
        var sc = Json.Deserialize<Scenario>("""{ "id": "old", "name": "Old scenario", "packs": [], "sheets": [], "umpireBoard": [] }""");
        Assert.Equal(Scenario.CurrentFormat, sc.Format);
        Assert.Equal(ScriptApi.Version, sc.ApiVersion);
        Json.Deserialize<PackManifest>(Json.Serialize(new PackManifest { Id = "p" }));
        Json.Deserialize<Scenario>(Json.Serialize(new Scenario { Id = "s" }));
    }

    [Fact]
    public void A_pack_in_a_newer_format_is_refused_with_words_that_say_to_update()
    {
        var e = Assert.Throws<ContentTooNewException>(() => Json.Deserialize<PackManifest>(
            $$"""{ "format": {{PackManifest.CurrentFormat + 1}}, "id": "new.pack", "name": "New pack", "pieces": [] }"""));
        Assert.Equal($"the pack \"New pack\" was made with a newer version of Kriegsspiel Table (pack format {PackManifest.CurrentFormat + 1}, this build reads up to {PackManifest.CurrentFormat}) — update the app", e.Message);
    }

    [Fact]
    public void A_pack_whose_scripts_need_a_newer_api_is_refused()
    {
        var e = Assert.Throws<ContentTooNewException>(() => Json.Deserialize<PackManifest>(
            $$"""{ "apiVersion": {{ScriptApi.Version + 1}}, "id": "new.pack", "pieces": [] }"""));
        Assert.Equal($"the pack \"new.pack\" was made with a newer version of Kriegsspiel Table (scripting API {ScriptApi.Version + 1}, this build has up to {ScriptApi.Version}) — update the app", e.Message);
    }

    [Fact]
    public void A_scenario_in_a_newer_format_or_api_is_refused_by_a_full_read_and_by_its_card()
    {
        var root = Temp();
        var path = Path.Combine(root, "scenario.json");
        File.WriteAllText(path, $$"""{ "format": {{Scenario.CurrentFormat + 1}}, "id": "s", "name": "Next year's", "packs": [], "sheets": [], "umpireBoard": [] }""");
        var e = Assert.Throws<ContentTooNewException>(() => Json.Load<Scenario>(path));
        Assert.Contains($"(scenario format {Scenario.CurrentFormat + 1}, this build reads up to {Scenario.CurrentFormat}) — update the app", e.Message);
        Assert.StartsWith("the scenario \"Next year's\"", e.Message);
        Assert.Equal(e.Message, Assert.Throws<ContentTooNewException>(() => Json.ScenarioCard(path)).Message);

        File.WriteAllText(path, $$"""{ "apiVersion": {{ScriptApi.Version + 1}}, "id": "s", "name": "Next year's", "packs": [], "sheets": [], "umpireBoard": [] }""");
        Assert.Contains("scripting API", Assert.Throws<ContentTooNewException>(() => Json.Load<Scenario>(path)).Message);
        Assert.Contains("scripting API", Assert.Throws<ContentTooNewException>(() => Json.ScenarioCard(path)).Message);

        File.WriteAllText(path, """{ "id": "s", "name": "Last year's", "packs": [], "sheets": [], "umpireBoard": [] }""");
        Assert.Equal("Last year's", Json.ScenarioCard(path).head.Name);
    }

    [Fact]
    public void An_installed_pack_from_a_newer_build_is_not_loaded_and_the_store_says_why()
    {
        var root = Temp();
        Directory.CreateDirectory(Path.Combine(root, "new.pack"));
        File.WriteAllText(Path.Combine(root, "new.pack", "pack.json"), $$"""{ "format": {{PackManifest.CurrentFormat + 1}}, "id": "new.pack", "name": "New pack", "pieces": [], "assets": {} }""");
        var store = new PackageStore(root);
        Assert.Null(store.Pack("new.pack"));
        Assert.Contains("update the app", store.Problem("new.pack"));
    }

    [Fact]
    public void What_is_written_carries_this_build_s_versions()
    {
        var json = Json.Serialize(new PackManifest { Id = "p", Format = 0, ApiVersion = 0 });
        Assert.Contains($"\"format\":{PackManifest.CurrentFormat}", json);
        Assert.Contains($"\"apiVersion\":{ScriptApi.Version}", json);
        json = Json.Serialize(new Scenario { Id = "s", Format = 0, ApiVersion = 0 }, indented: true);
        Assert.Contains($"\"format\": {Scenario.CurrentFormat}", json);
        Assert.Contains($"\"apiVersion\": {ScriptApi.Version}", json);
    }
}
