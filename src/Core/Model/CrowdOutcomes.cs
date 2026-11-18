namespace KTable.Core.Model;

public static class CrowdOutcomes
{
    public const int UsualMinutes = 5, MostMinutes = 120;

    public static Dictionary<string, CrowdFate?> Spread(IEnumerable<(string Key, Vec2 At)> people, Vec2 from, int count, string state, long start, long seconds,
        IReadOnlyList<(string Key, Vec2 At)>? captors = null)
    {
        var nearest = people.OrderBy(p => (p.At - from).Length).ThenBy(p => p.Key, StringComparer.Ordinal).Take(Math.Max(0, count)).ToList();
        var fates = new Dictionary<string, CrowdFate?>();
        for (int k = 0; k < nearest.Count; k++)
        {
            string? by = captors is { Count: > 0 } ? captors.MinBy(c => (c.At - nearest[k].At).Length).Key : null;
            fates[nearest[k].Key] = new CrowdFate { State = state, At = start + Math.Max(0, seconds) * k / nearest.Count, By = by };
        }
        return fates;
    }

    public static Dictionary<string, CrowdFate?> Lifted(IEnumerable<string> keys) => keys.Distinct().ToDictionary(k => k, _ => (CrowdFate?)null);

    public static Dictionary<string, CrowdFate?> Set(IEnumerable<string> keys, string state, long at) =>
        keys.Distinct().ToDictionary(k => k, _ => (CrowdFate?)new CrowdFate { State = state, At = at });
}
