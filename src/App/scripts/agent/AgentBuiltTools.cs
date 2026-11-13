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
            "a fort is a star of bastions or a plain redoubt. It is generated once from the seed and stored as laid; nothing stands on water, marsh or a steep slope. " +
            "A place takes its living from the painted land round it: a harbour on the sea, fishing and a mill on a river, woodcutters by a wood, peat by a marsh, a quarry by rock. " +
            "Ramparts, moats and painted water shape the ground itself, and a fort is not laid over another place's buildings.",
            Args.Schema(new Arg("kind", "string", "settlement, fort, redoubt or building", true), new Arg("x", "number", "its middle, map units", true), new Arg("z", "number", "its middle, map units", true),
                new Arg("radius", "number", "settlement or fort: its reach in map units"), new Arg("houses", "number", "settlement: 0–1, scattered houses to packed streets (0.5 if left out)"),
                new Arg("walls", "number", "settlement: 0–1, none, a wall with towers, a bastioned trace (0 if left out)"), new Arg("manner", "string", "how the place is built: continental, english, northern or southern; on an ancient-style map roman, egyptian or tribal"),
                new Arg("plan", "string", "settlement: grown, street, green, round or grid (grown if left out)"),
                new Arg("form", "string", "fort: star, or camp for the rampart camp of an ancient map (star if left out)"),
                new Arg("bastions", "number", "fort: 4–8, or 0 for a plain redoubt (5 if left out)"), new Arg("outworks", "number", "fort: 0–1, none, a glacis, ravelins (0.5 if left out)"),
                new Arg("class", "string", "building: " + string.Join(", ", BuiltThings.Blocks) + " (house if left out)"), new Arg("heading", "number", "fort or building: compass degrees it faces"),
                new Arg("seed", "number", "the same seed on the same ground lays the same place (random if left out)"),
                new Arg("name", "string", "settlement or fort: the name lettered beside it (none if left out)")),
            BuildPlace);
        yield return new AgentTool("build_road",
            "Lays a road or a watercourse through waypoints, as the Build tool's Roads does: " + LineNames + ". It can find its way over the relief between the waypoints; a road bridges the water it crosses, " +
            "and a stream or river that finds its way runs downhill from the first point.",
            Args.Schema(new Arg("points", Args.Points, "[[x, z], …] waypoints in map units", true), new Arg("class", "string", LineNames + " (road if left out)"),
                new Arg("finds_way", "boolean", "over the relief between waypoints (true if left out)")),
            BuildRoad);
        yield return new AgentTool("build_details",
            "Settles the country along a stroke, as the Build tool's Details does: plot boundaries and farmsteads on field, grass and bare ground within the radius, each farm joined to the nearest road by a track.",
            Args.Schema(new Arg("points", Args.Points, "[[x, z], …] the stroke in map units", true), new Arg("radius", "number", "the brush's radius, map units", true),
                new Arg("plots", "number", "0–1, big open fields to small bounded plots (0.5 if left out)"), new Arg("buildings", "number", "0–1, none, lone farms, hamlets (0.25 if left out)"),
                new Arg("seed", "number", "random if left out")),
            BuildDetails);
        yield return new AgentTool("list_built", "What is built on the map: each thing's id, kind, sheet, middle in map units, settings and the count of its parts by class. " +
            "With id, that one thing in full: every part by its number with its class, place and whether it is kept, and the corners of its wall.",
            Args.Schema(new Arg("id", "string", "one built thing, in full"), new Arg("offset", "number", "skip this many things, or parts with id (0 if left out)")), x => Task.FromResult(ListBuilt(x)));
        yield return new AgentTool("remove_built", "Removes a built thing by its id (list_built), or lays it afresh another way with again.",
            Args.Schema(new Arg("id", "string", "the built thing's id", true), new Arg("again", "boolean", "lay it afresh instead of removing it; parts moved by hand stay")), RemoveBuilt);
        yield return new AgentTool("add_to_built", "Stands one thing from the catalogue at a point, as a part of a built place: a building, a well, a ship, a quarry, a quay. It is kept when the place is laid again.",
            Args.Schema(new Arg("id", "string", "the built place's id (list_built)", true), new Arg("thing", "string", "the catalogue thing: " + string.Join(", ", BuiltThings.Addable), true),
                new Arg("x", "number", "where, map units", true), new Arg("z", "number", "where, map units", true)), AddToBuilt);
        yield return new AgentTool("shape_built", "Lays a settlement afresh with other houses, walls, plan or manner, as Pick does. Parts moved or added by hand stay.",
            Args.Schema(new Arg("id", "string", "the settlement's id (list_built)", true), new Arg("houses", "number", "0 scattered to 1 packed"), new Arg("walls", "number", "0 open, 0.5 walled, 1 bastioned"),
                new Arg("plan", "string", "grown, street, green, round or grid"), new Arg("manner", "string", "how it is built: " + string.Join(", ", Manners.All.Select(m => m.Id))),
                new Arg("seed", "number", "another seed lays it another way")), ShapeBuilt);
        yield return new AgentTool("edit_built",
            "Changes a built thing by hand, as Pick does with it or one of its parts chosen (list_built with id numbers the parts and gives the wall's corners): " +
            "move puts the thing's middle, or a part, at x, z; turn turns it; size makes a part larger or smaller; keep holds a part, or the whole thing without a part, as it is when the place is laid again and free lets it go; " +
            "remove_part takes a part out; fill adds houses along a stroke inside a settlement and thin takes houses out along it; wall_corner moves a corner of a settlement's wall, or a bastion's tip, to x, z; " +
            "gate opens a gate in the wall at x, z, or closes the gate there; name letters a settlement's or fort's name beside it. One undoable change.",
            Args.Schema(new Arg("id", "string", "the built thing's id (list_built)", true), new Arg("action", "string", string.Join(", ", Edits), true),
                new Arg("part", "number", "a part's number (list_built with id); left out, the whole thing"), new Arg("x", "number", "map units"), new Arg("z", "number", "map units"),
                new Arg("degrees", "number", "turn: degrees clockwise"), new Arg("by", "number", $"size: times its size, {LeastSizing}–{MostSizing}"),
                new Arg("points", Args.Points, "fill and thin: [[x, z], …] the stroke in map units"), new Arg("radius", "number", "fill and thin: the brush's radius in map units"),
                new Arg("corner", "number", "wall_corner: the corner's number (list_built with id)"), new Arg("name", "string", "name: the name; empty for none")),
            EditBuilt);
    }

    const string Move = "move", Turn = "turn", Size = "size", Keep = "keep", Free = "free", RemovePart = "remove_part", Fill = "fill", Thin = "thin", WallCorner = "wall_corner", Gate = "gate", Name = "name";
    static readonly string[] Edits = [Move, Turn, Size, Keep, Free, RemovePart, Fill, Thin, WallCorner, Gate, Name];
    const double LeastSizing = 0.2, MostSizing = 5, GateReachM = 40;

    async Task<ToolResult> EditBuilt(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, _)) return why!;
        if (!AgentTableTools.CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Built.FirstOrDefault(b => b.Id == id) is not { } thing) return ToolResult.Fail("No such built thing (list_built).");
        if (s.View.Sheets.FirstOrDefault(x => x.Id == thing.Sheet) is not { } sheet) return ToolResult.Fail("Its sheet is gone.");
        var action = (a.Str("action") ?? "").Trim().ToLowerInvariant();
        if (Array.IndexOf(Edits, action) < 0) return ToolResult.Fail("action is one of " + string.Join(", ", Edits) + ".");
        int part = a.Num("part") is { } number ? (int)number : -1;
        if (a.Num("part") != null && (part < 0 || part >= thing.Parts.Count)) return ToolResult.Fail($"part is 0 to {thing.Parts.Count - 1} (list_built with id).");
        Vec2? to = a.Num("x") is { } x && a.Num("z") is { } z ? new Vec2(x, z) : null;
        if (action is Move or WallCorner or Gate && to == null) return ToolResult.Fail($"{action} takes x and z, in map units.");
        if (action is Size or RemovePart && part < 0) return ToolResult.Fail($"{action} takes part (list_built with id).");
        var site = BuildTool.Site(s, sheet, thing.Id);
        var (changed, refused) = action switch
        {
            Move => (Moved(sheet, thing, part, to!.Value), null),
            Turn => a.Num("degrees") is { } degrees ? (part >= 0 ? BuiltThings.WithPartTurned(thing, part, degrees * Math.PI / 180) : BuiltThings.Turned(thing, degrees * Math.PI / 180), null) : (null, "turn takes degrees."),
            Size => a.Num("by") is >= LeastSizing and <= MostSizing and var by && thing.Parts[part].Kind != PartKinds.Sign ? (BuiltThings.WithPartSized(thing, part, by), null) : (null, $"size takes by, {LeastSizing}–{MostSizing}, and a part that is not a sign."),
            Keep or Free => (BuiltThings.WithKept(thing, part, action == Keep), null),
            RemovePart => thing.Parts.Count > 1 ? (BuiltThings.WithoutPart(thing, part), null) : (null, "It is the thing's last part: remove_built removes the thing."),
            Fill or Thin => Swept(site, sheet, thing, a, action == Fill),
            WallCorner => (a.Num("corner") is >= 0 and var corner ? Walls.CornerMoved(site, thing, (int)corner, On(sheet, to!.Value)) : null, "No such corner on its wall (list_built with id gives them)."),
            Gate => (Walls.Gated(site, thing, On(sheet, to!.Value), site.M(GateReachM)), "No wall of this place runs there."),
            _ => BuiltThings.Named(thing) ? (Named(thing, a.Str("name")), null) : ((BuiltThing?)null, "Only a settlement or a fort carries a name."),
        };
        if (changed == null) return ToolResult.Fail(refused ?? "Nothing changed.");
        s.Do(new SetBuilt { Thing = changed });
        await _a.Delay(0.2);
        _a.Log($"changed a built {thing.Kind} ({action.Replace('_', ' ')})");
        return new ToolResult(BuiltRow(s, s.View.Built.First(b => b.Id == id)).ToJsonString(AgentTableTools.Opts));
    }

    static Spot On(SheetDef sheet, Vec2 world) => Spot.Of(BuiltGeometry.ToSheet(sheet, world));

    static BuiltThing Moved(SheetDef sheet, BuiltThing thing, int part, Vec2 to)
    {
        var goal = BuiltGeometry.ToSheet(sheet, to);
        var from = part >= 0 ? BuiltSite.Round(thing.Parts[part]).At.Stored : BuiltGeometry.Box(thing) is var (low, high) ? new SheetPoint((low.X + high.X) / 2, (low.Y + high.Y) / 2) : goal;
        return part >= 0 ? BuiltThings.WithPartMoved(thing, part, goal.X - from.X, goal.Y - from.Y) : BuiltGeometry.Moved(thing, goal.X - from.X, goal.Y - from.Y);
    }

    static (BuiltThing?, string?) Swept(BuiltSite site, SheetDef sheet, BuiltThing place, JsonElement a, bool fills)
    {
        if (place.Kind != BuiltKinds.Settlement) return (null, "Only a settlement is filled or thinned.");
        if (AgentTableTools.Points(a) is not { Count: > 0 } pts || a.Num("radius") is not > 0 and var _) return (null, "fill and thin take points, [[x, z], …], and radius, in map units.");
        var stroke = pts.Select(p => On(sheet, p)).ToList();
        int seed = Random.Shared.Next(1, int.MaxValue);
        return (fills ? Brushed.Filled(site, place, stroke, a.Num("radius")!.Value, seed) : Brushed.Thinned(site, place, stroke, a.Num("radius")!.Value, seed), "The stroke passes over no houses of this place, or no room for more.");
    }

    static BuiltThing Named(BuiltThing thing, string? name)
    {
        var named = thing.Clone();
        named.Name = PlaceNames.Tidy(name);
        return named;
    }

    async Task<ToolResult> AddToBuilt(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, _)) return why!;
        if (!AgentTableTools.CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Built.FirstOrDefault(b => b.Id == id) is not { } place) return ToolResult.Fail("No such built thing (list_built).");
        if (s.View.Sheets.FirstOrDefault(x => x.Id == place.Sheet) is not { } sheet) return ToolResult.Fail("Its sheet is gone.");
        var thing = (a.Str("thing") ?? "").Trim().ToLowerInvariant();
        if (!BuiltThings.Addable.Contains(thing)) return ToolResult.Fail("thing is one of " + string.Join(", ", BuiltThings.Addable) + ".");
        if (a.Num("x") is not { } x || a.Num("z") is not { } z) return ToolResult.Fail("x and z are needed, in map units.");
        var made = BuiltThings.Made(BuildTool.Site(s, sheet, place.Id, Manners.Of(place)), thing, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(x, z))));
        s.Do(new SetBuilt { Thing = BuiltThings.WithParts(place, made) });
        await _a.Delay(0.2);
        _a.Log("stood a " + thing);
        return new ToolResult(BuiltRow(s, s.View.Built.First(b => b.Id == id)).ToJsonString(AgentTableTools.Opts));
    }

    async Task<ToolResult> ShapeBuilt(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, _)) return why!;
        if (!AgentTableTools.CanChange(s, out why)) return why!;
        var id = a.Str("id") ?? "";
        if (s.View.Built.FirstOrDefault(b => b.Id == id) is not { Kind: BuiltKinds.Settlement } place) return ToolResult.Fail("No such settlement (list_built).");
        if (s.View.Sheets.FirstOrDefault(x => x.Id == place.Sheet) is not { } sheet) return ToolResult.Fail("Its sheet is gone.");
        var shaped = place.Clone();
        if (a.Num("houses") is { } houses) shaped.Settings[Settlements.Houses] = Math.Clamp(houses, 0, 1);
        if (a.Num("walls") is { } walls) shaped.Settings[Settlements.Walls] = Math.Clamp(walls, 0, 1);
        if (a.Str("plan") is { Length: > 0 } plan)
        {
            if (Array.IndexOf(Plans.All, plan.Trim().ToLowerInvariant()) < 0) return ToolResult.Fail("plan is one of " + string.Join(", ", Plans.All) + ".");
            shaped.Settings[Settlements.Plan] = Plans.Value(plan.Trim().ToLowerInvariant());
        }
        if (a.Str("manner") is { Length: > 0 } manner)
        {
            if (Manners.All.All(m => m.Id != manner.Trim().ToLowerInvariant())) return ToolResult.Fail("manner is one of " + string.Join(", ", Manners.All.Select(m => m.Id)) + ".");
            shaped.Settings[Manners.Setting] = Manners.Of(manner.Trim().ToLowerInvariant());
        }
        s.Do(new SetBuilt { Thing = BuiltThings.Remade(BuildTool.Site(s, sheet, place.Id), shaped, a.Num("seed") is { } seed ? (int)Math.Clamp(seed, 1, int.MaxValue) : place.Seed) });
        await _a.Delay(0.2);
        _a.Log("shaped a settlement");
        return new ToolResult(BuiltRow(s, s.View.Built.First(b => b.Id == id)).ToJsonString(AgentTableTools.Opts));
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

    async Task<ToolResult> Laid(Session s, BuiltThing thing, string what, string? name = null)
    {
        if (thing.Parts.Count == 0) return ToolResult.Fail($"Nothing could be laid there: the ground under the {what} is water, marsh, too steep, or off the sheet.");
        int before = s.View.Built.Count;
        if (BuiltThings.Named(thing)) thing.Name = name;
        s.Do(BuildTool.Laying(s, thing));
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
        var site = BuildTool.Site(s, sheet, null, a.Str("manner") is { Length: > 0 } manner ? Manners.Of(manner.Trim().ToLowerInvariant()) : -1);
        var at = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(x, z)));
        double heading = (a.Num("heading") ?? 0) * Math.PI / 180;
        var facing = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(x + Math.Sin(heading), z - Math.Cos(heading)))) - at;
        if (kind == BuiltKinds.Building)
        {
            var cls = (a.Str("class") ?? "house").Trim().ToLowerInvariant();
            if (!BuiltThings.Blocks.Contains(cls)) return ToolResult.Fail("class is one of " + string.Join(", ", BuiltThings.Blocks) + ".");
            return await Laid(s, BuildTool.Heathen(BuiltThings.Building(site, sheet.Id, cls, at, facing.Left), BuildTool.Ancient(s)), cls);
        }
        if (a.Num("radius") is not > 0 and var _) return ToolResult.Fail("radius is needed, in map units.");
        double radius = a.Num("radius")!.Value;
        if (kind is BuiltKinds.Fort or BuildPrefs.Redoubt)
        {
            if (kind == BuiltKinds.Fort && (a.Str("form") ?? "").Trim().ToLowerInvariant() == ToolIcon.Camp)
                return await Laid(s, Camps.Lay(site, sheet.Id, at, radius, SeedOf(a), Math.Atan2(facing.Y, facing.X)), ToolIcon.Camp, a.Str("name"));
            int bastions = kind == BuildPrefs.Redoubt ? 0 : (int)(a.Num("bastions") ?? Forts.FewestBastions + 1);
            if (bastions != 0 && bastions is < Forts.FewestBastions or > Forts.MostBastions) return ToolResult.Fail($"bastions is {Forts.FewestBastions}–{Forts.MostBastions}, or 0 for a redoubt.");
            var fort = Forts.Lay(site, sheet.Id, at, radius, SeedOf(a), bastions, Math.Clamp(a.Num("outworks") ?? DefaultOutworks, 0, 1), Math.Atan2(facing.Y, facing.X));
            return await Laid(s, fort, bastions == 0 ? "redoubt" : "fort", a.Str("name"));
        }
        double houses = Math.Clamp(a.Num("houses") ?? DefaultHouses, 0, 1), walls = Math.Clamp(a.Num("walls") ?? 0, 0, 1);
        var plan = (a.Str("plan") ?? Plans.Grown).Trim().ToLowerInvariant();
        if (Array.IndexOf(Plans.All, plan) < 0) return ToolResult.Fail("plan is " + string.Join(", ", Plans.All) + ".");
        return await Laid(s, Settlements.Lay(site, sheet.Id, at, radius, SeedOf(a), houses, walls, Plans.Value(plan), BuildTool.Ancient(s)), "settlement", a.Str("name"));
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
        if (thing.Name != null) row["name"] = thing.Name;
        foreach (var (name, value) in thing.Settings) row[name] = Math.Round(value, 2);
        row["parts"] = new JsonObject(thing.Parts.GroupBy(p => p.Class).Select(g => new KeyValuePair<string, JsonNode?>(g.Key, g.Count())));
        return row;
    }

    ToolResult ListBuilt(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, _)) return why!;
        int offset = (int)Math.Max(0, a.Num("offset") ?? 0);
        if (a.Str("id") is { Length: > 0 } id) return s.View.Built.FirstOrDefault(b => b.Id == id) is { } one ? InFull(s, one, offset) : ToolResult.Fail("No such built thing (list_built).");
        var rows = new JsonArray(s.View.Built.Skip(offset).Take(MostListed).Select(b => (JsonNode?)BuiltRow(s, b)).ToArray());
        string more = s.View.Built.Count > offset + MostListed ? $"\n{s.View.Built.Count - offset - MostListed} more: pass offset {offset + MostListed}." : "";
        return new ToolResult(rows.ToJsonString(AgentTableTools.Opts) + more);
    }

    static ToolResult InFull(Session s, BuiltThing thing, int offset)
    {
        var row = BuiltRow(s, thing);
        if (s.View.Sheets.FirstOrDefault(x => x.Id == thing.Sheet) is not { } sheet) return new ToolResult(row.ToJsonString(AgentTableTools.Opts));
        JsonArray Place(Spot at) => BuiltGeometry.ToWorld(sheet, at.Stored) is var p ? new JsonArray(Math.Round(p.X, 2), Math.Round(p.Z, 2)) : new JsonArray();
        row["kept"] = BuiltThings.Fixed(thing);
        row["parts"] = new JsonArray(thing.Parts.Select((part, i) => (JsonNode?)new JsonObject { ["part"] = i, ["class"] = part.Class, ["at"] = Place(BuiltSite.Round(part).At), ["kept"] = part.Kept })
            .Skip(offset).Take(MostListed).ToArray());
        if (Walls.Handles(thing) is { } trace) row["wall_corners"] = new JsonArray(trace.Corners.Select(c => (JsonNode?)Place(c)).ToArray());
        string more = thing.Parts.Count > offset + MostListed ? $"\n{thing.Parts.Count - offset - MostListed} more parts: pass offset {offset + MostListed}." : "";
        return new ToolResult(row.ToJsonString(AgentTableTools.Opts) + more);
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
