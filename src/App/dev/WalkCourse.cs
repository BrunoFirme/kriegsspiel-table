using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class WalkCourse(Main main, Table table, string folder, string only)
{
    const string Pack = "test.course";
    const float SpacingMetres = 40f, LeadMetres = 1f, SettleSeconds = 0.6f, SlopeProbeMetres = 2f, TerrainWalkMetres = 30f;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    SceneTree Tree => main.GetTree();
    Session S => main.Session;
    float Metre => AvatarPose.UnitsPerMetre(S.View);

    public async Task Run()
    {
        await Wait(3);
        Directory.CreateDirectory(folder);
        if (!S.View.Packs.Contains(Pack)) main.Net.SharePack(Pack);
        await Wait(2);
        var start = Coords.FromWorld(table.Rig.Target);
        var defs = S.Store.Pack(Pack)?.Pieces.Select(d => d.Id).ToList() ?? [];
        for (int i = 0; i < defs.Count; i++)
            S.Do(new SpawnPiece { Layer = Layers.Umpire, Id = "course-" + i, Def = defs[i], Pos = start + new Vec2(i * SpacingMetres * Metre, 0) });
        await Wait(5);
        S.Me.Role = Role.Player;
        table.Walk.FollowRole();
        await Wait(0.3);
        if (table.Pieces.NodeOf(Layers.Umpire, "course-0") is { } first)
        {
            table.Walk.PlacedAt(first.TablePosition(), 0, null);
            await Wait(4);
        }
        for (int i = 0; i < defs.Count; i++)
        {
            if (table.Pieces.NodeOf(Layers.Umpire, "course-" + i) is not { } node || AvatarSolids.Where(node) is not { } frame) continue;
            string name = defs[i][(Pack.Length + 1)..];
            if (only.Length > 0 && !name.Contains(only, StringComparison.Ordinal)) continue;
            float half = (float)(S.Store.Def(defs[i])!.Size.W / 2) - LeadMetres;
            var a = frame * new Vector3(-half, 0, 0);
            var b = frame * new Vector3(half, 0, 0);
            foreach (bool run in new[] { false, true })
            {
                await Walk($"{name}-{(run ? "run" : "walk")}-there", a, b, run);
                await Walk($"{name}-{(run ? "run" : "walk")}-back", b, a, run);
            }
        }
        if (only.Length == 0 || only == "terrain") await Terrain(start);
        GD.Print("[course] done");
        Tree.Quit();
    }

    async Task Terrain(Vec2 near)
    {
        var (spot, along, slope) = Steepest(near);
        GD.Print(string.Create(Inv, $"[course] steepest ground near the start: {slope:0.0} degrees"));
        double reach = TerrainWalkMetres / 2 * Metre;
        var a = Coords.ToWorld(spot - new Vec2(along.X * reach, along.Z * reach), 0);
        var b = Coords.ToWorld(spot + new Vec2(along.X * reach, along.Z * reach), 0);
        foreach (bool run in new[] { false, true })
        {
            await Walk($"terrain-{slope:0}-{(run ? "run" : "walk")}-up", a, b, run);
            await Walk($"terrain-{slope:0}-{(run ? "run" : "walk")}-down", b, a, run);
        }
    }

    (Vec2 Spot, Vec2 Along, double Degrees) Steepest(Vec2 near)
    {
        double step = 10 * Metre, probe = SlopeProbeMetres * Metre, best = -1;
        var found = (near, new Vec2(1, 0), 0.0);
        for (int j = -60; j <= 60; j++)
            for (int i = -60; i <= 60; i++)
            {
                var p = near + new Vec2(i * step, j * step);
                double dx = (S.Ground.At(p + new Vec2(probe, 0)) - S.Ground.At(p - new Vec2(probe, 0))) / (2 * probe);
                double dz = (S.Ground.At(p + new Vec2(0, probe)) - S.Ground.At(p - new Vec2(0, probe))) / (2 * probe);
                double grade = Math.Sqrt(dx * dx + dz * dz), degrees = Math.Atan(grade) * 180 / Math.PI;
                if (degrees > 40 || degrees <= best) continue;
                best = degrees;
                found = (p, new Vec2(dx / Math.Max(grade, 1e-9), dz / Math.Max(grade, 1e-9)), degrees);
            }
        return found;
    }

    async Task Walk(string name, Vector3 from, Vector3 to, bool run)
    {
        var walk = table.Walk;
        float yaw = Mathf.Atan2(to.X - from.X, to.Z - from.Z);
        walk.PlacedAt(from, yaw, null);
        walk.Steer = () => new Steering(yaw, -0.05f, Vector2.Zero, false);
        await Wait(SettleSeconds);
        walk.PlacedAt(from, yaw, null);
        await Wait(SettleSeconds);
        var rows = new StringBuilder("t,x,y,z,eye,grounded,ground,shown\n");
        float expected = (run ? Walker.RunHeights : Walker.WalkHeights) * walk.Height;
        double limit = from.DistanceTo(to) / expected * 2.5 + 3;
        walk.Steer = () => new Steering(yaw, -0.05f, new Vector2(0, 1), run);
        ulong began = Time.GetTicksMsec();
        bool reached = false;
        void Sample()
        {
            var feet = walk.Feet;
            var eye = WorldShift.ToTable(table.Rig.Cam.GlobalPosition);
            float m = Metre;
            rows.Append(string.Create(Inv, $"{(Time.GetTicksMsec() - began) / 1000.0:0.0000},{feet.X / m:0.0000},{feet.Y / m:0.0000},{feet.Z / m:0.0000},{eye.Y / m:0.0000},{(walk.Grounded ? 1 : 0)},{S.Ground.At(Coords.FromWorld(feet)) / m:0.0000},{walk.Pose.At.Y / m:0.0000}\n"));
            reached |= new Vector2(feet.X - to.X, feet.Z - to.Z).Length() < walk.Height * 0.3f;
        }
        Tree.ProcessFrame += Sample;
        while (!reached && (Time.GetTicksMsec() - began) / 1000.0 < limit) await main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        Tree.ProcessFrame -= Sample;
        walk.Steer = () => new Steering(yaw, -0.05f, Vector2.Zero, false);
        File.WriteAllText(Path.Combine(folder, name + ".csv"), rows.ToString());
        if (!reached)
        {
            walk.ThirdPerson = true;
            await Wait(0.5);
            main.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(folder, name + ".png"));
            walk.ThirdPerson = false;
        }
        GD.Print($"[course] {name} {(reached ? "reached" : "stopped short")} in {(Time.GetTicksMsec() - began) / 1000.0:0.0} s");
    }

    async Task Wait(double s) => await main.ToSignal(Tree.CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
