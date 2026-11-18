using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server;

public sealed partial class GameServer
{
    public const string OrdersOff = "orders are off in this scenario: Scenario properties → Surroundings → Orders";
    readonly OrderWatch _arrivals = new();
    readonly Dictionary<string, (double Start, int Leg)> _legs = new();

    public List<Outgoing> PlayOrders()
    {
        double now = State.Clock.Moment;
        TimedOrders.Play(State, now);
        foreach (var p in _arrivals.Arrived(State)) Happened($"{NameOf(p)} arrived", umpiresOnly: true);
        return Onward(now);
    }

    List<Outgoing> Onward(double now)
    {
        var changed = new List<Stretch>();
        var standing = new HashSet<string>();
        foreach (var (layer, p) in TimedOrders.Ordered(State))
        {
            var order = p.Timed!;
            string key = layer + "/" + p.Id;
            standing.Add(key);
            var leg = (order.Start, order.LegAt(now));
            bool known = _legs.TryGetValue(key, out var was);
            _legs[key] = leg;
            if (known && was != leg) changed.Add(new Stretch { Layer = layer, Id = p.Id, Order = order.Stretch(now) });
        }
        foreach (string gone in _legs.Keys.Where(k => !standing.Contains(k)).ToList()) _legs.Remove(gone);
        if (changed.Count == 0) return [];
        return _peers.Values.Where(peer => Visibility.Watches(peer.Role)).Select(peer => new Outgoing(peer.PeerId, new Stretches { Pieces = changed })).ToList();
    }

    string? Route(Command c)
    {
        if (c is Batch b)
        {
            foreach (var item in b.Items)
                if (item != null && Route(item) is { } refused) return refused;
            return null;
        }
        if (c is not OrderPiece o) return null;
        if (State.Find(o.Layer, o.Id) is not { } p) return "no such piece";
        o.Name = NameOf(p);
        if (!o.Asks) return null;
        if (o.Stop)
        {
            (o.Order, o.Pos, o.RotDeg, o.To, o.Stop) = (null, p.Pos, p.RotDeg, null, false);
            return null;
        }
        if (!State.OrdersOn) return OrdersOff;
        if (o.To is not { Count: > 0 and <= OrderPiece.MostWaypoints } to) return $"an order goes to 1 to {OrderPiece.MostWaypoints} places";
        if (o.Pace is { } pace && !(pace >= TimedOrder.LeastPace && pace <= TimedOrder.MostPace)) return "that pace cannot be set";
        var points = new List<Vec2> { p.Pos };
        points.AddRange(to);
        double start = State.Clock.Moment;
        TimedOrder order;
        if (o.Speed is { } speed)
        {
            if (!(speed > 0 && speed <= Travellers.MostPace)) return "that speed cannot be set";
            if (State.Calibration.UnitsPerMeter is not (> 0 and var perMetre)) return "set the map's scale before giving a speed";
            order = TimedOrder.Straight(points, speed * perMetre, start, p.RotDeg);
            order.Speed = speed;
        }
        else
        {
            if (TravelMaps.Unknown(Travel.Travellers(), o.By, out var who) is { } unknown) return unknown;
            var legs = new List<TimedOrder>();
            for (int i = 1; i < points.Count; i++)
            {
                if (Travel.MapFor(points[i - 1], points[i]) is not { } map) return "no way there: that leg is not on one map sheet with built ways or laid ground";
                if (map.Find(points[i - 1], points[i], who!) is not { } found) return $"no way there by {who!.Name}";
                legs.Add(Bridged(found, points[i - 1], points[i], who!.Open * (State.Calibration.UnitsPerMeter ?? 1)));
            }
            order = TimedOrder.Joined(legs, start, 1, p.RotDeg);
            order.By = who!.Name;
        }
        order.Pace = o.Pace ?? 1;
        if (order.Fault() is { } fault) return fault;
        (o.Order, o.To, o.Pos, o.RotDeg) = (order, null, null, null);
        return null;
    }

    static TimedOrder Bridged(Route found, Vec2 from, Vec2 to, double unitsPerSecond)
    {
        var points = found.Points.ToList();
        var legs = found.Seconds.ToList();
        double Leg(Vec2 a, Vec2 b) => unitsPerSecond > 0 ? (b - a).Length / unitsPerSecond : 0;
        if (points.Count == 0) return TimedOrder.Along([from, to], [Leg(from, to)], 0, 1, 0);
        if (points[0] != from)
        {
            legs.Insert(0, Leg(from, points[0]));
            points.Insert(0, from);
        }
        if (points[^1] != to)
        {
            legs.Add(Leg(points[^1], to));
            points.Add(to);
        }
        return TimedOrder.Along(points, legs, 0, 1, 0);
    }

    List<string> OrdersEndedByHand(Command c)
    {
        var ended = new List<string>();
        void Look(Command x)
        {
            if (x is Batch b)
                foreach (var item in b.Items)
                    if (item != null) Look(item);
            if (x is MovePiece m && State.Find(m.Layer, m.Id) is { Timed: not null } p) ended.Add(NameOf(p));
        }
        Look(c);
        return ended;
    }
}
