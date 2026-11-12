using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Client.Prefs;

namespace KTable.App;

static class ProfileTabsTests
{
    static Button Plus(FloatPanel p) => p.FindChildren("*", nameof(Button), true, false).OfType<Button>().Single(b => b.Text == "+");

    static List<Button> Tabs(FloatPanel p) => Plus(p).GetParent().GetChildren().OfType<Button>().Where(b => b.Text != "+").ToList();

    static Button Tab(FloatPanel p, string name) => Tabs(p).Single(b => b.Text == name);

    static Button Named(FloatPanel p, string text) => p.Body.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == text);

    static async Task SaveAs(TestRunner r, FloatPanel p, string name)
    {
        var row = Plus(p).GetParent();
        Plus(p).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frame();
        var edit = row.GetChildren().OfType<LineEdit>().Single();
        Check.That(edit.HasFocus(), "the new profile's name is ready to type");
        edit.Text = name;
        edit.EmitSignal(LineEdit.SignalName.TextSubmitted, name);
        await r.Frames(2);
    }

    static string Lit(FloatPanel p) => string.Join(",", Tabs(p).Where(b => b.ButtonPressed).Select(b => b.Text));

    [NodeTest]
    static async Task Two_pov_profiles_kept_for_a_scenario_switch_a_pov_between_them_from_tabs_on_its_panel(TestRunner r)
    {
        var h = await HudTests.Host(r, scenario: "feud");
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var pov = h.Hud.Pov.Panel;
        var strip = Plus(pov).GetParent<Control>().GetParent<Control>();
        Check.That(strip.IsVisibleInTree() && Tabs(pov).Count == 0, "the POV panel carries a strip with only + while none is kept");
        Check.That(Mathf.IsEqualApprox(strip.GlobalPosition.Y + strip.Size.Y, pov.GlobalPosition.Y, 0.5f) && strip.GlobalPosition.Y >= pov.Ceiling - 0.5f,
            $"the strip sits on the panel's top edge, under the top bar: strip {strip.GlobalPosition.Y}+{strip.Size.Y}, panel {pov.GlobalPosition.Y}, ceiling {pov.Ceiling}");

        Named(pov, "Square").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frame();
        await SaveAs(r, pov, "Reports");
        Check.Equal("Reports", Lit(pov), "the kept profile is the lit one");

        Named(pov, "First person").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frame();
        Named(pov, "Hidden").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frame();
        Check.Equal("", Lit(pov), "settings off every profile light none");
        h.Table.SetSightingLens(90);
        await SaveAs(r, pov, "AAR");
        Check.Equal("AAR", Lit(pov), "the second one is lit");
        Check.Equal("Reports,AAR", string.Join(",", CaptureProfiles.Of(h.Hud.Cfg, "feud").Select(p => p.Name)), "both are kept for the scenario");

        Tab(pov, "Reports").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var d = h.Table.Draft!;
        Check.That(d is { FirstPerson: false, HideText: false, Format: SightingFormat.Square } && h.Table.Rig.Eye == null, "Reports puts the POV back to the square map with text");
        Check.Equal("Reports", Lit(pov), "and lights Reports");

        Tab(pov, "AAR").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        d = h.Table.Draft!;
        Check.That(d is { FirstPerson: true, HideText: true } && d.Lens == 90 && h.Table.Rig.Eye != null, "AAR looks through the piece's eyes at 90° with text hidden");
        Check.That(h.Hud.Cfg.SightingFirstPerson && h.Hud.Cfg.SightingHideText && h.Hud.Cfg.SightingLens == 90, "and is kept for the next POV");

        h.Table.CancelSighting();
        await r.Frame();
        Check.That(!pov.Visible && !strip.IsVisibleInTree(), "the strip goes with its panel");
        h.Table.SetTool(Tool.Capture);
        await r.Frames(2);
        var capture = h.Hud.Capture.Panel;
        Check.That(capture.Visible && !capture.FindChildren("*", nameof(Button), true, false).OfType<Button>().Any(b => b.Text is "+" or "Reports" or "AAR"),
            "the Capture tool's panel has no profiles");
        Named(capture, "GIF").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Named(capture, "Large").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frame();
        Check.That(h.Hud.Cfg.GifLarge, "the Capture tool's GIF size is set to Large");
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        Check.Equal("AAR", Lit(pov), "taken out again, the POV opens on the profile last used, lit though the Capture tool's GIF size changed");
    }
}
