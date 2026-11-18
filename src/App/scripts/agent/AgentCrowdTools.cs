using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class AgentCrowdTools(AgentTableTools tools, AgentEndpoint agent)
{
    const double Settle = 0.3, PersonReachM = 10;
    const int Minute = 60, UsualMade = 50;
    const string Up = "up";

    public static string Words =>
        "Orders: " + string.Join(", ", CrowdCommands.All.Select(c => $"{c.Id} ({c.Name})")) + ". Shapes: " + string.Join(", ", CrowdShapes.All) +
        ". Paces: " + string.Join(", ", CrowdPaces.All.Select(p => p.Id)) + ". Acts: " + string.Join(", ", RigMotion.Acts.Select(a => a.Id)) + ".";

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("crowds",
            "The crowds (how_to crowds): list them; make a crowd on laid ground; order people picked by id or circle; fate states what befalls them, never rolled; " +
            "wear makes one a piece, release lets it go; remove takes an order or made crowd away. One undo each.",
            Args.Schema(new Arg("action", "string", "list, make, order, fate, wear, release or remove", true),
                new Arg("id", "string", "whose: an order's, made crowd's or town's id; release: the piece; remove: an order or crowd"),
                new Arg("x", "number", "map units"), new Arg("z", "number", "map units"), new Arg("radius", "number", "metres round x, z"),
                new Arg("count", "number", "make: how many; else at most, nearest first"), new Arg("order", "string", "order: what they do"),
                new Arg("points", Args.Points, "order: where, [[x, z]]"), new Arg("target", "string", "order: a piece or another order's id; held: the captors' id"),
                new Arg("how", "string", "order: shape, pace and act words"), new Arg("minutes", "number", "order: how long; fate: spread over"),
                new Arg("state", "string", string.Join(", ", CrowdFates.All) + " or " + Up), new Arg("mix", "string", string.Join(", ", MadeMixes.All.Select(m => m.Id))),
                new Arg("player", "string", "wear: who wears them")),
            Act);
    }

    Task<ToolResult> Act(JsonElement a)
    {
        if (tools.Table(out var why) is not var (s, hud)) return Task.FromResult(why!);
        string action = (a.Str("action") ?? "").Trim().ToLowerInvariant();
        if (action != "list" && !AgentTableTools.CanChange(s, out why)) return Task.FromResult(why!);
        if (!s.View.Lighting.Crowds) return Task.FromResult(ToolResult.Fail("The crowds are off: set_light crowds (host)."));
        var layer = hud.T.Crowds;
        return action switch
        {
            "list" => List(s, layer),
            "make" => Make(a, s),
            "order" => Order(a, s, layer),
            "fate" => Fate(a, s, layer),
            "wear" => Wear(a, s, layer),
            "release" => Release(a, s),
            "remove" => Remove(a, s),
            _ => Task.FromResult(ToolResult.Fail("action is list, make, order, fate, wear, release or remove.")),
        };
    }

    static float Upm(Session s) => (float)(s.View.Calibration.UnitsPerMeter ?? 1);

    async Task<ToolResult> List(Session s, CrowdLayer layer)
    {
        await agent.Delay(Settle);
        var view = s.View;
        long now = view.Clock.Seconds;
        var towns = new JsonArray();
        for (int ti = 0; ti < layer.Towns; ti++)
        {
            var t = layer.Roster(ti);
            if (t.Made) continue;
            var (shown, outside) = layer.Counts(ti);
            towns.Add(new JsonObject { ["id"] = t.Thing, ["name"] = view.Built.FirstOrDefault(b => b.Id == t.Thing)?.Name ?? "", ["people"] = shown, ["out"] = outside });
        }
        var made = new JsonArray();
        foreach (var m in view.Crowds?.Made ?? [])
            made.Add(new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["mix"] = m.Mix, ["people"] = m.Count, ["x"] = Math.Round(m.At.X, 2), ["z"] = Math.Round(m.At.Z, 2) });
        var orders = new JsonArray();
        foreach (var o in view.Crowds?.Orders ?? [])
        {
            var row = new JsonObject { ["id"] = o.Id, ["name"] = o.Name, ["order"] = o.Do, ["people"] = o.Count, ["since"] = view.Clock.Cal.TimeOfDay(o.Start) };
            if (o.Until is { } until) row["until"] = view.Clock.Cal.TimeOfDay(until);
            if (layer.CentreOf(o) is { } middle) (row["x"], row["z"]) = (Math.Round(middle.X, 2), Math.Round(middle.Z, 2));
            row["there"] = layer.Arrived(o, now, out _);
            orders.Add(row);
        }
        var fates = new JsonObject();
        foreach (var g in (view.Crowds?.Fates ?? new()).Values.Where(f => f.At <= now).GroupBy(f => f.State)) fates[g.Key] = g.Count();
        var all = new JsonObject { ["towns"] = towns, ["made"] = made, ["orders"] = orders, ["fates"] = fates, ["out"] = layer.Out };
        return new ToolResult(all.ToJsonString(AgentTableTools.Opts));
    }

    async Task<ToolResult> Make(JsonElement a, Session s)
    {
        var view = s.View;
        if (a.Num("x") is not { } x || a.Num("z") is not { } z) return ToolResult.Fail("x and z are needed, in map units.");
        var at = new Vec2(x, z);
        string mix = a.Str("mix") ?? MadeMixes.Townsfolk;
        var crowd = new MadeCrowd
        {
            Id = "m-" + Guid.NewGuid().ToString("N")[..10], Name = MadeMixes.Of(mix).Name, Count = (int)(a.Num("count") ?? UsualMade), Mix = mix,
            Nationality = CrowdMade.NationalityAt(view, at), At = at, Seed = Random.Shared.Next(), Start = view.Clock.Seconds,
        };
        if ((crowd.Problem() ?? CrowdMade.Refusal(view, at)) is { } problem) return ToolResult.Fail(problem);
        s.Do(new SetMadeCrowd { Crowd = crowd });
        await agent.Delay(Settle);
        if (s.View.Crowds?.Made?.Any(m => m.Id == crowd.Id) != true) return ToolResult.Fail($"Not made: at most {view.Lighting.MadeMost} made people on this table.");
        agent.Log("made a crowd");
        return new ToolResult($"Made {crowd.Id}: {crowd.Count} {crowd.Name.ToLowerInvariant()}, {crowd.Nationality}.");
    }

    async Task<ToolResult> Remove(JsonElement a, Session s)
    {
        var view = s.View;
        string id = a.Str("id") ?? "";
        if (view.Crowds?.Made?.Any(m => m.Id == id) == true) s.Do(new Batch { Items = CrowdLayer.Unmade(view, [id]) });
        else if (view.Crowds?.Orders?.Any(o => o.Id == id) == true) s.Do(new SetCrowdOrder { Order = new CrowdOrder { Id = id }, Remove = true });
        else return ToolResult.Fail("No such order or made crowd (list).");
        await agent.Delay(Settle);
        return new ToolResult($"Took {id} away.");
    }

    List<(int Town, int Person)>? People(JsonElement a, Session s, CrowdLayer layer, out Vec2 from, out ToolResult? why)
    {
        why = null;
        from = default;
        var view = s.View;
        float upm = Upm(s);
        Vec2 At((int Town, int Person) p) => layer.Where(view, p.Town, p.Person, view.Clock.Seconds, upm).At;
        string? id = a.Str("id");
        var people = id != null ? layer.Group(id) : null;
        if (id != null && people is not { Count: > 0 }) { why = ToolResult.Fail("No such group, or nobody in it (list)."); return null; }
        if (a.Num("x") is { } x && a.Num("z") is { } z)
        {
            from = new Vec2(x, z);
            if (a.Num("radius") is { } radius)
            {
                var within = layer.Within(from, radius * upm).ToHashSet();
                people = people == null ? within.ToList() : people.Where(within.Contains).ToList();
            }
        }
        else if (people is { Count: > 0 }) from = new Vec2(people.Average(p => At(p).X), people.Average(p => At(p).Z));
        if (people == null) { why = ToolResult.Fail("Who: id, or x, z and radius."); return null; }
        var near = from;
        if (a.Num("count") is { } count) people = people.OrderBy(p => (At(p) - near).Length).Take(Math.Max(0, (int)count)).ToList();
        if (people.Count == 0) { why = ToolResult.Fail("Nobody there."); return null; }
        return people;
    }

    static string GroupName(GameState view, string? id) =>
        view.Crowds?.Orders?.FirstOrDefault(o => o.Id == id)?.Name ?? view.Crowds?.Made?.FirstOrDefault(m => m.Id == id)?.Name
        ?? (view.Built.FirstOrDefault(b => b.Id == id)?.Name is { Length: > 0 } town ? town : "People");

    async Task<ToolResult> Order(JsonElement a, Session s, CrowdLayer layer)
    {
        if (People(a, s, layer, out _, out var why) is not { } people) return why!;
        var view = s.View;
        string command = a.Str("order") ?? CrowdCommands.GoTo, target = a.Str("target") ?? "";
        var words = (a.Str("how") ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).ToList();
        bool isOrder = view.Crowds?.Orders?.Any(o => o.Id == target) == true;
        long now = view.Clock.Seconds;
        var order = new CrowdOrder
        {
            Id = "o-" + Guid.NewGuid().ToString("N")[..10], Name = GroupName(view, a.Str("id")), Members = layer.MembersOf(people), Said = $"{people.Count}", Do = command,
            To = AgentTableTools.Points(a) ?? [], Piece = target.Length > 0 && !isOrder ? target : null, Against = isOrder ? target : null,
            Shape = words.FirstOrDefault(CrowdShapes.All.Contains) ?? (command == CrowdCommands.Surround ? CrowdShapes.Ring : CrowdShapes.Loose),
            Pace = words.FirstOrDefault(w => CrowdPaces.All.Any(p => p.Id == w)) ?? CrowdPaces.Walk, Act = words.FirstOrDefault(w => RigMotion.Acts.Any(x => x.Id == w)),
            Start = now, Until = a.Num("minutes") is { } minutes and > 0 ? now + (long)(minutes * Minute) : null,
        };
        if (order.Piece != null && view.Find(Layers.Umpire, order.Piece) == null) return ToolResult.Fail("No such piece or order (list_pieces, list).");
        if (order.Problem() is { } problem) return ToolResult.Fail(problem + " (how_to crowds).");
        s.Do(new SetCrowdOrder { Order = order });
        await agent.Delay(Settle);
        if (s.View.Crowds?.Orders?.Any(o => o.Id == order.Id) != true) return ToolResult.Fail("The order was not taken (read_app_log).");
        agent.Log("gave a crowd order");
        return new ToolResult($"{order.Id}: {CrowdOrders.Line(order)}. wait_for crowd with this id waits until all are there.");
    }

    async Task<ToolResult> Fate(JsonElement a, Session s, CrowdLayer layer)
    {
        if (People(a, s, layer, out var from, out var why) is not { } people) return why!;
        var view = s.View;
        float upm = Upm(s);
        string state = (a.Str("state") ?? "").Trim().ToLowerInvariant();
        if (state != Up && !CrowdFates.All.Contains(state)) return ToolResult.Fail($"state is {string.Join(", ", CrowdFates.All)} or {Up}.");
        List<(string Key, Vec2 At)> Keyed(IEnumerable<(int Town, int Person)> who) =>
            who.Select(p => (CrowdPiece.Key(layer.Roster(p.Town).Thing, p.Person), layer.Where(view, p.Town, p.Person, view.Clock.Seconds, upm).At)).ToList();
        var keyed = Keyed(people);
        var captors = state == CrowdFates.Held && a.Str("target") is { } them ? Keyed(layer.Group(them)) : null;
        long seconds = (long)((a.Num("minutes") ?? 0) * Minute);
        var fates = state == Up ? CrowdOutcomes.Lifted(keyed.Select(k => k.Key)) : CrowdOutcomes.Spread(keyed, from, keyed.Count, state, view.Clock.Seconds, seconds, captors);
        s.Do(new SetCrowdFates { Fates = fates.Take(SetCrowdFates.Most).ToDictionary(f => f.Key, f => f.Value) });
        await agent.Delay(Settle);
        agent.Log("stated what befell crowd people");
        return new ToolResult($"{fates.Count} {state}{(seconds > 0 ? $" over {seconds / Minute} minutes" : "")}.");
    }

    async Task<ToolResult> Wear(JsonElement a, Session s, CrowdLayer layer)
    {
        var view = s.View;
        (CrowdTown Town, int Person)? found = a.Str("id") is { } key && CrowdPiece.Parse(key) is { } p && layer.TownOf(p.Thing) is >= 0 and var ti
            ? (layer.Roster(ti), p.Person)
            : a.Num("x") is { } x && a.Num("z") is { } z ? layer.PersonAt(new Vec2(x, z), PersonReachM * Upm(s)) : null;
        if (found is not { } one || layer.TownOf(one.Town.Thing) is not (>= 0 and var town) || layer.Person(town, one.Person) is not { } person)
            return ToolResult.Fail("Nobody there: x and z near someone out of doors.");
        string? player = a.Str("player");
        if (player != null && s.Peers.All(peer => peer.Role != Role.Player || peer.Name != player)) return ToolResult.Fail("No such player at the table (list_people).");
        string pieceId = "c-" + Guid.NewGuid().ToString("N")[..10], crowdKey = CrowdPiece.Key(one.Town.Thing, one.Person);
        var feet = Coords.ToWorld(person.At, layer.HeightAt(town, person.At));
        float yaw = (float)(Math.PI - person.Heading);
        var made = new List<Command>
        {
            new SpawnPiece
            {
                Layer = Layers.Umpire, Id = pieceId, Def = CrowdPiece.DefId, Pos = person.At, RotDeg = AvatarBody.RotOf(yaw), Y = feet.Y, Look = person.Look.Clone(), Crowd = crowdKey,
            },
        };
        if (player == null) made.Add(new SetCrowdFates { Fates = new() { [crowdKey] = new CrowdFate { State = CrowdFates.Kept, At = view.Clock.Seconds } } });
        s.Do(new Batch { Items = made });
        for (int i = 0; i < 20 && s.View.Find(Layers.Umpire, pieceId) == null; i++) await agent.Delay(0.05);
        if (player != null) layer.Tool.Assign?.Invoke(player, feet, yaw, pieceId);
        string name = one.Town.Residents[one.Person].Entry.Name;
        return new ToolResult(player != null ? $"{name} is {pieceId}, worn by {player}." : $"{name} is {pieceId}, kept on the table.");
    }

    async Task<ToolResult> Release(JsonElement a, Session s)
    {
        var view = s.View;
        string id = a.Str("id") ?? "";
        if (view.Find(Layers.Umpire, id) is not { Crowd: { } key } piece || CrowdPiece.Parse(key) is not { } who) return ToolResult.Fail("No crowd person piece by that id.");
        var back = new CrowdOrder { Name = "Back to their day", Members = new() { [who.Thing] = [who.Person] }, Do = CrowdCommands.Home, From = piece.Pos, Start = view.Clock.Seconds };
        var items = new List<Command> { new DeletePiece { Layer = Layers.Umpire, Id = id }, new SetCrowdOrder { Order = back } };
        if (view.Crowds?.Fates?.ContainsKey(key) == true) items.Add(new SetCrowdFates { Fates = new() { [key] = null } });
        s.Do(new Batch { Items = items });
        await agent.Delay(Settle);
        return new ToolResult($"Let go of {id}: they walk back into their day.");
    }
}
