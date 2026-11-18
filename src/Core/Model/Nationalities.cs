namespace KTable.Core.Model;

public static class Nationalities
{
    const string Shipped = "nationalities.json";

    static readonly Lazy<List<Nationality>> Read = new(() =>
    {
        using var stream = typeof(Nationalities).Assembly.GetManifestResourceStream(Shipped) ?? throw new InvalidOperationException("the shipped nationalities are missing");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Json.Deserialize<List<Nationality>>(bytes.ToArray());
    });

    static readonly Lazy<List<Nationality>> Peoples = new(() => Read.Value.Where(n => n.Id != CrowdBaseline.Id).ToList());

    public static Nationality Baseline => Read.Value.First(n => n.Id == CrowdBaseline.Id);

    public static IReadOnlyList<Nationality> All => Peoples.Value;

    public static Nationality? Named(string? id) => All.FirstOrDefault(n => n.Id == id);

    public static List<Nationality> Of(IEnumerable<PackManifest> packs) =>
        packs.SelectMany(p => p.Nationalities ?? []).Reverse().DistinctBy(n => n.Id).Reverse().ToList();

    public static IEnumerable<Nationality> Offered(IEnumerable<PackManifest> packs)
    {
        var own = Of(packs);
        return own.Concat(All.Where(n => own.All(o => o.Id != n.Id)));
    }
}
