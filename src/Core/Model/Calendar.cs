using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KTable.Core.Model;

public sealed class MonthDef
{
    public string Name { get; set; } = "";
    public int Days { get; set; } = 30;
    public MonthDef Clone() => new() { Name = Name, Days = Days };
}

public sealed class MoonDef
{
    public string Name { get; set; } = "";
    public double CycleDays { get; set; } = 29.530588;
    public double PhaseAtStart { get; set; }
    public MoonDef Clone() => new() { Name = Name, CycleDays = CycleDays, PhaseAtStart = PhaseAtStart };
}

public sealed class LeapRule
{
    public int Month { get; set; }
    public int Every { get; set; } = 4;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Except { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int Unless { get; set; }
    public LeapRule Clone() => new() { Month = Month, Every = Every, Except = Except, Unless = Unless };
}

public readonly record struct DateParts(long Year, int Month, int Day, int Hour, int Minute, int Second);

public sealed partial class CalendarDef
{
    public const int SecondsInMinute = 60, SecondsInHour = 3600, SecondsInDay = 86400;
    public const long MostSeconds = 10_000_000_000_000;
    public const string GregorianId = "gregorian", JulianId = "julian";
    const int ShortName = 3, JulianStartDay = -2, GregorianLongestDay = 172, JulianLongestDay = 167;
    const double DaysToKnownNewMoon = 730124.76;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<MonthDef> Months { get; set; } = new();
    public List<string> WeekDays { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int FirstWeekDay { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LeapRule? Leap { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Before { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? After { get; set; }
    public List<MoonDef> Moons { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int LongestDay { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long StartDay { get; set; }

    static readonly string[] RealMonths =
        { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
    static readonly int[] RealDays = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
    static readonly string[] RealWeek = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

    public static CalendarDef Gregorian { get; } = Real(GregorianId, "Gregorian", new LeapRule { Month = 1, Every = 4, Except = 100, Unless = 400 }, 0, GregorianLongestDay);
    public static CalendarDef Julian { get; } = Real(JulianId, "Julian", new LeapRule { Month = 1, Every = 4 }, JulianStartDay, JulianLongestDay);
    public static IReadOnlyList<CalendarDef> Standard { get; } = new[] { Gregorian, Julian };

    static CalendarDef Real(string id, string name, LeapRule leap, long startDay, int longestDay)
    {
        var moon = new MoonDef { Name = "Moon" };
        moon.PhaseAtStart = Wrapped(-DaysToKnownNewMoon / moon.CycleDays);
        return new CalendarDef
        {
            Id = id, Name = name, Months = RealMonths.Select((m, i) => new MonthDef { Name = m, Days = RealDays[i] }).ToList(),
            WeekDays = RealWeek.ToList(), Leap = leap, Before = "BC", After = "AD", Moons = { moon }, LongestDay = longestDay, StartDay = startDay,
        };
    }

    public CalendarDef Clone() => new()
    {
        Id = Id, Name = Name, Months = Months.Select(m => m.Clone()).ToList(), WeekDays = WeekDays.ToList(), FirstWeekDay = FirstWeekDay,
        Leap = Leap?.Clone(), Before = Before, After = After, Moons = Moons.Select(m => m.Clone()).ToList(), LongestDay = LongestDay, StartDay = StartDay,
    };

    [JsonIgnore] public bool Sound => Months.Count > 0 && Months.All(m => m.Days is > 0 and <= 1000) && Months.Count <= 100
        && (Leap == null || Leap.Every > 0 && Leap.Month >= 0 && Leap.Month < Months.Count && Leap.Except >= 0 && Leap.Unless >= 0)
        && Moons.All(m => m.CycleDays > 0 && double.IsFinite(m.CycleDays) && double.IsFinite(m.PhaseAtStart));

    [JsonIgnore] int CommonDays => Months.Sum(m => m.Days);

    static long Floored(long a, long b) => Math.DivRem(a, b, out long rest) - (rest < 0 ? 1 : 0);

    static long Rest(long a, long b) => a - Floored(a, b) * b;

    static double Wrapped(double turns) => turns - Math.Floor(turns);

    bool IsLeap(long index)
    {
        if (Leap is not { } rule) return false;
        long year = index + 1;
        if (Rest(year, rule.Every) != 0) return false;
        return rule.Except <= 0 || Rest(year, rule.Except) != 0 || (rule.Unless > 0 && Rest(year, rule.Unless) == 0);
    }

    long LeapsBefore(long index)
    {
        if (Leap is not { } rule) return 0;
        long leaps = Floored(index, rule.Every);
        if (rule.Except > 0) leaps -= Floored(index, rule.Except);
        if (rule.Except > 0 && rule.Unless > 0) leaps += Floored(index, rule.Unless);
        return leaps;
    }

    long DaysBefore(long index) => index * CommonDays + LeapsBefore(index);

    public int YearDays(long index) => CommonDays + (IsLeap(index) ? 1 : 0);

    int MonthDays(long index, int month) => Months[month].Days + (IsLeap(index) && Leap!.Month == month ? 1 : 0);

    long IndexOfDay(long day)
    {
        long index = Floored(day, CommonDays);
        while (DaysBefore(index) > day) index--;
        while (DaysBefore(index + 1) <= day) index++;
        return index;
    }

    long YearOf(long index) => Before != null && index < 0 ? index : index + 1;

    long? IndexOf(long year) => Before == null ? year - 1 : year > 0 ? year - 1 : year < 0 ? year : null;

    static long Bounded(long seconds) => Math.Clamp(seconds, -MostSeconds, MostSeconds);

    public DateParts Parts(long seconds)
    {
        seconds = Bounded(seconds);
        long day = Floored(seconds, SecondsInDay) - StartDay;
        int inDay = (int)Rest(seconds, SecondsInDay);
        long index = IndexOfDay(day);
        int left = (int)(day - DaysBefore(index)), month = 0;
        while (month < Months.Count - 1 && left >= MonthDays(index, month)) left -= MonthDays(index, month++);
        return new DateParts(YearOf(index), month + 1, left + 1, inDay / SecondsInHour, inDay % SecondsInHour / SecondsInMinute, inDay % SecondsInMinute);
    }

    public long? Seconds(DateParts p)
    {
        if (IndexOf(p.Year) is not { } index || p.Month < 1 || p.Month > Months.Count) return null;
        if (p.Day < 1 || p.Day > MonthDays(index, p.Month - 1) || p.Hour is < 0 or > 23 || p.Minute is < 0 or > 59 || p.Second is < 0 or > 59) return null;
        if (Math.Abs(index) > MostSeconds / SecondsInDay / CommonDays) return null;
        long day = DaysBefore(index) + p.Day - 1 + StartDay;
        for (int m = 0; m < p.Month - 1; m++) day += MonthDays(index, m);
        long seconds = day * SecondsInDay + p.Hour * SecondsInHour + p.Minute * SecondsInMinute + p.Second;
        return Math.Abs(seconds) > MostSeconds ? null : seconds;
    }

    public int DayOfYear(long seconds)
    {
        long day = Floored(Bounded(seconds), SecondsInDay) - StartDay;
        return (int)(day - DaysBefore(IndexOfDay(day))) + 1;
    }

    public double YearTurn(long seconds)
    {
        long day = Floored(Bounded(seconds), SecondsInDay) - StartDay;
        long index = IndexOfDay(day);
        int longest = LongestDay > 0 ? LongestDay : YearDays(index) / 2;
        return Wrapped((day - DaysBefore(index) + 1 - longest) / (double)YearDays(index));
    }

    public double HourOf(long seconds) => Rest(Bounded(seconds), SecondsInDay) / (double)SecondsInHour;

    public double MoonPhase(MoonDef moon, long seconds) => Wrapped(moon.PhaseAtStart + (Bounded(seconds) / (double)SecondsInDay - StartDay) / moon.CycleDays);

    public string? WeekDay(long seconds) =>
        WeekDays.Count == 0 ? null : WeekDays[(int)Rest(Floored(Bounded(seconds), SecondsInDay) + FirstWeekDay, WeekDays.Count)];

    static string Clock(DateParts p) =>
        p.Second == 0 ? string.Create(CultureInfo.InvariantCulture, $"{p.Hour:00}:{p.Minute:00}") : string.Create(CultureInfo.InvariantCulture, $"{p.Hour:00}:{p.Minute:00}:{p.Second:00}");

    public string Text(long seconds)
    {
        var p = Parts(seconds);
        string year = Math.Abs(p.Year).ToString("0000", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{(p.Year < 0 ? "-" : "")}{year}-{p.Month:00}-{p.Day:00}T{Clock(p)}");
    }

    static string Short(string name) => name.Length <= ShortName ? name : name[..ShortName];

    string YearWords(long year) => Before != null && year < 0 ? $"{-year} {Before}" : year.ToString(CultureInfo.InvariantCulture);

    public string Date(long seconds)
    {
        var p = Parts(seconds);
        string day = WeekDay(seconds) is { } week ? Short(week) + " " : "";
        return string.Create(CultureInfo.InvariantCulture, $"{day}{p.Day} {Short(Months[p.Month - 1].Name)} {YearWords(p.Year)}");
    }

    public string TimeOfDay(long seconds) => Clock(Parts(seconds));

    public string Words(long seconds) => $"{Date(seconds)}  {TimeOfDay(seconds)}";

    [GeneratedRegex(@"^\s*(-?\d{1,7})-(\d{1,3})-(\d{1,4})(?:[T ](\d{1,2}):(\d{2})(?::(\d{2}))?)?\s*$")]
    private static partial Regex Written();

    [GeneratedRegex(@"^(\d{1,2}):(\d{2})(?::(\d{2}))?$")]
    private static partial Regex WrittenClock();

    static int Number(Group g) => g.Success ? int.Parse(g.Value, CultureInfo.InvariantCulture) : 0;

    public long? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Written().Match(text);
        if (!m.Success) return ReadWords(text);
        long year = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return Seconds(new DateParts(year, Number(m.Groups[2]), Number(m.Groups[3]), Number(m.Groups[4]), Number(m.Groups[5]), Number(m.Groups[6])));
    }

    long? ReadWords(string text)
    {
        var numbers = new List<long>();
        int month = 0, hour = 0, minute = 0, second = 0;
        bool before = false;
        foreach (string word in text.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var clock = WrittenClock().Match(word);
            if (clock.Success) (hour, minute, second) = (Number(clock.Groups[1]), Number(clock.Groups[2]), Number(clock.Groups[3]));
            else if (long.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n)) numbers.Add(n);
            else if (Before != null && word.Equals(Before, StringComparison.OrdinalIgnoreCase)) before = true;
            else if (After != null && word.Equals(After, StringComparison.OrdinalIgnoreCase)) before = false;
            else if (MonthNamed(word) is { } named) month = named;
            else if (!IsWeekDay(word)) return null;
        }
        if (month == 0 || numbers.Count != 2 || numbers[0] is < 1 or > 1000) return null;
        return Seconds(new DateParts(before ? -Math.Abs(numbers[1]) : numbers[1], month, (int)numbers[0], hour, minute, second));
    }

    int? MonthNamed(string word)
    {
        if (word.Length < ShortName) return null;
        int at = Months.FindIndex(m => m.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase));
        return at < 0 ? null : at + 1;
    }

    bool IsWeekDay(string word) => word.Length >= ShortName && WeekDays.Any(d => d.StartsWith(word, StringComparison.OrdinalIgnoreCase));
}
