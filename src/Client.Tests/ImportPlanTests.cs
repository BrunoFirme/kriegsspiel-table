namespace KTable.Client.Tests;

public class ImportPlanTests
{
    [Fact]
    public void The_plan_lines_are_kept_and_only_the_converters_other_words_are_for_the_umpire()
    {
        var plan = new ImportPlan();
        var said = new[]
        {
            "Reading the module…", "PLAN pack tts.feud ", "PLAN pack tts.feud-maps", "PLAN scenario  feud", "PLAN file C:\\cache\\123.json",
            "PLAN something new", "Output: C:\\packages", "Cache hit", "Done.",
        }.Where(plan.Read).ToList();
        Assert.Equal(new[] { "Reading the module…", "Done." }, said);
        Assert.Equal(new[] { "tts.feud", "tts.feud-maps" }, plan.Packs);
        Assert.Equal("feud", plan.Scenario);
        Assert.Equal("C:\\cache\\123.json", plan.File);
        Assert.False(plan.Read("PLAN file "));
        Assert.Equal("", plan.File);
        Assert.Null(new ImportPlan().File);
    }
}
