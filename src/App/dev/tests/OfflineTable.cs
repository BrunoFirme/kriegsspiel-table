using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client.Prefs;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.App;

sealed class OfflineTable
{
    public required Table Table { get; init; }
    public required Session S { get; init; }
    public required GameServer Server { get; init; }
    public readonly List<string> Errors = new();

    public static OfflineTable Make(TestRunner r, int pieces, string[]? packs = null, System.Action<PackageStore>? prepare = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new PackageStore(root);
        prepare?.Invoke(store);
        store.RegisterDef(new PackManifest { Id = "test", Name = "Test" }, new PieceDef { Id = "x", Name = "Block" });
        var g = new GameState { Name = "Node test", Packs = packs?.ToList() ?? new() };
        g.Layers[Layers.Umpire] = Enumerable.Range(1, pieces).Select(i => new PieceInstance { Id = "p" + i, Def = "x", Pos = new Vec2(i, 0) }).ToList();
        var s = new Session(new Assets(store));
        var server = new GameServer(g, store);
        var t = new OfflineTable { Table = new Table { Name = "Table" }, S = s, Server = server };
        void Deliver(List<Outgoing> outs)
        {
            foreach (var o in outs)
                switch (o.Message)
                {
                    case CommandMessage cm:
                        s.OnCommand(cm.Command);
                        break;
                    case JoinResult jr:
                        s.OnJoined(jr);
                        break;
                    case ErrorMessage em:
                        t.Errors.Add(em.Text);
                        break;
                }
        }
        s.Send = c => Deliver(server.Handle(1, c));
        s.Request = m => Deliver(server.Request(1, m));
        s.SetName("Tester");
        Deliver(server.Join(1, new JoinRequest { Name = "Tester" }));
        r.AddChild(t.Table);
        t.Table.Bind(s, new Settings());
        return t;
    }

    public PieceNode Node(string id) => Table.Pieces.NodeOf(Layers.Umpire, id) ?? throw new TestFailure($"no node for {id}");
}
