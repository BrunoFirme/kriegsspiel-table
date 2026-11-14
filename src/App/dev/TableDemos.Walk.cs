using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class TableDemos
{
    const float WalkArrivedHeights = 0.12f, WalkLookDown = -0.08f;

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
        if (!_args.TryGetValue("walk", out var where) && !_args.ContainsKey("walk-path")) return;
        double wait = double.Parse(_args.GetValueOrDefault("walk-wait", "4"), inv);
        var stops = new Queue<Vec2>(Points(_args.GetValueOrDefault("walk-path", ""), inv));
        float[] look = _args.GetValueOrDefault("walk-look", "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, inv)).ToArray();
        float yaw = look.Length > 0 ? Mathf.DegToRad(look[0]) : table.Rig.Yaw, pitch = look.Length > 1 ? Mathf.DegToRad(look[1]) : WalkLookDown;
        bool steered = stops.Count > 0 || look.Length > 0;
        bool run = _args.ContainsKey("walk-run");
        if (steered) table.Walk.Steer = () => Steered(table, stops, ref yaw, pitch, run);
        Tree.CreateTimer(wait).Timeout += () =>
        {
            var start = Points(where ?? "", inv).Select(p => (Vector3?)Coords.ToWorld(p)).FirstOrDefault();
            if (!table.Walk.Walking) table.Walk.Enter(start, asked: true);
            else if (start is { } there) table.Walk.MoveTo(there);
            GD.Print($"[walk] entered at {table.Walk.Feet}, height {table.Walk.Height}");
        };
        var tell = new Timer { WaitTime = 1, Autostart = true };
        tell.Timeout += () =>
        {
            if (table.Walk.Walking)
                GD.Print($"[walk] feet {table.Walk.Feet} grounded {table.Walk.Grounded} solids {table.Walk.Solids} stops left {stops.Count} figures {table.Avatars.Count}");
        };
        table.AddChild(tell);
    }

    static Steering? Steered(Table table, Queue<Vec2> stops, ref float yaw, float pitch, bool run)
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
        return new Steering(yaw, pitch, Vector2.Zero, false);
    }

    static IEnumerable<Vec2> Points(string text, System.Globalization.CultureInfo inv) =>
        text.Split(';', System.StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(','))
            .Where(p => p.Length >= 2).Select(p => new Vec2(double.Parse(p[0], inv), double.Parse(p[1], inv)));
}
