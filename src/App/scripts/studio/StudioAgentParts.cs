using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.App;

static class StudioAgentParts
{
    const double LeastSize = 0.05, MostSize = 20, LeastTop = 0.02, MostTop = 2;
    const int MostParts = 200, OpaqueHex = 7;
    const string Shapes = "{kind, right, ahead, up, turn, size, tint, state} or {kind, count, ranks}";

    internal delegate Task<ToolResult> Change(JsonElement a, string what, Func<PieceStudio, PieceDef, Task<ToolResult>> work);

    internal static AgentTool Tool(Change change) => new("studio_set_parts",
        "Sets the pieces of this pack that stand on a kind as its parts: one piece on the table, drawn with them, and a change to a part's kind shows on every piece built from it. " +
        "A part is {kind, right, ahead, up, turn, size, tint, state}: map units from the piece's middle and above its underside (up starts at its top), turn in degrees clockwise from its facing, " +
        "size against the part's own, state a number from 1. {kind, count, ranks} instead spreads count of that kind in ranks over the piece. A part's script, counters, text and markers stay off. Replaces the list.",
        Args.Schema(new Arg("id", "string", "the kind's id", true), new Arg("parts", Args.Objects, $"[{Shapes}], empty for none", true),
            new Arg("of_state", "number", "for a kind whose states carry different parts: the state, from 1 (default 1)")),
        a => change(a, "set the parts", (s, d) => Task.FromResult(Set(s, d, a))));

    static ToolResult Set(PieceStudio s, PieceDef d, JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("parts", out var given) || given.ValueKind != JsonValueKind.Array) return ToolResult.Fail($"parts is a list of {Shapes}.");
        var list = new List<PartDef>();
        double top = Math.Clamp(d.Thickness, LeastTop, MostTop);
        foreach (var p in given.EnumerateArray())
        {
            var id = p.Str("kind") ?? "";
            if (s.Pack.Pieces.FirstOrDefault(k => k.Id == id) is not { } kind) return ToolResult.Fail($"No kind {id} in this pack: studio_list_pieces lists them.");
            if (PartRules.Refusal(d, kind, s.Pack.Pieces, s.Store.Def) is { } why) return ToolResult.Fail(why);
            var part = new PartDef
            {
                Def = kind.Id, X = p.Num("right") ?? 0, Z = -(p.Num("ahead") ?? 0), Y = p.Num("up") ?? top, Rot = p.Num("turn") ?? 0, Scale = Math.Clamp(p.Num("size") ?? 1, LeastSize, MostSize),
                Tint = StudioTools.IsColour(p.Str("tint")) ? StudioTools.Colour(p.Str("tint"), kind.Tint)[..OpaqueHex] : null,
                State = Math.Clamp((int)(p.Num("state") ?? 1) - 1, 0, Math.Max(0, kind.States.Count - 1)),
            };
            list.Add(part);
            if (p.Num("count") is { } count) PartRules.Repeat(list, part, (int)Math.Clamp(count, 1, MostParts), (int)(p.Num("ranks") ?? 1), d.Size);
        }
        if (list.Count > MostParts) return ToolResult.Fail($"A piece carries at most {MostParts} parts.");
        int look = Math.Clamp((int)(a.Num("of_state") ?? 1) - 1, 0, Math.Max(0, d.States.Count - 1));
        StudioTools.Edit(s, d, x =>
        {
            var now = PartRules.Listed(x, look);
            now.Clear();
            now.AddRange(list);
            PartRules.Tidy(x);
        });
        if (s.Model is { } m && m.Holds(d)) m.ShowPieces();
        return new ToolResult(StudioTools.Json(StudioTools.Summary(s, d)));
    }

    internal static JsonArray Summary(PieceStudio s, PieceDef d) =>
        new(d.AllParts.Select(p => (JsonNode?)JsonValue.Create(FormattableString.Invariant($"{s.Store.Def(p.Def)?.Name ?? p.Def} right {p.X:0.##}, ahead {-p.Z:0.##}, up {p.Y:0.##}"))).ToArray());
}
