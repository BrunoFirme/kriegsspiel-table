using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class Table : IDisposable
{
    public const long Ump = 1, Ump2 = 2, Ump3 = 3, Obs = 30;
    public const long Host = Ump, Lead = Ump2;
    public const string Password = "abc";

    public readonly string Root;
    public readonly Scenario Scenario;
    public readonly PackageStore Store;
    public readonly GameServer Server;
    public readonly Dictionary<long, string> Tokens = new();
    long _now = 1_000;

    public static List<DieFace> SixSides() =>
    [
        new() { Value = "1", Up = [0, 0, 1] }, new() { Value = "2", Up = [0, 1, 0] }, new() { Value = "3", Up = [1, 0, 0] },
        new() { Value = "4", Up = [-1, 0, 0] }, new() { Value = "5", Up = [0, -1, 0] }, new() { Value = "6", Up = [0, 0, -1] },
    ];

    public Table(bool join = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "ktable-server-tests", Guid.NewGuid().ToString("N"));
        Json.Save(Path.Combine(Root, "testpack", "pack.json"), new PackManifest
        {
            Id = "testpack", Name = "Test pack",
            Pieces =
            {
                new PieceDef { Id = "inf", Name = "Infantry", Counters = { new CounterDef { Name = "str", Min = 0, Max = 12, Default = 8 }, new CounterDef { Name = "ammo", Min = -2, Max = 3 } } },
                new PieceDef { Id = "marker", Name = "Marker" },
                new PieceDef { Id = "die6", Name = "d6", Kind = "model", Tags = { "die" }, Faces = SixSides() },
            },
            Assets = { ["arms"] = new AssetEntry { File = "assets/arms.png", W = 64, H = 64 } },
        });
        Store = new PackageStore(Root);
        Scenario = MakeScenario();
        Server = new GameServer(GameState.FromScenario(Scenario), Store, new GameAccess { Password = Password }, clock: () => ++_now);
        if (join)
        {
            HostJoins("Umpire One");
            LetIn(Ump2, "Umpire Two", Role.LeadUmpire);
            LetIn(Ump3, "Umpire Three", Role.Umpire);
            LetIn(Obs, "Watcher", Role.Observer);
        }
    }

    public static Scenario MakeScenario() => new()
    {
        Id = "scn-test", Name = "Test", Packs = { "testpack" },
        Sheets = { new SheetDef { Id = "s-000001", Name = "Map", Image = "sha", Package = "testpack" } },
        UmpireBoard =
        {
            new PieceInstance { Id = "p-000002", Def = "inf", Pos = new(0, 0), Counters = { ["str"] = 8 }, Text = { ["name"] = "UMPIRE-SECRET-NAME" } },
            new PieceInstance { Id = "p-000003", Def = "marker", Pos = new(1, 0) },
        },
        Drawings = { ["umpire"] = new() { new Stroke { Id = "d-000005", Points = { new(0, 0), new(1, 1) } } } },
    };

    public JoinResult HostJoins(string name) => JoinResultOf(Server.Join(Host, new JoinRequest { Name = name }), Host, ok: true);

    public List<Outgoing> Knock(long peer, string name, string? password = Password, string? token = null) =>
        Server.Join(peer, new JoinRequest { Name = name, Password = password ?? "", Token = token, AppVersion = "test", Protocol = ProtocolVersion.Current });

    public JoinResult LetIn(long peer, string name, Role role, long by = Host)
    {
        var k = JoinResultOf(Knock(peer, name), peer, ok: false);
        Assert.True(k.Waiting, k.Error);
        var jr = JoinResultOf(Server.Control(by, new Admit { PeerId = peer, Role = role }), peer, ok: true);
        Assert.Equal(role, jr.Role);
        Assert.False(string.IsNullOrEmpty(jr.Token));
        Tokens[peer] = jr.Token!;
        return jr;
    }

    public JoinResult Rejoin(long peer, string token, string name = "") => JoinResultOf(Knock(peer, name, password: null, token: token), peer, ok: true);

    public static JoinResult JoinResultOf(List<Outgoing> outs, long peer, bool ok)
    {
        var jr = Assert.IsType<JoinResult>(outs.First(o => o.PeerId == peer && o.Message is JoinResult).Message);
        Assert.Equal(ok, jr.Ok);
        return jr;
    }

    public List<Outgoing> Door(long by, Message m)
    {
        var res = Server.Control(by, m);
        var err = res.Where(o => o.PeerId == by).Select(o => o.Message).OfType<ErrorMessage>().FirstOrDefault();
        Assert.True(err == null, err?.Text);
        return res;
    }

    public string DoorError(long by, Message m)
    {
        var res = Server.Control(by, m);
        var o = Assert.Single(res);
        Assert.Equal(by, o.PeerId);
        return Assert.IsType<ErrorMessage>(o.Message).Text;
    }

    public List<Outgoing> Ok(long peer, Command c)
    {
        var res = Server.Handle(peer, c);
        var err = res.Select(o => o.Message).OfType<ErrorMessage>().FirstOrDefault();
        Assert.True(err == null, err?.Text);
        return res;
    }

    public string Error(long peer, Command c)
    {
        var res = Server.Handle(peer, c);
        var o = Assert.Single(res);
        Assert.Equal(peer, o.PeerId);
        return Assert.IsType<ErrorMessage>(o.Message).Text;
    }

    public string StateJson => Json.Serialize(Server.State);

    public void Dispose()
    {
        BestEffort.DeleteFolder(Root);
    }
}
