using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class MedievalStyleTests
{
    static int Medieval => MapStyles.Index(MapStyles.Medieval);
    static int Survey => MapStyles.Index(MapStyles.Survey);

    [NodeTest]
    static async Task The_medieval_map_letters_in_the_faces_the_Draw_tool_offers_places_in_blackletter_and_regions_in_red_capitals(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0);
        var letters = h.Table.MapLettering;
        var place = letters.HandFor(Medieval, NameKind.Place);
        var water = letters.HandFor(Medieval, NameKind.Water);
        var region = letters.HandFor(Medieval, NameKind.Area);
        var fraktur = MapLettering.Face(Stroke.FontFraktur);
        Check.That(place.Font is FontVariation { BaseFont: var face } && face == fraktur, "a place on the medieval map is lettered in the Draw tool's Fraktur");
        Check.That(water.Font is FontVariation { BaseFont: var flowing } && flowing == fraktur, "water on the medieval map is lettered in blackletter too");
        Check.That(region.Font is FontVariation { BaseFont: var titled } && titled == MapLettering.Face(Stroke.FontDidone), "regions take the Draw tool's Didone");
        Check.That(MapLettering.OwnLettering(Medieval, NameKind.Plain).Face is FontVariation { BaseFont: var shown } && shown == fraktur, "the Draw tool shows the map's own lettering in the face the map draws");
        Check.That(MapLettering.MapHand(Stroke.FontWater) == NameKind.Water && MapLettering.MapHand(Stroke.FontRegion) == NameKind.Area && MapLettering.MapHand(null) == NameKind.Plain,
            "the Draw tool writes in the map's lettering for places, water and regions");
        Check.That(MapLettering.OwnLettering(Medieval, NameKind.Area) is { Capitals: true, Face: FontVariation { BaseFont: var ruled } } && ruled == MapLettering.Face(Stroke.FontDidone),
            "its region lettering shows in capitals of the map's Didone");
        Check.That(letters.HandFor(MapStyles.Index(MapStyles.Ancient), NameKind.Place).Font is FontVariation { BaseFont: var carved } && carved == MapLettering.Face(Stroke.FontDidone),
            "the ancient map letters in the Draw tool's Didone");
        Check.That(letters.HandFor(Medieval, NameKind.Plain, font: Stroke.FontFraktur).Font is FontVariation { BaseFont: var picked } && picked == fraktur, "Fraktur picked in the Draw tool is the map's blackletter");
        Check.That(letters.HandFor(Medieval, NameKind.Plain, bold: true).Font is FontVariation { VariationEmbolden: > 0 }, "the map's own blackletter takes bold like any picked face");
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
