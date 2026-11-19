using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class FlickTests
{
    const string U = Layers.Umpire;
    const double FlickSeconds = 0.15, LongestWait = 8, Ahead = 6;
    const float Close = 10;

    static async Task<(Vec2 from, Vec2 released)> Flick(TestRunner r, OfflineTable t, string id, Vec2 release, double effort)
    {
        t.Table.Rig.Frame(Coords.ToWorld(release), Close);
        double speed = effort * t.Table.Rig.ViewSpanAt(Coords.ToWorld(release));
        var from = new Vec2(release.X - speed * FlickSeconds, release.Z);
        t.S.Do(new MovePiece { Layer = U, Id = id, Pos = from, RotDeg = 0, Stack = 0 });
        await r.Frames(3);
        t.Table.Selection.Set(new[] { id });
        t.Table.Hand.Start(from);
        var at = from;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < FlickSeconds)
        {
            await r.Frame();
            at = new Vec2(from.X + speed * watch.Elapsed.TotalSeconds, from.Z);
            t.Table.Hand.Move(at);
        }
        t.Table.Hand.Drop();
        return (from, at);
    }

    static async Task Committed(TestRunner r, OfflineTable t, string id, Vec2 before)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < LongestWait && (t.S.View.Find(U, id)!.Pos - before).Length < 1e-6) await r.Frame();
        await r.Frames(3);
    }

    static async Task<double> Thrown(TestRunner r, OfflineTable t, string id, Vec2 release, double effort)
    {
        var (from, released) = await Flick(r, t, id, release, effort);
        await Committed(r, t, id, from);
        return (t.S.View.Find(U, id)!.Pos - released).Length;
    }

    [NodeTest]
    static async Task A_harder_flick_throws_a_block_farther(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        double[] efforts = { 1.5, 3, 5 };
        var reached = new double[efforts.Length];
        for (int i = 0; i < efforts.Length; i++) reached[i] = await Thrown(r, t, "p1", new Vec2(0, 20 * i), efforts[i]);
        GD.Print($"[flick] block went {string.Join(", ", reached.Select(d => d.ToString("0.0")))} past the release");
        Check.That(reached[0] > 0.5 && reached[1] > reached[0] && reached[2] > reached[1], $"each harder flick goes farther: {string.Join(", ", reached)}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_hard_flick_throws_a_die_farther_than_a_gentle_one(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { "std.dice" }, prepare: StandardPacks.Ensure);
        await r.Frame();
        t.S.Do(new SpawnPiece { Layer = U, Def = "std.dice.d6", Id = "d1", Pos = new Vec2(0, 0) });
        await r.Frames(5);
        async Task<double> Cast(Vec2 release, double effort)
        {
            var (_, released) = await Flick(r, t, "d1", release, effort);
            Check.That(t.Node("d1").Rolling, $"let go at {effort} screens a second, it rolls");
            for (int i = 0; i < 1200 && t.Node("d1").Rolling; i++) await r.Frame();
            await r.Frames(3);
            return (t.S.View.Find(U, "d1")!.Pos - released).Length;
        }
        double gentle = await Cast(new Vec2(0, 0), 0.6), hard = await Cast(new Vec2(0, 30), 3.5);
        GD.Print($"[flick] die went {gentle:0.0} gently, {hard:0.0} hard");
        Check.That(hard > 2 * gentle, $"the hard flick goes much farther: {gentle} then {hard}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    static void Weights(PackageStore store)
    {
        var pack = new PackManifest { Id = "test", Name = "Test" };
        store.RegisterDef(pack, new PieceDef { Id = "heavy", Name = "Heavy", Kind = "flat", Size = new Size2(2, 2), Thickness = 0.6 });
        store.RegisterDef(pack, new PieceDef { Id = "light", Name = "Light", Kind = "flat", Size = new Size2(0.6, 0.6), Thickness = 0.3 });
    }

    static async Task<double> Shoved(TestRunner r, string thrown, string struck)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: Weights, arrange: g => g.Layers[U] = new()
        {
            new PieceInstance { Id = "thrown", Def = thrown, Pos = new Vec2(-40, 0) },
            new PieceInstance { Id = "struck", Def = struck, Pos = new Vec2(Ahead, 0) },
        });
        await r.Frames(3);
        float usual = t.Table.Settings.LiftHeight;
        t.Table.Settings.LiftHeight = 0;
        var (from, _) = await Flick(r, t, "thrown", new Vec2(0, 0), 2.5);
        await Committed(r, t, "thrown", from);
        t.Table.Settings.LiftHeight = usual;
        double moved = (t.S.View.Find(U, "struck")!.Pos - new Vec2(Ahead, 0)).Length;
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        return moved;
    }

    [NodeTest]
    static async Task A_heavy_block_shoves_a_light_one_farther_than_the_light_one_shoves_it(TestRunner r)
    {
        double light = await Shoved(r, "heavy", "light"), heavy = await Shoved(r, "light", "heavy");
        GD.Print($"[flick] the heavy block shoved the light one {light:0.00}, the light one shoved the heavy {heavy:0.00}");
        Check.That(light > 0.25 && light > 3 * heavy, $"momentum counts: heavy into light {light}, light into heavy {heavy}");
    }
}
