using System.Globalization;
using System.Net;
using System.Net.Sockets;
using KTable.NetRelay;

const int DefaultHostPort = 7777;
const int BadArguments = 2;
const int CannotStart = 1;

var usage = $"""
    Relays a game's UDP traffic and makes each connection behave like a real one: ping, jitter, loss, limited
    bandwidth, cuts. Clients join the relay's ports instead of the host's.

      NetRelay --lane <port>[=<profile>[,setting=value...]] [--lane ...] [--to <host:port>] [--host-up <kbit/s>] [--open] [--check]
      NetRelay profiles

      --lane     a port clients join, and how its connection behaves (lan if not given); clients on one port share it
      --to       the game host (127.0.0.1:{DefaultHostPort} if not given)
      --host-up  the host's upload, shared by everyone (no limit if not given)
      --open     take clients from other computers too, not only this one
      --check    print the connections and stop

    Example: NetRelay --to 127.0.0.1:7790 --lane 7791=good --lane 7792=mobile,loss=5 --host-up 2000

    Profiles:
    {Relay.ProfileList()}

    Commands while running:
    {Relay.Commands}
    """;

if (args is [] or ["--help" or "-h" or "help" or "/?"]) { Console.WriteLine(usage); return 0; }
if (args is ["profiles"]) { Console.WriteLine(Relay.ProfileList()); return 0; }

IPEndPoint host;
var lanes = new List<LaneSetting>();
double hostUpKbps = 0;
bool open = false, check = false;
try
{
    host = new IPEndPoint(IPAddress.Loopback, DefaultHostPort);
    for (var i = 0; i < args.Length; i++)
    {
        var option = args[i];
        string Value() => i + 1 < args.Length ? args[++i] : throw new FormatException($"{option} needs a value");
        switch (option)
        {
            case "--lane":
                lanes.Add(LaneSetting.Parse(Value()));
                break;
            case "--to":
                host = Address(Value());
                break;
            case "--host-up":
                hostUpKbps = Kbps(Value());
                break;
            case "--open":
                open = true;
                break;
            case "--check":
                check = true;
                break;
            default: throw new FormatException($"{option} is not an option: NetRelay --help lists them");
        }
    }
    if (lanes.Count == 0) throw new FormatException("give at least one --lane, such as --lane 7801=mobile");
    if (lanes.GroupBy(l => l.Port).FirstOrDefault(g => g.Count() > 1) is { } twice) throw new FormatException($"port {twice.Key} is given twice");
}
catch (FormatException e) { Console.Error.WriteLine(e.Message); return BadArguments; }
catch (SocketException e) { Console.Error.WriteLine("cannot find the host: " + e.Message); return BadArguments; }

if (check)
{
    Console.WriteLine($"host {host}, upload {Profile.Rate(hostUpKbps)}");
    foreach (var lane in lanes) Console.WriteLine($"  {lane.Port,-6} {lane.Spec,-14} {Profile.Parse(lane.Spec).Describe()}");
    return 0;
}

Relay relay;
try { relay = new Relay(host, lanes, hostUpKbps, open ? IPAddress.Any : IPAddress.Loopback, Console.WriteLine); }
catch (SocketException e) { Console.Error.WriteLine("cannot open the relay's ports: " + e.Message); return CannotStart; }

using (relay)
{
    Console.WriteLine($"relaying to {host}" + (open ? ", open to other computers" : ""));
    Console.WriteLine(relay.Describe());
    Console.WriteLine("type help for the commands");
    while (Console.ReadLine() is { } line && line.Trim() is not ("quit" or "exit"))
        Console.WriteLine(relay.Do(line));
}
return 0;

static IPEndPoint Address(string text)
{
    var colon = text.LastIndexOf(':');
    var (name, portText) = colon < 0 ? ("127.0.0.1", text) : (text[..colon].Trim('[', ']'), text[(colon + 1)..]);
    if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > IPEndPoint.MaxPort)
        throw new FormatException($"--to {text}: give the host as address:port, such as 127.0.0.1:7777");
    var address = IPAddress.TryParse(name, out var given) ? given
        : Dns.GetHostAddresses(name).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
          ?? throw new FormatException($"--to {text}: no IPv4 address found for {name}");
    return new IPEndPoint(address, port);
}

static double Kbps(string text) =>
    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var kbps) && double.IsFinite(kbps) && kbps >= 0
        ? kbps : throw new FormatException($"--host-up {text}: give kbit/s, such as 2000");
