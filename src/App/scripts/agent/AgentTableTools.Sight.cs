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

sealed partial class AgentTableTools
{
    const double NearDefault = 0.5, NearMost = 100;
    const int SightListed = 50;

    IEnumerable<AgentTool> SightTools()
    {
        yield return new AgentTool("what_is_at",
            "What lies at a point of the map, as the person sees by resting the pointer there: the pieces on it or within radius (name, kind, state, description, counters, text lines), " +
            "the lines and names drawn there, stamps, the umpire zones and map sheets it is in, and the ground (height, ground type and its details).",
            Args.Schema(new Arg("x", "number", "map units", true), new Arg("z", "number", "map units", true), new Arg("radius", "number", $"map units round it ({NearDefault} if left out)")),
            x => Task.FromResult(WhatIsAt(x)));
        yield return new AgentTool("read_pointers",
            "Where the other umpires point on the map now, as their pointing hands show it: each one's name, the point, how many seconds since it last moved and whether they carry pieces. " +
            "Only umpires who share their pointer show; observers never do.",
            Args.Schema(),
            _ => Task.FromResult(ReadPointers()));
    }

    ToolResult WhatIsAt(JsonElement a)
    {
        if (Table(out var why) is not var (s, _)) return why!;
        if (a.Num("x") is not { } x || a.Num("z") is not { } z) return ToolResult.Fail("x and z are needed.");
        var at = new Vec2(x, z);
        double radius = Math.Clamp(a.Num("radius") ?? NearDefault, 0, NearMost);
        bool Covers(PieceInstance p)
        {
            if ((p.Pos - at).Length <= radius) return true;
            if (s.Def(p.Def) is not { } d) return false;
            var local = Coords.ToLocal(at, p.Pos, p.RotDeg);
            return Math.Abs(local.X) <= d.Size.W * p.Scale * p.WideBy / 2 && Math.Abs(local.Y) <= d.Size.H * p.Scale * p.DeepBy / 2;
        }
        JsonObject Piece(PieceInstance p)
        {
            var d = s.Def(p.Def);
            return new JsonObject
            {
                ["id"] = p.Id, ["name"] = PieceName(s, p), ["kind"] = d?.Name ?? p.Def, ["distance"] = Math.Round((p.Pos - at).Length, 2),
                ["state"] = d is { States.Count: > 1 } ? d.States[Math.Clamp(p.State, 0, d.States.Count - 1)].Name is { Length: > 0 } look ? look : $"{p.State + 1}" : null,
                ["description"] = p.Note, ["counters"] = p.Counters.Count > 0 ? JsonSerializer.SerializeToNode(p.Counters) : null,
                ["text"] = p.Text.Count > 0 ? JsonSerializer.SerializeToNode(p.Text) : null,
                ["face_down"] = p.FaceDown ? true : null, ["locked"] = p.Locked ? true : null, ["holds"] = p.Stowed is { Count: > 0 } held ? held.Count : null,
            };
        }
        JsonArray Few<T>(IEnumerable<T> items, Func<T, JsonNode> row) => new(items.Take(SightListed).Select(i => (JsonNode?)row(i)).ToArray());
        var pieces = s.View.Layer(Layers.Umpire).Where(Covers).OrderBy(p => (p.Pos - at).Length).ToList();
        var lines = s.View.Drawings.Values.SelectMany(l => l)
            .Where(st => st.Points.Count > 0 && (Polyline.Near(st.Points, at, radius) || st.Fill && Polyline.Inside(st.Points, at))).ToList();
        var stamps = s.View.DecalLayer(Layers.Umpire).Where(d => (new Vec2(d.X, d.Z) - at).Length <= Math.Max(radius, Math.Max(d.W, d.H) / 2)).ToList();
        var o = new JsonObject
        {
            ["pieces"] = Few(pieces, Piece),
            ["drawn"] = Few(lines, st => new JsonObject { ["id"] = st.Id, ["text"] = st.Text, ["colour"] = st.Color, ["area"] = st.Fill ? true : null }),
            ["stamps"] = Few(stamps, d => new JsonObject { ["id"] = d.Id, ["name"] = d.Name }),
            ["zones"] = Few(s.View.Zones.Where(zn => zn.Contains(at)), zn => new JsonObject { ["name"] = zn.Name, ["owner"] = zn.Owner }),
            ["sheets"] = Few(s.View.Sheets.Where(sh => OnSheet(sh, at)), sh => new JsonObject { ["id"] = sh.Id, ["name"] = sh.Name }),
            ["ground"] = GroundRow(s.Ground, at),
        };
        return new ToolResult(o.ToJsonString(Opts));
    }

    static bool OnSheet(SheetDef sh, Vec2 at)
    {
        var local = Coords.ToLocal(at, sh.Pos, sh.RotDeg);
        return Math.Abs(local.X) <= sh.Size.W / 2 && Math.Abs(local.Y) <= sh.Size.H / 2;
    }

    ToolResult ReadPointers()
    {
        if (Table(out var why) is not var (_, hud)) return why!;
        var pointers = hud.T.Pointers;
        if (!pointers.Show) return new ToolResult("This screen hides the others' pointers (Show the others' pointers, in the people list), so none can be read.");
        var rows = new JsonArray(pointers.Seen().Select(p => (JsonNode?)new JsonObject
        {
            ["name"] = p.Name, ["x"] = Math.Round(p.At.X, 2), ["z"] = Math.Round(p.At.Z, 2), ["seconds_still"] = Math.Round(p.Idle, 1), ["carrying"] = p.Carrying ? true : null,
        }).ToArray());
        return new ToolResult(rows.Count == 0 ? "No other umpire's pointer has been seen at this table." : rows.ToJsonString(Opts));
    }
}
