namespace KTable.Converter;

public sealed record ImportPlan(bool Restore, string Name, IReadOnlyList<string> Packs, string? Scenario, IReadOnlyList<Layout> Layouts, string? Taken = null)
{
    const int MostOfTheSameName = 99;

    public static ImportPlan Of(TtsSave save, string? name, string? slugArg, Func<string, string?>? packOwner = null)
    {
        if (KtMarker.Find(save) is { } shared)
        {
            var sid = KtMarker.SafePackId(shared.Id) ? shared.Id : "user." + DefFactory.Slug(shared.Name);
            return new ImportPlan(true, shared.Name, new[] { sid }, null, []);
        }
        name ??= save.SaveName;
        var slug = DefFactory.Slug(slugArg ?? name);
        string? taken = null;
        if (slugArg == null && packOwner != null && MarkFor(packOwner, "tts." + slug, save.WorkshopId) is { } mark)
        {
            taken = "tts." + slug;
            slug = $"{slug}-{mark}";
        }
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
        return new ImportPlan(false, name, new[] { packId, packId + ".extras" }.Concat(layouts.Select(l => l.ExtrasPack)).ToList(), slug, layouts, taken);
    }

    static string? MarkFor(Func<string, string?> packOwner, string packId, string mine)
    {
        if (packOwner(packId) is not { } theirs || theirs == mine) return null;
        if (mine.Length > 0) return mine;
        for (int n = 2; n <= MostOfTheSameName; n++)
            if (packOwner($"{packId}-{n}") == null) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return DefFactory.Slug(Guid.NewGuid().ToString("N")[..8]);
    }

    public IEnumerable<string> Scenarios => Scenario == null ? [] : Layouts.Select(l => l.Slug).Prepend(Scenario);
}
