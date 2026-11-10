namespace KTable.Converter;

public sealed record ImportPlan(bool Restore, string Name, IReadOnlyList<string> Packs, string? Scenario, string? Taken = null, string? TakenFolder = null)
{
    const int MostOfTheSameName = 99;

    public const string ConvertedPrefix = "tts.";

    public static ImportPlan Of(ModuleSave save, string? name, string? slugArg, Func<string, string?>? packOwner = null, Func<string, string?>? packOfModule = null,
        Func<string, string?>? scenarioIn = null)
    {
        if (KtMarker.Find(save) is { } shared)
        {
            var sid = KtMarker.SafePackId(shared.Id) ? shared.Id : "user." + DefFactory.Slug(shared.Name);
            return new ImportPlan(true, shared.Name, new[] { sid }, null);
        }
        name ??= save.SaveName;
        var slug = DefFactory.Slug(slugArg ?? name);
        string? taken = null;
        var mine = slugArg == null && save.WorkshopId.Length > 0 ? packOfModule?.Invoke(save.WorkshopId) : null;
        if (mine != null && mine.StartsWith(ConvertedPrefix, StringComparison.Ordinal)) slug = mine[ConvertedPrefix.Length..];
        else if (slugArg == null && packOwner != null && MarkFor(packOwner, ConvertedPrefix + slug, save.WorkshopId) is { } mark)
        {
            taken = ConvertedPrefix + slug;
            slug = $"{slug}-{mark}";
        }
        string? takenFolder = null;
        if (slugArg == null && mine == null && scenarioIn != null && !Free(slug))
        {
            takenFolder = slug;
            slug = Enumerable.Range(2, MostOfTheSameName - 1).Select(n => $"{takenFolder}-{n}").FirstOrDefault(Free) ?? $"{takenFolder}-{DefFactory.Slug(Guid.NewGuid().ToString("N")[..8])}";
        }
        var packId = ConvertedPrefix + slug;
        return new ImportPlan(false, name, new[] { packId, packId + ".extras" }, slug, taken, takenFolder);

        bool Free(string dir) =>
            (scenarioIn!(dir) is not { } there || there == ConvertedPrefix + dir) && (packOwner?.Invoke(ConvertedPrefix + dir) is not { } owner || owner == save.WorkshopId);
    }

    static string? MarkFor(Func<string, string?> packOwner, string packId, string mine)
    {
        if (packOwner(packId) is not { } theirs || theirs == mine) return null;
        if (mine.Length > 0) return mine;
        for (int n = 2; n <= MostOfTheSameName; n++)
            if (packOwner($"{packId}-{n}") == null) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return DefFactory.Slug(Guid.NewGuid().ToString("N")[..8]);
    }

    public IEnumerable<string> Scenarios => Scenario == null ? [] : [Scenario];
}
