namespace KTable.Converter;

public sealed record ImportPlan(bool Restore, string Name, IReadOnlyList<string> Packs, string? Scenario, IReadOnlyList<Layout> Layouts)
{
    public static ImportPlan Of(TtsSave save, string? name, string? slugArg)
    {
        if (KtMarker.Find(save) is { } shared)
        {
            var sid = KtMarker.SafePackId(shared.Id) ? shared.Id : "user." + DefFactory.Slug(shared.Name);
            return new ImportPlan(true, shared.Name, new[] { sid }, null, []);
        }
        name ??= save.SaveName;
        var slug = DefFactory.Slug(slugArg ?? name);
        var packId = "tts." + slug;
        var layouts = new List<Layout>();
        foreach (var bag in MemoryBags.Of(save))
        {
            var called = bag.Nickname.Length > 0 ? bag.Nickname : $"layout {layouts.Count + 1}";
            var own = DefFactory.Slug($"{slug}-{called}");
            const int KeptOfName = 30;
            if (own == slug || layouts.Any(l => l.Slug == own)) own = $"{slug[..Math.Min(slug.Length, KeptOfName)].Trim('-')}-{DefFactory.Slug(bag.Guid)}";
            layouts.Add(new Layout(bag, $"{name} — {called}", own, $"tts.{own}.extras"));
        }
        return new ImportPlan(false, name, new[] { packId, packId + ".extras" }.Concat(layouts.Select(l => l.ExtrasPack)).ToList(), slug, layouts);
    }

    public IEnumerable<string> Scenarios => Scenario == null ? [] : Layouts.Select(l => l.Slug).Prepend(Scenario);
}
