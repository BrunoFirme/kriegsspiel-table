using System.Diagnostics;
using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public static class Ticker
{
    static readonly Stopwatch Running = Stopwatch.StartNew();

    public static Func<double>? Source { get; set; }

    public static double Seconds => Source?.Invoke() ?? Running.Elapsed.TotalSeconds;
}

public sealed class GameTimer
{
    public const int MostNameLength = 80, MostOnAClock = 200;

    public string Name { get; set; } = "";
    public long Due { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Everyone { get; set; }
    public GameTimer Clone() => new() { Name = Name, Due = Due, Everyone = Everyone };
}

public class ClockTime : IJsonOnDeserialized
{
    public const int MostTurnSeconds = 6_000_000;
    public const double MostRate = 100_000;

    long _base;
    double _since = Ticker.Seconds;

    public long Seconds
    {
        get => Runs ? Bounded(_base + (long)Math.Floor(Rate * Math.Max(0, Ticker.Seconds - _since))) : _base;
        set => Put(value);
    }

    public int TurnSeconds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double Rate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<GameTimer>? Timers { get; set; }

    [JsonIgnore] public bool Runs => Rate > 0;

    [JsonIgnore] public long Now => Seconds;

    static long Bounded(long seconds) => Math.Clamp(seconds, -CalendarDef.MostSeconds, CalendarDef.MostSeconds);

    public void Settle() => Put(Seconds);

    public void Put(long seconds)
    {
        _base = Bounded(seconds);
        _since = Ticker.Seconds;
    }

    public bool CanMove(long by) => Math.Abs(Now + by) <= CalendarDef.MostSeconds;

    public void Move(long by) => _base = Bounded(_base + by);

    public void Run(double rate)
    {
        Settle();
        Rate = double.IsFinite(rate) ? Math.Clamp(rate, 0, MostRate) : 0;
    }

    public GameTimer? Timer(string name) => Timers?.Find(t => t.Name == name);

    public IEnumerable<GameTimer> Due(long after, long upTo) => Timers?.Where(t => t.Due > after && t.Due <= upTo).OrderBy(t => t.Due) ?? Enumerable.Empty<GameTimer>();

    public GameTimer? NextDue() => Timers?.Where(t => t.Due > Now).MinBy(t => t.Due);

    internal void CopyTo(ClockTime other)
    {
        other._base = _base;
        other.TurnSeconds = TurnSeconds;
        other.Rate = Rate;
        other.Timers = Timers?.Select(t => t.Clone()).ToList();
        other._since = _since;
    }

    public ClockTime CopyTime()
    {
        var copy = new ClockTime();
        CopyTo(copy);
        return copy;
    }

    void IJsonOnDeserialized.OnDeserialized() => Read();

    protected virtual void Read() => _since = Ticker.Seconds;
}

public sealed class GameClock : ClockTime
{
    public const int UsualTurnSeconds = 900, MostSteps = 12, MostNameLength = 80;
    public static readonly long UsualStart = CalendarDef.Gregorian.Seconds(new DateParts(1800, 1, 1, 6, 0, 0)) ?? 0;
    public static readonly IReadOnlyList<int> UsualSteps = new[] { CalendarDef.SecondsInMinute, 10 * CalendarDef.SecondsInMinute, CalendarDef.SecondsInHour, CalendarDef.SecondsInDay };

    string? _formerTime;
    int? _formerMinutes;

    public GameClock()
    {
        Seconds = UsualStart;
        TurnSeconds = UsualTurnSeconds;
    }

    public int Turn { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CalendarDef? Calendar { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<int>? Steps { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, ClockTime>? Apart { get; set; }

    [JsonPropertyName("gameTime"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FormerGameTime { get => null; set => _formerTime = value; }

    [JsonPropertyName("incrementMinutes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FormerIncrementMinutes { get => null; set => _formerMinutes = value; }

    [JsonIgnore] public CalendarDef Cal => Calendar ?? CalendarDef.Gregorian;

    [JsonIgnore] public IReadOnlyList<int> StepList => Steps is { Count: > 0 } own ? own : UsualSteps;

    [JsonIgnore]
    public string GameTime
    {
        get => Cal.Text(Now);
        set => Put(Cal.Read(value) ?? Now);
    }

    public static GameClock At(string gameTime, int turn = 0, int turnSeconds = UsualTurnSeconds) => new() { Turn = turn, GameTime = gameTime, TurnSeconds = turnSeconds };

    public ClockTime Of(string? place) => place != null && Apart != null && Apart.TryGetValue(place, out var apart) ? apart : this;

    public IEnumerable<(string? Place, ClockTime Time)> All()
    {
        yield return (null, this);
        if (Apart == null) yield break;
        foreach (var (place, time) in Apart) yield return (place, time);
    }

    public bool CanAdvance(int turns) => All().All(c => c.Time.CanMove((long)turns * c.Time.TurnSeconds));

    public void Advance(int turns)
    {
        Turn += turns;
        foreach (var (_, time) in All()) time.Move((long)turns * time.TurnSeconds);
    }

    public GameClock Copy()
    {
        var copy = new GameClock { Turn = Turn, Name = Name, Calendar = Calendar?.Clone(), Steps = Steps?.ToList() };
        CopyTo(copy);
        copy.Apart = Apart?.ToDictionary(a => a.Key, a => a.Value.CopyTime());
        return copy;
    }

    public GameClock AsOf(string? place)
    {
        if (place == null || Apart == null || !Apart.TryGetValue(place, out var time)) return this;
        var seen = new GameClock { Turn = Turn, Name = Name, Calendar = Calendar, Steps = Steps };
        time.CopyTo(seen);
        return seen;
    }

    public GameClock Paused()
    {
        var copy = Copy();
        copy.PauseAll();
        return copy;
    }

    public void PauseAll()
    {
        foreach (var (_, time) in All()) time.Run(0);
    }

    public GameClock Shown()
    {
        var copy = Copy();
        foreach (var (_, time) in copy.All()) time.Timers = time.Timers?.Where(t => t.Everyone).ToList() is { Count: > 0 } shown ? shown : null;
        return copy;
    }

    public static string StepText(int seconds)
    {
        const int minute = CalendarDef.SecondsInMinute, hour = CalendarDef.SecondsInHour, day = CalendarDef.SecondsInDay;
        if (seconds <= 0) return "none";
        if (seconds % day == 0) return seconds == day ? "1 day" : $"{seconds / day} days";
        if (seconds < minute) return $"{seconds} s";
        if (seconds % minute != 0) return $"{seconds / minute}′ {seconds % minute}″";
        if (seconds < hour) return $"{seconds / minute}′";
        return seconds % hour == 0 ? $"{seconds / hour} h" : $"{seconds / hour} h {seconds % hour / minute}′";
    }

    protected override void Read()
    {
        base.Read();
        if (_formerMinutes is { } minutes) TurnSeconds = Math.Clamp(minutes, 1, MostTurnSeconds / CalendarDef.SecondsInMinute) * CalendarDef.SecondsInMinute;
        if (_formerTime is { } text)
        {
            if (Cal.Read(text) is { } seconds) Seconds = seconds;
            else if (text.Trim() is { Length: > 0 } words) Name = words.Length <= MostNameLength ? words : words[..MostNameLength];
        }
        _formerTime = null;
        _formerMinutes = null;
    }
}
