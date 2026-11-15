using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class WalkDemos(Main main, Dictionary<string, string> args, Table table)
{
    const float WalkArrivedHeights = 0.12f, WalkLookDown = -0.08f, CircleTurn = 1.3f, CirclePace = 0.4f;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    Session Session => main.Session;
    Net Net => main.Net;
    SceneTree Tree => main.GetTree();

    public void Run()
    {
        Staged();
        Watched();
        FrameProbe();
        if (!args.TryGetValue("walk", out var where) && !args.ContainsKey("walk-path")) return;
        Walk(where);
        RideProbe();
        Tell();
    }

    void Staged()
    {
        if (args.ContainsKey("ground-scan")) Tree.CreateTimer(3).Timeout += ScanGround;
        if (args.TryGetValue("add-pack", out var pack)) Tree.CreateTimer(1.5).Timeout += () => { if (!Session.View.Packs.Contains(pack)) Net.SharePack(pack); };
        if (args.TryGetValue("place", out var placed)) Tree.CreateTimer(2.5).Timeout += () => Place(placed);
        if (args.TryGetValue("carry", out var carry)) _ = PickUp(Numbers(carry));
        if (args.ContainsKey("no-suspension")) table.Suspension.Off = true;
        if (args.TryGetValue("go-to", out var goTo))
            Tree.CreateTimer(double.Parse(goTo, Inv)).Timeout += () => { if (table.Avatars.Named().FirstOrDefault() is { Peer: not 0 } who) table.FlyToFigure(who.Peer); };
        if (args.TryGetValue("walls-down", out var wd)) Tree.CreateTimer(double.Parse(wd, Inv)).Timeout += () => table.Cutaway.Set(true);
    }

    void ScanGround()
    {
        foreach (var sheet in Session.View.Sheets)
            for (double z = sheet.Pos.Z - sheet.Size.H / 2 + 10; z < sheet.Pos.Z + sheet.Size.H / 2; z += 10)
            {
                double left = sheet.Pos.X - sheet.Size.W / 2 + 10;
                var heights = Enumerable.Range(0, (int)(sheet.Size.W / 10) - 1).Select(i => Session.Ground.At(new Vec2(left + i * 10, z)).ToString("0.0", Inv));
                GD.Print($"[ground] z {z:0} from x {left:0}: " + string.Join(" ", heights));
            }
    }

    void Place(string placed)
    {
        foreach (var one in placed.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(',')))
            Session.Do(new KTable.Core.Protocol.SpawnPiece
            {
                Layer = Layers.Umpire, Def = one[0], Pos = new Vec2(double.Parse(one[1], Inv), double.Parse(one[2], Inv)),
                RotDeg = one.Length > 3 ? double.Parse(one[3], Inv) : 0,
            });
    }

    void Watched()
    {
        if (!args.ContainsKey("watch-figures")) return;
        var watch = new Timer { WaitTime = 0.5, Autostart = true };
        watch.Timeout += () =>
        {
            if (Session.View.Layer(Layers.Umpire).LastOrDefault() is not { } last || table.Pieces.NodeOf(Layers.Umpire, last.Id) is not { } node) return;
            if (AvatarSolids.Where(node) is not { } space) return;
            foreach (var peer in table.Avatars.Peers().ToList())
                if (table.Avatars.At(peer) is { } at) GD.Print($"[figure] {peer} on the last piece at {space.AffineInverse() * at}, piece at {space.Origin}");
        };
        table.AddChild(watch);
    }

    void FrameProbe()
    {
        if (!args.TryGetValue("frame-probe", out var fp) || Numbers(fp) is not [var fromS, var forS]) return;
        var times = new List<double>();
        ulong began = Time.GetTicksMsec();
        bool told = false;
        Tree.ProcessFrame += () =>
        {
            double at = (Time.GetTicksMsec() - began) / 1000.0;
            if (at < fromS) return;
            if (at < fromS + forS)
            {
                times.Add(table.GetProcessDeltaTime() * 1000);
                if (times[^1] > 20) GD.Print($"[frames] {Time.GetTicksMsec()} slow frame {times[^1]:0.0} ms");
                return;
            }
            if (told || times.Count == 0) return;
            told = true;
            times.Sort();
            double mean = times.Average();
            GD.Print($"[frames] {times.Count} frames, mean {mean:0.00} ms ({1000 / mean:0} fps), median {times[times.Count / 2]:0.00}, "
                + $"99th {times[(int)(times.Count * 0.99)]:0.00}, worst {times[^1]:0.00} ms");
        };
    }

    void Walk(string? where)
    {
        double wait = double.Parse(args.GetValueOrDefault("walk-wait", "4"), Inv);
        var stops = new Queue<Vec2>(Points(args.GetValueOrDefault("walk-path", "")));
        float[] look = args.GetValueOrDefault("walk-look", "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, Inv)).ToArray();
        float yaw = look.Length > 0 ? Mathf.DegToRad(look[0]) : table.Rig.Yaw, pitch = look.Length > 1 ? Mathf.DegToRad(look[1]) : WalkLookDown;
        bool run = args.ContainsKey("walk-run"), held = look.Length > 0;
        double[] circle = Numbers(args.GetValueOrDefault("walk-circle", ""));
        double riseFor = double.Parse(args.GetValueOrDefault("walk-rise", "0"), Inv);
        ulong born = Time.GetTicksMsec();
        double Since() => (Time.GetTicksMsec() - born) / 1000.0;
        if (circle.Length == 2)
            table.Walk.Steer = () => stops.Count > 0 || Since() < circle[0] || Since() > circle[1]
                ? Steered(stops, ref yaw, pitch, run, held)
                : new Steering(table.Rig.Yaw + CircleTurn / Engine.PhysicsTicksPerSecond, pitch, new Vector2(0, CirclePace), false);
        else if (stops.Count > 0 || held)
            table.Walk.Steer = () => Steered(stops, ref yaw, pitch, run, held) is { } st ? st with { Rise = Since() > wait && Since() < wait + riseFor ? 1 : 0 } : null;
        Tree.CreateTimer(wait).Timeout += () => Enter(where);
    }

    void Enter(string? where)
    {
        var start = Points(where ?? "").Select(p => (Vector3?)Coords.ToWorld(p)).FirstOrDefault();
        if (!table.Walk.Walking) table.Walk.Enter(start, asked: true);
        if (args.ContainsKey("walk-third")) table.Walk.ThirdPerson = true;
        if (args.TryGetValue("body", out var body)) table.Walk.Body = AvatarBody.Clean(body);
        if (args.TryGetValue("body-file", out var bodyFile))
        {
            var kept = BodyFiles.Keep(bodyFile, Session.Store.Root, out var files, out var why);
            GD.Print($"[body] {bodyFile} -> {kept ?? why}");
            if (kept != null) Net.UploadThen(files, () => { table.Walk.Body = kept; GD.Print($"[body] wearing {kept}"); });
        }
        if (args.ContainsKey("walk-fly")) table.Walk.Fly(true);
        else if (start is { } there) table.Walk.MoveTo(there);
        GD.Print($"[walk] {Time.GetTicksMsec()} entered at {table.Walk.Feet}, height {table.Walk.Height}");
    }

    void RideProbe()
    {
        if (!args.ContainsKey("ride-probe")) return;
        Vector3? seen = null;
        float? rideWas = null;
        float bump = 0, travel = 0, worst = 0, lowest = float.MaxValue, highest = float.MinValue;
        int frames = 0;
        Tree.ProcessFrame += () =>
        {
            if (!GodotObject.IsInstanceValid(table) || table.Walk.Ride is not { } ride || AvatarSolids.Where(ride) is not { } space)
            {
                seen = null;
                return;
            }
            var here = space.AffineInverse() * table.Rig.Cam.GlobalPosition;
            if (rideWas is { } before)
            {
                bump = Mathf.Max(bump, Mathf.Abs(space.Origin.Y - before));
                travel += Mathf.Abs(space.Origin.Y - before);
            }
            rideWas = space.Origin.Y;
            if (seen is { } was) worst = Mathf.Max(worst, (here - was).Length());
            lowest = Mathf.Min(lowest, here.Y);
            highest = Mathf.Max(highest, here.Y);
            seen = here;
            if (++frames % 120 != 0) return;
            GD.Print($"[probe] eye on the ride {here}, largest step in a frame {worst:0.0000}, eye height on the ride {lowest:0.000} to {highest:0.000}, ride at {space.Origin}");
            GD.Print($"[bumps] ride's largest rise or fall in a frame {bump:0.0000}, up and down in all {travel:0.000}");
            (bump, travel, worst, lowest, highest) = (0, 0, 0, float.MaxValue, float.MinValue);
        };
    }

    void Tell()
    {
        var tell = new Timer { WaitTime = 1, Autostart = true };
        tell.Timeout += () =>
        {
            if (table.Walk.Riding) GD.Print($"[ride] own place on the ride {table.Walk.RideAt}");
            foreach (var near in Session.View.Layer(Layers.Umpire).Where(q => System.Math.Abs(q.Pos.X - 286) < 3 && System.Math.Abs(q.Pos.Z + 75) < 3))
                GD.Print($"[near] {near.Id} {near.Def} at {near.Pos.X:0.000},{near.Pos.Z:0.000} y {near.Y:0.0000} scale {near.Scale} stack {near.Stack}");
            if (Session.View.Layer(Layers.Umpire).LastOrDefault() is { } lp && table.Pieces.NodeOf(Layers.Umpire, lp.Id) is { } ln)
                GD.Print($"[piece] last piece base {ln.GlobalPosition}, ground there {Session.Ground.At(lp.Pos):0.0000}, scale {lp.Scale}, below {ln.Def?.Below}, "
                    + $"thickness {ln.Thickness}, local {ln.Model?.Position}, aabb {ln.Model?.Mesh?.GetAabb()}, def {lp.Def}");
            if (table.Walk.Walking)
                GD.Print($"[walk] feet {table.Walk.Feet} grounded {table.Walk.Grounded} riding {table.Walk.Riding} solids {table.Walk.Solids} figures {table.Avatars.Count}");
        };
        table.AddChild(tell);
    }

    async System.Threading.Tasks.Task PickUp(double[] a)
    {
        await main.ToSignal(Tree.CreateTimer(a[0]), SceneTreeTimer.SignalName.Timeout);
        if (Session.View.Layer(Layers.Umpire).LastOrDefault() is not { } piece) return;
        table.SetSelection(new[] { piece.Id });
        var from = piece.Pos;
        var to = new Vec2(a[1], a[2]);
        table.Hand.Start(from);
        GD.Print($"[carry] picked up {piece.Def} at {from.X:0.0},{from.Z:0.0}");
        double seconds = a.Length > 4 ? a[4] : 3;
        ulong began = Time.GetTicksMsec();
        bool turned = false;
        for (double k = 0; k < 1; k = (Time.GetTicksMsec() - began) / 1000.0 / seconds)
        {
            table.Hand.Move(new Vec2(from.X + (to.X - from.X) * k, from.Z + (to.Z - from.Z) * k));
            if (k >= 0.5 && !turned && a.Length > 3 && a[3] != 0) table.Hand.Rotate(a[3]);
            turned |= k >= 0.5;
            await main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
        table.Hand.Drop();
        await main.ToSignal(Tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        var rests = Session.View.Find(Layers.Umpire, piece.Id);
        GD.Print($"[carry] set down, rests at {rests?.Pos.X:0.00},{rests?.Pos.Z:0.00} turned {rests?.RotDeg:0.0}");
    }

    Steering? Steered(Queue<Vec2> stops, ref float yaw, float pitch, bool run, bool held)
    {
        var feet = table.Walk.Feet;
        while (stops.Count > 0)
        {
            var to = Coords.ToWorld(stops.Peek()) - feet;
            to.Y = 0;
            if (to.Length() < table.Walk.Height * WalkArrivedHeights)
            {
                stops.Dequeue();
                continue;
            }
            yaw = Mathf.Atan2(to.X, to.Z);
            return new Steering(yaw, pitch, new Vector2(0, 1), run);
        }
        return held ? new Steering(yaw, pitch, Vector2.Zero, false) : null;
    }

    static double[] Numbers(string text) => text.Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, Inv)).ToArray();

    static IEnumerable<Vec2> Points(string text) =>
        text.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(','))
            .Where(p => p.Length >= 2).Select(p => new Vec2(double.Parse(p[0], Inv), double.Parse(p[1], Inv)));
}
