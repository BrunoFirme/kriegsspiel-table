using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class TableDemos
{
    const float WalkArrivedHeights = 0.12f, WalkLookDown = -0.08f, CircleTurn = 1.3f, CirclePace = 0.4f;

    void Walked(Table table)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (_args.ContainsKey("ground-scan"))
            Tree.CreateTimer(3).Timeout += () =>
            {
                foreach (var sheet in Session.View.Sheets)
                    for (double z = sheet.Pos.Z - sheet.Size.H / 2 + 10; z < sheet.Pos.Z + sheet.Size.H / 2; z += 10)
                        GD.Print($"[ground] z {z:0} from x {sheet.Pos.X - sheet.Size.W / 2 + 10:0}: " + string.Join(" ", Enumerable.Range(0, (int)(sheet.Size.W / 10) - 1)
                            .Select(i => Session.Ground.At(new Vec2(sheet.Pos.X - sheet.Size.W / 2 + 10 + i * 10, z)).ToString("0.0", inv))));
            };
        if (_args.TryGetValue("add-pack", out var pack)) Tree.CreateTimer(1.5).Timeout += () => { if (!Session.View.Packs.Contains(pack)) Net.SharePack(pack); };
        if (_args.TryGetValue("place", out var placed))
            Tree.CreateTimer(2.5).Timeout += () =>
            {
                foreach (var one in placed.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(',')))
                    Session.Do(new KTable.Core.Protocol.SpawnPiece
                    {
                        Layer = Layers.Umpire, Def = one[0], Pos = new Vec2(double.Parse(one[1], inv), double.Parse(one[2], inv)),
                        RotDeg = one.Length > 3 ? double.Parse(one[3], inv) : 0,
                    });
            };
        if (_args.ContainsKey("watch-figures"))
        {
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
        if (_args.TryGetValue("carry", out var carry)) _ = PickUp(table, carry.Split(',').Select(x => double.Parse(x, inv)).ToArray());
        if (!_args.TryGetValue("walk", out var where) && !_args.ContainsKey("walk-path")) return;
        double wait = double.Parse(_args.GetValueOrDefault("walk-wait", "4"), inv);
        var stops = new Queue<Vec2>(Points(_args.GetValueOrDefault("walk-path", ""), inv));
        float[] look = _args.GetValueOrDefault("walk-look", "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, inv)).ToArray();
        float yaw = look.Length > 0 ? Mathf.DegToRad(look[0]) : table.Rig.Yaw, pitch = look.Length > 1 ? Mathf.DegToRad(look[1]) : WalkLookDown;
        bool steered = stops.Count > 0 || look.Length > 0;
        bool run = _args.ContainsKey("walk-run");
        bool held = look.Length > 0;
        double[] circle = _args.GetValueOrDefault("walk-circle", "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, inv)).ToArray();
        ulong born = Time.GetTicksMsec();
        if (circle.Length == 2)
        {
            steered = true;
            Steering? Circled()
            {
                double t = (Time.GetTicksMsec() - born) / 1000.0;
                if (stops.Count > 0 || t < circle[0] || t > circle[1]) return Steered(table, stops, ref yaw, pitch, run, held);
                return new Steering(table.Rig.Yaw + CircleTurn / Engine.PhysicsTicksPerSecond, pitch, new Vector2(0, CirclePace), false);
            }
            table.Walk.Steer = Circled;
        }
        if (_args.ContainsKey("no-suspension")) table.Suspension.Off = true;
        if (_args.ContainsKey("ride-probe"))
        {
            Vector3? seen = null;
            float? rideWas = null;
            float bump = 0, travel = 0;
            float worst = 0, lowest = float.MaxValue, highest = float.MinValue;
            int frames = 0;
            Tree.ProcessFrame += () =>
            {
                if (!GodotObject.IsInstanceValid(table) || table.Walk.Ride is not { } ride || AvatarSolids.Where(ride) is not { } space) { seen = null; return; }
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
                bump = 0;
                travel = 0;
                worst = 0;
                lowest = float.MaxValue;
                highest = float.MinValue;
            };
        }
        if (steered && circle.Length != 2) table.Walk.Steer = () => Steered(table, stops, ref yaw, pitch, run, held);
        Tree.CreateTimer(wait).Timeout += () =>
        {
            var start = Points(where ?? "", inv).Select(p => (Vector3?)Coords.ToWorld(p)).FirstOrDefault();
            if (!table.Walk.Walking) table.Walk.Enter(start, asked: true);
            if (_args.ContainsKey("walk-third")) table.Walk.ThirdPerson = true;
            else if (start is { } there) table.Walk.MoveTo(there);
            GD.Print($"[walk] entered at {table.Walk.Feet}, height {table.Walk.Height}");
        };
        var tell = new Timer { WaitTime = 1, Autostart = true };
        tell.Timeout += () =>
        {
            if (table.Walk.Riding) GD.Print($"[ride] own place on the ride {table.Walk.RideAt}");
            if (table.Walk.Walking)
                GD.Print($"[walk] feet {table.Walk.Feet} grounded {table.Walk.Grounded} riding {table.Walk.Riding} solids {table.Walk.Solids} stops left {stops.Count} figures {table.Avatars.Count}");
        };
        table.AddChild(tell);
    }

    async System.Threading.Tasks.Task PickUp(Table table, double[] a)
    {
        await _main.ToSignal(Tree.CreateTimer(a[0]), SceneTreeTimer.SignalName.Timeout);
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
            await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
        }
        table.Hand.Drop();
        await _main.ToSignal(Tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        var rests = Session.View.Find(Layers.Umpire, piece.Id);
        GD.Print($"[carry] set down, rests at {rests?.Pos.X:0.00},{rests?.Pos.Z:0.00} turned {rests?.RotDeg:0.0}");
    }

    static Steering? Steered(Table table, Queue<Vec2> stops, ref float yaw, float pitch, bool run, bool held)
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

    static IEnumerable<Vec2> Points(string text, System.Globalization.CultureInfo inv) =>
        text.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(','))
            .Where(p => p.Length >= 2).Select(p => new Vec2(double.Parse(p[0], inv), double.Parse(p[1], inv)));
}
