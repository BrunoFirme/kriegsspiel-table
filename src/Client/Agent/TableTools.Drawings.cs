using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed partial class TableTools
{
    const int DrawingsListed = 200;
    const double ListRadius = 5, EraseRadius = 1;

    IEnumerable<AgentTool> DrawingTools()
    {
        yield return new AgentTool("list_drawings",
            "The lines and names drawn on the map, by the pen or by draw: id, layer, colour, width, text for a name, where each starts and ends. With near, only those passing within radius of that point. A page at a time.",
            Args.Schema(new Arg("near", Args.Numbers, "[x, z] map units"), new Arg("radius", "number", $"map units (default {ListRadius})"), Paging.Offset), ListDrawings);
        yield return new AgentTool("erase_drawings",
            "Erases drawn lines, as the pen's eraser does: by id (list_drawings, or what draw answered), or every line passing within radius of near. Each is undoable.",
            Args.Schema(new Arg("ids", "array", "the lines' ids"), new Arg("near", Args.Numbers, "[x, z] map units"), new Arg("radius", "number", $"map units (default {EraseRadius})")), EraseDrawings);
    }

    static Vec2? Near(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty("near", out var n) ? Point(n) : null;

    static HashSet<string> Ids(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty("ids", out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToHashSet() : new HashSet<string>();

    static JsonArray At(Vec2 p) => new(JsonValue.Create(Math.Round(p.X, 2)), JsonValue.Create(Math.Round(p.Z, 2)));

    Task<ToolResult> ListDrawings(JsonElement a)
    {
        if (Open(out var why) is not { } s) return Task.FromResult(why!);
        var near = Near(a);
        var radius = a.Num("radius") ?? ListRadius;
        var found = s.View.Drawings.SelectMany(kv => kv.Value.Select(st => (Layer: kv.Key, Stroke: st)))
            .Where(x => x.Stroke.Points.Count > 0 && !(near is { } n && Distance(x.Stroke, n) > radius)).ToList();
        if (found.Count == 0) return Task.FromResult(new ToolResult(near == null ? "Nothing is drawn on the map." : "No drawn line passes there."));
        var (page, head) = Paging.Page(found, a, DrawingsListed, "lines");
        var rows = new JsonArray(page.Select(x => (JsonNode?)new JsonObject
        {
            ["id"] = x.Stroke.Id, ["layer"] = x.Layer, ["colour"] = x.Stroke.Color, ["width"] = Math.Round(x.Stroke.Width, 3), ["points"] = x.Stroke.Points.Count,
            ["from"] = At(x.Stroke.Points[0]), ["to"] = At(x.Stroke.Points[^1]), ["board"] = x.Stroke.Board.Length > 0 ? x.Stroke.Board : null, ["text"] = x.Stroke.Text,
            ["filled"] = x.Stroke.Fill ? true : null,
        }).ToArray());
        return Task.FromResult(new ToolResult(head + "\n" + rows.ToJsonString(Json)));
    }

    async Task<ToolResult> EraseDrawings(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var ids = Ids(a);
        var near = Near(a);
        if (ids.Count == 0 && near == null) return ToolResult.Fail("Give ids (list_drawings) or near.");
        var radius = a.Num("radius") ?? EraseRadius;
        var hits = s.View.Drawings.SelectMany(kv => kv.Value.Select(st => (Layer: kv.Key, Stroke: st)))
            .Where(x => ids.Contains(x.Stroke.Id) || near is { } n && x.Stroke.Points.Count > 0 && Distance(x.Stroke, n) <= radius).ToList();
        var missing = ids.Where(id => hits.All(h => h.Stroke.Id != id)).ToList();
        var allowed = hits.Where(h => s.CanEdit(h.Layer)).ToList();
        if (allowed.Count == 0)
            return ToolResult.Fail(hits.Count > 0 ? "Those lines are on a layer this seat cannot change." : missing.Count > 0 ? "No drawn line " + string.Join(", ", missing) + " (list_drawings)." : "No drawn line passes there.");
        foreach (var (layer, st) in allowed) s.Do(new EraseStroke { Layer = layer, StrokeId = st.Id });
        var gone = allowed.Select(h => h.Stroke.Id).ToHashSet();
        await Until(() => !s.View.Drawings.Values.SelectMany(l => l).Any(st => gone.Contains(st.Id)), 3);
        _log($"erased {gone.Count} drawn line{(gone.Count == 1 ? "" : "s")}");
        var text = $"Erased {gone.Count} line{(gone.Count == 1 ? "" : "s")}: {string.Join(", ", gone)}.";
        if (missing.Count > 0) text += " Not found: " + string.Join(", ", missing) + ".";
        if (allowed.Count < hits.Count) text += $" {hits.Count - allowed.Count} on a layer this seat cannot change were left.";
        return new ToolResult(text);
    }

    static double Distance(Stroke st, Vec2 p)
    {
        if (st.Points.Count == 1) return ToSegment(p, st.Points[0], st.Points[0]);
        double best = double.MaxValue;
        for (int i = 1; i < st.Points.Count; i++) best = Math.Min(best, ToSegment(p, st.Points[i - 1], st.Points[i]));
        return best;
    }

    static double ToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
        double t = len2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2, 0, 1);
        double x = a.X + t * dx - p.X, z = a.Z + t * dz - p.Z;
        return Math.Sqrt(x * x + z * z);
    }
}
