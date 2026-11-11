using System.Globalization;
using KTable.Core;

namespace KTable.Client;

public sealed record EndedRun(int Pid, DateTime Started);

public sealed class LastRun(string dir)
{
    const string Prefix = "running-", Suffix = ".txt";
    const string StampFormat = "yyyy-MM-dd HH:mm:ss";

    static readonly string[] GraphicsLostMarks =
        { "device was lost", "VK_ERROR_DEVICE_LOST", "DXGI_ERROR_DEVICE_REMOVED", "DXGI_ERROR_DEVICE_HUNG", "DXGI_ERROR_DEVICE_RESET", "887A0005", "887A0006", "887A0007" };

    public static bool GraphicsLost(IEnumerable<string> lines) => lines.Any(l => GraphicsLostMarks.Any(m => l.Contains(m, StringComparison.OrdinalIgnoreCase)));

    string MarkOf(int pid) => Path.Combine(dir, $"{Prefix}{pid.ToString(CultureInfo.InvariantCulture)}{Suffix}");

    public void Begin(int pid, DateTime started)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(MarkOf(pid), started.ToString("O", CultureInfo.InvariantCulture));
    }

    public void End(int pid) => BestEffort.DeleteFile(MarkOf(pid));

    public List<EndedRun> EndedUnexpectedly(Func<int, DateTime, bool> stillRunning)
    {
        var ended = new List<EndedRun>();
        if (!Directory.Exists(dir)) return ended;
        foreach (var mark in Directory.EnumerateFiles(dir, Prefix + "*" + Suffix))
        {
            var name = Path.GetFileNameWithoutExtension(mark)[Prefix.Length..];
            var started = BestEffort.Read(() => DateTime.Parse(File.ReadAllText(mark).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTime.MinValue);
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || started == DateTime.MinValue) { BestEffort.DeleteFile(mark); continue; }
            if (stillRunning(pid, started)) continue;
            BestEffort.DeleteFile(mark);
            ended.Add(new EndedRun(pid, started));
        }
        return ended.OrderBy(r => r.Started).ToList();
    }

    public static List<string> LogSince(IEnumerable<string> lines, DateTime started, int keep)
    {
        var from = started.AddTicks(-(started.Ticks % TimeSpan.TicksPerSecond));
        var kept = new Queue<string>();
        var inRun = false;
        foreach (var line in lines)
        {
            if (line.Length >= StampFormat.Length && DateTime.TryParseExact(line[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                inRun = at >= from;
            if (!inRun) continue;
            kept.Enqueue(line);
            if (kept.Count > keep) kept.Dequeue();
        }
        return kept.ToList();
    }
}
