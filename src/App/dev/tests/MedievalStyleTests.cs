using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class MedievalStyleTests
{
    static int Medieval => MapStyles.Index(MapStyles.Medieval);
    static int Survey => MapStyles.Index(MapStyles.Survey);

    [NodeTest]
    static async Task The_medieval_map_letters_places_in_blackletter_and_regions_in_red_capitals(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0);
        var letters = h.Table.MapLettering;
        var place = letters.HandFor(Medieval, NameKind.Place);
        var water = letters.HandFor(Medieval, NameKind.Water);
        var region = letters.HandFor(Medieval, NameKind.Area);
        Check.That(place.Font is FontVariation { BaseFont: var face } && face == Fonts.Blackletter, "a place on the medieval map is lettered in blackletter");
        Check.That(water.Font is FontVariation { BaseFont: var flowing } && flowing == Fonts.Blackletter, "water on the medieval map is lettered in blackletter too");
        Check.That(region.Capitals && !place.Capitals, "regions are in capitals, places are not");
        Check.That(region.Ink != place.Ink, "regions are rubricated in their own ink");
        Check.That(letters.HandFor(Survey, NameKind.Place).Font is FontVariation { BaseFont: SystemFont }, "the 18th-century survey keeps its serif");
    }

    [NodeTest]
    static async Task The_medieval_hand_dots_its_tracks_and_rubricates_its_roads(TestRunner r)
    {
        var medieval = BuiltHand.Of(Medieval);
        var survey = BuiltHand.Of(Survey);
        Check.That(medieval.Rubricated && !survey.Rubricated, "only the medieval hand draws the red line down its roads");
        Check.That(medieval.Line("track").Dash > 0 && medieval.Line("track").Dash < survey.Line("track").Dash, "medieval tracks are chains of dots, finer than the survey's dashes");
        await r.Frame();
    }
}
