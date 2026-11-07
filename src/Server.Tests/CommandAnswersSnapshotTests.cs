using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server.Scripting;

namespace KTable.Server.Tests;

public class CommandAnswersSnapshotTests
{
    const string Inside = "p-000001", Outside = "p-000002";
    static readonly Vec2 InPos = new(5, 5), OutPos = new(50, 50);
    const long ObserverId = 4;

    static readonly Role[] Roles = { Role.Observer, Role.Umpire, Role.LeadUmpire, Role.HostUmpire };

    internal static List<(Type type, string name)> Registered() =>
        typeof(Command).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => (a.DerivedType, (string)a.TypeDiscriminator!))
            .OrderBy(x => x.Item2, StringComparer.Ordinal).ToList();

    static string SnapshotPath => Path.Combine(TestSource.Dir("Server.Tests.csproj"), "Snapshots", "command-answers.txt");

    [Fact]
    public void Every_command_answers_as_the_snapshot_says()
    {
        var lines = Answers();
        var text = string.Join("\n", lines) + "\n";
        if (!File.Exists(SnapshotPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            File.WriteAllText(SnapshotPath, text);
            Assert.Fail($"snapshot written, review and commit: {SnapshotPath}");
        }
        var stored = ReadLines(SnapshotPath);
        var removed = stored.Except(lines).ToList();
        var added = lines.Except(stored).ToList();
        Assert.True(removed.Count == 0 && added.Count == 0,
            "the server's answers to commands changed. If that was meant, delete the snapshot and run again to re-take it.\n"
            + string.Concat(removed.Select(l => "- " + l + "\n")) + string.Concat(added.Select(l => "+ " + l + "\n")));
    }

    [Fact]
    public void Every_registered_command_type_has_a_line_in_the_snapshot()
    {
        Assert.True(File.Exists(SnapshotPath), "no snapshot yet: run Every_command_answers_as_the_snapshot_says to take it");
        var named = ReadLines(SnapshotPath).Select(l => l.Split(' ')[0]).ToHashSet();
        var missing = Registered().Where(r => !named.Contains(r.name)).Select(r => r.name).ToList();
        Assert.True(missing.Count == 0, "command types with no line in the snapshot (decide who may send them and what observers get, then re-take it): " + string.Join(", ", missing));
        Assert.Equal(83, Registered().Count);
    }

    static List<string> ReadLines(string path) =>
        File.ReadAllText(path).Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    internal static List<string> Answers()
    {
        var lines = new List<string>();
        foreach (var (type, name) in Registered())
        {
            lines.Add($"{name} {Describe(type, () => (Command)Activator.CreateInstance(type)!)}");
        }
        var variants = new (string name, Func<Command> make)[]
        {
            ("Say(to=me)", () => new Say { Text = "x", To = $"p:{ObserverId}" }),
            ("Say(to=other)", () => new Say { Text = "x", To = "p:99" }),
            ("Say(to=umpires)", () => new Say { Text = "x", To = "u:" }),
            ("SelectPiece(to=me)", () => new SelectPiece { Layer = Layers.Umpire, Id = Inside, To = $"p:{ObserverId}" }),
            ("ShowDialog(to=other)", () => new ShowDialog { To = "p:99", Title = "t", Text = "x" }),
            ("RollDice(on-pieces)", () => new RollDice { Layer = Layers.Umpire, PieceIds = { Inside } }),
            ("RestoreTurn(with-state)", () => new RestoreTurn { Index = 0, State = new GameState { Zones = { new Zone { Id = "z-000009" } } } }),
            ("Batch(SetZone)", () => new Batch { Items = { new SetZone { Zone = new Zone { Id = "z-000009" } } } }),
            ("Batch(SetZone+Say)", () => new Batch { Items = { new SetZone { Zone = new Zone { Id = "z-000009" } }, new Say { Text = "x" } } }),
            ("Batch(MovePiece)", () => new Batch { Items = { new MovePiece { Layer = Layers.Umpire } } }),
            ("Batch(Batch(MovePiece))", () => new Batch { Items = { new Batch { Items = { new MovePiece { Layer = Layers.Umpire } } } } }),
            ("MovePiece(other-layer)", () => new MovePiece { Layer = "elsewhere" }),
        };
        foreach (var (name, make) in variants) lines.Add($"{name} {Describe(make().GetType(), make)}");
        return lines.OrderBy(l => l, StringComparer.Ordinal).ToList();
    }

    static string Describe(Type type, Func<Command> make)
    {
        var sb = new StringBuilder();
        sb.Append(typeof(LayerCommand).IsAssignableFrom(type) ? "layer" : "table");
        sb.Append(" | allowed ").Append(RoleAnswers(r => GameServer.Allowed(Peer(r), make())));
        sb.Append(" | in-batch ").Append(RoleAnswers(r => GameServer.Allowed(Peer(r), new Batch { Items = { make() } })));
        sb.Append(" | observer ").Append(ObserverGets(make));
        sb.Append(" | zone ").Append(ZoneChecks(make));
        sb.Append(" | apply ").Append(ApplyAnswer(make));
        sb.Append(" | undo-reloads ").Append(Touched(make));
        return sb.ToString();
    }

    static PeerInfo Peer(Role r) => new() { PeerId = r == Role.Observer ? ObserverId : 3, Name = "Co", Role = r };

    static string RoleAnswers(Func<Role, bool> f) => string.Join(" ", Roles.Select(r => $"{Short(r)}:{(f(r) ? "y" : "n")}"));

    static string Short(Role r) => r switch { Role.Observer => "O", Role.Umpire => "U", Role.LeadUmpire => "L", _ => "H" };

    static string ObserverGets(Func<Command> make)
    {
        var c = make();
        var g = new GameState();
        var obs = new PeerInfo { PeerId = ObserverId, Name = "Watcher", Role = Role.Observer };
        var umpireSees = Visibility.ForPeer(c, new PeerInfo { PeerId = 3, Name = "Co", Role = Role.Umpire }, g);
        if (!ReferenceEquals(umpireSees, c)) return "umpire-does-not-see-it-whole";
        var got = Visibility.ForPeer(c, obs, g);
        if (got == null) return "drop";
        if (ReferenceEquals(got, c)) return "same";
        return "changed:" + Json.Serialize<Command>(got).Replace(" ", "");
    }

    static GameState ZoneTable()
    {
        var g = new GameState();
        g.Layer(Layers.Umpire).Add(new PieceInstance { Id = Inside, Def = "x", Pos = InPos });
        g.Layer(Layers.Umpire).Add(new PieceInstance { Id = Outside, Def = "x", Pos = OutPos });
        g.Sheets.Add(new SheetDef { Id = "s-000003", Name = "Map" });
        g.Zones.Add(new Zone { Id = "z-000004", Name = "SW", Owner = "Co", Polygon = { new(0, 0), new(10, 0), new(10, 10), new(0, 10) } });
        g.Boards.Add(new BoardDef { Id = "b-000005", Name = "Layer 1" });
        g.NextId = 6;
        return g;
    }

    static List<PropertyInfo> Targets(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite && p.Name != nameof(LayerCommand.Layer) && p.DeclaringType != typeof(Command))
        .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(List<string>) || p.PropertyType == typeof(Vec2)
                    || p.PropertyType == typeof(Vec2?) || p.PropertyType == typeof(PieceInstance))
        .OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

    static void Aim(Command c, PropertyInfo p, bool inside)
    {
        var id = inside ? Inside : Outside;
        var pos = inside ? InPos : OutPos;
        if (p.PropertyType == typeof(string)) p.SetValue(c, id);
        else if (p.PropertyType == typeof(List<string>)) p.SetValue(c, new List<string> { id });
        else if (p.PropertyType == typeof(Vec2) || p.PropertyType == typeof(Vec2?)) p.SetValue(c, pos);
        else if (p.PropertyType == typeof(PieceInstance)) p.SetValue(c, new PieceInstance { Id = id, Def = "x", Pos = pos });
    }

    static Command Aimed(Func<Command> make, PropertyInfo? outside)
    {
        var c = make();
        if (c is Batch b) { foreach (var i in b.Items) AimAll(i, outside); return c; }
        AimAll(c, outside);
        return c;
    }

    static void AimAll(Command c, PropertyInfo? outside)
    {
        foreach (var p in Targets(c.GetType())) Aim(c, p, inside: p != outside);
    }

    static string ZoneChecks(Func<Command> make)
    {
        var srv = new GameServer(ZoneTable());
        var umpire = new PeerInfo { PeerId = 3, Name = "Co", Role = Role.Umpire };
        var lead = new PeerInfo { PeerId = 2, Name = "Lead", Role = Role.LeadUmpire };
        var sample = Aimed(make, null);
        if (srv.ZoneDenies(lead, sample) != null) return "lead-denied";
        if (srv.ZoneDenies(umpire, sample) is { } baseErr) return "base-denied:" + baseErr;
        var props = sample is Batch b ? b.Items.SelectMany(i => Targets(i.GetType())).ToList() : Targets(sample.GetType());
        var checks = props.Where(p => srv.ZoneDenies(umpire, Aimed(make, p)) != null).Select(p => p.Name).Distinct().ToList();
        return checks.Count == 0 ? "-" : string.Join(",", checks);
    }

    static string ApplyAnswer(Func<Command> make)
    {
        var g = ZoneTable();
        var c = Aimed(make, null);
        if (c is SpawnPiece sp) sp.Id = "p-000099";
        if (c is RestorePiece rp) rp.Piece.Id = "p-000099";
        try
        {
            var err = StateMachine.Apply(g, c, out var inverse);
            if (err != null) return "refused:" + err.Replace(' ', '_');
            return "ok inverse:" + InverseName(inverse);
        }
        catch (Exception e) { return "throws:" + e.GetType().Name; }
    }

    static string InverseName(Command? c) => c switch
    {
        null => "none",
        Batch b => "Batch(" + string.Join("+", b.Items.Select(InverseName)) + ")",
        _ => c.GetType().Name,
    };

    static string Touched(Func<Command> make)
    {
        var c = make();
        void Mark(Command x, string prefix)
        {
            foreach (var p in Targets(x.GetType()))
            {
                if (p.PropertyType == typeof(string)) p.SetValue(x, prefix + p.Name);
                else if (p.PropertyType == typeof(List<string>)) p.SetValue(x, new List<string> { prefix + p.Name });
                else if (p.PropertyType == typeof(PieceInstance)) p.SetValue(x, new PieceInstance { Id = prefix + p.Name + ".Id" });
            }
        }
        if (c is Batch b) for (int i = 0; i < b.Items.Count; i++) Mark(b.Items[i], $"[{i}].");
        else Mark(c, "");
        var ids = c.ScriptsToReload().ToList();
        return ids.Count == 0 ? "-" : string.Join(",", ids);
    }
}

static class TestSource
{
    public static string Dir(string csproj)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, csproj))) return d.FullName;
        throw new DirectoryNotFoundException($"{csproj} not found above {AppContext.BaseDirectory}");
    }
}
