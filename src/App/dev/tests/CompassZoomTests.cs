using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class CompassZoomTests
{
    static readonly Vector2 FarAway = new(-5000, -5000);

    static float Said(LineEdit zoom) => float.Parse(zoom.Text.Replace(" u", "", StringComparison.Ordinal), CultureInfo.InvariantCulture);

    [NodeTest]
    static async Task Pointing_at_the_compass_shows_the_zoom_under_it_to_type_and_the_wheel_over_it_zooms(TestRunner r)
    {
        const float Typed = 12.5f;
        var h = await HudTests.Host(r);
        var compass = h.Hud.Compass;
        var rig = h.Table.Rig;
        float before = rig.ZoomGoal;
        Pointer.Drive(FarAway);
        await r.Frames(4);
        var zoom = compass.GetChildren().OfType<LineEdit>().Single();
        Check.That(!zoom.Visible, "with the pointer elsewhere the compass shows no zoom");
        Check.Equal(before, rig.ZoomGoal, "and nothing has moved the view");
        Pointer.Drive(compass.GetGlobalRect().GetCenter());
        await r.Seconds(0.4);
        Check.That(zoom.Visible && zoom.Modulate.A > 0.9f, $"pointing at the compass shows the zoom: {zoom.Visible}, {zoom.Modulate.A}");
        Check.That(Mathf.Abs(Said(zoom) - rig.ZoomGoal) < 0.06f, $"it says how far the eye is: {zoom.Text} for {rig.ZoomGoal}");
        Check.That(zoom.Position.Y >= compass.Size.Y || zoom.Position.Y + zoom.Size.Y <= 0, $"it sits under the compass, or over it near the foot of the screen: {zoom.Position}");
        zoom.Text = "12,5 u";
        zoom.EmitSignal(LineEdit.SignalName.TextSubmitted, zoom.Text);
        Check.Equal(Typed, rig.ZoomGoal, "a typed distance is where the view goes");
        zoom.Text = "far";
        zoom.EmitSignal(LineEdit.SignalName.TextSubmitted, zoom.Text);
        Check.That(rig.ZoomGoal == Typed && Said(zoom) == Typed, "something that is no number changes nothing and the zoom is said again");
        compass._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = compass.Size / 2 });
        Check.That(rig.ZoomGoal < Typed, $"the wheel over the compass zooms in: {rig.ZoomGoal}");
        Check.That(compass.TooltipText.Contains("wheel", StringComparison.Ordinal) && zoom.TooltipText.Contains("type", StringComparison.Ordinal), "the compass and its zoom say so on hover");
        Pointer.Drive(FarAway);
        await r.Seconds(0.4);
        Check.That(!zoom.Visible, "and with the pointer gone the zoom goes too");
    }
}
