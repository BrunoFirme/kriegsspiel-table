using System.Globalization;
using System.Net;
using System.Net.Sockets;
using static System.FormattableString;

namespace KTable.NetRelay;

public sealed record LaneSetting(int Port, string Spec)
{
    public static LaneSetting Parse(string text)
    {
        var eq = text.IndexOf('=');
        var portText = eq < 0 ? text : text[..eq];
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > IPEndPoint.MaxPort)
            throw new FormatException($"--lane {text}: start with a port from 1 to {IPEndPoint.MaxPort}, such as 7801=mobile");
        var spec = eq < 0 ? "lan" : text[(eq + 1)..];
        Profile.Parse(spec);
        return new(port, spec);
    }
}

public sealed class Relay : IDisposable
{
    public const string Commands = """
        status                     every connection's counts (an empty line does the same)
        cut <port|all> [seconds]   drop everything both ways, for good or for that long
        restore <port|all>         end a cut
        set <port|all> <profile>   change a connection, such as: set 7801 mobile,loss=5
        hostup <kbit/s>            limit the host's upload shared by everyone (0 for no limit)
        profiles                   the named profiles and the settings
        quit                       stop the relay
        """;

    readonly Scheduler _scheduler = new();
    readonly Link _hostUp;
    readonly List<Lane> _lanes = [];

    public Relay(IPEndPoint host, IEnumerable<LaneSetting> lanes, double hostUpKbps, IPAddress listenOn, Action<string> say)
    {
        Host = host;
        _hostUp = new Link(HostUpload(hostUpKbps), Direction.Down);
        try
        {
            foreach (var lane in lanes) _lanes.Add(new Lane(new IPEndPoint(listenOn, lane.Port), host, lane.Spec, _scheduler, _hostUp, say));
        }
        catch (Exception e) when (e is SocketException or FormatException)
        {
            Dispose();
            throw;
        }
    }

    public IPEndPoint Host { get; }
    public IReadOnlyList<Lane> Lanes => _lanes;

    static Profile HostUpload(double kbps) => Profile.Lan with { DownKbps = kbps };

    public static string ProfileList() =>
        string.Join(Environment.NewLine, Profile.Presets.Select(p => $"  {p.Name,-8} {p.Profile.Describe()}"))
        + Environment.NewLine + "  settings: " + Profile.Settings;

    public string Status()
    {
        var hostUp = _hostUp.Profile.DownKbps;
        var head = $"host {Host}, upload {Profile.Rate(hostUp)}" + (hostUp > 0 ? Invariant($", queue {_hostUp.QueuedMs(Clock.Now):0} ms") : "");
        return string.Join(Environment.NewLine, _lanes.Select(l => l.Status()).Prepend(head));
    }

    public string Describe() =>
        string.Join(Environment.NewLine, _lanes.Select(l => $"  {l.Port,-6} {l.Spec,-14} {l.Profile.Describe()}"));

    public string Do(string line)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            return words switch
            {
                [] or ["status"] => Status(),
                ["cut", var which] => Each(which, l => l.Cut(null)),
                ["cut", var which, var seconds] => Each(which, l => l.Cut(Seconds(seconds))),
                ["restore", var which] => Each(which, l => l.Restore()),
                ["set", var which, var spec] => Each(which, l => l.Set(spec)),
                ["hostup", var kbps] => HostUp(kbps),
                ["profiles"] => ProfileList(),
                ["help" or "?"] => Commands,
                _ => $"not understood: {line.Trim()}{Environment.NewLine}{Commands}",
            };
        }
        catch (FormatException e) { return e.Message; }
    }

    string Each(string which, Action<Lane> act)
    {
        var chosen = which == "all" ? _lanes : _lanes.Where(l => l.Port.ToString(CultureInfo.InvariantCulture) == which).ToList();
        if (chosen.Count == 0) throw new FormatException($"there is no connection on {which}: the ports are {string.Join(", ", _lanes.Select(l => l.Port))}, or all");
        foreach (var lane in chosen) act(lane);
        return string.Join(Environment.NewLine, chosen.Select(l => l.Status()));
    }

    static double Seconds(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && double.IsFinite(s) && s > 0
            ? s : throw new FormatException($"{text}: give the seconds as a number above 0");

    string HostUp(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var kbps) || !double.IsFinite(kbps) || kbps < 0)
            throw new FormatException($"{text}: give the host's upload in kbit/s, or 0 for no limit");
        _hostUp.Profile = HostUpload(kbps);
        return Status();
    }

    public void Dispose()
    {
        foreach (var lane in _lanes) lane.Dispose();
        _scheduler.Dispose();
    }
}
