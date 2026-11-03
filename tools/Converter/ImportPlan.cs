namespace KTable.Converter;

public sealed record ImportPlan(bool Restore, string Name, IReadOnlyList<string> Packs, string? Scenario)
{
    public static ImportPlan Of(TtsSave save, string? name, string? slugArg)
    {
        if (KtMarker.Find(save) is { } shared)
        {
            var sid = KtMarker.SafePackId(shared.Id) ? shared.Id : "user." + DefFactory.Slug(shared.Name);
            return new ImportPlan(true, shared.Name, new[] { sid }, null);
        }
        name ??= save.SaveName;
        var slug = DefFactory.Slug(slugArg ?? name);
        var packId = "tts." + slug;
        return new ImportPlan(false, name, new[] { packId, packId + ".extras" }, slug);
    }
}
