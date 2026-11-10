using System.Text.Json.Nodes;

namespace KTable.Core.Protocol;

public static class Touched
{
    const string One = "id", Many = "ids", OneSuffix = "Id", ManySuffix = "Ids";
    static readonly HashSet<string> AlsoNames = new(StringComparer.Ordinal) { "holder", "parent", "child", "board", "piece" };
    static readonly HashSet<string> NotThings = new(StringComparer.Ordinal) { "scenarioId" };

    public static string[] By(Command c)
    {
        if (c is RestoreTurn or LoadSave) return [];
        var keys = new HashSet<string>(StringComparer.Ordinal);
        Walk(JsonNode.Parse(Json.Serialize(c)), keys);
        if (keys.Count == 0) keys.Add("kind:" + c.GetType().Name);
        return keys.ToArray();
    }

    static bool Names(string name) =>
        !NotThings.Contains(name) && (name is One or Many || name.EndsWith(OneSuffix, StringComparison.Ordinal) || name.EndsWith(ManySuffix, StringComparison.Ordinal) || AlsoNames.Contains(name));

    static void Walk(JsonNode? node, HashSet<string> keys)
    {
        if (node is JsonArray list)
        {
            foreach (var item in list) Walk(item, keys);
            return;
        }
        if (node is not JsonObject o) return;
        foreach (var (name, value) in o)
        {
            if (Names(name)) foreach (var id in Strings(value)) keys.Add(id);
            Walk(value, keys);
        }
    }

    static IEnumerable<string> Strings(JsonNode? value)
    {
        if (value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0) yield return s;
        if (value is not JsonArray list) yield break;
        foreach (var item in list)
            if (item is JsonValue iv && iv.TryGetValue<string>(out var each) && each.Length > 0) yield return each;
    }

    public static bool Meet(string[] a, string[] b) => a.Any(b.Contains);
}
