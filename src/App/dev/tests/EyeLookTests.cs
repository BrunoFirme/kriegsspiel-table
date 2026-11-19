using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class EyeLookTests
{
    const double UnitsPerMetre = 0.1;
    const float Within = 1e-4f;

    static bool AllOn(EyeLook.Wanted w) => w.Haze && w.Sun && w.Relief && w.Footing && w.Grass;

    static bool AllOff(EyeLook.Wanted w) => !w.Haze && !w.Sun && !w.Relief && !w.Footing && !w.Grass;

    [NodeTest]
    static async Task Walking_brings_the_eye_look_and_God_view_and_the_table_view_never_have_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, arrange: g => g.Calibration = new Calibration { UnitsPerMeter = UnitsPerMetre });
        await r.Frames(3);
        var walk = t.Table.Walk;
        var eye = t.Table.Eye;
        try
        {
            Check.That(AllOff(eye.Shown) && !t.Table.Stage.Hazy && !GroundTiles.Sunlit, "the table view has none of it");
            walk.TakeControl("p1", t.Node("p1").TablePosition());
            t.Table.BodiesArrived([new BodyOf("p1", t.S.Me.PeerId, t.S.Me.Name, AvatarPose.NoColour)]);
            await r.Frames(3);
            Check.That(walk.Immersed && AllOn(eye.Shown), $"through the avatar's eyes it all comes on: {eye.Shown}");
            Check.That(t.Table.Stage.Hazy && GroundTiles.Sunlit, "the haze is on and the map's own shading steps aside");
            Check.That(Math.Abs(eye.Shown.UnitsPerMetre - UnitsPerMetre) < Within, "sized by the map's scale");
            walk.SeeAsGod(true);
            await r.Frames(2);
            Check.That(AllOff(eye.Shown) && !t.Table.Stage.Hazy && !GroundTiles.Sunlit, $"God view has none of it: {eye.Shown}");
            Check.That(eye.Grass is not { Showing: true }, "and no grass");
            walk.SeeAsGod(false);
            await r.Frames(2);
            Check.That(AllOn(eye.Shown), "back through the avatar's eyes it returns");
            walk.Leave();
            await r.Frames(2);
            Check.That(AllOff(eye.Shown) && !t.Table.Stage.Hazy && !GroundTiles.Sunlit, "leaving the avatar takes it all away");
        }
        finally
        {
            walk.Leave();
            t.Table.QueueFree();
            await r.Frames(1);
        }
        Check.That(!GroundTiles.Sunlit, "a table that closes leaves the ground as the map draws it");
    }

    [NodeTest]
    static async Task Each_switch_turns_off_its_own_piece_and_grass_waits_for_a_scale(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        var walk = t.Table.Walk;
        var eye = t.Table.Eye;
        var cfg = t.Table.Settings;
        try
        {
            walk.TakeControl("p1", t.Node("p1").TablePosition());
            t.Table.BodiesArrived([new BodyOf("p1", t.S.Me.PeerId, t.S.Me.Name, AvatarPose.NoColour)]);
            await r.Frames(3);
            Check.That(eye.Shown.Haze && !eye.Shown.Grass, $"an uncalibrated map gets the haze, sized by the avatar, but no grass: {eye.Shown}");
            Check.That(Math.Abs(eye.Shown.UnitsPerMetre - AvatarPose.UnitsPerMetreOf(walk.Height)) < Within, "the avatar's own scale sizes it");
            t.S.View.Calibration.UnitsPerMeter = UnitsPerMetre;
            await r.Frames(2);
            Check.That(AllOn(eye.Shown), "a scale brings the grass");
            cfg.EyeHaze = false;
            await r.Frames(2);
            Check.That(!eye.Shown.Haze && !t.Table.Stage.Hazy && eye.Shown.Sun && eye.Shown.Relief && eye.Shown.Footing && eye.Shown.Grass, "Haze off takes only the haze");
            cfg.EyeSun = false;
            await r.Frames(2);
            Check.That(!eye.Shown.Sun && !GroundTiles.Sunlit && eye.Shown.Relief, "Sunlit ground off gives the map its own shading back");
            cfg.EyeRelief = false;
            cfg.EyeFooting = false;
            cfg.EyeGrass = false;
            await r.Frames(2);
            Check.That(AllOff(eye.Shown), $"with every switch off nothing is left: {eye.Shown}");
        }
        finally
        {
            (cfg.EyeHaze, cfg.EyeSun, cfg.EyeRelief, cfg.EyeFooting, cfg.EyeGrass) = (true, true, true, true, true);
            walk.Leave();
            t.Table.QueueFree();
            await r.Frames(1);
        }
    }

    [NodeTest]
    static async Task Filmic_light_cloud_shadows_and_near_shadows_come_only_through_the_avatars_eyes(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, arrange: g => g.Calibration = new Calibration { UnitsPerMeter = UnitsPerMetre });
        await r.Frames(3);
        var walk = t.Table.Walk;
        var eye = t.Table.Eye;
        var cfg = t.Table.Settings;
        var stage = t.Table.Stage;
        var sun = t.Table.FindChildren("*", nameof(DirectionalLight3D), true, false).OfType<DirectionalLight3D>().First();
        bool Usual() => !stage.Filmed && eye.CloudShade == 0 && sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel4Splits && !ReliefSurface.Shadowless;
        try
        {
            Check.That(Usual(), "the table view keeps its own light and shadows");
            walk.TakeControl("p1", t.Node("p1").TablePosition());
            t.Table.BodiesArrived([new BodyOf("p1", t.S.Me.PeerId, t.S.Me.Name, AvatarPose.NoColour)]);
            await r.Frames(3);
            Check.That(stage.Filmed && eye.CloudShade > 0, $"walking brings filmic light and cloud shadows: {stage.Filmed} {eye.CloudShade}");
            Check.That(sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel2Splits && ReliefSurface.Shadowless, "the sun spends its shadows on what stands, not on the ground's relief");
            Check.That(Math.Abs(sun.DirectionalShadowMaxDistance - TableStage.EyeShadowM * UnitsPerMetre) < Within, $"shadows reach down the street: {sun.DirectionalShadowMaxDistance}");
            cfg.EyeFilmic = false;
            cfg.EyeClouds = false;
            await r.Frames(2);
            Check.That(!stage.Filmed && eye.CloudShade == 0, "each has its own switch");
            cfg.EyeFilmic = true;
            cfg.EyeClouds = true;
            walk.SeeAsGod(true);
            await r.Frames(2);
            Check.That(Usual(), "God view keeps the table's own light and shadows");
            walk.SeeAsGod(false);
            await r.Frames(2);
            walk.Leave();
            await r.Frames(2);
            Check.That(Usual(), "leaving the avatar gives them back");
        }
        finally
        {
            (cfg.EyeFilmic, cfg.EyeClouds) = (true, true);
            walk.Leave();
            t.Table.QueueFree();
            await r.Frames(1);
        }
    }

    [NodeTest]
    static async Task A_clock_night_is_lit_by_a_faint_cool_moon_without_shadows(TestRunner r)
    {
        long At(int hour) => CalendarDef.Gregorian.Seconds(new DateParts(1800, 6, 1, hour, 0, 0)) ?? 0;
        var t = OfflineTable.Make(r, pieces: 1, arrange: g =>
        {
            g.Clock.Seconds = At(12);
            g.Lighting.FromClock = true;
        });
        await r.Frames(3);
        var sun = t.Table.FindChildren("*", nameof(DirectionalLight3D), true, false).OfType<DirectionalLight3D>().First();
        try
        {
            t.Table.Stage.ApplyLighting(t.Table.Stage.LightNow());
            float noon = sun.LightEnergy;
            Check.That(sun.Visible && sun.LightColor.B <= sun.LightColor.R + Within, $"at noon the sun shines: {sun.LightColor}");
            t.S.View.Clock.Seconds = At(23);
            t.Table.Stage.ApplyLighting(t.Table.Stage.LightNow());
            Check.That(sun.Visible && sun.LightEnergy > 0 && sun.LightEnergy < noon * 0.5f, $"at night a faint moon lights the land: {sun.LightEnergy} against {noon}");
            Check.That(sun.LightColor.B > sun.LightColor.R && !sun.ShadowEnabled, $"cool and without shadows: {sun.LightColor} {sun.ShadowEnabled}");
            Check.That(t.Table.Stage.SunShare == 0, "and casts no cloud shadows");
        }
        finally
        {
            t.Table.QueueFree();
            await r.Frames(1);
        }
    }

    [NodeTest]
    static Task The_land_past_the_edge_runs_out_from_open_sides_and_never_over_a_neighbouring_sheet(TestRunner r)
    {
        var owner = new Node3D();
        var across = new Vector3(30, 0, 0);
        var down = new Vector3(0, 0, 20);
        var west = Flat(owner, Vector3.Zero, across, down);
        var east = Flat(owner, across, across, down);
        const float Reach = 900;
        Vector3[] Ring(GroundTiles.Sheet sheet, List<GroundTiles.Sheet> all) =>
            EyeHorizon.Ring(sheet, all, Reach) is { } mesh ? mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array() : [];
        bool Inside(Vector3 p, GroundTiles.Sheet s) => s.UvOf(p) is { } uv && uv.X > 0.01f && uv.Y > 0.01f && uv.X < 0.99f && uv.Y < 0.99f;
        var alone = Ring(west, [west]);
        Check.That(alone.Length > 0 && alone.Max(p => p.X) >= 30 + Reach * 0.99f && alone.Min(p => p.Z) <= -Reach * 0.99f, "a lone sheet's land runs out the full reach on every side");
        Check.That(!alone.Any(p => Inside(p, west)), "and never over the sheet itself");
        var beside = Ring(west, [west, east]);
        Check.That(beside.Length > 0 && !beside.Any(p => Inside(p, east)), "beside a neighbour it lays nothing over that sheet");
        Check.That(beside.Min(p => p.X) <= -Reach * 0.99f, "its open sides still run out");
        owner.Free();
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_full_tile_store_still_repaints_stale_tiles_in_place_and_new_ones_wait_for_room(TestRunner r)
    {
        var owner = new Node3D();
        var sheet = Flat(owner, Vector3.Zero, new Vector3(30, 0, 0), new Vector3(0, 0, 20));
        var resident = new GroundTiles.Tile(3, 1, 1);
        var unplaced = new GroundTiles.Tile(4, 2, 2);
        sheet.Baked[resident] = 0;
        List<(GroundTiles.Sheet Sheet, GroundTiles.Tile Tile, float Size, bool Shown)> due = [(sheet, unplaced, 1, true), (sheet, resident, 1, true)];
        int room = 0;
        var fit = GroundTiles.Fitting(due, 24, ref room);
        Check.That(fit.Count == 1 && fit[0].Tile == resident, "with the store full a new tile waits and a resident one is still repainted");
        room = 1;
        fit = GroundTiles.Fitting(due, 24, ref room);
        Check.That(fit.Count == 2 && room == 0, "free room lets a new tile in");
        room = 5;
        Check.That(GroundTiles.Fitting(due, 1, ref room).Count == 1, "never more than the frame's share");
        owner.Free();
        return Task.CompletedTask;
    }

    static GroundTiles.Sheet Flat(Node3D owner, Vector3 origin, Vector3 across, Vector3 down)
    {
        var pages = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        var texture = ImageTexture.CreateFromImage(pages);
        return new GroundTiles.Sheet
        {
            Key = origin.ToString(), Owner = owner, Bake = new ShaderMaterial(), View = new ShaderMaterial(), Levels = 0, Pages = pages, PagesTex = texture, Atlas = texture,
            World = (uv, y) => origin + across * uv.X + down * uv.Y + Vector3.Up * y, Rise = _ => 0, Low = 0, High = 0, Origin = origin, AlongU = across, AlongV = down,
        };
    }

    [NodeTest]
    static Task Ground_detail_takes_only_the_offered_sizes(TestRunner r)
    {
        int was = GroundTiles.Slots;
        try
        {
            GroundTiles.Resize(32);
            Check.That(GroundTiles.Slots == 32, "an offered size is taken");
            GroundTiles.Resize(33);
            Check.That(GroundTiles.Slots == KTable.Client.Prefs.Settings.GroundSlotsUsual, $"anything else falls back to the usual: {GroundTiles.Slots}");
        }
        finally
        {
            GroundTiles.Resize(was);
        }
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Grass_rings_double_outward_and_keep_the_clump_count_nearly_flat(TestRunner r)
    {
        Check.That(GroundGrass.RingsFor(KTable.Client.Prefs.Settings.EyeGrassReachLeast) == 1, "the shortest reach needs one ring");
        Check.That(GroundGrass.RingsFor(KTable.Client.Prefs.Settings.EyeGrassReachMost) == GroundGrass.MostRings, "the longest uses every ring");
        for (int i = 1; i < GroundGrass.MostRings; i++)
        {
            Check.That(Math.Abs(GroundGrass.InnerM(i) - GroundGrass.OuterM(i - 1)) < Within, $"ring {i} starts where ring {i - 1} ends");
            Check.That(Math.Abs(GroundGrass.SpacingOf(i) - 2 * GroundGrass.SpacingOf(i - 1)) < Within, $"ring {i} spaces its clumps twice as far apart");
        }
        int Clumps(float reach)
        {
            int all = 0;
            for (int i = 0; i < GroundGrass.RingsFor(reach); i++) all += GroundGrass.AcrossOf(i, reach) * GroundGrass.AcrossOf(i, reach);
            return all;
        }
        float shortest = KTable.Client.Prefs.Settings.EyeGrassReachLeast * 2, longest = KTable.Client.Prefs.Settings.EyeGrassReachMost;
        Check.That(Clumps(longest) < Clumps(shortest) * 3, $"four times the reach takes under three times the clumps: {Clumps(longest)} against {Clumps(shortest)}");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_settlements_own_tracks_and_paths_are_beaten_earth_and_country_ones_keep_their_grass(TestRunner r)
    {
        Check.That(BuiltLayer.Beaten(0.4f, Surface.Track) && BuiltLayer.Beaten(0, Surface.TroddenGrass), "a track or path inside a settlement is beaten earth");
        Check.That(!BuiltLayer.Beaten(-1, Surface.Track) && !BuiltLayer.Beaten(-1, Surface.TroddenGrass), "a country track or path keeps its grass");
        Check.That(!BuiltLayer.Beaten(0.4f, Surface.Road) && !BuiltLayer.Beaten(0.4f, Surface.Planks), "other street surfaces stay as their culture lays them");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_sheet_finds_the_map_place_under_the_eye_however_it_lies(TestRunner r)
    {
        var turn = Mathf.DegToRad(35);
        var origin = new Vector3(10, 2, -5);
        var across = new Vector3(Mathf.Cos(turn), 0, Mathf.Sin(turn)) * 30;
        var down = new Vector3(-Mathf.Sin(turn), 0, Mathf.Cos(turn)) * 20;
        var owner = new Node3D();
        var sheet = Flat(owner, origin, across, down);
        var at = sheet.UvOf(origin + across * 0.3f + down * 0.8f + Vector3.Up * 7);
        Check.That(at is { } uv && Math.Abs(uv.X - 0.3f) < Within && Math.Abs(uv.Y - 0.8f) < Within, $"a point on a turned sheet maps back to its place: {at}");
        Check.That(sheet.UvOf(origin - across * 0.1f) is { X: < 0 }, "a point beside it falls outside");
        Check.That(Math.Abs(sheet.SizeUv.X - 30) < Within && Math.Abs(sheet.SizeUv.Y - 20) < Within, "the sheet knows its size across and down");
        owner.Free();
        return Task.CompletedTask;
    }
}
