using System.Text.Json.Serialization;
using KTable.Core.Protocol;

namespace KTable.Core.Model;

public sealed class CrowdTownSet
{
    public const string Working = "working", Market = "market", Holy = "holy", Curfew = "curfew";
    public static readonly IReadOnlyList<string> Days = [Working, Market, Holy, Curfew];
    const int MostId = 120;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? People { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Day { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool NoTraffic { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CrowdTalk? Talk { get; set; }

    [JsonIgnore] public bool Empty => People == null && Day == null && !NoTraffic && Talk is not { Lines.Count: > 0 };

    public CrowdTownSet Clone() => new() { People = People, Day = Day, NoTraffic = NoTraffic, Talk = Talk?.Clone() };

    public string? Problem() =>
        People is { } p && (p.Trim().Length == 0 || p.Length > MostId) ? "no such nationality"
        : Day != null && !Days.Contains(Day) ? "no such day"
        : Talk?.Problem();

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
    public const int MostLines = 60, MostLine = Earshot.MostLength;
    public const double LeastMinutes = 0.5, MostMinutes = 24 * 60;

    public List<string> Lines { get; set; } = new();
    public double EveryMin { get; set; } = 5;
    public int Loud { get; set; } = Earshot.Say;

    public CrowdTalk Clone() => new() { Lines = new(Lines), EveryMin = EveryMin, Loud = Loud };

    public string? Problem() =>
        Lines.Count > MostLines ? $"at most {MostLines} phrases a town"
        : Lines.Any(l => l.Trim().Length == 0) ? "an empty phrase"
        : Lines.Any(l => l.Length > MostLine) ? "a phrase is too long"
        : EveryMin is < LeastMinutes or > MostMinutes ? "how often is out of reach"
        : Loud is < Earshot.Whisper or > Earshot.Shout ? "no such voice"
        : null;
}
