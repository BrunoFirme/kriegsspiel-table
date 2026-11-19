using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public sealed class ClearStroke
{
    public const int MostPoints = 400;

    public List<Vec2> Points { get; set; } = new();
    public double Radius { get; set; }

    public ClearStroke Clone() => new() { Points = Points.ToList(), Radius = Radius };

    public bool Covers(Vec2 p)
    {
        if (Points.Count == 1) return (p - Points[0]).Length <= Radius;
        for (int i = 0; i + 1 < Points.Count; i++)
            if (Apart(p, Points[i], Points[i + 1]) <= Radius) return true;
        return false;
    }

    [JsonIgnore] public bool Sound => Points.Count is > 0 and <= MostPoints && double.IsFinite(Radius) && Radius > 0 && Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Z));

    static double Apart(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        double len = ab.X * ab.X + ab.Z * ab.Z, t = len <= 0 ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / len, 0, 1);
        return (p - new Vec2(a.X + ab.X * t, a.Z + ab.Z * t)).Length;
    }
}

public sealed class CrowdState
{
    public const int MostStrokes = 200, MostPeoples = 64, MostOrders = 500;

    public Dictionary<string, Nationality> Peoples { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<ClearStroke>? Clear { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CrowdOrder>? Orders { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, CrowdFate>? Fates { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<MadeCrowd>? Made { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, CrowdLook>? Dressed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, CrowdScript>? Scripts { get; set; }

    [JsonIgnore] public bool Empty =>
        Peoples.Count == 0 && Clear is not { Count: > 0 } && Orders is not { Count: > 0 } && Fates is not { Count: > 0 } && Made is not { Count: > 0 } && Dressed is not { Count: > 0 }
        && Scripts is not { Count: > 0 };

    public CrowdState Clone() => new()
    {
        Peoples = Peoples.ToDictionary(p => p.Key, p => Json.Clone(p.Value)), Clear = Clear?.Select(s => s.Clone()).ToList(), Orders = Orders?.Select(o => o.Clone()).ToList(),
        Fates = Fates?.ToDictionary(f => f.Key, f => f.Value.Clone()), Made = Made?.Select(m => m.Clone()).ToList(),
        Dressed = Dressed?.ToDictionary(d => d.Key, d => d.Value.Clone()), Scripts = Scripts?.ToDictionary(s => s.Key, s => s.Value.Clone()),
    };

    public bool Clears(Vec2 p) => Clear?.Any(s => s.Covers(p)) == true;

    public string? FateOf(string key, long at) => Fates?.TryGetValue(key, out var f) == true && f.Holds(at) ? f.State : null;

    public IEnumerable<string> HeldBy(IReadOnlySet<string> captors) =>
        Fates?.Where(f => f.Value.State == CrowdFates.Held && f.Value.By is { } by && captors.Contains(by)).Select(f => f.Key) ?? [];
}

public static class Population
{
    public const int Least = 0, Usual = 100, Most = 200;
    public static readonly IReadOnlyList<int> Levels = [0, 25, 50, 100, 150, 200];

    public static int Of(BuiltPart part) => part.Population ?? Usual;

    public static double Share(int level) => Math.Clamp(level, Least, Most) / (double)Most;
}
