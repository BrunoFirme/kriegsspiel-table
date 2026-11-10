namespace KTable.Converter.Tests;

public sealed class ImportPlanTests
{
    [Fact]
    public void A_module_named_like_a_scenario_made_here_takes_a_folder_of_its_own_and_keeps_it_when_converted_again()
    {
        var scenarios = new Dictionary<string, string> { ["waterloo"] = "local.waterloo" };
        var packs = new Dictionary<string, string>();
        string? In(string dir) => scenarios.GetValueOrDefault(dir);
        string? Owner(string id) => packs.GetValueOrDefault(id);
        string? PackOf(string workshopId) => packs.FirstOrDefault(kv => kv.Value == workshopId).Key;
        var module = new ModuleSave { SaveName = "Waterloo", WorkshopId = "2222222222" };

        var first = ImportPlan.Of(module, null, null, Owner, PackOf, In);
        Assert.Equal(("tts.waterloo-2", "waterloo-2", "waterloo"), (first.Packs[0], first.Scenario, first.TakenFolder));

        scenarios["waterloo-2"] = "tts.waterloo-2";
        packs["tts.waterloo-2"] = "2222222222";
        var again = ImportPlan.Of(module, null, null, Owner, PackOf, In);
        Assert.Equal(("tts.waterloo-2", "waterloo-2", null), (again.Packs[0], again.Scenario, again.TakenFolder));

        packs.Clear();
        packs["tts.waterloo-2"] = "";
        var fromAFile = ImportPlan.Of(new ModuleSave { SaveName = "Waterloo" }, null, null, Owner, PackOf, In);
        Assert.Equal("waterloo-2", fromAFile.Scenario);

        var itsOwn = ImportPlan.Of(new ModuleSave { SaveName = "Ligny", WorkshopId = "3333333333" }, null, null, Owner, PackOf, dir => dir == "ligny" ? "tts.ligny" : null);
        Assert.Equal(("ligny", null), (itsOwn.Scenario, itsOwn.TakenFolder));
    }
}
