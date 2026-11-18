using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class ZoomStripTests
{
    static ZoomStrip Zoom(HudTests.Rig h) => h.Hud.Strip.Zoom ?? throw new TestFailure("the tool strip has no zoom");

    [NodeTest]
    static async Task The_zoom_at_the_foot_of_the_tool_strip_shows_how_much_of_the_map_the_screen_takes_in_and_takes_a_typed_width(TestRunner r)
    {
        const float Typed = 12.5f;
        var h = await HudTests.Host(r);
        float Across() => r.GetViewport().GetVisibleRect().Size.X / h.Table.Rig.PixelsPerUnit();
        var zoom = Zoom(h);
        var rig = h.Table.Rig;
        await r.Frames(3);
        var strip = h.Hud.Strip.Panel.GetGlobalRect();
        Check.That(Mathf.Abs(strip.Size.X - Ui.Px(h.Hud.Cfg.StripSize + 10)) < 1, $"the strip is no wider for it: {strip.Size.X}");
        Check.That(strip.Encloses(zoom.GetGlobalRect()), "the zoom sits inside the tool strip");
        Check.That(h.Hud.Strip.Buttons.Values.All(b => b.GetGlobalRect().End.Y <= zoom.GetGlobalRect().Position.Y + 1), "below every button of it");
        Check.That(!h.Hud.Compass.GetChildren().OfType<LineEdit>().Any() && !h.Hud.Compass.TooltipText.Contains("wheel", StringComparison.Ordinal), "the compass keeps to north");
        var field = zoom.Field;
        Check.That(field.IsVisibleInTree() && strip.Encloses(field.GetGlobalRect()), "its number is always there, inside the strip");
        Check.That(ZoomStrip.Number(field.Text) is { } said && Mathf.Abs((float)said - Across()) < Across() * 0.05f && zoom.Unit.Text == "u",
            $"how much of the map the screen takes in from side to side: {field.Text} {zoom.Unit.Text} for {Across()}");
        field.Text = "12,5";
        field.EmitSignal(LineEdit.SignalName.TextSubmitted, field.Text);
        Check.Equal("12.5", field.Text, "a typed width is what it then says");
        await r.Seconds(1.2);
        Check.That(Mathf.Abs(Across() - Typed) < Typed * 0.02f, $"and the screen takes in that much of the map: {Across()}");
        float typed = rig.ZoomGoal;
        field.Text = "far";
        field.EmitSignal(LineEdit.SignalName.TextSubmitted, field.Text);
        Check.That(rig.ZoomGoal == typed && field.Text == "12.5", "something that is no number changes nothing");
        Check.Equal("2.4k", ZoomStrip.Compact(2400), "a long width is said short enough for the strip");
        Check.Equal(2400.0, ZoomStrip.Number("2.4k"), "and read back the same way");
    }

    [NodeTest]
    static async Task On_a_short_window_the_zoom_slider_gives_up_room_before_the_buttons(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var strip = h.Hud.Strip;
        strip.FitTo(float.MaxValue);
        await r.Frames(2);
        float full = strip.Panel.GetCombinedMinimumSize().Y, dial = Zoom(h).Dial.CustomMinimumSize.Y;
        strip.FitTo(full - Ui.Px(20));
        Check.That(Zoom(h).Dial.CustomMinimumSize.Y < dial - Ui.Px(19), $"the slider is shorter: {dial} to {Zoom(h).Dial.CustomMinimumSize.Y}");
        Check.That(strip.Buttons.Values.All(b => Mathf.IsEqualApprox(b.CustomMinimumSize.Y, Ui.Px(h.Hud.Cfg.StripSize))), "and the buttons keep their size");
        ((IHudShell)h.Hud).Relayout();
    }

    [NodeTest]
    static async Task The_zoom_slider_dials_closer_upward_and_its_buttons_step_in_and_out(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var zoom = Zoom(h);
        var rig = h.Table.Rig;
        var dial = zoom.Dial;
        await r.Frames(2);
        float before = rig.ZoomGoal, shown = (float)dial.Value;
        Check.That(shown > 0 && shown < 1, $"the slider stands where the zoom is: {shown}");
        dial.Value = shown + 0.1;
        Check.That(rig.ZoomGoal < before, $"up is closer: {before} to {rig.ZoomGoal}");
        dial.Value = shown - 0.1;
        Check.That(rig.ZoomGoal > before, "down is further");
        dial.Value = 1;
        Check.That(Mathf.IsEqualApprox(rig.ZoomGoal, rig.MinDistance), "its top is as close as the view goes");
        dial.Value = 0;
        Check.That(Mathf.IsEqualApprox(rig.ZoomGoal, rig.MaxDistance), "its foot as far");
        rig.ZoomTo(before);
        await r.Seconds(0.4);
        Check.That(Mathf.Abs((float)dial.Value - shown) < 0.01f, "and it follows a zoom made any other way");
        var steps = zoom.GetChildren().OfType<Button>().ToList();
        Check.Equal(2, steps.Count, "a button over the slider and one under it");
        steps[0].EmitSignal(BaseButton.SignalName.Pressed);
        Check.That(rig.ZoomGoal < before, "the top one zooms in");
        float inner = rig.ZoomGoal;
        steps[1].EmitSignal(BaseButton.SignalName.Pressed);
        steps[1].EmitSignal(BaseButton.SignalName.Pressed);
        Check.That(rig.ZoomGoal > inner, "the foot one zooms out");
        rig.ZoomTo(before);
    }
}
