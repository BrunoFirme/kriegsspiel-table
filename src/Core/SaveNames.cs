using System.Globalization;
using System.Text;

namespace KTable.Core;

public static class SaveNames
{
    const int LongestName = 80;
    static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    public static string Clean(string? name, string fallback = "Save")
    {
        var b = new StringBuilder();
        foreach (var c in (name ?? "").Trim())
            b.Append(c < 32 || "<>:\"/\\|?*".Contains(c) ? '-' : c);
        var s = b.ToString().Trim().TrimEnd('.', ' ');
        if (s.StartsWith('.')) s = s.TrimStart('.');
        if (s.Length > LongestName) s = s[..LongestName].TrimEnd('.', ' ');
        if (s.Length == 0) return fallback;
        if (DeviceNames.Contains(s.Split('.')[0], StringComparer.OrdinalIgnoreCase)) s += "_";
        return s;
    }

    public static string ShortTime(string gameTime) =>
        DateTime.TryParse(gameTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.ToString("d MMM HH'h'mm", CultureInfo.InvariantCulture) : "";

    public static string Turn(int turn, string gameTime) => ShortTime(gameTime) is { Length: > 0 } when ? $"Turn {turn} · {when}" : $"Turn {turn}";

    public static string Game(int number) => $"Game {number}";

    public static string From(string save, string turn) => Clean($"{save} · from {turn}");
}
