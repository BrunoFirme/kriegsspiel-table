using System.Globalization;
using System.Text.RegularExpressions;

namespace KTable.Core.Model;

public static partial class ClockWords
{
    const int DaysInWeek = 7;

    [GeneratedRegex(@"\G\s*(\d+(?:\.\d+)?)\s*([a-zA-Z′″]+)")]
    private static partial Regex Measure();

    [GeneratedRegex(@"^\s*(\d{1,2}):(\d{2})(?::(\d{2}))?\s*$")]
    private static partial Regex TimeOfDay();

    static int? Unit(string word) => word.ToLowerInvariant() switch
    {
        "s" or "sec" or "secs" or "second" or "seconds" or "″" => 1,
        "m" or "min" or "mins" or "minute" or "minutes" or "′" => CalendarDef.SecondsInMinute,
        "h" or "hr" or "hrs" or "hour" or "hours" => CalendarDef.SecondsInHour,
        "d" or "day" or "days" => CalendarDef.SecondsInDay,
        "w" or "week" or "weeks" => DaysInWeek * CalendarDef.SecondsInDay,
        _ => null,
    };

    public static long? Span(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string words = text.Trim();
        if (words.StartsWith("in ", StringComparison.OrdinalIgnoreCase)) words = words[3..];
        double seconds = 0;
        int at = 0;
        while (at < words.Length)
        {
            var m = Measure().Match(words, at);
            if (!m.Success || Unit(m.Groups[2].Value) is not { } unit) return null;
            seconds += double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * unit;
            at = m.Index + m.Length;
            while (at < words.Length && char.IsWhiteSpace(words[at])) at++;
        }
        return seconds is > 0 and <= CalendarDef.MostSeconds ? (long)Math.Round(seconds) : null;
    }

    public static long? When(CalendarDef calendar, long now, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (Span(text) is { } span) return now + span;
        var m = TimeOfDay().Match(text);
        if (!m.Success) return calendar.Read(text);
        int hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int second = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        if (hour > 23 || minute > 59 || second > 59) return null;
        var today = calendar.Parts(now);
        if (calendar.Seconds(today with { Hour = hour, Minute = minute, Second = second }) is not { } then) return null;
        return then > now ? then : then + CalendarDef.SecondsInDay;
    }

    public static long? Moment(CalendarDef calendar, long now, string? text)
    {
        if (calendar.Read(text) is { } read) return read;
        var m = TimeOfDay().Match(text ?? "");
        if (!m.Success) return null;
        int hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), minute = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        int second = m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
        return hour > 23 || minute > 59 || second > 59 ? null : calendar.Seconds(calendar.Parts(now) with { Hour = hour, Minute = minute, Second = second });
    }

    public static string Left(long seconds)
    {
        if (seconds <= 0) return "due";
        if (seconds < CalendarDef.SecondsInMinute) return $"{seconds} s";
        if (seconds < CalendarDef.SecondsInHour) return $"{seconds / CalendarDef.SecondsInMinute}′";
        if (seconds < CalendarDef.SecondsInDay) return $"{seconds / CalendarDef.SecondsInHour} h {seconds % CalendarDef.SecondsInHour / CalendarDef.SecondsInMinute}′";
        return $"{seconds / CalendarDef.SecondsInDay} d {seconds % CalendarDef.SecondsInDay / CalendarDef.SecondsInHour} h";
    }

    public static string Due(CalendarDef calendar, long now, long due) =>
        calendar.DayOfYear(now) == calendar.DayOfYear(due) && Math.Abs(due - now) < CalendarDef.SecondsInDay ? calendar.TimeOfDay(due) : calendar.Words(due);

    public static string Short(CalendarDef calendar, long seconds)
    {
        var p = calendar.Parts(seconds);
        string month = calendar.Months[p.Month - 1].Name;
        return string.Create(CultureInfo.InvariantCulture, $"{p.Day} {(month.Length <= 3 ? month : month[..3])} {calendar.TimeOfDay(seconds)}");
    }

    public static string Rate(double rate) => rate <= 0 ? "Paused" : "×" + rate.ToString("0.##", CultureInfo.InvariantCulture);
}
