using System.Globalization;
using static System.FormattableString;

namespace KTable.NetRelay;

public sealed record Profile(
    double PingMs, double JitterMs, double LossPercent, double BurstLength,
    double DuplicatePercent, double ReorderPercent, double DownKbps, double UpKbps, double QueueMs)
{
    const double DefaultQueueMs = 500;
    const double KbpsPerMbps = 1000;

    public static readonly Profile Lan = new(0, 0, 0, 1, 0, 0, 0, 0, DefaultQueueMs);

    public static readonly IReadOnlyList<(string Name, Profile Profile)> Presets =
    [
        ("lan", Lan),
        ("good", Lan with { PingMs = 30, JitterMs = 4, DownKbps = 50_000, UpKbps = 10_000 }),
        ("dsl", Lan with { PingMs = 60, JitterMs = 8, LossPercent = 0.2, DownKbps = 8_000, UpKbps = 800 }),
        ("wifi", Lan with { PingMs = 45, JitterMs = 25, LossPercent = 1, BurstLength = 2, DownKbps = 20_000, UpKbps = 5_000 }),
        ("mobile", Lan with { PingMs = 90, JitterMs = 40, LossPercent = 2, BurstLength = 3, DownKbps = 5_000, UpKbps = 1_500 }),
        ("far", Lan with { PingMs = 280, JitterMs = 20, LossPercent = 0.5, DownKbps = 20_000, UpKbps = 5_000 }),
        ("awful", Lan with { PingMs = 400, JitterMs = 150, LossPercent = 8, BurstLength = 4, ReorderPercent = 1, DownKbps = 1_000, UpKbps = 256 }),
    ];

    public static string Settings => "ping, jitter (ms), loss, dup, reorder (%), burst (packets), down, up (kbit/s, 0 for no limit), queue (ms)";

    public static Profile Parse(string spec)
    {
        var parts = spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new FormatException("a connection needs a profile or settings, such as mobile or ping=120,loss=2");
        var profile = Lan;
        var settings = parts.AsEnumerable();
        if (!parts[0].Contains('='))
        {
            profile = Named(parts[0]) ?? throw new FormatException($"there is no profile called {parts[0]}: the profiles are {string.Join(", ", Presets.Select(p => p.Name))}");
            settings = parts.Skip(1);
        }
        return settings.Aggregate(profile, With);
    }

    public static Profile? Named(string name) =>
        Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Profile;

    static Profile With(Profile profile, string setting)
    {
        var eq = setting.IndexOf('=');
        if (eq < 0) throw new FormatException($"{setting} needs a value, such as {setting}=100 (a profile name only goes first)");
        var key = setting[..eq].Trim().ToLowerInvariant();
        var text = setting[(eq + 1)..].Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v) || v < 0)
            throw new FormatException($"{key}={text}: the value must be a number of 0 or more");
        return key switch
        {
            "ping" => profile with { PingMs = v },
            "jitter" => profile with { JitterMs = v },
            "loss" => profile with { LossPercent = Percent(key, v) },
            "burst" => profile with { BurstLength = v >= 1 ? v : throw new FormatException($"burst={text}: a burst is at least 1 packet") },
            "dup" => profile with { DuplicatePercent = Percent(key, v) },
            "reorder" => profile with { ReorderPercent = Percent(key, v) },
            "down" => profile with { DownKbps = v },
            "up" => profile with { UpKbps = v },
            "queue" => profile with { QueueMs = v },
            _ => throw new FormatException($"{key} is not a setting: the settings are {Settings}"),
        };
    }

    static double Percent(string key, double v) => v <= 100 ? v : throw new FormatException(Invariant($"{key}={v}: a percentage is at most 100"));

    public string Describe()
    {
        var parts = new List<string> { Invariant($"ping {PingMs:0} ms") + (JitterMs > 0 ? Invariant($" ± {JitterMs:0}") : "") };
        if (LossPercent > 0) parts.Add(Invariant($"loss {LossPercent:0.##}%") + (BurstLength > 1 ? Invariant($" in bursts of {BurstLength:0.#}") : ""));
        if (DuplicatePercent > 0) parts.Add(Invariant($"duplicates {DuplicatePercent:0.##}%"));
        if (ReorderPercent > 0) parts.Add(Invariant($"late {ReorderPercent:0.##}%"));
        parts.Add($"down {Rate(DownKbps)}");
        parts.Add($"up {Rate(UpKbps)}");
        if ((DownKbps > 0 || UpKbps > 0) && QueueMs != DefaultQueueMs) parts.Add(Invariant($"queue {QueueMs:0} ms"));
        return string.Join(", ", parts);
    }

    public static string Rate(double kbps) => kbps switch
    {
        <= 0 => "no limit",
        >= KbpsPerMbps => Invariant($"{kbps / KbpsPerMbps:0.#} Mbit/s"),
        _ => Invariant($"{kbps:0} kbit/s"),
    };
}
