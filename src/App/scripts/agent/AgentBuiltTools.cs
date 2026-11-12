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

sealed class AgentBuiltTools
{
    readonly AgentTableTools _tools;
    readonly AgentEndpoint _a;

    public AgentBuiltTools(AgentTableTools tools, AgentEndpoint a)
    {
        _tools = tools;
        _a = a;
    }

    const int LaidWaitTenths = 50, MostListed = 200;
    const double DefaultHouses = 0.5, DefaultOutworks = 0.5, DefaultPlots = 0.5, DefaultBuildings = 0.25;
    public const string NotPainted = "Roads and towns are built, not painted: build_road lays a road and build_place a settlement.";

    static string LineNames => string.Join(", ", BuiltRoads.Roads.Concat(BuiltRoads.Waters));

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("build_place",
            "Lays a settlement, a fort or a single building on the map, as the Build tool's Places does. A settlement grows its lanes from the road that crosses it and fronts them with houses; " +
            "a fort is a star of bastions or a plain redoubt. It is generated once from the seed and stored as laid; nothing stands on water, marsh or a steep slope.",
            Args.Schema(new Arg("kind", "string", "settlement, fort, redoubt or building", true), new Arg("x", "number", "its middle, map units", true), new Arg("z", "number", "its middle, map units", true),
                new Arg("radius", "number", "settlement or fort: its reach in map units"), new Arg("houses", "number", "settlement: 0–1, scattered houses to packed streets (0.5 if left out)"),
                new Arg("walls", "number", "settlement: 0–1, none, a wall with towers, a bastioned trace (0 if left out)"), new Arg("grid", "boolean", "settlement: streets laid out square instead of grown"),
                new Arg("bastions", "number", "fort: 4–8, or 0 for a plain redoubt (5 if left out)"), new Arg("outworks", "number", "fort: 0–1, none, a glacis, ravelins (0.5 if left out)"),
                new Arg("class", "string", "building: " + string.Join(", ", BuiltThings.Blocks) + " (house if left out)"), new Arg("heading", "number", "fort or building: compass degrees it faces"),
                new Arg("seed", "number", "the same seed on the same ground lays the same place (random if left out)")),
            BuildPlace);
        yield return new AgentTool("build_road",
            "Lays a road or a watercourse through waypoints, as the Build tool's Roads does: " + LineNames + ". It can find its way over the relief between the waypoints; a road bridges the water it crosses, " +
            "and a stream or river that finds its way runs downhill from the first point.",
            Args.Schema(new Arg("points", "array", "[[x, z], …] waypoints in map units", true), new Arg("class", "string", LineNames + " (road if left out)"),
                new Arg("finds_way", "boolean", "over the relief between waypoints (true if left out)")),
            BuildRoad);
        yield return new AgentTool("build_details",
            "Settles the country along a stroke, as the Build tool's Details does: plot boundaries and farmsteads on field, grass and bare ground within the radius, each farm joined to the nearest road by a track.",
            Args.Schema(new Arg("points", "array", "[[x, z], …] the stroke in map units", true), new Arg("radius", "number", "the brush's radius, map units", true),
                new Arg("plots", "number", "0–1, big open fields to small bounded plots (0.5 if left out)"), new Arg("buildings", "number", "0–1, none, lone farms, hamlets (0.25 if left out)"),
                new Arg("seed", "number", "random if left out")),
            BuildDetails);
        yield return new AgentTool("list_built", "What is built on the map: each thing's id, kind, sheet, middle in map units, settings and the count of its parts by class.",
            Args.Schema(new Arg("offset", "number", "skip this many (0 if left out)")), x => Task.FromResult(ListBuilt(x)));
        yield return new AgentTool("remove_built", "Removes a built thing by its id (list_built), or lays it afresh another way with again.",
            Args.Schema(new Arg("id", "string", "the built thing's id", true), new Arg("again", "boolean", "lay it afresh instead of removing it; parts moved by hand stay")), RemoveBuilt);
    }

    bool BuildSheet(Session s, Hud hud, Vec2 at, out SheetDef sheet, out ToolResult? why)
    {
        sheet = null!;
        if (!AgentTableTools.CanChange(s, out why)) return false;
        if (!ReliefTool.Scaled(s)) { why = ToolResult.Fail(AgentTableTools.NeedsScaleWords); return false; }
        if (ReliefTool.Target(s, hud.T.Sheets, at) is not { } target) { why = ToolResult.Fail("No map sheet under that point (view_map)."); return false; }
        sheet = target;
        return true;
    }

    async Task<ToolResult> Laid(Session s, BuiltThing thing, string what)
    {
        if (thing.Parts.Count == 0) return ToolResult.Fail($"Nothing could be laid there: the ground under the {what} is water, marsh, too steep, or off the sheet.");
        int before = s.View.Built.Count;
        s.Do(new LayBuilt { Thing = thing });
        for (int i = 0; i < LaidWaitTenths && s.View.Built.Count == before; i++) await _a.Delay(0.1);
        if (s.View.Built.Count == before) return ToolResult.Fail($"The {what} was not laid (read_log).");
        _a.Log($"laid a {what}");
        return new ToolResult(BuiltRow(s, s.View.Built[^1]).ToJsonString(AgentTableTools.Opts));
    }

    static int SeedOf(JsonElement a) => a.Num("seed") is { } seed ? (int)Math.Clamp(seed, 1, int.MaxValue) : Random.Shared.Next(1, int.MaxValue);

    async Task<ToolResult> BuildPlace(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, hud)) return why!;
        var kind = (a.Str("kind") ?? "").Trim().ToLowerInvariant();
        if (Array.IndexOf(BuildPrefs.PlaceKinds, kind) < 0) return ToolResult.Fail("kind is settlement, fort, redoubt or building.");
        if (a.Num("x") is not { } x || a.Num("z") is not { } z) return ToolResult.Fail("x and z are needed, in map units.");
        if (!BuildSheet(s, hud, new Vec2(x, z), out var sheet, out why)) return why!;
        var site = BuildTool.Site(s, sheet);
        var at = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(x, z)));
        double heading = (a.Num("heading") ?? 0) * Math.PI / 180;
        var facing = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(x + Math.Sin(heading), z - Math.Cos(heading)))) - at;
        if (kind == BuiltKinds.Building)
        {
            var cls = (a.Str("class") ?? "house").Trim().ToLowerInvariant();
            if (!BuiltThings.Blocks.Contains(cls)) return ToolResult.Fail("class is one of " + string.Join(", ", BuiltThings.Blocks) + ".");
            return await Laid(s, BuiltThings.Building(site, sheet.Id, cls, at, facing.Left), cls);
        }
        if (a.Num("radius") is not > 0 and var _) return ToolResult.Fail("radius is needed, in map units.");
        double radius = a.Num("radius")!.Value;
        if (kind is BuiltKinds.Fort or BuildPrefs.Redoubt)
        {
            int bastions = kind == BuildPrefs.Redoubt ? 0 : (int)(a.Num("bastions") ?? Forts.FewestBastions + 1);
            if (bastions != 0 && bastions is < Forts.FewestBastions or > Forts.MostBastions) return ToolResult.Fail($"bastions is {Forts.FewestBastions}–{Forts.MostBastions}, or 0 for a redoubt.");
            return await Laid(s, Forts.Lay(site, sheet.Id, at, radius, SeedOf(a), bastions, Math.Clamp(a.Num("outworks") ?? DefaultOutworks, 0, 1), Math.Atan2(facing.Y, facing.X)), bastions == 0 ? "redoubt" : "fort");
        }
        double houses = Math.Clamp(a.Num("houses") ?? DefaultHouses, 0, 1), walls = Math.Clamp(a.Num("walls") ?? 0, 0, 1);
        return await Laid(s, Settlements.Lay(site, sheet.Id, at, radius, SeedOf(a), houses, walls, a.Flag("grid") == true ? 1 : 0), "settlement");
    }

    async Task<ToolResult> BuildRoad(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, hud)) return why!;
        if (AgentTableTools.Points(a) is not { Count: > 0 } pts) return ToolResult.Fail("points is needed: [[x, z], …], at most " + AgentTableTools.MostPoints + ".");
        var cls = (a.Str("class") ?? "road").Trim().ToLowerInvariant();
        if (!BuiltRoads.Roads.Contains(cls) && !BuiltRoads.Waters.Contains(cls)) return ToolResult.Fail("class is one of " + LineNames + ".");
        bool findsWay = a.Flag("finds_way") != false;
        if (pts.Count < 2 && !(findsWay && BuiltRoads.IsWater(cls))) return ToolResult.Fail("A road takes two or more waypoints.");
        if (!BuildSheet(s, hud, pts[0], out var sheet, out why)) return why!;
        return await Laid(s, BuiltRoads.Lay(BuildTool.Site(s, sheet), sheet.Id, pts.Select(p => Spot.Of(BuiltGeometry.ToSheet(sheet, p))).ToList(), cls, findsWay), cls);
    }

    async Task<ToolResult> BuildDetails(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, hud)) return why!;
        if (AgentTableTools.Points(a) is not { Count: > 0 } pts) return ToolResult.Fail("points is needed: [[x, z], …], at most " + AgentTableTools.MostPoints + ".");
        if (a.Num("radius") is not > 0 and var _) return ToolResult.Fail("radius is needed, in map units.");
        if (!BuildSheet(s, hud, pts[0], out var sheet, out why)) return why!;
        double plots = Math.Clamp(a.Num("plots") ?? DefaultPlots, 0, 1), buildings = Math.Clamp(a.Num("buildings") ?? DefaultBuildings, 0, 1);
        var stroke = pts.Select(p => Spot.Of(BuiltGeometry.ToSheet(sheet, p))).ToList();
        return await Laid(s, BuiltDetails.Lay(BuildTool.Site(s, sheet), sheet.Id, stroke, a.Num("radius")!.Value, SeedOf(a), plots, buildings), "stretch of country");
    }

    static JsonObject BuiltRow(Session s, BuiltThing thing)
    {
        var row = new JsonObject { ["id"] = thing.Id, ["kind"] = thing.Kind, ["sheet"] = thing.Sheet, ["seed"] = thing.Seed };
        if (BuiltGeometry.Box(thing) is var (low, high) && s.View.Sheets.FirstOrDefault(x => x.Id == thing.Sheet) is { } sheet)
        {
            var middle = BuiltGeometry.ToWorld(sheet, new SheetPoint((low.X + high.X) / 2, (low.Y + high.Y) / 2));
            row["x"] = Math.Round(middle.X, 2);
            row["z"] = Math.Round(middle.Z, 2);
        }
        foreach (var (name, value) in thing.Settings) row[name] = Math.Round(value, 2);
        row["parts"] = new JsonObject(thing.Parts.GroupBy(p => p.Class).Select(g => new KeyValuePair<string, JsonNode?>(g.Key, g.Count())));
        return row;
    }

    ToolResult ListBuilt(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, _)) return why!;
        int offset = (int)Math.Max(0, a.Num("offset") ?? 0);
        var rows = new JsonArray(s.View.Built.Skip(offset).Take(MostListed).Select(b => (JsonNode?)BuiltRow(s, b)).ToArray());
        string more = s.View.Built.Count > offset + MostListed ? $"\n{s.View.Built.Count - offset - MostListed} more: pass offset {offset + MostListed}." : "";
        return new ToolResult(rows.ToJsonString(AgentTableTools.Opts) + more);
    }

    async Task<ToolResult> RemoveBuilt(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, _)) return why!;
        if (!AgentTableTools.CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Built.FirstOrDefault(b => b.Id == id) is not { } thing) return ToolResult.Fail("No such built thing (list_built).");
        if (a.Flag("again") == true)
        {
            if (s.View.Sheets.FirstOrDefault(x => x.Id == thing.Sheet) is not { } sheet) return ToolResult.Fail("Its sheet is gone.");
            s.Do(new SetBuilt { Thing = BuiltThings.Remade(BuildTool.Site(s, sheet, thing.Id), thing, BuiltThings.Rerolls(thing) ? Random.Shared.Next(1, int.MaxValue) : thing.Seed) });
            await _a.Delay(0.2);
            return new ToolResult(BuiltRow(s, s.View.Built.First(b => b.Id == id)).ToJsonString(AgentTableTools.Opts));
        }
        s.Do(new RemoveBuilt { Id = id });
        for (int i = 0; i < LaidWaitTenths && s.View.Built.Any(b => b.Id == id); i++) await _a.Delay(0.1);
        _a.Log("removed a built " + thing.Kind);
        return new ToolResult($"Removed the {thing.Kind} {id}.");
    }
}
