using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class SunPlaceTests
{
    const double East = 90, NorthOnTheRight = 90, Low = 25;

    static IEnumerable<Node> Descendants(Node n) => n.GetChildren().SelectMany(c => Descendants(c).Prepend(c));

    static DirectionalLight3D Sun(Node table) => Descendants(table).OfType<DirectionalLight3D>().Single();

    static bool Near(float a, double b) => Mathf.Abs(Mathf.AngleDifference(Mathf.DegToRad(a), Mathf.DegToRad((float)b))) < 0.01f;

    static async Task Light(TestRunner r, Session s, System.Action<Lighting> change)
    {
        var l = s.View.Lighting.Clone();
        change(l);
        s.Do(new SetLighting { Lighting = l });
        await r.Frames(2);
    }

    [NodeTest]
    static async Task The_table_s_sun_stands_where_the_light_places_it_and_turns_with_the_map_s_north(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 1);
        var sun = Sun(h.Table);
        Check.That(Near(sun.RotationDegrees.X, -88) && Near(sun.RotationDegrees.Y, 35), $"an old table keeps its near-overhead light: {sun.RotationDegrees}");
        float overheadEnergy = sun.LightEnergy;

        await Light(r, h.S, l => (l.SunFrom, l.SunHeight) = (East, Low));
        Check.That(Near(sun.RotationDegrees.X, -Low) && Near(sun.RotationDegrees.Y, 90), $"a morning sun stands low in the east: {sun.RotationDegrees}");
        var shines = -sun.GlobalTransform.Basis.Z;
        Check.That(shines.X < -0.8f && shines.Y < 0, $"its light runs west, toward the left of the map, and down: {shines}");
        Check.That(sun.LightEnergy > overheadEnergy * 2, $"a low sun is lifted so flat ground stays as bright: {sun.LightEnergy} against {overheadEnergy}");

        h.S.Do(new SetCalibration { North = NorthOnTheRight });
        for (int i = 0; i < 30 && h.S.View.Calibration.North != NorthOnTheRight; i++) await r.Frame();
        await r.Frames(2);
        shines = -sun.GlobalTransform.Basis.Z;
        Check.That(shines.Z < -0.8f, $"with north on the right of the map, an east sun shines from the bottom toward the top: {shines}");

        await Light(r, h.S, l => (l.FromClock, l.SunEnergy) = (true, 0.5));
        await r.Frames(2);
        var (from, height) = DayLight.SunAt(DayLight.HourOf(h.S.View.Clock));
        var (pitch, yaw) = DayLight.Aim(from, height, NorthOnTheRight);
        Check.That(Near(sun.RotationDegrees.X, pitch) && Near(sun.RotationDegrees.Y, yaw), $"following the clock places the sun by the hour: {sun.RotationDegrees}");
        h.Table.ApplyScale(new Calibration());
    }

    [NodeTest]
    static async Task Hills_cast_shadows_while_shadows_are_on(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 1);
        var grid = ReliefGrid.Flat(40, 40);
        ReliefBrush.Stroke(grid, new[] { (0.2, 0.5), (0.8, 0.5) }, 40, new Brush(BrushMode.Raise, 0, 0.6));
        var (sha, _) = ReliefImport.Save(grid, h.S.Store.Root);
        h.S.Relief.Remember(sha, grid);
        h.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(40, 40), Relief = sha, Height = 1 } });
        await r.Seconds(1.5);
        var surface = Descendants(h.Table).OfType<ReliefSurface>().FirstOrDefault() ?? throw new TestFailure("no relief drawn");
        var ground = Descendants(surface).OfType<MeshInstance3D>().ToList();
        Check.That(ground.Count > 0 && ground.All(m => m.CastShadow == GeometryInstance3D.ShadowCastingSetting.On),
            $"every piece of the relief casts a shadow: {ground.Count(m => m.CastShadow == GeometryInstance3D.ShadowCastingSetting.On)} of {ground.Count}");
        await Light(r, h.S, l => (l.SunFrom, l.SunHeight, l.Shadows) = (East, Low, true));
        Check.That(Sun(h.Table).ShadowEnabled, "the low sun throws shadows");
        await Light(r, h.S, l => l.Shadows = false);
        Check.That(!Sun(h.Table).ShadowEnabled, "and none once Shadows is off");
    }

    [NodeTest]
    static async Task The_light_page_places_the_sun_by_its_rose_its_bearing_box_its_height_and_its_presets(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var rose = await RoseDragTests.OpenOn<SunRose>(r, h, "Light & surroundings");
        Check.Equal(Lighting.OverheadFrom, rose.From, "the rose starts where the scenario's sun is");

        var east = RoseDragTests.Centre(rose) + new Vector2(40, 3);
        RoseDragTests.Press(east, true);
        RoseDragTests.Press(east, false);
        await r.Frames(2);
        Check.Equal(East, h.S.View.Lighting.SunFrom, "a click just off east snaps to east and is sent");

        var box = Descendants(rose.GetParent()).OfType<SpinBox>().Single();
        Check.Equal(East, box.Value, "the bearing box follows the rose");
        box.Value = 200;
        await r.Frames(2);
        Check.Equal(200.0, h.S.View.Lighting.SunFrom, "a typed bearing is sent");
        Check.Equal(200.0, rose.From, "and turns the rose");

        var pick = Descendants(h.Hud).OfType<OptionButton>().First(o => Enumerable.Range(0, o.ItemCount).Any(i => o.GetItemText(i) == LightPresets.AfternoonSun));
        int afternoon = Enumerable.Range(0, pick.ItemCount).First(i => pick.GetItemText(i) == LightPresets.AfternoonSun);
        pick.Select(afternoon);
        pick.EmitSignal(OptionButton.SignalName.ItemSelected, afternoon);
        await r.Frames(2);
        Check.That(LightPresets.Same(h.S.View.Lighting, LightPresets.Named(LightPresets.AfternoonSun)!), "the afternoon preset puts the sun low in the west");
        Check.Equal(270.0, rose.From, "and the rose shows it");
        Check.That(!rose.Moon, "as a sun");

        int moonlit = Enumerable.Range(0, pick.ItemCount).First(i => pick.GetItemText(i) == LightPresets.MoonlitNight);
        pick.Select(moonlit);
        pick.EmitSignal(OptionButton.SignalName.ItemSelected, moonlit);
        await r.Frames(2);
        Check.That(rose.Moon, "a moonlit night draws a moon on the rose");
    }

    [NodeTest]
    static async Task A_piece_in_hand_marks_the_ground_straight_below_it_wherever_the_sun_stands_and_the_mark_goes_when_it_is_set_down(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 2);
        await Light(r, h.S, l => (l.SunFrom, l.SunHeight) = (East, Low));
        h.Table.SetSelection(new[] { "p1" });
        h.Table.Hand.Start(new Vec2(1, 0));
        h.Table.Hand.Move(new Vec2(4, 3));
        await r.Frames(2);
        var held = h.Table.Pieces.NodeOf(Layers.Umpire, "p1") ?? throw new TestFailure("no node for p1");
        var mark = held.GetChildren().OfType<LandingMark>().SingleOrDefault() ?? throw new TestFailure("the piece in hand has no landing mark");
        var flat = new Vector2(mark.GlobalPosition.X - held.GlobalPosition.X, mark.GlobalPosition.Z - held.GlobalPosition.Z);
        Check.That(flat.Length() < 0.01f, $"the mark is straight below the piece, not where the low sun throws its shadow: {flat}");
        Check.That(mark.GlobalPosition.Y < held.GlobalPosition.Y, "and below it");
        Check.That(mark.Area.HasPoint(-held.Footprint / 2) && mark.Size.X < held.Footprint.X * 1.1f && mark.Size.Z < held.Footprint.Y * 1.1f, $"the piece's own size: {mark.Size} for {held.Footprint}");
        Check.That((mark.Layers & Table.LiveOnlyLayer) != 0, "and kept out of pictures");
        Check.That(h.Table.Pieces.NodeOf(Layers.Umpire, "p2")?.GetChildren().OfType<LandingMark>().Any() == false, "a piece left on the table has none");
        h.Table.Hand.Drop();
        for (int i = 0; i < 300 && h.Table.Hand.Holding; i++) await r.Frame();
        await r.Frames(3);
        var down = h.Table.Pieces.NodeOf(Layers.Umpire, "p1");
        Check.That(!h.Table.Hand.Holding && !GodotObject.IsInstanceValid(mark) && down?.GetChildren().OfType<LandingMark>().Any() == false, "the mark goes once the piece is set down");
    }
}
