using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Client.Agent;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class AgentTableTools
{
    const double RollWaitSeconds = 10, DecalSize = 1, LeastGrowth = 0.05, MostGrowth = 40;
    const string PasswordWithheld = "the host gives it";
    const int DecalsListed = 200;
    static readonly string[] DecalActions = { "list", "add", "remove", "stamp", "lift" };

    IEnumerable<AgentTool> TableExtraTools()
    {
        yield return new AgentTool("throw_dice",
            "Throws dice pieces on the table, as Roll on their right-click menu does: they tumble where they lie and come to rest on a side, for everyone to see, and the Log gets the roll. " +
            "Returns the side each shows. roll_dice rolls in the Log alone, with no piece.",
            Args.Schema(new Arg("ids", "array", "the dice pieces' ids", true)),
            ThrowDice);
        yield return new AgentTool("select_pieces",
            "Selects pieces on the person's own screen, as clicking or a selection box does, so they can move, turn or change them together; add keeps what is selected, none clears it, frame turns the view onto them. " +
            "With nothing given, lists what is selected.",
            Args.Schema(new Arg("ids", "array", "the pieces' ids"), new Arg("add", "boolean", "add to what is selected"), new Arg("none", "boolean", "select nothing"), new Arg("frame", "boolean", "turn the view onto them")),
            x => Task.FromResult(SelectPieces(x)));
        yield return new AgentTool("decals",
            "The Decals panel's work: pictures stamped flat on the table or on a piece (blood, an arrow, a mark). list shows the scenario's decals and what is stamped where; " +
            "add puts a picture (a file or web link) among the decals; remove takes one out of them; stamp presses one at x, z, on the table or on the piece given, turned and sized; " +
            "lift takes stamps off: a table stamp by its id, a piece's stamp by its number, or every table stamp with all.",
            Args.Schema(new Arg("action", "string", string.Join(", ", DecalActions), true), new Arg("name", "string", "the decal's name (list shows them)"),
                new Arg("picture", "string", "add: a picture's full path or web link"), new Arg("size", "number", $"add: its height in map units ({DecalSize} if left out); stamp: times that size"),
                new Arg("x", "number", "stamp: map units"), new Arg("z", "number", "stamp: map units"), new Arg("turn", "number", "stamp: degrees"),
                new Arg("on", "string", "stamp: the piece to press it on; lift: the piece to lift a stamp from"), new Arg("id", "string", "lift: a table stamp's id"),
                new Arg("number", "number", "lift: a piece's stamp, from 1 as list shows"), new Arg("all", "boolean", "lift: every stamp on the table (those on pieces stay)")),
            Decals);
        yield return new AgentTool("joining",
            "How people join this table, as Invite… and the Joining page show it: where they connect (this network and the internet), whether the table has a password (never the password itself, " +
            "which stays with the people), whether umpires' pointers are always shown, and the invitation to paste where the group meets. pointers_always_shown changes that rule (host and lead umpires).",
            Args.Schema(new Arg("pointers_always_shown", "boolean", "every umpire's pointer is shown, none can hide it")),
            Joining);
    }

    async Task<ToolResult> ThrowDice(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (!CanChange(s, out why)) return why!;
        var nodes = Ids(a).Select(id => hud.T.Pieces.NodeOf(Layers.Umpire, id)).OfType<PieceNode>().Where(n => n.IsDie && s.CanTouch(n.Layer, n.Piece)).ToList();
        if (nodes.Count == 0) return ToolResult.Fail("ids names no die this seat can throw: dice are pieces with sides (list_pieces).");
        hud.T.RollDicePhysics(nodes, Godot.Vector3.Zero, hop: true);
        for (double t = 0; t < RollWaitSeconds && nodes.Any(n => Godot.GodotObject.IsInstanceValid(n) && n.Rolling); t += Waits.Poll) await _a.Delay(Waits.Poll);
        await _a.Landed(s);
        var rows = new JsonArray();
        foreach (var n in nodes.Where(Godot.GodotObject.IsInstanceValid))
        {
            var p = s.View.Find(Layers.Umpire, n.Piece.Id) ?? n.Piece;
            var faces = PieceInstance.FacesOf(n.Def, p);
            var face = faces.ElementAtOrDefault(p.ShownFace);
            rows.Add(new JsonObject { ["id"] = p.Id, ["name"] = PieceName(s, p), ["side"] = p.ShownFace + 1, ["value"] = face?.Value is { Length: > 0 } v ? v : null });
        }
        _a.Log($"threw {nodes.Count} di{(nodes.Count == 1 ? "e" : "ce")}");
        return new ToolResult(rows.ToJsonString(Opts));
    }

    ToolResult SelectPieces(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        var t = hud.T;
        var ids = Ids(a);
        var missing = ids.Where(id => s.View.Find(Layers.Umpire, id) == null).ToList();
        var found = ids.Except(missing).ToList();
        if (a.Flag("none") == true) t.SetSelection(Array.Empty<string>());
        else if (found.Count > 0)
        {
            if (a.Flag("frame") == true && a.Flag("add") != true) t.Locate(found.Select(id => s.View.Find(Layers.Umpire, id)!).ToList());
            else t.SetSelection(found, a.Flag("add") == true);
        }
        else if (ids.Count > 0) return ToolResult.Fail("No such piece: " + string.Join(", ", missing));
        var selected = t.SelectedNodes.Select(n => n.Piece).ToList();
        if (a.Flag("frame") == true && a.Flag("add") == true && selected.Count > 0) t.Locate(selected);
        var head = selected.Count == 0 ? "Nothing is selected." : $"{selected.Count} selected: {string.Join(", ", selected.Take(ListShown).Select(p => $"{PieceName(s, p)} ({p.Id})"))}{(selected.Count > ListShown ? ", …" : "")}.";
        return new ToolResult(head + (missing.Count > 0 ? " No such piece: " + string.Join(", ", missing) + "." : ""));
    }

    const int ListShown = 60;

    async Task<ToolResult> Decals(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        var action = (a.Str("action") ?? "").Trim().ToLowerInvariant();
        if (!DecalActions.Contains(action)) return ToolResult.Fail("action is " + string.Join(", ", DecalActions) + ".");
        if (action == "list") return new ToolResult(DecalList(s).ToJsonString(Opts));
        if (!CanChange(s, out why)) return why!;
        var palette = s.View.DecalPalette;
        PaletteDecal? Named() => a.Str("name")?.Trim() is { Length: > 0 } n ? palette.FirstOrDefault(d => string.Equals(d.Name, n, StringComparison.OrdinalIgnoreCase)) : null;
        switch (action)
        {
            case "add":
            {
                if (a.Str("picture")?.Trim() is not { Length: > 0 } picture) return ToolResult.Fail("picture is needed: a file or a web link.");
                if (palette.Count >= PaletteDecal.MostInAPalette) return ToolResult.Fail($"The scenario holds {PaletteDecal.MostInAPalette} decals at most: remove one first.");
                if (await _a.KeepPicture(picture, false) is not { } kept) return ToolResult.Fail($"{picture} could not be used as a picture.");
                var size = Math.Clamp(a.Num("size") ?? DecalSize, PaletteDecal.SmallestSize, PaletteDecal.LargestSize);
                var name = a.Str("name")?.Trim() is { Length: > 0 } given ? given : System.IO.Path.GetFileNameWithoutExtension(picture);
                hud.N.ShareThen(new[] { kept }, new SetDecalPalette { Entries = palette.Select(x => x.Clone()).Append(new PaletteDecal { Name = name, Picture = kept, Size = size }).ToList() });
                for (double t = 0; t < RollWaitSeconds && s.View.DecalPalette.All(d => d.Picture != kept); t += Waits.Poll) await _a.Delay(Waits.Poll);
                _a.Log($"added the decal {name}");
                return new ToolResult($"{name} is among the decals.\n" + DecalList(s).ToJsonString(Opts));
            }
            case "remove":
            {
                if (Named() is not { } gone) return ToolResult.Fail("name is a decal's name (list).");
                s.Do(new SetDecalPalette { Entries = palette.Where(x => x != gone).Select(x => x.Clone()).ToList() });
                await _a.Landed(s);
                _a.Log($"took {gone.Name} out of the decals");
                return new ToolResult($"{gone.Name} is out of the decals; what was stamped with it stays.");
            }
            case "stamp":
            {
                if (Named() is not { } decal) return ToolResult.Fail("name is a decal's name (list).");
                double growth = Math.Clamp(a.Num("size") ?? 1, LeastGrowth, MostGrowth), turn = a.Num("turn") ?? 0;
                for (double t = 0; t < RollWaitSeconds && !hud.T.Decals.Sized(decal.Picture); t += Waits.Poll) await _a.Delay(Waits.Poll);
                if (a.Str("on") is { Length: > 0 } onId)
                {
                    if (hud.T.Pieces.NodeOf(Layers.Umpire, onId) is not { } node) return ToolResult.Fail($"No piece {onId} on this table.");
                    if (!StampTool.Stampable(node) || !s.CanTouch(node.Layer, node.Piece)) return ToolResult.Fail($"{PieceName(s, node.Piece)} takes no stamp: models, dice and effects do not, nor a piece with {DecalDef.MostOnAPiece} already.");
                    var at = new Vec2(a.Num("x") ?? node.Piece.Pos.X, a.Num("z") ?? node.Piece.Pos.Z);
                    var onPiece = StampTool.OnPieceAt(node, hud.T.Decals.ShapeOf(decal, at, growth, turn), turn);
                    s.Do(new SetDecals { Layer = node.Layer, Id = node.Piece.Id, Decals = node.Piece.Decals.Select(d => d.Clone()).Append(onPiece).ToList() });
                    await _a.Landed(s);
                    _a.Log($"stamped {decal.Name} on {PieceName(s, node.Piece)}");
                    return new ToolResult($"Stamped {decal.Name} on {PieceName(s, node.Piece)}.");
                }
                if (a.Num("x") is not { } x || a.Num("z") is not { } z) return ToolResult.Fail("x and z are needed, or on.");
                if (s.View.Decals.Values.Sum(l => l.Count) >= PaletteDecal.MostOnTheTable) return ToolResult.Fail($"The table holds {PaletteDecal.MostOnTheTable} stamps at most: lift some first.");
                var before = s.View.DecalLayer(Layers.Umpire).Select(d => d.Id).ToHashSet();
                s.Do(new StampDecal { Layer = Layers.Umpire, Decal = hud.T.Decals.ShapeOf(decal, new Vec2(x, z), growth, turn) });
                await _a.Landed(s);
                var made = s.View.DecalLayer(Layers.Umpire).FirstOrDefault(d => !before.Contains(d.Id));
                _a.Log($"stamped {decal.Name} on the table");
                return new ToolResult(made == null ? "Sent, but no stamp came back." : $"Stamped {decal.Name} at {x:0.##}, {z:0.##} ({made.Id}).", made == null);
            }
            default:
                return await Lift(a, s, hud);
        }
    }

    async Task<ToolResult> Lift(JsonElement a, Session s, Hud hud)
    {
        if (a.Flag("all") == true)
        {
            var stamps = s.View.Decals.Where(kv => s.CanEdit(kv.Key))
                .SelectMany(kv => kv.Value.Where(d => d.Id != null).Select(d => (Command)new RemoveDecal { Layer = kv.Key, DecalId = d.Id! })).ToList();
            if (stamps.Count == 0) return ToolResult.Fail("Nothing is stamped on the table.");
            s.Do(new Batch { Items = stamps });
            await _a.Landed(s);
            _a.Log($"lifted {stamps.Count} stamps off the table");
            return new ToolResult($"Lifted {stamps.Count} stamps off the table; those on pieces stay.");
        }
        if (a.Str("on") is { Length: > 0 } onId)
        {
            if (s.View.Find(Layers.Umpire, onId) is not { } p) return ToolResult.Fail($"No piece {onId} on this table.");
            int number = (int)(a.Num("number") ?? 0);
            if (number < 1 || number > p.Decals.Count) return ToolResult.Fail(p.Decals.Count == 0 ? $"{PieceName(s, p)} has no stamp." : $"number is 1 to {p.Decals.Count}.");
            var gone = p.Decals[number - 1];
            s.Do(new SetDecals { Layer = Layers.Umpire, Id = p.Id, Decals = p.Decals.Where(d => d != gone).Select(d => d.Clone()).ToList() });
            await _a.Landed(s);
            _a.Log($"lifted {gone.Name} off {PieceName(s, p)}");
            return new ToolResult($"Lifted {gone.Name} off {PieceName(s, p)}.");
        }
        if (a.Str("id") is not { Length: > 0 } id || s.View.DecalLayer(Layers.Umpire).FirstOrDefault(d => d.Id == id) is not { } stamp) return ToolResult.Fail("Give a table stamp's id, on and number for a piece's, or all (list shows them).");
        s.Do(new RemoveDecal { Layer = Layers.Umpire, DecalId = id });
        await _a.Landed(s);
        _a.Log($"lifted {stamp.Name} off the table");
        return new ToolResult($"Lifted {stamp.Name} off the table.");
    }

    static JsonObject DecalList(Session s)
    {
        JsonObject Stamp(DecalDef d, int? number = null) => new()
        {
            ["id"] = number == null ? d.Id : null, ["number"] = number, ["name"] = d.Name, ["x"] = Math.Round(d.X, 3), ["z"] = Math.Round(d.Z, 3),
            ["width"] = Math.Round(d.W, 3), ["height"] = Math.Round(d.H, 3), ["turn"] = Math.Round(d.Rot, 1), ["face"] = number == null ? null : d.Face,
        };
        var table = s.View.DecalLayer(Layers.Umpire);
        var onPieces = s.View.Layer(Layers.Umpire).Where(p => p.Decals.Count > 0).ToList();
        return new JsonObject
        {
            ["decals"] = new JsonArray(s.View.DecalPalette.Select(d => (JsonNode?)new JsonObject { ["name"] = d.Name, ["size"] = d.Size }).ToArray()),
            ["on_the_table"] = new JsonArray(table.Take(DecalsListed).Select(d => (JsonNode?)Stamp(d)).ToArray()),
            ["on_pieces"] = new JsonArray(onPieces.Take(DecalsListed).Select(p => (JsonNode?)new JsonObject
            {
                ["piece"] = p.Id, ["name"] = PieceName(s, p), ["stamps"] = new JsonArray(p.Decals.Select((d, i) => (JsonNode?)Stamp(d, i + 1)).ToArray()),
            }).ToArray()),
            ["note"] = table.Count > DecalsListed || onPieces.Count > DecalsListed ? $"each list shows at most {DecalsListed}" : null,
        };
    }

    async Task<ToolResult> Joining(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        var problems = new List<string>();
        if (a.Flag("pointers_always_shown") is { } always && always != s.View.PointersAlwaysShown)
        {
            if (!s.IsLead) problems.Add("only the host and the lead umpires set the pointer rule");
            else
            {
                s.Do(new SetPointerRule { AlwaysShown = always });
                await _a.Landed(s);
                _a.Log(always ? "showed every umpire's pointer" : "let umpires hide their pointers");
            }
        }
        var (lan, internet, port) = InviteDialog.Where(hud.N, _a.Cfg);
        bool locked = s.Access?.Password is { Length: > 0 };
        var o = new JsonObject
        {
            ["your_network"] = lan, ["internet"] = internet ?? (hud.N.UpnpStatus == "trying" ? "asking the router to open the port" : $"port {port} not open on the router"),
            ["port"] = port, ["password"] = s.Access == null ? null : locked ? "set" : "none: the door is open", ["pointers_always_shown"] = s.View.PointersAlwaysShown,
            ["invitation"] = s.Access == null ? null : InviteDialog.Invitation(s.View.Name, internet ?? lan ?? $"port {port}", locked ? PasswordWithheld : ""),
        };
        return new ToolResult((problems.Count > 0 ? "Not done: " + string.Join("; ", problems) + ".\n" : "") + o.ToJsonString(Opts), problems.Count > 0);
    }
}
