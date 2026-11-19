using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class HearthTour(Main main, Table table, string folder)
{
    const float Hall = 0.72f, Gallery = Hall + 3.6f, Attic = Hall + 6.6f, Pit = Hall - 0.54f, Dais = Hall + 0.54f, Raised = Hall + 0.36f;
    const float MidX = 12f, MidZ = 8f;

    static readonly (string Name, float X, float Y, float Z, float LookX, float LookZ, float Pitch)[] Stops =
    {
        ("01-threshold", 12f, Raised, 1.0f, 12f, 9f, -0.12f),
        ("02-pit", 12.0f, Pit, 4.0f, 11.5f, 9f, 0.0f),
        ("03-to-dais", 15.3f, Hall, 5.85f, 22f, 5.85f, -0.05f),
        ("04-narrator", 22.3f, Dais, 5.85f, 12f, 5.85f, -0.1f),
        ("05-bar", 6.8f, Hall, 7.6f, 3.5f, 11f, -0.05f),
        ("06-stair", 1.3f, Hall, 2.2f, 1.3f, 9f, 0.25f),
        ("07-gallery", 9.5f, Gallery, 10.5f, 15f, 4f, -0.35f),
        ("08-pulpit", 20.3f, Gallery, 8.9f, 20.1f, 4.5f, -0.65f),
        ("09-crows-nest", 20.4f, Attic, 10.2f, 13f, 5f, -0.45f),
        ("10-nest", 6.0f, Attic, 12.6f, 1.0f, 13.6f, -0.15f),
        ("11-art-loft", 18.6f, Attic, 12.5f, 22.5f, 13.8f, -0.15f),
        ("12-green-room", 8.9f, Gallery, 12.6f, 8.0f, 15f, -0.15f),
        ("13-solar", 12.4f, Raised, 12.6f, 14.5f, 15f, -0.1f),
    };

    SceneTree Tree => main.GetTree();

    public async Task Run()
    {
        Directory.CreateDirectory(folder);
        await Wait(3);
        var s = main.Session;
        var middle = Coords.FromWorld(table.Rig.Target);
        s.Do(new SpawnPiece { Layer = Layers.Umpire, Id = "hearth", Def = FirmeEffects.PackId + ".tavern", Pos = middle });
        await Wait(8);
        if (table.Pieces.NodeOf(Layers.Umpire, "hearth") is not { } inn || AvatarSolids.Where(inn) is not { } frame)
        {
            GD.Print("[hearth] no tavern");
            Tree.Quit();
            return;
        }
        float metre = AvatarPose.UnitsPerMetre(s.View);
        var centre = frame * new Vector3(0, 4f, 0);
        foreach (var (px, pz) in new[] { (12f, 5.85f), (12f, 1f), (1f, 1f), (23f, 1f), (1f, 15f), (23f, 15f), (20f, 6f), (12f, -3f) })
        {
            var w = frame * new Vector3(px - MidX, 0, MidZ - pz);
            GD.Print($"[hearth] ground at {px}, {pz}: {(s.Ground.At(new Vec2(w.X, w.Z)) - w.Y) / metre:0.00} m over the piece's base");
        }
        foreach (var (name, yaw, pitch, wide) in new[] { ("00-front", 0.25f, -0.35f, 48f), ("00-back", 2.9f, -0.5f, 48f) })
        {
            table.Rig.Show(new CameraRig.View(centre, wide * metre, Yaw(frame, new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw))), pitch, false));
            await Settled();
            Shot(name);
        }
        table.Cutaway.Set(WallView.Cutaway);
        foreach (var (name, storey) in new[] { ("00-cut-ground", 0), ("00-cut-gallery", 1), ("00-cut-attic", 2) })
        {
            table.Cutaway.Step(storey - table.Cutaway.Storey);
            table.Rig.Show(new CameraRig.View(centre, 40 * metre, Yaw(frame, new Vector3(0.3f, 0, -1)), -0.9f, false));
            await Settled();
            Shot(name);
        }
        table.Cutaway.Set(WallView.Up);
        s.Me.Role = Role.Player;
        table.Walk.FollowRole();
        table.Walk.ThirdPerson = false;
        await Wait(0.5);
        s.Do(new SetState { Layer = Layers.Umpire, Id = "hearth", State = 1 });
        s.Do(new SetLighting { Lighting = new Lighting { SunEnergy = 0.9, Ambient = 0.6, Warmth = 0.3, Shadows = true } });
        await Route(frame, "front-door-pit-dais", (12f, 0f, -4.5f), new[] { (12f, -1.6f), (12f, 1.0f), (12f, 2.6f), (12f, 4.3f), (13.3f, 4.7f), (14f, 5.85f), (15.2f, 5.85f), (16.3f, 5.85f), (17.5f, 5.85f) });
        await Route(frame, "wall-stair", (1.3f, Hall, 2.6f), new[] { (1.3f, 5.3f), (1.3f, 6.2f), (1.3f, 9.5f), (3f, 10.5f) });
        var spiral = new List<(float, float)> { (21.3f, 10.6f) };
        for (int a = 192; a <= 540; a += 12) spiral.Add((22.1f + 0.7f * Mathf.Cos(Mathf.DegToRad(a)), 10.6f + 0.7f * Mathf.Sin(Mathf.DegToRad(a))));
        spiral.Add((20.5f, 10.1f));
        await Route(frame, "spiral", (20.4f, Gallery, 10.6f), spiral.ToArray());
        if (System.Environment.GetEnvironmentVariable("HEARTH_ROUTES_ONLY") == "1")
        {
            await Route(frame, "stair-down", (3f, Gallery, 10.5f), new[] { (1.3f, 9.8f), (1.3f, 9.0f), (1.3f, 6.2f), (1.3f, 2.8f) });
            await Route(frame, "pit-out-south", (12f, Pit, 4.3f), new[] { (12f, 3.6f), (12f, 2.6f) });
            await Route(frame, "pit-out-east", (13.6f, Pit, 5.85f), new[] { (14.5f, 5.85f), (15.4f, 5.85f) });
            await Route(frame, "pit-in-east", (15.4f, Hall, 5.85f), new[] { (14.5f, 5.85f), (13.6f, 5.85f) });
            await Route(frame, "pit-in-north", (12f, Hall, 9.0f), new[] { (12f, 8.2f), (12f, 7.4f) });
            var down = new List<(float, float)> { (21.3f, 10.6f) };
            for (int a = 528; a >= 180; a -= 12) down.Add((22.1f + 0.7f * Mathf.Cos(Mathf.DegToRad(a)), 10.6f + 0.7f * Mathf.Sin(Mathf.DegToRad(a))));
            down.Add((20.4f, 10.6f));
            await Route(frame, "spiral-down", (20.5f, Attic, 10.1f), down.ToArray());
            Tree.Quit();
            return;
        }
        s.Do(new SetLighting { Lighting = new Lighting { SunEnergy = 0.9, Ambient = 0.6, Warmth = 0.3, Shadows = true } });
        foreach (var stop in Stops) await Visit(frame, stop, "");
        s.Do(new SetLighting { Lighting = new Lighting { SunEnergy = 0.04, Ambient = 0.1, Warmth = 0.2, Shadows = true } });
        foreach (var stop in Stops)
            if (stop.Name is "01-threshold" or "02-pit" or "04-narrator" or "05-bar" or "07-gallery" or "10-nest") await Visit(frame, stop, "-night");
        var outside = frame * new Vector3(3f, 0.05f, MidZ + 9f);
        float back = Yaw(frame, new Vector3(-3f, 0, -9f));
        table.Walk.PlacedAt(outside, back, null);
        table.Walk.Steer = () => new Steering(back, 0.12f, Vector2.Zero, false);
        table.Walk.ThirdPerson = true;
        await Settled();
        Shot("14-front-night");
        GD.Print("[hearth] done");
        Tree.Quit();
    }

    async Task Visit(Transform3D frame, (string Name, float X, float Y, float Z, float LookX, float LookZ, float Pitch) stop, string suffix)
    {
        var feet = frame * new Vector3(stop.X - MidX, stop.Y + 0.05f, MidZ - stop.Z);
        float yaw = Yaw(frame, new Vector3(stop.LookX - stop.X, 0, stop.Z - stop.LookZ));
        table.Walk.PlacedAt(feet, yaw, null);
        table.Walk.Steer = () => new Steering(yaw, stop.Pitch, Vector2.Zero, false);
        await Settled();
        var local = frame.AffineInverse() * table.Walk.Feet;
        GD.Print($"[hearth] {stop.Name}{suffix} feet at {local.X + MidX:0.00}, {local.Y:0.00}, {MidZ - local.Z:0.00} wanted {stop.X:0.00}, {stop.Y:0.00}, {stop.Z:0.00}");
        Shot(stop.Name + suffix);
    }

    async Task Route(Transform3D frame, string name, (float X, float Y, float Z) start, (float X, float Z)[] points)
    {
        float metre = AvatarPose.UnitsPerMetre(main.Session.View);
        table.Walk.PlacedAt(frame * new Vector3(start.X - MidX, start.Y + 0.05f, MidZ - start.Z), 0, null);
        await Wait(2);
        int reached = 0;
        foreach (var p in points)
        {
            var goal = frame * new Vector3(p.X - MidX, 0, MidZ - p.Z);
            table.Walk.Steer = () =>
            {
                var to = goal - table.Walk.Feet;
                return new Steering(Mathf.Atan2(to.X, to.Z), -0.05f, new Vector2(0, 0.6f), false);
            };
            ulong until = Time.GetTicksMsec() + 8000;
            while (Time.GetTicksMsec() < until && new Vector2(goal.X - table.Walk.Feet.X, goal.Z - table.Walk.Feet.Z).Length() > 0.2f * metre)
                await main.ToSignal(Tree, SceneTree.SignalName.PhysicsFrame);
            var local = frame.AffineInverse() * table.Walk.Feet;
            bool near = new Vector2(goal.X - table.Walk.Feet.X, goal.Z - table.Walk.Feet.Z).Length() <= 0.2f * metre;
            GD.Print($"[hearth] route {name} {(near ? "reached" : "STUCK at")} {p.X:0.00}, {p.Z:0.00}: feet {local.X + MidX:0.00}, {local.Y:0.00}, {MidZ - local.Z:0.00}");
            if (!near)
            {
                if (table.Pieces.NodeOf(Layers.Umpire, "hearth") is { } inn)
                    for (int i = 0; i < 6; i++)
                    {
                        var f = AvatarSolids.Where(inn);
                        GD.Print($"[hearth] frame {f?.Origin.X:0.000000} {f?.Origin.Y:0.000000} {f?.Origin.Z:0.000000} x {f?.Basis.X.X:0.000000} riding {table.Walk.Riding} grounded {table.Walk.Grounded} carried {table.Walk.CarriedElsewhere(inn)} hand {table.Hand.Has(inn)} others {table.Motion.CarriedByOthers(inn)} node {inn.GetInstanceId()} pos {inn.Position.X:0.000000} {inn.Position.Y:0.000000} {inn.Position.Z:0.000000}");
                        await main.ToSignal(Tree, SceneTree.SignalName.PhysicsFrame);
                    }
                Shot("stuck-" + name);
                table.Walk.ThirdPerson = true;
                await Wait(1.5);
                Shot("stuck-" + name + "-behind");
                table.Walk.ThirdPerson = false;
                break;
            }
            reached++;
        }
        table.Walk.Steer = () => new Steering(table.Rig.Yaw, -0.05f, Vector2.Zero, false);
        GD.Print($"[hearth] route {name} {reached} of {points.Length}");
    }

    static float Yaw(Transform3D frame, Vector3 local)
    {
        var d = frame.Basis * local;
        return Mathf.Atan2(d.X, d.Z);
    }

    async Task Settled()
    {
        await Wait(2.5);
        await GroundTiles.WhenSettled(main);
        await Wait(1);
    }

    void Shot(string name)
    {
        var path = Path.Combine(folder, name + ".png");
        main.GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[hearth] shot {path}");
    }

    async Task Wait(double seconds) => await main.ToSignal(Tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
