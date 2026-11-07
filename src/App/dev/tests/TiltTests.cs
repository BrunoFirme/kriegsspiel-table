using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class TiltTests
{
    const string U = Layers.Umpire;
    const float Across = 1.4142f, Rise = 0.7071f;

    static void Prism(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "tilt", "assets");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "prism.obj"), new[]
        {
            "v -0.7071 0 -0.5", "v 0.7071 0 -0.5", "v 0 0.7071 -0.5", "v -0.7071 0 0.5", "v 0.7071 0 0.5", "v 0 0.7071 0.5",
            "f 1 2 5 4", "f 1 3 2", "f 4 5 6", "f 1 4 6 3", "f 2 3 6 5",
        });
        var pack = new PackManifest { Id = "tilt", Name = "Tilt" };
        pack.Assets["prism"] = new AssetEntry { File = "assets/prism.obj", Kind = "mesh" };
        store.RegisterDef(pack, new PieceDef { Id = "tri", Name = "Triangle block", Kind = "model", Mesh = "prism", Size = new Size2(Across, 1), Thickness = Rise, Shape = "box" });
    }

    static void OnItsSide(GameState g) => g.Layers[U].Add(new PieceInstance { Id = "t1", Def = "tri", Pos = new Vec2(-4, 0), TiltZ = 90 });

    [NodeTest]
    static Task A_turned_box_reaches_its_turned_corners_up_and_across(TestRunner r)
    {
        var side = Basis.FromEuler(new Vector3(0, 0, Mathf.Pi / 2));
        var size = new Vector3(Across, Rise, 1);

        Check.That(Mathf.IsEqualApprox(PieceNode.HalfRise(side, size), Across / 2), $"standing on its side it rises half its width: {PieceNode.HalfRise(side, size)}");
        Check.That(PieceNode.HalfShadow(side, size).IsEqualApprox(new Vector2(Rise / 2, 0.5f)), $"and covers half its height across: {PieceNode.HalfShadow(side, size)}");
        return Task.CompletedTask;
    }

    [ScreenTest]
    static async Task A_triangle_block_on_its_side_stands_tilted_is_picked_where_it_stands_and_carries_what_lies_on_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, prepare: Prism, arrange: OnItsSide);
        await r.Frames(20);
        var n = t.Node("t1");

        var up = n.GlobalBasis * Vector3.Up;
        Check.That(Mathf.Abs(up.Y) < 0.05f, $"its own up lies along the table: {up}");
        float top = n.TopY, floor = n.BaseY(n.Piece);
        Check.That(Mathf.IsEqualApprox(top - floor, Across, 0.02f), $"it stands as high as it is wide: {top - floor}");
        Check.That(n.Contains(new Vec2(-4 + Rise / 2 - 0.05, 0.4)) && !n.Contains(new Vec2(-4 + Across / 2 - 0.05, 0)), "it is picked where its turned body stands, not where it would lie");

        t.S.Do(new KTable.Core.Protocol.MovePiece { Layer = U, Id = "p1", Pos = new Vec2(-4, 0), Stack = 1, Y = top - 0.03f });
        await r.Frames(5);
        Check.That(t.Node("p1").BaseY(t.Node("p1").Piece) >= top - 0.05f, $"a piece laid on it rests on its turned top: {t.Node("p1").BaseY(t.Node("p1").Piece)} against {top}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
