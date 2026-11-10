using System.Text.Json;

namespace KTable.Client.Agent;

public static class Paging
{
    public static readonly Arg Offset = new("offset", "number", "skip this many first: the next page starts where the last one ended");

    public static (List<T> Page, string Head) Page<T>(IReadOnlyList<T> all, JsonElement a, int limit, string what)
    {
        int offset = Math.Max(0, (int)(a.Num("offset") ?? 0));
        var page = all.Skip(offset).Take(limit).ToList();
        if (all.Count == 0) return (page, $"no {what}");
        if (offset == 0 && page.Count == all.Count) return (page, $"{all.Count} {what}");
        var head = page.Count == 0 ? $"none of the {all.Count} {what} after {offset}" : $"{what} {offset + 1}–{offset + page.Count} of {all.Count}";
        return (page, offset + page.Count < all.Count ? head + $" (offset {offset + page.Count} for the next page)" : head);
    }
}
