using System;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

static class GroundColourTests
{
    const int SettleFrames = 40;
    const float ViewAcross = 4, Close = 0.03f;
    const byte Field = 2;

    static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < Close && Math.Abs(a.G - b.G) < Close && Math.Abs(a.B - b.B) < Close;

    static async Task<Color> PaperSeen(TestRunner r, HudTests.Rig h, Lighting light)
    {
        h.S.Do(new SetLighting { Lighting = light });
        await r.Frames(SettleFrames);
        var shot = r.GetViewport().GetTexture().GetImage();
        return shot.GetPixel(shot.GetWidth() / 2, shot.GetHeight() / 2);
    }

    [NodeTest]
    static async Task Every_map_style_has_its_lettering_and_its_built_hand(TestRunner r)
    {
        Check.Equal(MapStyles.ShaderOrder.Count, MapLettering.Styles, "names on the map have an ink and a paper for every style");
        Check.Equal(MapStyles.ShaderOrder.Count, ContourLines.Styles, "contour numbers have an ink and a face for every style");
        Check.Equal(MapStyles.ShaderOrder.Count * GroundKinds.All.Count, ReliefSurface.Palette().Length, "the shader's palette holds every style's kinds");
        Check.That(BuiltHand.Of(MapStyles.Index(MapStyles.Staff)) != BuiltHand.Of(MapStyles.Index(MapStyles.Survey)), "the staff sheet has a built hand of its own");
        await r.Frame();
    }

    [ScreenTest]
    static async Task Bare_ground_shows_its_paper_s_own_colour_whatever_the_colour_of_the_light(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0, sheets: 1);
        var id = h.S.View.Sheets[0].Id;
        var flat = ReliefGrid.Flat(20, 20);
        var (sha, _) = ReliefImport.Save(flat, h.S.Store.Root);
        h.S.Relief.Remember(sha, flat);
        h.S.Do(new SetSheetRelief { Id = id, Relief = sha, Height = 1 });
        h.Hud.Visible = false;
        if (!h.Table.Rig.TopDown) h.Table.Rig.ToggleTopDown();
        h.Table.Rig.Frame(Coords.ToWorld(h.S.View.Sheets[0].Pos), ViewAcross);
        var paper = Color.FromHtml(GroundKinds.Color(Field, MapStyles.Modern));
        var modern = Json.Clone(h.S.View.Lighting);
        modern.MapStyle = MapStyles.Modern;
        var ceiling = await PaperSeen(r, h, modern);
        Check.That(Near(ceiling, paper), $"under the ceiling light the paper is {paper.ToHtml(false)}, seen {ceiling.ToHtml(false)}");
        var lamp = Json.Clone(LightPresets.Named("Warm lamp")!);
        lamp.MapStyle = MapStyles.Modern;
        var warm = await PaperSeen(r, h, lamp);
        Check.That(Near(warm, paper), $"a warm lamp leaves the paper's hue alone, seen {warm.ToHtml(false)}");
        var night = Json.Clone(LightPresets.Named(LightPresets.MoonlitNight)!);
        night.MapStyle = MapStyles.Modern;
        var dim = await PaperSeen(r, h, night);
        Check.That(dim.R < paper.R * 0.85f, $"a moonlit night dims the paper, seen {dim.ToHtml(false)}");
        Check.That(Math.Abs(dim.R / dim.B - paper.R / paper.B) < 0.06f, $"and still leaves its hue alone, seen {dim.ToHtml(false)}");
    }
}
