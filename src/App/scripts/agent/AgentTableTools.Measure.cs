using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class AgentTableTools
{
    const int MostStops = 200, MostInRange = 200;

    IEnumerable<AgentTool> MeasureTools()
    {
        yield return new AgentTool("measure",
            "Measures as the ruler does: the straight distance from stop to stop and in all, in the map's scale (metres, paces or yards; map units when it has none), " +
            "with each leg's compass bearing (0° north, 90° east, north as the scenario sets it) and how far the ground rises or falls. A stop is a point or a piece, measured from its centre.",
            Args.Schema(new Arg("stops", "array", "two or more stops in order: [x, z] in map units, or a piece's id", true)),
            x => Task.FromResult(Measure(x)));
        yield return new AgentTool("in_range",
            "What the range rings show: the pieces within reach of a piece, centre to centre, nearest first, with their distance and bearing. " +
            "The reach is the piece's own \"range\" counter or text line (in metres), else the reach given.",
            Args.Schema(new Arg("id", "string", "the piece measured from", true),
                new Arg("reach", "number", "in the map's scale unit (metres, paces or yards; map units when it has none), when the piece has no range of its own"),
                new Arg("limit", "number", $"at most this many (1–{MostInRange}, all within reach if left out)")),
            x => Task.FromResult(InRange(x)));
    }

    static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

    ToolResult Measure(JsonElement a)
    {
        if (Table(out var why) is not var (s, _)) return why!;
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("stops", out var raw) || raw.ValueKind != JsonValueKind.Array)
            return ToolResult.Fail("stops is needed: [[x, z] or a piece's id, …].");
        var stops = new List<(Vec2 at, string? id)>();
        foreach (var e in raw.EnumerateArray())
        {
            if (stops.Count >= MostStops) return ToolResult.Fail($"At most {MostStops} stops.");
            if (e.ValueKind == JsonValueKind.String)
            {
                var id = e.GetString() ?? "";
                if (s.View.Find(Layers.Umpire, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
                stops.Add((p.Pos, id));
            }
            else if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 2 && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Number)
                stops.Add((new Vec2(e[0].GetDouble(), e[1].GetDouble()), null));
            else return ToolResult.Fail("Each stop is [x, z] or a piece's id.");
        }
        if (stops.Count < 2) return ToolResult.Fail("Two stops at least.");
        var cal = s.View.Calibration;
        var ground = s.Ground;
        var legs = new JsonArray();
        double total = 0;
        for (int i = 1; i < stops.Count; i++)
        {
            var (from, to) = (stops[i - 1], stops[i]);
            double len = (to.at - from.at).Length;
            total += len;
            double rise = ground.At(to.at) - ground.At(from.at);
            legs.Add(new JsonObject
            {
                ["from"] = from.id ?? Point(from.at), ["to"] = to.id ?? Point(to.at),
                ["distance"] = cal.Describe(len, s.View.Grid), ["map_units"] = Math.Round(len, 3), ["bearing"] = Math.Round(Bearing(cal, from.at, to.at), 1),
                ["rise"] = Math.Abs(rise) < MinRise ? "level" : (rise > 0 ? "up " : "down ") + cal.Describe(Math.Abs(rise)),
            });
        }
        var result = new JsonObject { ["scale"] = Scale(cal), ["legs"] = legs, ["total"] = cal.Describe(total, s.View.Grid), ["total_map_units"] = Math.Round(total, 3) };
        return new ToolResult(result.ToJsonString(Opts));
    }

    const double MinRise = 0.01;

    ToolResult InRange(JsonElement a)
    {
        if (Table(out var why) is not var (s, _)) return why!;
        var id = a.Str("id") ?? "";
        var layer = s.View.Layer(Layers.Umpire);
        if (layer.FirstOrDefault(p => p.Id == id) is not { } from) return ToolResult.Fail($"No piece {id} on this table.");
        var cal = s.View.Calibration;
        bool scaled = cal.UnitsPerMeter is > 0;
        double reach;
        string source;
        if (OwnRange(from) is { } metres)
        {
            reach = scaled ? metres * cal.UnitsPerMeter!.Value : metres;
            source = "its own range";
        }
        else if (a.Num("reach") is > 0 and var given)
        {
            reach = cal.ToTableUnits(given);
            source = "the reach given";
        }
        else return ToolResult.Fail("This piece has no range counter or text line: give reach.");
        int limit = Math.Clamp((int)(a.Num("limit") ?? MostInRange), 1, MostInRange);
        var near = layer.Where(p => p.Id != id && !from.Attached.Contains(p.Id))
            .Select(p => (p, d: (p.Pos - from.Pos).Length)).Where(x => x.d <= reach).OrderBy(x => x.d).ToList();
        var rows = new JsonArray(near.Take(limit).Select(x => (JsonNode?)new JsonObject
        {
            ["id"] = x.p.Id, ["name"] = PieceName(s, x.p), ["distance"] = cal.Describe(x.d, s.View.Grid), ["map_units"] = Math.Round(x.d, 3),
            ["bearing"] = Math.Round(Bearing(cal, from.Pos, x.p.Pos), 1),
        }).ToArray());
        var result = new JsonObject
        {
            ["from"] = id, ["reach"] = cal.Describe(reach), ["reach_from"] = source, ["scale"] = Scale(cal),
            ["within"] = near.Count, ["pieces"] = rows,
        };
        return new ToolResult(result.ToJsonString(Opts));
    }

    static double? OwnRange(PieceInstance p)
    {
        foreach (var (k, v) in p.Counters)
            if (string.Equals(k, "range", StringComparison.OrdinalIgnoreCase) && v > 0) return v;
        foreach (var (k, v) in p.Text)
            if (string.Equals(k, "range", StringComparison.OrdinalIgnoreCase) && double.TryParse(v, System.Globalization.NumberStyles.Any, Inv, out var d) && d > 0) return d;
        return null;
    }

    static string PieceName(Session s, PieceInstance p) => p.Label is { Length: > 0 } l ? l : s.Store.Def(p.Def)?.Name ?? p.Def;

    static double Bearing(Calibration cal, Vec2 from, Vec2 to) => cal.CompassBearing(Math.Atan2(to.X - from.X, to.Z - from.Z) * 180 / Math.PI);

    static JsonNode Point(Vec2 p) => new JsonArray(Math.Round(p.X, 3), Math.Round(p.Z, 3));

    static string Scale(Calibration cal) => cal.UnitsPerMeter is > 0 and var u
        ? string.Format(Inv, "{0:0.####} map units to the metre, shown in {1}", u, cal.UnitName)
        : "no scale: distances in map units (set_map_scale gives one)";
}
