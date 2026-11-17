using System.Globalization;
using System.Text;
using System.Text.Json;
using KTable.Core.Model;

namespace KTable.Client.Agent;

public sealed class PlaceTools
{
    const string Everyone = "everyone";
    readonly TableTools _t;

    public PlaceTools(TableTools t) => _t = t;

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("list_places",
            "The Places panel's tree: map sheets, named built things, buildings and the umpires' pins, each with its x, z and the players whose body stands there.",
            Args.Schema(), ListPlaces);
        yield return new AgentTool("send_to_place",
            "Moves a player's body, or every body, to a place from list_places. One undoable change.",
            Args.Schema(new Arg("who", "string", "a player's name, or everyone", true), new Arg("place", "string", "the place's name", true)), SendToPlace);
    }

    IReadOnlyList<PlaceBody> Bodies() => _t.Hooks.Bodies?.Invoke() ?? [];

    bool Umpire(ClientSession s, out ToolResult? why)
    {
        why = s.IsUmpire ? null : ToolResult.Fail("Places are kept for the umpires.");
        return why == null;
    }

    Task<ToolResult> ListPlaces(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return Task.FromResult(why!);
        if (!Umpire(s, out why)) return Task.FromResult(why!);
        var tree = PlaceTree.Of(s.View, s.Def, Bodies());
        if (tree.Count == 0) return Task.FromResult(new ToolResult("No places: no map sheets and no pins."));
        var text = new StringBuilder();
        void Write(MapPlace p, int depth)
        {
            text.Append(' ', depth * 2).Append("- ").Append(p.Name).Append(" (").Append(Words(p.Kind)).Append(") ")
                .Append(string.Create(CultureInfo.InvariantCulture, $"{p.At.X:0.##}, {p.At.Z:0.##}"));
            if (p.Here.Count > 0) text.Append(" · here: ").Append(string.Join(", ", p.Here));
            text.Append('\n');
            foreach (var inner in p.Inside) Write(inner, depth + 1);
        }
        foreach (var p in tree) Write(p, 0);
        return Task.FromResult(new ToolResult(Hearsay.Marked(Hearsay.Held(text.ToString().TrimEnd()))));
    }

    static string Words(PlaceKind k) => k switch
    {
        PlaceKind.Sheet => "map sheet",
        PlaceKind.Built => "built",
        PlaceKind.Building => "building",
        _ => "pin",
    };

    async Task<ToolResult> SendToPlace(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why) || !Umpire(s, out why)) return why!;
        var bodies = Bodies();
        if (bodies.Count == 0) return ToolResult.Fail("No player has a body at the table.");
        string who = (a.Str("who") ?? "").Trim();
        var going = who.Equals(Everyone, StringComparison.OrdinalIgnoreCase) ? bodies : bodies.Where(b => b.Name.Equals(who, StringComparison.OrdinalIgnoreCase)).ToList();
        if (going.Count == 0) return ToolResult.Fail("who is one of " + string.Join(", ", bodies.Select(b => b.Name)) + ", or everyone.");
        if (PlaceTree.Find(PlaceTree.Of(s.View, s.Def), a.Str("place") ?? "") is not { } place) return ToolResult.Fail("No place of that name: list_places names them.");
        if (PlaceTree.Send(s.View, place, going) is not { } move) return ToolResult.Fail("Those bodies are not on the table.");
        s.Do(move);
        await _t.Landed(s);
        _t.Log("sent players to a place");
        return new ToolResult(Hearsay.Marked(Hearsay.Held($"{string.Join(", ", going.Select(b => b.Name))} sent to {place.Name}.")));
    }
}
