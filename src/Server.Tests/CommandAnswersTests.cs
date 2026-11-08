using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class CommandAnswersTests
{
    const string U = Layers.Umpire;
    static readonly Vec2 Inside = new(5, 5), Outside = new(50, 50);

    static readonly HashSet<Type> NotOnUndoStack = new()
    {
        typeof(RollDice), typeof(AddPack), typeof(RestoreTurn), typeof(LoadSave), typeof(ClickWidget), typeof(EditWidget), typeof(PickMenuItem),
        typeof(HoldPiece), typeof(Say), typeof(Hotkey), typeof(PickGlobalMenuItem), typeof(NumberTyped), typeof(SelectPiece), typeof(ShowContents), typeof(SetHighlight),
        typeof(SetBookHighlight), typeof(PlayEffect), typeof(GlidePiece), typeof(ShowCast), typeof(ReportSelection), typeof(SetWebModel),
    };

    public static IEnumerable<Type> Registered() =>
        typeof(Command).GetCustomAttributes<JsonDerivedTypeAttribute>().Select(a => a.DerivedType);

    internal static List<(Type type, string name)> Named() =>
        typeof(Command).GetCustomAttributes<JsonDerivedTypeAttribute>()
            .Select(a => (a.DerivedType, (string)a.TypeDiscriminator!))
            .OrderBy(x => x.Item2, StringComparer.Ordinal).ToList();

    static readonly PeerInfo Obs = new() { PeerId = 4, Name = "Watcher", Role = Role.Observer };
    static readonly PeerInfo Stranger = new() { PeerId = 5, Name = "Who", Role = (Role)99 };
    static readonly PeerInfo[] Everyone =
    {
        Obs,
        new() { PeerId = 3, Name = "Co", Role = Role.Umpire },
        new() { PeerId = 2, Name = "Lead", Role = Role.LeadUmpire },
        new() { PeerId = 1, Name = "Head", Role = Role.HostUmpire },
        Stranger,
    };

    static Command Filled(Type t)
    {
        var c = (Command)Activator.CreateInstance(t)!;
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.DeclaringType != typeof(Command)))
        {
            if (p.Name == nameof(LayerCommand.Layer)) p.SetValue(c, U);
            else if (p.PropertyType == typeof(string)) p.SetValue(c, p.Name + "-x");
            else if (p.PropertyType == typeof(Vec2) || p.PropertyType == typeof(Vec2?)) p.SetValue(c, Inside);
            else if (p.PropertyType == typeof(List<string>)) p.SetValue(c, new List<string> { p.Name + "-1", p.Name + "-2" });
            else if (p.PropertyType == typeof(PieceInstance)) p.SetValue(c, new PieceInstance { Id = "Piece-x", Def = "inf", Pos = Inside });
        }
        return c;
    }

    static List<string> Names(Command c)
    {
        var ids = new List<string>();
        foreach (var p in c.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.DeclaringType != typeof(Command) && p.Name != nameof(LayerCommand.Layer)))
            switch (p.GetValue(c))
            {
                case string s:
                    ids.Add(s);
                    break;
                case List<string> l:
                    ids.AddRange(l);
                    break;
                case PieceInstance pi:
                    ids.Add(pi.Id);
                    break;
                case Batch: break;
            }
        if (c is Batch b) foreach (var i in b.Items) ids.AddRange(Names(i));
        return ids.Distinct().ToList();
    }

    static List<(string name, Action<Vec2> set)> Points(Command c)
    {
        var res = new List<(string, Action<Vec2>)>();
        foreach (var p in c.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.DeclaringType != typeof(Command)))
        {
            if (p.PropertyType == typeof(Vec2) || p.PropertyType == typeof(Vec2?)) res.Add((p.Name, v => p.SetValue(c, v)));
            else if (p.PropertyType == typeof(PieceInstance) && p.GetValue(c) is PieceInstance pi) res.Add((p.Name + ".Pos", v => pi.Pos = v));
        }
        if (c is Batch b) foreach (var (i, n) in b.Items.Select((i, n) => (i, n))) res.AddRange(Points(i).Select(x => ($"[{n}].{x.Item1}", x.Item2)));
        return res;
    }

    static string Zone(Command c, string owner, Func<Command> fresh)
    {
        string Run(Command cmd, string? outsideId)
        {
            var state = new GameState();
            foreach (var id in Names(cmd)) state.Layer(U).Add(new PieceInstance { Id = id, Def = "inf", Pos = id == outsideId ? Outside : Inside });
            state.Zones.Add(new Zone { Id = "z-1", Name = "SW", Owner = owner, Polygon = { new(0, 0), new(10, 0), new(10, 10), new(0, 10) } });
            return GameServer.ZoneDenies(state, new PeerInfo { PeerId = 3, Name = "Co", Role = Role.Umpire }, cmd) ?? "ok";
        }
        var sb = new StringBuilder();
        sb.Append("all inside=").Append(Run(c, null));
        foreach (var id in Names(c)) sb.Append("; ").Append(id).Append(" outside=").Append(Run(c, id));
        foreach (var (name, _) in Points(c))
        {
            var moved = fresh();
            Points(moved).First(p => p.name == name).set(Outside);
            sb.Append("; ").Append(name).Append(" outside=").Append(Run(moved, null));
        }
        if (c is LayerCommand)
        {
            var other = fresh();
            ((LayerCommand)other).Layer = "players";
            foreach (var (_, set) in Points(other)) set(Outside);
            sb.Append("; on another layer, all outside=").Append(Run(other, Names(other).FirstOrDefault()));
        }
        return sb.ToString();
    }

    static string Seen(Command c, PeerInfo peer)
    {
        var state = new GameState();
        var got = Visibility.ForPeer(c, peer, state);
        return got == null ? "nothing" : ReferenceEquals(got, c) ? "it" : "copy " + Json.Serialize<Command>(got);
    }

    static string Applied(Command c)
    {
        if (c is SpawnPiece sp) sp.Id = "p-new";
        if (c is RestorePiece rp) rp.Piece.Id = "p-new";
        var state = new GameState();
        foreach (var id in Names(c).Where(id => id != "p-new")) state.Layer(U).Add(new PieceInstance { Id = id, Def = "inf", Pos = Inside });
        try
        {
            var err = StateMachine.Apply(state, c, out var inverse);
            return err != null ? "refused: " + err : "ok, inverse " + InverseName(inverse);
        }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    static string InverseName(Command? c) => c switch
    {
        null => "none",
        Batch b => "Batch(" + string.Join("+", b.Items.Select(InverseName)) + ")",
        _ => c.GetType().Name,
    };

    static string Allowed(Command c) => string.Join(" ", Everyone.Select(p => $"{p.Role}={(GameServer.Allowed(p, c) ? "yes" : "no")}"));

    static void Describe(StringBuilder sb, string title, Func<Command> make)
    {
        var c = make();
        sb.Append("== ").AppendLine(title);
        sb.Append("  sent by:     ").AppendLine(Allowed(c));
        sb.Append("  in a batch:  ").AppendLine(Allowed(new Batch { Items = { make() } }));
        sb.Append("  zone:        ").AppendLine(Zone(c, "co", make));
        sb.Append("  zone, none of mine: ").AppendLine(Zone(c, "someone else", make));
        sb.Append("  observer:    ").AppendLine(Seen(c, Obs));
        sb.Append("  stranger:    ").AppendLine(Seen(c, Stranger));
        sb.Append("  umpires:     ").AppendLine(string.Join(" ", Everyone.Where(p => Visibility.IsUmpire(p.Role)).Select(p => Seen(c, p))));
        sb.Append("  reloads:     ").AppendLine(string.Join(",", c.ScriptsToReload()));
        sb.Append("  undo stack:  ").AppendLine(c.OnUndoStack() ? "yes" : "no");
        sb.Append("  applied:     ").AppendLine(Applied(make()));
    }

    static IEnumerable<(string, Func<Command>)> Variants()
    {
        yield return ("Say to everyone", () => new Say { Text = "hi" });
        yield return ("Say to the observer", () => new Say { Text = "hi", To = "p:4" });
        yield return ("Say to another peer", () => new Say { Text = "hi", To = "p:3" });
        yield return ("Say to the umpires", () => new Say { Text = "hi", To = "u:" });
        yield return ("SelectPiece for the observer", () => new SelectPiece { Layer = U, Id = "p-1", To = "p:4" });
        yield return ("SelectPiece for no one", () => new SelectPiece { Layer = U, Id = "p-1", To = null! });
        yield return ("ShowContents for the observer", () => new ShowContents { Layer = U, Id = "p-1", To = "p:4" });
        yield return ("ShowDialog to another peer", () => new ShowDialog { To = "p:3", Title = "t" });
        yield return ("RollDice on the board", () => new RollDice { Layer = U, PieceIds = { "p-1", "p-2" }, Results = { 3, 4 } });
        yield return ("RollDice on a layer without pieces", () => new RollDice { Layer = U });
        yield return ("RollDice with pieces and no layer", () => new RollDice { PieceIds = { "p-1" } });
        yield return ("LoadSave with its table", () => new LoadSave
        {
            Seq = 8, Actor = 1, At = 99, Name = "Turn 2",
            State = new GameState
            {
                Name = "t",
                Zones = { new Zone { Id = "z-1", Owner = "co" } },
                Dice = { new DiceRoll { Seq = 3, Sides = 6, Results = { 2 } } },
                Layers = { [U] = new List<PieceInstance> { new() { Id = "p-1", Def = "inf", Pos = Inside } } },
            },
        });
        yield return ("RestoreTurn with its table", () => new RestoreTurn
        {
            Seq = 7, Actor = 1, At = 99, Index = 2,
            State = new GameState
            {
                Name = "t",
                Zones = { new Zone { Id = "z-1", Owner = "co" } },
                Dice = { new DiceRoll { Seq = 3, Sides = 6, Results = { 2 } } },
                Layers = { [U] = new List<PieceInstance> { new() { Id = "p-1", Def = "inf", Pos = Inside } } },
            },
        });
        yield return ("Batch of mixed changes", () => new Batch
        {
            Seq = 5, Actor = 3, At = 11,
            Items =
            {
                new MovePiece { Layer = U, Id = "m-1", Pos = Inside }, Filled(typeof(SetZone)), new Say { Text = "x", To = "p:3" },
                new SetState { Layer = U, Id = "s-1", State = 2 }, new RestorePiece { Layer = U, Piece = new PieceInstance { Id = "r-1", Def = "inf", Pos = Inside } },
                new Attach { Layer = U, Parent = "a-1", Child = "a-2" }, new SetCounter { Layer = "players", Id = "far", Name = "n" },
                new SetScript { Layer = U, Id = "sc-1", Text = "--" }, new Batch { Items = { new DeletePiece { Layer = U, Id = "d-1" }, new RollDice() } },
            },
        });
        yield return ("Batch holding a sweeping change", () => new Batch { Items = { Filled(typeof(MovePiece)), new Undo { TargetSeq = 1 } } });
        yield return ("Batch holding a runtime change", () => new Batch { Items = { Filled(typeof(MovePiece)), Filled(typeof(SetWidgets)) } });
        yield return ("Batch holding a lead's change", () => new Batch { Items = { Filled(typeof(MovePiece)), Filled(typeof(SetGlobalScript)) } });
        yield return ("Batch of nothing seen", () => new Batch { Items = { Filled(typeof(RemoveZone)), new RollDice() } });
    }

    static string Answers()
    {
        var sb = new StringBuilder();
        foreach (var t in Registered()) Describe(sb, t.Name, () => Filled(t));
        foreach (var t in Registered()) Describe(sb, t.Name + " (empty)", () => (Command)Activator.CreateInstance(t)!);
        foreach (var (title, make) in Variants()) Describe(sb, title, make);
        return sb.ToString().Replace("\r\n", "\n");
    }

    static string SnapshotPath() => Path.Combine(AppContext.BaseDirectory, "Snapshots", "command-answers-detailed.txt");

    [Fact]
    public void Every_command_answers_as_the_snapshot_records()
    {
        var path = SnapshotPath();
        var now = Answers();
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, now);
            Assert.Fail("no snapshot: today's answers are written to " + path + "; copy it to src/Server.Tests/Snapshots after checking it");
        }
        var was = File.ReadAllText(path).Replace("\r\n", "\n");
        if (was != now) File.WriteAllText(path + ".actual", now);
        Assert.Equal(was, now);
    }

    [Fact]
    public void Every_command_is_registered_and_answers_every_question()
    {
        var concrete = typeof(Command).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Command)) && !t.IsAbstract).OrderBy(t => t.Name);
        Assert.Equal(concrete.Select(t => t.Name), Registered().OrderBy(t => t.Name).Select(t => t.Name));

        var snapshot = File.Exists(SnapshotPath()) ? File.ReadAllText(SnapshotPath()).Replace("\r\n", "\n") : "";
        foreach (var t in Registered())
        {
            Assert.True(snapshot.Contains($"== {t.Name}\n"), $"{t.Name} is not in the snapshot: review its answers and record them");
            foreach (var c in new[] { (Command)Activator.CreateInstance(t)!, Filled(t) })
            {
                _ = c.SentBy();
                _ = c.ShownToObservers();
                Assert.Equal(!NotOnUndoStack.Contains(t), c.OnUndoStack());
                _ = c.ZonePieces().ToList();
                _ = c.ZonePoints().ToList();
                _ = c.ScriptsToReload().ToList();
            }
        }
    }

    [Fact]
    public void Only_the_commands_visibility_reads_claim_its_special_rules()
    {
        var rules = new Dictionary<Shown, Type[]>
        {
            [Shown.Parts] = new[] { typeof(Batch) },
            [Shown.Addressee] = new[] { typeof(Say), typeof(SelectPiece), typeof(ShowContents) },
            [Shown.DiceOnBoard] = new[] { typeof(RollDice) },
            [Shown.WholeTable] = new[] { typeof(RestoreTurn), typeof(LoadSave) },
        };
        foreach (var t in Registered())
        {
            var shown = ((Command)Activator.CreateInstance(t)!).ShownToObservers();
            if (rules.TryGetValue(shown, out var readers)) Assert.Contains(t, readers);
        }
    }
}
