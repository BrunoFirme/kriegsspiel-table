using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class WalkFeelTests
{
    const float Rise = 2f, Plateau = 2f, Apron = 4f, Depth = 2f;
    const float Riser = 0.18f, Tread = 0.3f, Landing = 1.2f;
    const int Steps = 8;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    delegate float Profile(float x);

    static (float Length, Profile Height, List<float> Breaks) Hill(float degrees)
    {
        float run = Rise / Mathf.Tan(Mathf.DegToRad(degrees)), length = 2 * run + Plateau, half = length / 2;
        float H(float x)
        {
            x += half;
            if (x <= 0 || x >= length) return 0;
            return x < run ? x / run * Rise : x <= run + Plateau ? Rise : (length - x) / run * Rise;
        }
        return (length, H, [-half, -half + run, -half + run + Plateau, half]);
    }

    static (float Length, Profile Height, List<float> Breaks) Stairs()
    {
        float up = Steps * Tread, length = 2 * up + Landing, half = length / 2;
        float H(float x)
        {
            x += half;
            if (x <= 0 || x >= length) return 0;
            if (x < up) return ((int)(x / Tread) + 1) * Riser;
            if (x <= up + Landing) return Steps * Riser;
            float down = x - up - Landing;
            return down < up ? (Steps - 1 - (int)(down / Tread)) * Riser : 0;
        }
        var breaks = new List<float> { -half, half };
        for (int i = 0; i <= Steps; i++)
            foreach (var at in new[] { -half + i * Tread, -half + up + Landing + i * Tread })
                breaks.AddRange([at - 0.001f, at + 0.001f]);
        return (length, H, breaks.Where(x => x >= -half && x <= half).ToList());
    }

    static string Model((float Length, Profile Height, List<float> Breaks) shape)
    {
        float core = shape.Length / 2, half = core + Apron;
        var xs = shape.Breaks.Concat([-half, -core, core, half]).Distinct().OrderBy(x => x).ToList();
        float H(float x) => x > -core && x < core ? shape.Height(x) : 0;
        var lines = new List<string>();
        foreach (var x in xs)
            foreach (var z in new[] { -half, half })
                lines.Add(string.Create(Inv, $"v {x:0.####} {H(x):0.####} {z:0.####}"));
        foreach (var x in xs)
            foreach (var z in new[] { -half, half })
                lines.Add(string.Create(Inv, $"v {x:0.####} {-Depth:0.####} {z:0.####}"));
        int n = xs.Count, top = 1, low = 1 + 2 * n;
        for (int i = 0; i + 1 < n; i++)
        {
            int a = top + 2 * i, b = top + 2 * (i + 1);
            lines.Add($"f {a} {a + 1} {b + 1}");
            lines.Add($"f {a} {b + 1} {b}");
            lines.Add($"f {a} {b} {low + 2 * (i + 1)}");
            lines.Add($"f {a} {low + 2 * (i + 1)} {low + 2 * i}");
            lines.Add($"f {a + 1} {low + 2 * i + 1} {low + 2 * (i + 1) + 1}");
            lines.Add($"f {a + 1} {low + 2 * (i + 1) + 1} {b + 1}");
        }
        return string.Join("\n", lines) + "\n";
    }

    static void Course(PackageStore store, string name, (float Length, Profile Height, List<float> Breaks) shape)
    {
        var dir = Path.Combine(store.Root, "feel", "assets");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".obj"), Model(shape));
        var pack = new PackManifest { Id = "feel", Name = "Feel" };
        pack.Assets[name] = new AssetEntry { File = $"assets/{name}.obj", Kind = "mesh" };
        float side = shape.Length + 2 * Apron;
        store.RegisterDef(pack, new PieceDef { Id = name, Name = name, Kind = "model", Mesh = name, Size = new Size2(side, side), Thickness = Rise + Depth, Shape = "box" });
    }

    static OfflineTable Table(TestRunner r, string name, (float Length, Profile Height, List<float> Breaks) shape) =>
        OfflineTable.Make(r, pieces: 0, prepare: store => Course(store, name, shape), arrange: g =>
            g.Layers[Layers.Umpire].Add(new PieceInstance { Id = "course", Def = name, Pos = new Vec2(0, 0), Scale = AvatarPose.UnitsPerMetre(g) }));

    sealed record Walked(float Seconds, float Metres, float Air, float Climbed, bool Reached);

    static async Task<Walked> Cross(TestRunner r, OfflineTable t, float length, bool run, bool back, double most)
    {
        var walk = t.Table.Walk;
        await r.Frames(3);
        var node = t.Table.Pieces.NodeOf(Layers.Umpire, "course")!;
        var frame = AvatarSolids.Where(node)!.Value;
        float half = length / 2 + Apron - 1, metre = AvatarPose.UnitsPerMetre(t.S.View);
        Vector3 from = frame * new Vector3(back ? half : -half, 0, 0), to = frame * new Vector3(back ? -half : half, 0, 0);
        float yaw = Mathf.Atan2(to.X - from.X, to.Z - from.Z);
        if (!walk.Walking) walk.Enter(from);
        walk.MoveTo(from);
        walk.Steer = () => new Steering(yaw, 0, Vector2.Zero, false);
        await r.Seconds(1.2);
        walk.Steer = () => new Steering(yaw, 0, new Vector2(0, 1), run);
        float seconds = 0, air = 0, frames = 0, climbed = 0, low = walk.Feet.Y;
        var start = walk.Feet;
        bool reached = false;
        while (seconds < most && !reached)
        {
            await r.Frames(1);
            seconds += (float)t.Table.GetProcessDeltaTime();
            frames++;
            air += walk.Grounded ? 0 : 1;
            climbed = Mathf.Max(climbed, (walk.Feet.Y - low) / metre);
            reached = new Vector2(walk.Feet.X - to.X, walk.Feet.Z - to.Z).Length() < walk.Height * 0.3f;
        }
        walk.Steer = null;
        return new Walked(seconds, new Vector2(walk.Feet.X - start.X, walk.Feet.Z - start.Z).Length() / metre, air / Mathf.Max(frames, 1), climbed, reached);
    }

    [NodeTest]
    static async Task Gentle_ground_keeps_the_pace_and_only_a_real_climb_slows_a_walker(TestRunner r)
    {
        await r.Frames(1);
        float Grade(float degrees) => Mathf.Tan(Mathf.DegToRad(degrees));
        Check.That(Mathf.IsEqualApprox(Walker.HillFactor(0), 1, 0.001f), "flat ground keeps the pace");
        Check.That(Mathf.IsEqualApprox(Walker.HillFactor(Grade(12)), 1, 0.001f), "a gentle rise keeps the pace");
        Check.That(Mathf.IsEqualApprox(Walker.HillFactor(-Grade(25)), 1, 0.001f), "going down keeps the pace");
        Check.That(Walker.HillFactor(Grade(30)) is > 0.6f and < 0.85f, $"30 degrees up: {Walker.HillFactor(Grade(30)):0.00}");
        Check.That(Walker.HillFactor(Grade(44)) < Walker.HillFactor(Grade(30)), "a steeper climb is slower still");
    }

    [NodeTest]
    static async Task A_walker_crosses_a_thirty_degree_hill_on_the_ground_slower_uphill_and_never_hops_coming_down(TestRunner r)
    {
        var shape = Hill(30);
        var t = Table(r, "hill30", shape);
        try
        {
            var walked = await Cross(r, t, shape.Length, run: true, back: false, most: 12);
            float pace = walked.Metres / walked.Seconds;
            Check.That(walked.Reached, $"they get over the hill: {walked}");
            Check.That(walked.Climbed > Rise * 0.9f, $"they climb it rather than stopping at its foot: {walked.Climbed:0.00} m");
            Check.That(pace < 5f * 0.97f, $"the climb takes longer than the flat would: {pace:0.00} m/s");
            Check.That(walked.Air < 0.03f, $"they stay on the ground: {walked.Air:P0} of frames off it");
        }
        finally
        {
            t.Table.Walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_slope_too_steep_to_climb_sends_a_walker_gently_back_down(TestRunner r)
    {
        var shape = Hill(55);
        var t = Table(r, "hill55", shape);
        try
        {
            var walked = await Cross(r, t, shape.Length, run: false, back: false, most: 5);
            Check.That(!walked.Reached && walked.Climbed < Rise * 0.6f, $"they cannot walk up it: {walked}");
        }
        finally
        {
            t.Table.Walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_walker_runs_up_and_down_stairs_without_stopping_or_hopping(TestRunner r)
    {
        var shape = Stairs();
        var t = Table(r, "stairs", shape);
        try
        {
            var walked = await Cross(r, t, shape.Length, run: true, back: false, most: 10);
            Check.That(walked.Reached, $"they get over the stairs: {walked}");
            Check.That(walked.Climbed > Steps * Riser * 0.9f, $"up the stairs, not around them: {walked.Climbed:0.00} m");
            Check.That(walked.Air < 0.05f, $"and down them on their feet: {walked.Air:P0} of frames off the ground");
        }
        finally
        {
            t.Table.Walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task A_walker_stays_put_in_a_piece_while_it_is_carried_and_after_it_is_set_down(TestRunner r)
    {
        const int Frames = 60;
        var t = Table(r, "carried", Hill(10));
        try
        {
            var walk = t.Table.Walk;
            t.Table.Suspension.Off = true;
            await r.Frames(3);
            var node = t.Table.Pieces.NodeOf(Layers.Umpire, "course")!;
            float metre = AvatarPose.UnitsPerMetre(t.S.View);
            var top = AvatarSolids.Where(node)!.Value * new Vector3(0, Rise, 0);
            walk.Enter(top);
            await r.Frames(2);
            walk.MoveTo(top);
            walk.Steer = () => new Steering(0, 0, Vector2.Zero, false);
            await r.Seconds(1.2);
            Vector3 Inside() => AvatarSolids.Where(node)!.Value.AffineInverse() * walk.Feet;
            var before = Inside();
            var p = node.Piece;
            walk.CarriedElsewhere = n => n == node;
            node.Held = true;
            float drift = 0;
            for (int i = 0; i <= Frames; i++)
            {
                float k = (float)i / Frames;
                node.ShowPose(node.HandPose(new Vec2(p.Pos.X + k * 3 * metre, p.Pos.Z), p.RotDeg + k * 30, (float)p.Y + k * 6 * metre));
                for (int tick = 0; tick < 2; tick++) await r.ToSignal(r.GetTree(), SceneTree.SignalName.PhysicsFrame);
                drift = Mathf.Max(drift, (Inside() - before).Length());
            }
            Check.That(drift < 0.05f, $"lifted 6 m, carried 3 m and turned, they stay where they stood: {drift:0.000} m off");
            walk.CarriedElsewhere = _ => false;
            node.Held = false;
            node.Refresh(node.Piece);
            await r.Seconds(1);
            Check.That((Inside() - before).Length() < 0.1f, $"set down again, they still stand there: {Inside() - before}");
        }
        finally
        {
            t.Table.Walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task Sneaking_and_marching_set_the_pace_and_the_look_until_the_umpire_locks_the_walk(TestRunner r)
    {
        var t = Table(r, "paces", Hill(10));
        try
        {
            var walk = t.Table.Walk;
            float metre = AvatarPose.UnitsPerMetre(t.S.View);
            t.S.Me.Role = Role.Player;
            walk.FollowRole();
            await r.Frames(2);
            walk.PlacedAt(new Vector3(-30 * metre, 0, -30 * metre), 0, null);
            await r.Frames(3);
            foreach (var (stance, run, want) in new[] { (RigMotion.Sneak, true, 0.75f), (RigMotion.March, false, 1.4f), (RigMotion.March, true, 2.3f) })
            {
                walk.Stand(stance);
                walk.Steer = () => new Steering(0, 0, new Vector2(0, 1), run);
                await r.Seconds(0.6);
                var from = walk.Feet;
                await r.Seconds(1);
                float pace = new Vector2(walk.Feet.X - from.X, walk.Feet.Z - from.Z).Length() / metre;
                Check.That(Mathf.Abs(pace - want) < want * 0.15f, $"{stance}{(run ? " with Shift" : "")} at {pace:0.00} m/s, wanted {want}");
                Check.That(walk.Pose.Gait == stance, $"everyone is told the walk: {walk.Pose.Gait}");
            }
            t.S.View.Everyone = new PlayerRights { Walk = false };
            await r.Frames(3);
            Check.That(walk.Stance == null && walk.Pose.Gait == null, "an umpire who locks the walk puts the player back on their usual one");
            walk.Stand(RigMotion.Sneak);
            Check.That(walk.Stance == null, "and a locked player cannot change it");
        }
        finally
        {
            t.Table.Walk.Leave();
            t.Table.QueueFree();
        }
    }

    [NodeTest]
    static async Task Walking_and_running_keep_two_steady_speeds(TestRunner r)
    {
        var shape = Hill(10);
        var t = Table(r, "flat", shape);
        try
        {
            var walk = t.Table.Walk;
            float metre = AvatarPose.UnitsPerMetre(t.S.View);
            walk.Enter(new Vector3(-30 * metre, 0, -30 * metre));
            foreach (var (run, want) in new[] { (false, 1.5f), (true, 5f) })
            {
                walk.Steer = () => new Steering(0, 0, new Vector2(0, 1), run);
                await r.Seconds(0.8);
                var from = walk.Feet;
                await r.Seconds(1);
                float pace = new Vector2(walk.Feet.X - from.X, walk.Feet.Z - from.Z).Length() / metre;
                Check.That(Mathf.Abs(pace - want) < want * 0.12f, $"{(run ? "running" : "walking")} at {pace:0.00} m/s, wanted {want}");
            }
        }
        finally
        {
            t.Table.Walk.Leave();
            t.Table.QueueFree();
        }
    }
}
