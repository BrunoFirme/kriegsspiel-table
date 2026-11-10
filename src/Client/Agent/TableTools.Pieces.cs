using System.Text.Json;
using System.Text.Json.Nodes;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed partial class TableTools
{
    IEnumerable<AgentTool> PieceTools()
    {
        yield return new AgentTool("play_effect",
            "Plays effect pieces once (a volley, a puff of smoke, a burst of fire), as Play once on their right-click menu does, for everyone at the table.",
            Args.Schema(new Arg("ids", "array", "the effect pieces' ids", true)),
            PlayEffects);
        yield return new AgentTool("place_markers",
            "Puts down the markers a piece's kind brings (studio_set_markers gives a kind its markers), each at its own place beside the piece and turned with it, " +
            "as the Markers rows on its right-click menu do. Without kinds, every marker it has. One undoable change each.",
            Args.Schema(new Arg("id", "string", "the piece's id", true), new Arg("kinds", "array", "the markers to put down, by kind id or name; leave out for all")),
            PlaceMarkers);
    }

    async Task<ToolResult> PlayEffects(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var found = List(a, "ids").Distinct().Select(id => s.View.Find(Layers.Umpire, id)).OfType<PieceInstance>().ToList();
        var effects = found.Where(p => PieceKinds.IsEffect(s.Def(p.Def))).ToList();
        if (effects.Count == 0) return ToolResult.Fail(found.Count == 0 ? "ids names no piece on this table." : "None of them is an effect: list_kinds shows the effects among the kinds.");
        foreach (var p in effects) s.Do(new PlayEffect { Layer = Layers.Umpire, Id = p.Id });
        await Landed(s);
        _log($"played {effects.Count} effect{(effects.Count == 1 ? "" : "s")} once");
        var left = found.Count - effects.Count;
        return new ToolResult($"Played once: {string.Join(", ", effects.Select(p => NameOf(s, p)))}." + (left > 0 ? $" {left} not an effect, left alone." : ""));
    }

    async Task<ToolResult> PlaceMarkers(JsonElement a)
    {
        if (Open(out var why) is not { } s) return why!;
        if (!CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Find(Layers.Umpire, id) is not { } p) return ToolResult.Fail($"No piece {id} on this table.");
        var markers = s.Def(p.Def)?.Markers().Where(m => s.Def(m.Piece) != null).ToList() ?? new List<AttachmentDef>();
        if (markers.Count == 0) return ToolResult.Fail($"{NameOf(s, p)}'s kind brings no markers.");
        var asked = List(a, "kinds");
        bool Same(string k, AttachmentDef m) => string.Equals(k, m.Piece, StringComparison.OrdinalIgnoreCase) || string.Equals(k, s.Def(m.Piece)!.Name, StringComparison.OrdinalIgnoreCase);
        bool Wanted(AttachmentDef m) => asked.Count == 0 || asked.Any(k => Same(k, m));
        var chosen = markers.Where(Wanted).ToList();
        if (chosen.Count == 0) return ToolResult.Fail("It brings: " + string.Join(", ", markers.Select(m => $"{s.Def(m.Piece)!.Name} ({m.Piece})")) + ".");
        var before = s.View.Layer(Layers.Umpire).Select(x => x.Id).ToHashSet();
        foreach (var m in chosen) s.Do(new SpawnPiece { Layer = Layers.Umpire, Def = m.Piece, Pos = MarkerPlace.World(p.Pos, p.RotDeg, m.Offset), RotDeg = p.RotDeg });
        await Until(() => s.View.Layer(Layers.Umpire).Count(x => !before.Contains(x.Id)) >= chosen.Count, 4);
        var made = s.View.Layer(Layers.Umpire).Where(x => !before.Contains(x.Id)).ToList();
        _log($"put down {made.Count} marker{(made.Count == 1 ? "" : "s")} beside {NameOf(s, p)}");
        var rows = new JsonArray(made.Select(x => (JsonNode?)new JsonObject { ["id"] = x.Id, ["name"] = NameOf(s, x), ["x"] = Math.Round(x.Pos.X, 3), ["z"] = Math.Round(x.Pos.Z, 3) }).ToArray());
        return new ToolResult($"Put down {made.Count} of {chosen.Count} beside {NameOf(s, p)}.\n" + rows.ToJsonString(Json), made.Count < chosen.Count);
    }
}
