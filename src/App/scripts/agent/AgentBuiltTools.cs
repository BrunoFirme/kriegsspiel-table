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

    const string CultureWords = "what it is built in: the name of a mix kept in the scenario, or a culture of the map's age (read_scenario lists both)";
    const string BuildersWords = "a culture of the map's age that raises its walls and roofs", AdornersWords = "a culture of the map's age that gives its towers, domes and gateways";
    const string QuarterWords = "a culture of the map's age that builds and adorns one quarter of it, or none";
    const string ShareWords = "how much of the place the quarter takes, 0.1 to 0.5 (0.25 if left out)";

    static Arg[] Cultured => [new Arg("culture", "string", CultureWords), new Arg("builders", "string", BuildersWords), new Arg("adorners", "string", AdornersWords),
        new Arg("quarter", "string", QuarterWords), new Arg("quarter_share", "number", ShareWords)];

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("build_place",
            "Lays a settlement, a fort or a single building on the map, as the Build tool's Places does. A settlement fits the ground inside its reach: it keeps off water, woods and steep slopes, runs out along the roads, " +
            "bridges a river once, takes the road that crosses it as its main street, and grows streets that follow the slope, the shore and each other, closest at the middle; " +
            "the fort is the culture's own and the fieldwork the age's. It is generated once from the seed and stored as laid; nothing stands on water, marsh or a steep slope. " +
            "A place takes its living from the painted land round it: a harbour on the sea, fishing and a mill on a river, woodcutters by a wood, peat by a marsh, a quarry by rock. " +
            "Ramparts, moats and painted water shape the ground itself, and a fort is not laid over another place's buildings.",
            Args.Schema([new Arg("kind", "string", PlaceKindWords, true), new Arg("x", "number", "its middle, map units", true), new Arg("z", "number", "its middle, map units", true),
                new Arg("radius", "number", "settlement or fort: its reach in map units"), new Arg("houses", "number", "settlement: 0–1, scattered houses to packed streets (0.5 if left out)"),
                new Arg("spread", "number", "settlement: 0–1, a close huddle to straggling out along its roads (0.5 if left out)"),
                new Arg("order", "number", "settlement: 0–1, winding as it grew to straight and laid out, with planned quarters (0.5 if left out)"),
                new Arg("ravaged", "number", "settlement: 0–1, the share of its homes damaged as the age did it: burned shells, and rubble and craters once guns and bombs come in (0 if left out)"),
                new Arg("walls", "number", WallsWords + " (0 if left out)"), .. Cultured,
                new Arg("plan", "string", "settlement: one the culture offers of " + string.Join(", ", Plans.All) + " (the culture's own if left out: a small place takes its village plan)"),
                new Arg("bastions", "number", "star fort: 4–8 (5 if left out)"), new Arg("outworks", "number", "star fort or redoubt: 0–1, none, a glacis, ravelins (0.5 if left out)"),
                new Arg("class", "string", "building: its catalogue name, such as house, farm, church or barn (house if left out); one that does not fit the map's age is answered with those that do"),
                new Arg("heading", "number", "fort or building: compass degrees it faces; settlement: the line of its main street where no road decides it"),
                new Arg("seed", "number", "the same seed on the same ground lays the same place (random if left out)"),
                new Arg("name", "string", "settlement or fort: the name lettered beside it (none if left out)")]),
            BuildPlace);
        yield return new AgentTool("build_road",
            "Lays a road or a watercourse through waypoints, as the Build tool's Roads does: " + LineNames + ", those of them the map's age has (chaussee in the 18th-century styles; highway, railway, trench and wire in the Modern). " +
            "It can find its way over the relief between the waypoints; a road bridges the water it crosses, " +
            "and a stream or river that finds its way runs downhill from the first point.",
            Args.Schema(new Arg("points", Args.Points, "[[x, z], …] waypoints in map units", true), new Arg("class", "string", LineNames + " (road if left out)"),
                new Arg("finds_way", "boolean", "over the relief between waypoints (true if left out)")),
            BuildRoad);
        yield return new AgentTool("build_details",
            "Settles the country along a stroke, as the Build tool's Details does: plot boundaries and farmsteads on field, grass and bare ground within the radius, each farm joined to the nearest road by a track.",
            Args.Schema([new Arg("points", Args.Points, "[[x, z], …] the stroke in map units", true), new Arg("radius", "number", "the brush's radius, map units", true),
                new Arg("plots", "number", "0–1, big open fields to small bounded plots (0.5 if left out)"), new Arg("buildings", "number", "0–1, none, lone farms, hamlets (0.25 if left out)"),
                new Arg("shares", "boolean", "fields as strips of one width running out from the stroke as far as water, wood or a place"), .. Cultured,
                new Arg("seed", "number", "random if left out")]),
            BuildDetails);
        yield return new AgentTool("list_built", "What is built on the map: each thing's id, kind, sheet, middle in map units, culture, plan or fort, settings and the count of its parts by class. " +
            "With id, that one thing in full: every part by its number with its class, place and whether it is kept, and the corners of its wall.",
            Args.Schema(new Arg("id", "string", "one built thing, in full"), new Arg("offset", "number", "skip this many things, or parts with id (0 if left out)")), x => Task.FromResult(ListBuilt(x)));
        yield return new AgentTool("remove_built", "Removes a built thing by its id (list_built), or lays it afresh another way with again.",
            Args.Schema(new Arg("id", "string", "the built thing's id", true), new Arg("again", "boolean", "lay it afresh instead of removing it; parts moved by hand stay")), RemoveBuilt);
        yield return new AgentTool("add_to_built", "Stands one thing from the catalogue at a point, as a part of a built place: a building, a well, a ship, a quarry, a quay, an airstrip. It is kept when the place is laid again; " +
            "edit_built then turns and sizes it by its part number.",
            Args.Schema(new Arg("id", "string", "the built place's id (list_built)", true),
                new Arg("thing", "string", "the catalogue thing's name, such as house, well, ship, quay or tree; one that does not fit the map's age is answered with those that do", true),
                new Arg("x", "number", "where, map units", true), new Arg("z", "number", "where, map units", true)), AddToBuilt);
        yield return new AgentTool("shape_built", "Lays a settlement, a fort, a building or a stretch of country afresh another way, as Pick does with it chosen: its culture, builders, adorners and quarter; " +
            "a settlement's houses, spread, order, walls and plan; a star fort's bastions and outworks. Parts moved or added by hand stay; a thing kept as it is (edit_built keep) is refused.",
            Args.Schema([new Arg("id", "string", "the built thing's id (list_built)", true), new Arg("houses", "number", "settlement: 0 scattered to 1 packed"), new Arg("spread", "number", "settlement: 0 huddled to 1 straggling"),
                new Arg("order", "number", "settlement: 0 winding to 1 laid out"), new Arg("ravaged", "number", "settlement: 0 none to 1 most homes burned, shelled or bombed as the age did it"),
                new Arg("walls", "number", WallsWords),
                new Arg("plan", "string", "settlement: one the culture offers of " + string.Join(", ", Plans.All)), .. Cultured,
                new Arg("bastions", "number", "star fort: 4–8"), new Arg("outworks", "number", "star fort: 0–1, none, a glacis, ravelins"),
                new Arg("seed", "number", "another seed lays it another way")]), ShapeBuilt);
        yield return new AgentTool("culture_mix", "Keeps a mix of cultures in the scenario under a name, as the Build panel's Add mix and Keep mix do: one culture lays a place out, others build and adorn it, and one may hold a quarter. " +
            "build_place, build_details and shape_built then take the name as culture, and read_scenario lists the mixes. With from, the mix is the one a built thing has; with remove, the name is taken off the list and places laid in it stay.",
            Args.Schema([new Arg("name", "string", "the mix's name on the Culture list", true), new Arg("from", "string", "a built thing's id (list_built) whose mix is kept"), .. Cultured.Skip(1),
                new Arg("lays", "string", "a culture of the map's age that lays the place out"), new Arg("new_name", "string", "another name for a mix already kept"),
                new Arg("remove", "boolean", "take the mix off the list")]), CultureMix);
        yield return new AgentTool("edit_built",
            "Changes a built thing by hand, as Pick does with it or one of its parts chosen (list_built with id numbers the parts and gives the wall's corners): " +
            "move puts the thing's middle, or a part, at x, z; turn turns it; size makes a part larger or smaller; keep holds a part, or the whole thing without a part, as it is when the place is laid again and free lets it go; " +
            "remove_part takes a part out; fill adds houses along a stroke inside a settlement and thin takes houses out along it; lamps_out puts out its lamps along a stroke and lamps_lit lights them again; wall_corner moves a corner of a settlement's wall, or a bastion's tip, to x, z; " +
            "gate opens a gate in the wall at x, z, or closes the gate there; name letters a settlement's or fort's name beside it. One undoable change.",
            Args.Schema(new Arg("id", "string", "the built thing's id (list_built)", true), new Arg("action", "string", string.Join(", ", Edits), true),
                new Arg("part", "number", "a part's number (list_built with id); left out, the whole thing"), new Arg("x", "number", "map units"), new Arg("z", "number", "map units"),
                new Arg("degrees", "number", "turn: degrees clockwise"), new Arg("by", "number", $"size: times its size, {LeastSizing}–{MostSizing}"),
                new Arg("points", Args.Points, "fill, thin, lamps_out and lamps_lit: [[x, z], …] the stroke in map units"), new Arg("radius", "number", "fill, thin, lamps_out and lamps_lit: the brush's radius in map units"),
                new Arg("corner", "number", "wall_corner: the corner's number (list_built with id)"), new Arg("name", "string", "name: the name; empty for none")),
            EditBuilt);
    }

    const string PlaceKindWords = "settlement, fort (the culture's own: camp, hillfort, fortress, castle, laager, star fort or strongpoint), redoubt (the age's fieldwork: redoubt, or battery in the Modern style; none on an ancient or medieval map) or building";
    const string WallsWords = "settlement: 0 open, 0.5 walled (Modern style: trenches and wire; Japanese: a castle inside; Rus': a kremlin inside a palisade; Mongol: a ring of carts), 1 a bastioned trace in the 18th-century styles, walled in the others";
    static readonly (string Arg, string Setting)[] SideArgs = [("builders", Manners.Builders), ("adorners", Manners.Adorners), ("quarter", Manners.Ward)];
    static readonly string[] Shapeable = [BuiltKinds.Settlement, BuiltKinds.Fort, BuiltKinds.Building, BuiltKinds.Details];
    static readonly string[] NamedSettings = [Manners.Setting, Manners.Builders, Manners.Adorners, Manners.Ward, Manners.WardShare, Settlements.Plan, Forts.Form];
    const string NoQuarter = "none";

    static string CulturesOf(int epoch) => string.Join(", ", Manners.In(epoch).Select(Manners.Id));

    static bool Known(string? asked, int epoch, out int manner)
    {
        var id = asked?.Trim().ToLowerInvariant() ?? "";
        manner = Manners.All.Any(m => m.Id == id) ? Manners.For(epoch, Manners.Of(id)) : -1;
        return manner >= 0;
    }

    static IEnumerable<NamedMix> Mixes(Session s) => (s.View.Cultures ?? []).Where(mix => Manners.Named(Manners.Parse(mix.Code).Lays).Epoch == BuildTool.Epoch(s));

    static NamedMix? MixNamed(Session s, string? name) => Mixes(s).FirstOrDefault(kept => string.Equals(kept.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    static string? Culture(Session s, JsonElement a, out int lays, out Manners.Mix? mix)
    {
        lays = -1;
        mix = null;
        if (a.Str("culture") is not { Length: > 0 } asked) return null;
        if (MixNamed(s, asked) is { } kept)
        {
            mix = Manners.Parse(kept.Code);
            lays = mix.Value.Lays;
            return null;
        }
        if (Known(asked, BuildTool.Epoch(s), out lays)) return null;
        var mixes = string.Join(", ", Mixes(s).Select(m => m.Name));
        return $"culture is one of {CulturesOf(BuildTool.Epoch(s))}{(mixes.Length > 0 ? ", or a mix kept in the scenario: " + mixes : "")}.";
    }

    static void Mixed(Manners.Mix mix, BuiltThing thing)
    {
        thing.Settings[Manners.Setting] = mix.Lays;
        thing.Settings[Manners.Builders] = mix.Builds;
        thing.Settings[Manners.Adorners] = mix.Adorns;
        thing.Settings[Manners.Ward] = mix.Ward;
        if (mix.Ward >= 0) thing.Settings[Manners.WardShare] = mix.Share;
    }

    static string? Sided(Session s, JsonElement a, Manners.Mix? mix, BuiltThing thing)
    {
        if (mix is { } kept) kept.Onto(thing);
        foreach (var (arg, setting) in SideArgs)
        {
            if (a.Str(arg)?.Trim().ToLowerInvariant() is not { Length: > 0 } asked) continue;
            if (setting == Manners.Ward && asked == NoQuarter) thing.Settings[setting] = -1;
            else if (Known(asked, BuildTool.Epoch(s), out int side)) thing.Settings[setting] = side;
            else return $"{arg} is one of {CulturesOf(BuildTool.Epoch(s))}{(setting == Manners.Ward ? ", or none" : "")}.";
        }
        if (a.Num("quarter_share") is { } share) thing.Settings[Manners.WardShare] = Math.Clamp(share, Manners.LeastWard, Manners.MostWard);
        return null;
    }

    static double Walled(Session s, double walls) =>
        !Defences.Bastioned(BuildTool.Epoch(s)) && walls >= Settlements.BastionedFrom ? (Settlements.WalledFrom + Settlements.BastionedFrom) / 2 : Math.Clamp(walls, 0, 1);

    static bool Modern(Session s) => BuildTool.Epoch(s) == Manners.Modern;

    static string? Unfit(Session s, string cls, bool blocksOnly)
    {
        var fitting = Catalogue.For(BuildTool.Ancient(s), blocksOnly, Modern(s), BuildTool.Epoch(s) == Manners.Medieval).SelectMany(section => section.Entries).Select(e => e.Id).ToList();
        return fitting.Contains(cls) ? null : "one that fits the map's age: " + string.Join(", ", fitting) + ".";
    }

    async Task<ToolResult> CultureMix(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, _)) return why!;
        if (!AgentTableTools.CanChange(s, out why)) return why!;
        if (PlaceNames.Tidy(a.Str("name")) is not { Length: > 0 } name) return ToolResult.Fail("name is needed.");
        var list = (s.View.Cultures ?? []).Select(mix => mix.Clone()).ToList();
        var known = list.FirstOrDefault(mix => string.Equals(mix.Name, name, StringComparison.OrdinalIgnoreCase));
        string did;
        if (a.Flag("remove") == true)
        {
            if (known == null) return ToolResult.Fail($"No mix {name} is kept (read_scenario lists them).");
            list.Remove(known);
            did = "took a mix off the Culture list";
        }
        else if (PlaceNames.Tidy(a.Str("new_name")) is { Length: > 0 } renamed)
        {
            if (known == null) return ToolResult.Fail($"No mix {name} is kept (read_scenario lists them).");
            if (list.Any(mix => mix != known && string.Equals(mix.Name, renamed, StringComparison.OrdinalIgnoreCase))) return ToolResult.Fail($"A mix is already named {renamed}.");
            known.Name = renamed;
            did = "renamed a mix";
        }
        else
        {
            if (Kept(s, a, known, out var mix) is { } refused) return ToolResult.Fail(refused);
            if (mix.Pure) return ToolResult.Fail("A mix needs a second culture: builders, adorners or a quarter other than the one that lays it.");
            list.RemoveAll(other => other == known);
            if (list.Count >= NamedMix.Most) return ToolResult.Fail($"The scenario keeps {NamedMix.Most} mixes at most.");
            list.Add(new NamedMix { Name = name, Code = mix.Code });
            did = "kept a mix of cultures";
        }
        s.Do(new SetCultures { Cultures = list });
        await _a.Delay(0.2);
        _a.Log(did);
        var kept = (s.View.Cultures ?? []).Select(mix => (JsonNode?)new JsonObject { ["name"] = mix.Name, ["mix"] = Manners.Parse(mix.Code).Name });
        return new ToolResult(new JsonArray(kept.ToArray()).ToJsonString(AgentTableTools.Opts));
    }

    static string? Kept(Session s, JsonElement a, NamedMix? known, out Manners.Mix mix)
    {
        int epoch = BuildTool.Epoch(s);
        var draft = new BuiltThing { Kind = BuiltKinds.Settlement };
        mix = default;
        if (a.Str("from") is { Length: > 0 } from)
        {
            if (s.View.Built.FirstOrDefault(b => b.Id == from) is not { } thing || Array.IndexOf(Shapeable, thing.Kind) < 0) return "No such settlement, fort, building or stretch of country (list_built).";
            Mixed(Manners.MixOf(thing), draft);
        }
        else if (known != null) Mixed(Manners.Parse(known.Code), draft);
        else draft.Settings[Manners.Setting] = Manners.For(epoch, -1);
        if (a.Str("lays") is { Length: > 0 } lays)
        {
            if (!Known(lays, epoch, out int laid)) return $"lays is one of {CulturesOf(epoch)}.";
            draft.Settings[Manners.Setting] = laid;
        }
        if (Sided(s, a, null, draft) is { } refused) return refused;
        mix = Manners.MixOf(draft);
        return Manners.Named(mix.Lays).Epoch == epoch ? null : "That mix is of another age than the map's.";
    }

    const string Move = "move", Turn = "turn", Size = "size", Keep = "keep", Free = "free", RemovePart = "remove_part", Fill = "fill", Thin = "thin", LampsOut = "lamps_out", LampsLit = "lamps_lit", WallCorner = "wall_corner", Gate = "gate", Name = "name";
    static readonly string[] Edits = [Move, Turn, Size, Keep, Free, RemovePart, Fill, Thin, LampsOut, LampsLit, WallCorner, Gate, Name];
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
            Size => a.Num("by") is >= LeastSizing and <= MostSizing and var by ? (BuiltThings.WithPartSized(thing, part, by), null) : (null, $"size takes by, {LeastSizing}–{MostSizing}."),
            Keep or Free => (BuiltThings.WithKept(thing, part, action == Keep), null),
            RemovePart => thing.Parts.Count > 1 ? (BuiltThings.WithoutPart(thing, part), null) : (null, "It is the thing's last part: remove_built removes the thing."),
            Fill or Thin => Swept(site, sheet, thing, a, action == Fill),
            LampsOut or LampsLit => Switched(s, sheet, thing, a, action == LampsLit),
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

    static (BuiltThing?, string?) Switched(Session s, SheetDef sheet, BuiltThing thing, JsonElement a, bool on)
    {
        if (AgentTableTools.Points(a) is not { Count: > 0 } pts || a.Num("radius") is not > 0 and var _) return (null, "lamps_out and lamps_lit take points, [[x, z], …], and radius, in map units.");
        if (s.View.Calibration.UnitsPerMeter is not { } perMetre || perMetre <= 0) return (null, GroundKinds.NeedsScale + ".");
        var stroke = pts.Select(p => On(sheet, p)).ToList();
        return (Lamps.Switched(thing, BuildTool.Epoch(s), perMetre, stroke, a.Num("radius")!.Value, on), on ? "No dark lamp of this thing lies under the stroke." : "No lit lamp of this thing lies under the stroke.");
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
        if (Unfit(s, thing, false) is { } unfit) return ToolResult.Fail("thing is " + unfit);
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
        if (s.View.Built.FirstOrDefault(b => b.Id == id) is not { } place || Array.IndexOf(Shapeable, place.Kind) < 0) return ToolResult.Fail("No such settlement, fort, building or stretch of country (list_built).");
        if (s.View.Sheets.FirstOrDefault(x => x.Id == place.Sheet) is not { } sheet) return ToolResult.Fail("Its sheet is gone.");
        if (BuiltThings.Fixed(place)) return ToolResult.Fail("It is kept as it is: edit_built with free lets it be laid afresh.");
        var shaped = place.Clone();
        if (Culture(s, a, out int lays, out var mix) is { } refused) return ToolResult.Fail(refused);
        if (mix is { } kept) Mixed(kept, shaped);
        else if (lays >= 0) shaped.Settings[Manners.Setting] = lays;
        if (Sided(s, a, null, shaped) is { } sideRefused) return ToolResult.Fail(sideRefused);
        bool town = place.Kind == BuiltKinds.Settlement, star = place.Kind == BuiltKinds.Fort && Defences.Named(place).Starred;
        if (!town && (a.Num("houses") != null || a.Num("spread") != null || a.Num("order") != null || a.Num("ravaged") != null || a.Num("walls") != null || a.Str("plan") is { Length: > 0 }))
            return ToolResult.Fail("houses, spread, order, ravaged, walls and plan are a settlement's.");
        if (!star && (a.Num("bastions") != null || a.Num("outworks") != null)) return ToolResult.Fail("bastions and outworks are a star fort's.");
        if (a.Num("houses") is { } houses) shaped.Settings[Settlements.Houses] = Math.Clamp(houses, 0, 1);
        if (a.Num("spread") is { } spread) shaped.Settings[Settlements.Spread] = Math.Clamp(spread, 0, 1);
        if (a.Num("order") is { } order) shaped.Settings[Settlements.Order] = Math.Clamp(order, 0, 1);
        if (a.Num("ravaged") is { } ravaged) shaped.Settings[Settlements.Ravaged] = Math.Clamp(ravaged, 0, 1);
        if (a.Num("walls") is { } walls) shaped.Settings[Settlements.Walls] = Walled(s, walls);
        if (a.Str("plan")?.Trim().ToLowerInvariant() is { Length: > 0 } plan)
        {
            var offered = Plans.Offered(Manners.Of(shaped));
            if (!offered.Contains(plan)) return ToolResult.Fail("plan is one its culture offers: " + string.Join(", ", offered) + ".");
            shaped.Settings[Settlements.Plan] = Plans.Value(plan);
        }
        if (a.Num("bastions") is { } bastions)
        {
            if (bastions is < Forts.FewestBastions or > Forts.MostBastions) return ToolResult.Fail($"bastions is {Forts.FewestBastions}–{Forts.MostBastions}.");
            shaped.Settings[Forts.Bastions] = (int)bastions;
        }
        if (a.Num("outworks") is { } outworks) shaped.Settings[Forts.Outworks] = Math.Clamp(outworks, 0, 1);
        s.Do(new SetBuilt { Thing = BuiltThings.Remade(BuildTool.Site(s, sheet, place.Id), shaped, a.Num("seed") is { } seed ? (int)Math.Clamp(seed, 1, int.MaxValue) : place.Seed) });
        await _a.Delay(0.2);
        _a.Log("shaped a built " + place.Kind);
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
        if (Culture(s, a, out int lays, out var mix) is { } refused) return ToolResult.Fail(refused);
        var site = BuildTool.Site(s, sheet, null, lays);
        var at = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(x, z)));
        double heading = (a.Num("heading") ?? 0) * Math.PI / 180;
        var facing = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(x + Math.Sin(heading), z - Math.Cos(heading)))) - at;
        if (kind == BuiltKinds.Building)
        {
            var cls = (a.Str("class") ?? "house").Trim().ToLowerInvariant();
            if (Unfit(s, cls, true) is { } unfit) return ToolResult.Fail("class is " + unfit);
            var building = BuildTool.Heathen(BuiltThings.Building(site, sheet.Id, cls, at, facing.Left), BuildTool.Ancient(s));
            return Sided(s, a, mix, building) is { } sideRefused ? ToolResult.Fail(sideRefused) : await Laid(s, building, cls);
        }
        if (a.Num("radius") is not > 0 and var _) return ToolResult.Fail("radius is needed, in map units.");
        double radius = a.Num("radius")!.Value;
        if (kind is BuiltKinds.Fort or BuildPrefs.Redoubt)
        {
            var work = kind == BuildPrefs.Redoubt ? Defences.Fieldwork(site.Manner) : Defences.Fort(site.Manner);
            if (work == null) return ToolResult.Fail("the map's age has no fieldwork: build a fort.");
            int bastions = (int)(a.Num("bastions") ?? Forts.FewestBastions + 1);
            if (work.Starred && bastions is < Forts.FewestBastions or > Forts.MostBastions) return ToolResult.Fail($"bastions is {Forts.FewestBastions}–{Forts.MostBastions}.");
            var fort = Defences.Lay(site, sheet.Id, work.Id, at, radius, SeedOf(a), bastions, Math.Clamp(a.Num("outworks") ?? DefaultOutworks, 0, 1), Math.Atan2(facing.Y, facing.X));
            return Sided(s, a, mix, fort) is { } sideRefused ? ToolResult.Fail(sideRefused) : await Laid(s, fort, work.Id, a.Str("name"));
        }
        double houses = Math.Clamp(a.Num("houses") ?? DefaultHouses, 0, 1), walls = Walled(s, a.Num("walls") ?? 0);
        var offered = Plans.Offered(site.Manner);
        var plan = (a.Str("plan") ?? Plans.Own(site.Manner, radius / site.UnitsPerMetre)).Trim().ToLowerInvariant();
        if (!offered.Contains(plan)) return ToolResult.Fail("plan is one the culture offers: " + string.Join(", ", offered) + ".");
        var shape = new Settlements.Shape(a.Num("spread") ?? Settlements.UsualSpread, a.Num("order") ?? Settlements.UsualOrder, a.Num("heading") == null ? null : Math.Atan2(facing.Y, facing.X),
            a.Num("ravaged") ?? 0);
        var place = Settlements.Lay(site, sheet.Id, at, radius, SeedOf(a), houses, walls, Plans.Value(plan), BuildTool.Ancient(s), null, shape);
        return Sided(s, a, mix, place) is { } placeRefused ? ToolResult.Fail(placeRefused) : await Laid(s, place, "settlement", a.Str("name"));
    }

    async Task<ToolResult> BuildRoad(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, hud)) return why!;
        if (AgentTableTools.Points(a) is not { Count: > 0 } pts) return ToolResult.Fail("points is needed: [[x, z], …], at most " + AgentTableTools.MostPoints + ".");
        var cls = (a.Str("class") ?? "road").Trim().ToLowerInvariant();
        var routes = Defences.Routes(BuildTool.Epoch(s));
        if (!routes.Contains(cls)) return ToolResult.Fail("class is a line of the map's age: " + string.Join(", ", routes) + ".");
        bool findsWay = a.Flag("finds_way") != false;
        if (pts.Count < 2 && !(findsWay && BuiltRoads.IsWater(cls))) return ToolResult.Fail("A road takes two or more waypoints.");
        if (!BuildSheet(s, hud, pts[0], out var sheet, out why)) return why!;
        var line = BuiltRoads.Lay(BuildTool.Site(s, sheet), sheet.Id, pts.Select(p => Spot.Of(BuiltGeometry.ToSheet(sheet, p))).ToList(), cls, findsWay);
        return await Laid(s, BuildTool.Heathen(line, BuildTool.Ancient(s)), cls);
    }

    async Task<ToolResult> BuildDetails(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, hud)) return why!;
        if (AgentTableTools.Points(a) is not { Count: > 0 } pts) return ToolResult.Fail("points is needed: [[x, z], …], at most " + AgentTableTools.MostPoints + ".");
        if (a.Num("radius") is not > 0 and var _) return ToolResult.Fail("radius is needed, in map units.");
        if (!BuildSheet(s, hud, pts[0], out var sheet, out why)) return why!;
        double plots = Math.Clamp(a.Num("plots") ?? DefaultPlots, 0, 1), buildings = Math.Clamp(a.Num("buildings") ?? DefaultBuildings, 0, 1);
        var stroke = pts.Select(p => Spot.Of(BuiltGeometry.ToSheet(sheet, p))).ToList();
        if (Culture(s, a, out int lays, out var mix) is { } refused) return ToolResult.Fail(refused);
        var country = BuiltDetails.Lay(BuildTool.Site(s, sheet, null, lays), sheet.Id, stroke, a.Num("radius")!.Value, SeedOf(a), plots, buildings, a.Flag("shares") == true);
        return Sided(s, a, mix, country) is { } sideRefused ? ToolResult.Fail(sideRefused) : await Laid(s, country, "stretch of country");
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
        foreach (var (name, value) in thing.Settings.Where(setting => Array.IndexOf(NamedSettings, setting.Key) < 0)) row[name] = Math.Round(value, 2);
        if (Array.IndexOf(Shapeable, thing.Kind) >= 0)
        {
            var mix = Manners.MixOf(thing);
            row["culture"] = Manners.Id(mix.Lays);
            if (!mix.Pure) row["builders"] = Manners.Id(mix.Builds);
            if (!mix.Pure) row["adorners"] = Manners.Id(mix.Adorns);
            if (mix.Ward >= 0) row["quarter"] = Manners.Id(mix.Ward);
            if (mix.Ward >= 0) row["quarter_share"] = Math.Round(mix.Share, 2);
        }
        if (thing.Kind == BuiltKinds.Settlement) row["plan"] = Settlements.Form(thing);
        if (thing.Kind == BuiltKinds.Fort) row["fort"] = Defences.KindOf(thing);
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
