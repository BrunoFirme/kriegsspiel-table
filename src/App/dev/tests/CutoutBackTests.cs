using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CutoutBackTests
{
    static PieceDef Cut(string id, string? back) => new()
    {
        Id = id, Name = id, Shape = "alpha", Size = new Size2(2, 1), Image = "front", Back = back,
        Outline = new() { new() { new Vec2(0, 0), new Vec2(1, 0), new Vec2(0.5, 1) } },
    };

    static MeshInstance3D Underside(PieceNode n) =>
        n.GetChildren().OfType<MeshInstance3D>().SingleOrDefault(m => m.MaterialOverride is StandardMaterial3D { CullMode: BaseMaterial3D.CullModeEnum.Back })
        ?? throw new TestFailure($"{n.Piece.Def}: one underside drawn from below only");

    [NodeTest]
    static async Task A_cut_out_piece_s_underside_faces_down_only_and_lies_under_its_face_as_a_flat_piece_s_does(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: store =>
        {
            var pack = new PackManifest { Id = "test.cut", Name = "Cut" };
            store.RegisterDef(pack, Cut("own-back", "back"));
            store.RegisterDef(pack, Cut("same-print", null));
        }, arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
        {
            new() { Id = "a", Def = "own-back", Pos = new Vec2(0, 0) },
            new() { Id = "b", Def = "same-print", Pos = new Vec2(4, 0) },
        });
        await r.Frames(3);
        var own = Underside(t.Node("a"));
        Check.That(own.Basis.Y.Y < -0.99f && own.Basis.X.X < -0.99f && own.Basis.Z.Z > 0.99f,
            $"its own back is turned over about the forward axis, mirrored across as a flat piece's underside is: {own.Basis}");
        var same = Underside(t.Node("b"));
        Check.That(same.Mesh is PlaneMesh { FlipFaces: true } && same.Basis.IsEqualApprox(Basis.Identity), "the face's own print faces down under itself");
        t.Table.QueueFree();
    }
}
