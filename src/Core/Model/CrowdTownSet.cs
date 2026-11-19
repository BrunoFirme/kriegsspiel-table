using System.Text.Json.Serialization;
using KTable.Core.Protocol;

namespace KTable.Core.Model;

public sealed class CrowdTownSet
{
    public const string Working = "working", Market = "market", Holy = "holy", Curfew = "curfew";
    public static readonly IReadOnlyList<string> Days = [Working, Market, Holy, Curfew];
    public const int MostTalks = 24, MostLines = 40, MostLine = Earshot.MostLength;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Day { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool NoTraffic { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<CrowdTalk>? Talk { get; set; }

    [JsonIgnore] public bool Empty => Day == null && !NoTraffic && Talk is not { Count: > 0 };

    public CrowdTownSet Clone() => new() { Day = Day, NoTraffic = NoTraffic, Talk = Talk?.Select(t => t.Clone()).ToList() };

    public string? Problem() =>
        Day != null && !Days.Contains(Day) ? "no such day"
        : Talk is { Count: > MostTalks } ? $"at most {MostTalks} kinds of talk"
        : Talk?.FirstOrDefault(t => t.Problem() != null)?.Problem();

    [JsonIgnore] public DayKind? Kind => Day switch
    {
        Working => DayKind.Working,
        Market => DayKind.Market,
        Holy => DayKind.Holy,
        Curfew => DayKind.Curfew,
        _ => null,
    };
}

public sealed class CrowdTalk
{
    public const double LeastMinutes = 0.5, MostMinutes = 24 * 60;

    public string Place { get; set; } = "market";
    public List<string> Lines { get; set; } = new();
    public double EveryMin { get; set; } = 5;
    public int Loud { get; set; } = Earshot.Say;

    public CrowdTalk Clone() => new() { Place = Place, Lines = new(Lines), EveryMin = EveryMin, Loud = Loud };

    public string? Problem() =>
        !Enum.TryParse<CrowdUse>(Place, true, out _) ? "no such place"
        : Lines.Count > CrowdTownSet.MostLines ? $"at most {CrowdTownSet.MostLines} lines a place"
        : Lines.Any(l => l.Length > CrowdTownSet.MostLine) ? "a line is too long"
        : EveryMin is < LeastMinutes or > MostMinutes ? "how often is out of reach"
        : null;
}
