using KTable.Core.Model;

namespace KTable.Client.Tests;

public class BuildPrefsTests
{
    static BuildPrefs Saved(string mode, string place = BuiltKinds.Settlement)
    {
        var prefs = new BuildPrefs { Mode = mode, Place = place };
        prefs.Normalise();
        return prefs;
    }

    [Theory]
    [InlineData("places", BuiltKinds.Settlement, BuildPrefs.Laying, BuildPrefs.Places)]
    [InlineData("places", BuiltKinds.Building, BuildPrefs.Drawing, BuildPrefs.Things)]
    [InlineData("roads", BuiltKinds.Settlement, BuildPrefs.Drawing, BuildPrefs.Roads)]
    [InlineData("trace", BuiltKinds.Settlement, BuildPrefs.Drawing, BuildPrefs.Blocks)]
    [InlineData("details", BuiltKinds.Settlement, BuildPrefs.Brushing, BuildPrefs.Details)]
    [InlineData("lamps", BuiltKinds.Settlement, BuildPrefs.Brushing, BuildPrefs.Lamps)]
    [InlineData("pick", BuiltKinds.Settlement, BuildPrefs.Pick, BuildPrefs.Pick)]
    [InlineData("nonsense", BuiltKinds.Settlement, BuildPrefs.Laying, BuildPrefs.Places)]
    public void A_Build_mode_saved_by_an_earlier_version_opens_on_the_same_tool(string mode, string place, string verb, string activity)
    {
        var prefs = Saved(mode, place);
        Assert.Equal((verb, activity), (prefs.Mode, prefs.Activity()));
        Assert.Contains(prefs.Place, BuildPrefs.PlaceKinds);
    }

    [Fact]
    public void Each_tool_is_reached_by_its_verb_and_remembered_under_it()
    {
        var prefs = new BuildPrefs();
        prefs.Do(BuildPrefs.Blocks);
        prefs.Do(BuildPrefs.Lamps);
        prefs.Mode = BuildPrefs.Drawing;
        Assert.Equal(BuildPrefs.Blocks, prefs.Activity());
        prefs.Mode = BuildPrefs.Brushing;
        Assert.Equal(BuildPrefs.Lamps, prefs.Activity());
        prefs.Block = "nothing at all";
        prefs.Normalise();
        Assert.Equal("house", prefs.Block);
    }
}
