using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed class OrderTools
{
    const double KmhToMps = 1000.0 / 3600, AnswerSeconds = 5;
    readonly TableTools _t;

    public OrderTools(TableTools t) => _t = t;

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("order_piece",
            "Sends a piece on a timed order: it goes to a place, or through several in turn, over the built ways and across laid ground at its traveller's pace, " +
            "moving as the game clock runs on every PC (set_clock rate runs the clock). With speed it goes straight at that speed instead. A new order replaces the old one from where the piece is; " +
            "stop ends it there; moving the piece by hand ends it too. wait_for arrival waits until it gets there. One undoable change.",
            Schema(), Order);
    }

    static JsonObject Schema()
    {
        var schema = Args.Schema(new Arg("id", "string", "the piece's id", true),
            new Arg("to", "array", "where to: [x, z], a piece's id or a place's name, or a list of them gone through in turn"),
            new Arg("by", "string", "foot (default), rider, cart, coach, or one a pack's paces add"),
            new Arg("speed", "number", "km/h in a straight line, for a sheet with no built ways or laid ground"),
            new Arg("pace", "number", $"a share of the traveller's pace, {TimedOrder.LeastPace}–{TimedOrder.MostPace} (1 if left out)"),
            new Arg("stop", "boolean", "ends its order where it is now"));
        schema["properties"]!["to"]!.AsObject().Remove("type");
        return schema;
    }

    static Vec2? Spot(ClientSession s, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 2 && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Number)
            return new Vec2(e[0].GetDouble(), e[1].GetDouble());
        if (e.ValueKind != JsonValueKind.String || e.GetString()?.Trim() is not { Length: > 0 } text) return null;
        if (TableTools.Find(s, text) is { } piece) return piece.Pos;
        return PlaceTree.Find(PlaceTree.Of(s.View, s.Def), text)?.At;
    }

    internal static List<Vec2>? Places(ClientSession s, JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("to", out var to)) return null;
        if (Spot(s, to) is { } one) return [one];
        if (to.ValueKind != JsonValueKind.Array || to.GetArrayLength() is 0 or > OrderPiece.MostWaypoints) return null;
        var all = new List<Vec2>();
        foreach (var e in to.EnumerateArray())
        {
            if (Spot(s, e) is not { } at) return null;
            all.Add(at);
        }
        return all;
    }

    async Task<ToolResult> Order(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Find(Layers.Umpire, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        var c = new OrderPiece { Layer = Layers.Umpire, Id = id };
        if (a.Flag("stop") == true)
        {
            if (p.Timed == null) return ToolResult.Fail($"{TableTools.NameOf(s, p)} has no order.");
            c.Stop = true;
        }
        else
        {
            if (Places(s, a) is not { } places) return ToolResult.Fail($"to is [x, z], a piece's id, a place's name, or a list of 1 to {OrderPiece.MostWaypoints} of them.");
            if (a.Num("speed") is { } kmh && !(kmh > 0)) return ToolResult.Fail("speed is km/h above 0.");
            c.To = places;
            c.By = a.Str("by");
            c.Speed = a.Num("speed") * KmhToMps;
            c.Pace = a.Num("pace");
        }
        var before = p.Timed;
        string? refused = null;
        void Refused(string e) => refused ??= e;
        s.Error += Refused;
        try
        {
            s.Do(c);
            await _t.Until(() => refused != null || !ReferenceEquals(s.View.Find(Layers.Umpire, id)?.Timed, before), AnswerSeconds);
        }
        finally { s.Error -= Refused; }
        if (refused != null) return ToolResult.Fail($"Not ordered: {refused}.");
        if (s.View.Find(Layers.Umpire, id) is not { } now) return new ToolResult("The piece is no longer in view.");
        _t.Log($"ordered {TableTools.NameOf(s, now)}");
        return new ToolResult(Told(s, now));
    }

    internal static string Told(ClientSession s, PieceInstance p)
    {
        string name = TableTools.NameOf(s, p);
        var clock = s.View.Clock;
        if (p.Timed is not { } order) return Invariant($"{name} has no order; it stands at {p.Pos.X:0.###}, {p.Pos.Z:0.###}.");
        double now = clock.Moment;
        var at = order.At(now);
        string how = order.Speed is { } mps ? Invariant($"straight at {mps / KmhToMps:0.#} km/h") : $"by {order.By ?? Travellers.Foot}";
        string pace = Math.Abs(order.Pace - 1) > 1e-9 ? Invariant($" at {order.Pace:0.##} of its pace") : "";
        string due = clock.Cal.Words((long)Math.Ceiling(order.Arrives));
        if (at.Arrived) return Invariant($"{name} has arrived at {order.End.X:0.###}, {order.End.Z:0.###} ({due}).");
        string left = ClockWords.Left((long)Math.Ceiling(order.Arrives - now));
        string still = clock.Runs ? "" : " The clock is paused, so it waits until the clock runs.";
        return Invariant($"{name} goes {how}{pace} to {order.End.X:0.###}, {order.End.Z:0.###}, due {due} ({left} of game time).{still}");
    }

    internal static Dictionary<string, TimedOrder> Going(ClientSession s, string? id) =>
        TimedOrders.Ordered(s.View).Where(o => o.Layer == Layers.Umpire && (id == null || o.Piece.Id == id) && !o.Piece.Timed!.At(s.View.Clock.Moment).Arrived)
            .ToDictionary(o => o.Piece.Id, o => o.Piece.Timed!);

    internal static int Reached(ClientSession s, Dictionary<string, TimedOrder> going, List<string> lines)
    {
        double now = s.View.Clock.Moment;
        foreach (var (id, order) in going.ToList())
        {
            var p = s.View.Find(Layers.Umpire, id);
            string name = p == null ? id : $"{TableTools.NameOf(s, p)} ({id})";
            if (p?.Timed is not { } still || !ReferenceEquals(still, order) && still.Start != order.Start) lines.Add($"{name}: its order ended before it arrived");
            else if (still.At(now).Arrived) lines.Add($"{name} arrived · {s.View.Clock.GameTime}");
            else continue;
            going.Remove(id);
        }
        return lines.Count;
    }

    static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
