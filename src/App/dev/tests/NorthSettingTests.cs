using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class NorthSettingTests
{
    const double NorthOnTheRight = 90;

    static bool Same(float a, float b) => Mathf.Abs(Mathf.AngleDifference(a, b)) < 0.001f;

    [NodeTest]
    static async Task With_north_set_to_the_right_of_the_map_the_compass_ruler_wind_and_sightings_all_follow_it(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 1);
        try
        {
            h.S.Do(new SetCalibration { North = NorthOnTheRight });
            h.S.Do(new SetWind { Wind = new Wind { From = 270, Strength = 2 } });
            for (int i = 0; i < 30 && h.S.View.Calibration.North != NorthOnTheRight; i++) await r.Frame();
            await r.Frames(2);
            Check.Equal(NorthOnTheRight, h.S.View.Calibration.North, "north is set");

            Check.That(Same(NorthCompass.NorthOnScreen(Mathf.Pi, NorthOnTheRight), Mathf.Pi / 2), "with the top of the map up, the needle points right");
            var compass = h.Hud.Compass;
            var middle = compass.Size / 2;
            compass._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = middle, GlobalPosition = compass.GlobalPosition + middle });
            compass._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = middle, GlobalPosition = compass.GlobalPosition + middle });
            Check.That(Same(h.Table.Rig.YawGoal, CameraRig.YawLooking(NorthOnTheRight)), "a click on the compass turns the view to look toward the right of the map");
            Check.That(Same(NorthCompass.NorthOnScreen(h.Table.Rig.YawGoal, NorthOnTheRight), 0), "and then the needle points up");

            h.Table.SetTool(Tool.Ruler);
            h.Table.Ruler.A = new Vec2(0, 0);
            h.Table.Ruler.B = new Vec2(10, 0);
            h.Table.Overlay.Redraw();
            Check.Equal("000°", h.Table.Labels.TextOf("ruler-bearing"), "a ruler drawn to the right of the map reads north");
            h.Table.Ruler.B = new Vec2(0, 10);
            h.Table.Overlay.Redraw();
            Check.Equal("270°", h.Table.Labels.TextOf("ruler-bearing"), "one drawn to the top of the map reads west");
            h.Table.SetTool(Tool.Select);

            var drift = EffectFx.WindHeading;
            Check.That(Mathf.Abs(drift.X) < 0.001f && drift.Z > 0.999f, $"a west wind blows toward east, the bottom of the map: {drift}");

            h.Table.Selection.Set(new[] { "p1" });
            h.Table.SetTool(Tool.Sighting);
            h.Table.Sighting.SetFirstPerson(true);
            await r.Frame();
            h.Table.Sighting.Face("N");
            Check.That(Same(h.Table.Rig.YawGoal, CameraRig.YawLooking(NorthOnTheRight)), "the sighting's N looks toward the right of the map");
            h.Table.Sighting.Face("E");
            Check.That(Same(h.Table.Rig.YawGoal, CameraRig.YawLooking(180)), "and E toward the bottom");
            h.Table.Sighting.SetFirstPerson(false);
            h.Table.SetTool(Tool.Select);
        }
        finally
        {
            EffectFx.SetScale(new Calibration());
            EffectFx.SetWind(new Wind());
        }
    }
}
