namespace KTable.Core.Model;

public static class MoonPhases
{
    const int Eighths = 8, Percent = 100;

    static readonly string[] Names = { "new", "waxing crescent", "first quarter", "waxing gibbous", "full", "waning gibbous", "last quarter", "waning crescent" };

    public static double Lit(double phase) => (1 - Math.Cos(2 * Math.PI * phase)) / 2;

    public static string Name(double phase) => Names[(int)Math.Floor(phase * Eighths + 0.5) % Eighths];

    public static IEnumerable<(string Name, double Phase, double Lit)> Of(CalendarDef calendar, long seconds) =>
        calendar.Moons.Select(m => calendar.MoonPhase(m, seconds)).Zip(calendar.Moons, (phase, m) => (m.Name, phase, Lit(phase)));

    public static string Words(double phase) => $"{Name(phase)}, {Math.Round(Lit(phase) * Percent)}% lit";

    public static string? Line(CalendarDef calendar, long seconds) =>
        calendar.Moons.Count == 0 ? null : string.Join("; ", Of(calendar, seconds).Select(m => $"{m.Name} {Words(m.Phase)}"));
}
