using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Core.Model;

namespace KTable.Client.Agent;

public sealed class TravelTools
{
    static readonly string[] Ends = ["from", "to"];
    readonly TableTools _t;

    public TravelTools(TableTools t) => _t = t;

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("travel",
            "How long a trip takes in game time over the built ways and across the laid ground of one map sheet, and with route its way as points. No answer on a plain picture sheet or across sheets.",
            Schema(), Travel);
    }

    static JsonObject Schema()
    {
        const string end = "[x, z], a piece's id, or a place's name as list_places gives it";
        var schema = Args.Schema(new Arg("from", "string", end, true), new Arg("to", "string", end, true),
            new Arg("by", "string", "foot (default), rider, cart, coach, or one a pack's paces add"), new Arg("route", "boolean", "also the points: [x, z, seconds of the leg ending there]"));
        foreach (var name in Ends) schema["properties"]![name]!.AsObject().Remove("type");
        return schema;
    }

    static Vec2? At(ClientSession s, JsonElement a, string name)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty(name, out var e)) return null;
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 2 && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Number)
            return new Vec2(e[0].GetDouble(), e[1].GetDouble());
        if (e.ValueKind != JsonValueKind.String || e.GetString()?.Trim() is not { Length: > 0 } text) return null;
        if (TableTools.Find(s, text) is { } piece) return piece.Pos;
        return PlaceTree.Find(PlaceTree.Of(s.View, s.Def), text)?.At;
    }

    Task<ToolResult> Travel(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return Task.FromResult(why!);
        return Task.FromResult(Answer(s, a, _t.Hooks.Relief ?? (_ => null)));
    }

    internal static ToolResult Answer(ClientSession s, JsonElement a, Func<SheetDef, ReliefGrid?> relief)
    {
        if (TravelMaps.Unknown(TravelMaps.Travellers(s.View, s.Store.Pack), a.Str("by"), out var who) is { } unknown) return ToolResult.Fail(unknown + ".");
        if (At(s, a, "from") is not { } from || At(s, a, "to") is not { } to) return ToolResult.Fail("from and to are each [x, z], a piece's id or a place's name.");
        if (TravelMaps.SheetOf(s.View, from, to) is not { } sheet) return new ToolResult("No answer: from and to are not on one map sheet.");
        if (s.Travel.Map(s.View, sheet, relief) is not { } map) return new ToolResult("No answer: that sheet has no built way and no laid ground.");
        if (map.Find(from, to, who!) is not { } route) return new ToolResult($"No way there by {who!.Name}.");
        string told = $"By {who!.Name}: {Words(route.Total)} ({Math.Round(route.Total).ToString(CultureInfo.InvariantCulture)} s).";
        if (a.Flag("route") != true) return new ToolResult(told);
        var points = new JsonArray();
        for (int i = 0; i < route.Points.Count; i++)
            points.Add(new JsonArray(Math.Round(route.Points[i].X, 2), Math.Round(route.Points[i].Z, 2), i == 0 ? 0 : Math.Round(route.Seconds[i - 1])));
        return new ToolResult(told + " Points: " + points.ToJsonString());
    }

    static string Words(double seconds)
    {
        const int minute = CalendarDef.SecondsInMinute;
        int whole = (int)Math.Round(seconds);
        return whole < minute ? $"{whole} s" : GameClock.StepText((int)Math.Round(seconds / minute) * minute);
    }
}
