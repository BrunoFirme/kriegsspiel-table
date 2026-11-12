using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class RoseDragTests
{
    const float Far = 600;
    const string Light = "Light & surroundings", WindPage = "Wind";

    static IEnumerable<Node> Descendants(Node n) => n.GetChildren().SelectMany(c => Descendants(c).Prepend(c));

    internal static Vector2 Centre(Control c) => (Vector2)c.GetWindow().Position + c.GetGlobalRect().GetCenter();

    internal static void Press(Vector2 at, bool down) =>
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at, ButtonMask = down ? MouseButtonMask.Left : 0 });

    static void Move(Vector2 at, bool held = true, bool shift = false) =>
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = held ? MouseButtonMask.Left : 0, ShiftPressed = shift });

    internal static async Task<T> OpenOn<T>(TestRunner r, HudTests.Rig h, string page) where T : CompassRose
    {
        DialogPages.LastPage["scenario"] = page;
        ScenarioDialog.Open(h.Hud.Ctx);
        await r.Frames(3);
        DialogPages.LastPage.Remove("scenario");
        var rose = Descendants(h.Hud).OfType<T>().Single();
        Check.That(rose.IsVisibleInTree(), $"the {page} page shows its rose");
        return rose;
    }

    [NodeTest]
    static async Task The_sun_sits_under_the_pointer_its_bearing_from_the_direction_and_its_height_from_the_distance_to_the_centre(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var rose = await OpenOn<SunRose>(r, h, Light);
        var slider = Descendants(h.Hud).OfType<HSlider>().Single(s => s.MinValue == Lighting.LowestSun && s.MaxValue == Lighting.HighestSun);
        var c = Centre(rose);
        float ring = rose.Radius * SunRose.Track;

        async Task Sees(Vector2 at, double from, double height, string what, bool shift = false)
        {
            Move(at, shift: shift);
            await r.Frame();
            Check.Equal(from, rose.From, what + ", bearing");
            Check.Equal(height, rose.Height, what + ", height");
            Check.Equal(height, slider.Value, what + ", the Height slider");
        }

        Press(c + new Vector2(0, -0.25f * ring), true);
        await r.Frame();
        Check.Equal(0.0, rose.From, "a press a quarter of the way to the north rim takes north");
        Check.Equal(69.0, rose.Height, "and a high sun");

        await Sees(c + new Vector2(0.6f * ring, 0), 90, 39, "well out to the east, a low sun in the east");
        await Sees(c + new Vector2(0.3f, 0.3f), 90, 89, "the very centre stands the sun overhead and keeps the bearing");
        await Sees(c + new Vector2(0, Far), 180, Lighting.LowestSun, "far below the rose, the lowest sun in the south");
        await Sees(c + new Vector2(-Far, 0), 270, Lighting.LowestSun, "far left of it, in the west");
        await Sees(c + new Vector2(Far * 0.7f, -Far * 0.72f), 45, Lighting.LowestSun, "far up and right, snapped to the north-east");
        await Sees(c + new Vector2(Far, -Far * 0.1f), 84, Lighting.LowestSun, "Shift takes single degrees", shift: true);
        await Sees(c + new Vector2(Far, -Far * 0.1f), 90, Lighting.LowestSun, "without it the bearing snaps to the near point");

        var end = c + new Vector2(-0.6f * ring, 0);
        Move(end);
        Press(end, false);
        await r.Frames(2);
        Check.Equal(270.0, h.S.View.Lighting.SunFrom, "the release sends the bearing");
        Check.Equal(39.0, h.S.View.Lighting.SunHeight, "and the height");

        Move(c + new Vector2(0, Far), held: false);
        await r.Frame();
        Check.That(rose.From == 270 && rose.Height == 39, $"after the release the pointer no longer moves the sun: {rose.From}, {rose.Height}");
    }

    [NodeTest]
    static async Task The_wind_rose_turns_with_the_pointer_wherever_it_goes_while_held(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var rose = await OpenOn<WindRose>(r, h, WindPage);
        var c = Centre(rose);
        Press(c + new Vector2(20, 0), true);
        await r.Frame();
        Move(c + new Vector2(0, Far));
        await r.Frame();
        Check.Equal(180.0, rose.From, "the wind follows the pointer far below the rose");
        Press(c + new Vector2(0, Far), false);
        await r.Frames(2);
        Check.Equal(180.0, h.S.View.Wind.From, "and the release sends it");
        Move(c + new Vector2(-Far, 0), held: false);
        await r.Frame();
        Check.Equal(180.0, rose.From, "and lets go");
    }
}
