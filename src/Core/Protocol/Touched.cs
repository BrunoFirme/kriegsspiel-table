using System.Text.Json.Nodes;

namespace KTable.Core.Protocol;

public readonly record struct Reach(string[] Things, string[] SitsOn)
{
    public bool Changes(Reach other) => Things.Any(other.Things.Contains) || SitsOn.Any(other.Things.Contains);
}

public static class Touched
{
    const string One = "id", Many = "ids", OneSuffix = "Id", ManySuffix = "Ids", Child = "child", Piece = "piece";
    static readonly HashSet<string> Places = new(StringComparer.Ordinal) { "holder", "parent", "board" };
    static readonly HashSet<string> NotThings = new(StringComparer.Ordinal) { "scenarioId" };

    public static Reach By(Command c)
    {
        if (c is RestoreTurn or LoadSave) return new Reach([], []);
        var things = new HashSet<string>(StringComparer.Ordinal);
        var places = new HashSet<string>(StringComparer.Ordinal);
        Walk(JsonNode.Parse(Json.Serialize(c)), things, places);
        if (things.Count == 0) things.Add("kind:" + c.GetType().Name);
        return new Reach(things.ToArray(), places.ToArray());
    }

    static bool Names(string name) =>
        !NotThings.Contains(name) && (name is One or Many or Child or Piece || name.EndsWith(OneSuffix, StringComparison.Ordinal) || name.EndsWith(ManySuffix, StringComparison.Ordinal));

    static void Walk(JsonNode? node, HashSet<string> things, HashSet<string> places)
    {
        if (node is JsonArray list)
        {
            foreach (var item in list) Walk(item, things, places);
            return;
        }
        if (node is not JsonObject o) return;
        foreach (var (name, value) in o)
        {
            if (Names(name)) things.UnionWith(Strings(value));
            else if (Places.Contains(name)) places.UnionWith(Strings(value));
            Walk(value, things, places);
        }
    }

    static IEnumerable<string> Strings(JsonNode? value)
    {
        if (value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0) yield return s;
        if (value is not JsonArray list) yield break;
        foreach (var item in list)
            if (item is JsonValue iv && iv.TryGetValue<string>(out var each) && each.Length > 0) yield return each;
    }
}
