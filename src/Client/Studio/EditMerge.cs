using System.Text.Json.Nodes;

namespace KTable.Client.Studio;

public static class EditMerge
{
    public static JsonNode? Back(JsonNode? from, JsonNode? to, JsonNode? now)
    {
        if (JsonNode.DeepEquals(from, to)) return now?.DeepClone();
        if (JsonNode.DeepEquals(to, now)) return from?.DeepClone();
        if (from is JsonObject fo && to is JsonObject t && now is JsonObject n) return Objects(fo, t, n);
        if (from is JsonArray fa && to is JsonArray ta && now is JsonArray na && fa.Count == ta.Count && ta.Count == na.Count)
        {
            var a = new JsonArray();
            for (int i = 0; i < fa.Count; i++) a.Add(Back(fa[i], ta[i], na[i]));
            return a;
        }
        return from?.DeepClone();
    }

    static JsonObject Objects(JsonObject from, JsonObject to, JsonObject now)
    {
        var o = new JsonObject();
        var keys = new List<string>();
        foreach (var source in new[] { now, from, to })
            foreach (var (key, _) in source)
                if (!keys.Contains(key)) keys.Add(key);
        foreach (var key in keys)
            if (Back(from[key], to[key], now[key]) is { } v) o[key] = v;
        return o;
    }
}
