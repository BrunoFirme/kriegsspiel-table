using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class OffCentreModelTests
{
    const string U = Layers.Umpire;
    const float Near = 0.02f;
    static readonly Vec2 At = new(3, 2);
    const double Turned = 35;

    static void Rank(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "ranks", "assets");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "rank.obj"), new[]
        {
            "v -0.5 0 -1.06", "v 0.5 0 -1.06", "v 0.5 0 0.03", "v -0.5 0 0.03",
            "v -0.5 0.15 -1.06", "v 0.5 0.15 -1.06", "v 0.5 0.15 0.03", "v -0.5 0.15 0.03",
            "f 1 2 3 4", "f 5 8 7 6", "f 1 5 6 2", "f 2 6 7 3", "f 3 7 8 4", "f 4 8 5 1",
        });
        var pack = new PackManifest { Id = "ranks", Name = "Ranks" };
        pack.Assets["rank"] = new AssetEntry { File = "assets/rank.obj", Kind = "mesh" };
        store.RegisterDef(pack, new PieceDef { Id = "rank", Name = "Line", Kind = "model", Mesh = "rank", Size = new Size2(1.3, 1.417), Thickness = 0.2, Shape = "box" });
    }

    static Vec2 Flat(Vector3 world) => Coords.FromWorld(world);

    [NodeTest]
    static async Task A_model_drawn_off_its_pivot_is_framed_and_picked_where_it_is_drawn(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: Rank, arrange: g => g.Layers[U].Add(new PieceInstance { Id = "line", Def = "rank", Pos = At, RotDeg = Turned }));
        await r.Frames(20);
        var n = t.Node("line");
        var model = n.GetChildren().OfType<MeshInstance3D>().First(m => m.Mesh is ArrayMesh);
        var drawn = Flat((model.GlobalTransform * model.Mesh.GetAabb()).GetCenter());
        var middle = n.MiddleAt(At, Turned);
        Check.That((drawn - middle).Length < Near, $"the footprint's middle is the drawn model's middle: {middle} against {drawn}");
        Check.That((middle - At).Length > 0.6, $"the model is drawn well off its pivot: {(middle - At).Length:0.00}");

        var mirrored = At - (middle - At);
        Check.That(n.Contains(drawn), "a click on the drawn middle picks the piece");
        Check.That(!n.Contains(mirrored), "a click on the empty ground across the pivot misses it");

        var before = n.GetChildren().OfType<MeshInstance3D>().ToHashSet();
        n.SetSelected(true);
        var ring = n.GetChildren().OfType<MeshInstance3D>().Single(m => !before.Contains(m));
        Check.That((Flat(ring.GlobalPosition) - drawn).Length < Near, $"the selection frame goes round the drawn model: {Flat(ring.GlobalPosition)} against {drawn}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
