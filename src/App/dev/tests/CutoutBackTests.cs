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
        n.GetChildren().OfType<MeshInstance3D>().SingleOrDefault(m => m.VisibilityRangeBegin == 0 && m.MaterialOverride is StandardMaterial3D { CullMode: BaseMaterial3D.CullModeEnum.Back })
        ?? throw new TestFailure($"{n.Piece.Def}: one underside drawn from below only");

    static List<MeshInstance3D> FarOff(PieceNode n) => n.GetChildren().OfType<MeshInstance3D>().Where(m => m.VisibilityRangeBegin > 0).ToList();

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

    [NodeTest]
    static async Task Seen_from_far_off_a_piece_lying_face_down_shows_its_back_not_its_face_through_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: store =>
        {
            var pack = new PackManifest { Id = "test.far", Name = "Far" };
            store.RegisterDef(pack, Cut("cut-card", "back"));
            store.RegisterDef(pack, new PieceDef { Id = "round-card", Name = "round-card", Shape = "circle", Size = new Size2(1, 1), Image = "front", Back = "back" });
        }, arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
        {
            new() { Id = "a", Def = "cut-card", Pos = new Vec2(0, 0), FaceDown = true },
            new() { Id = "b", Def = "round-card", Pos = new Vec2(4, 0), FaceDown = true },
        });
        await r.Frames(3);
        foreach (var id in new[] { "a", "b" })
        {
            var node = t.Node(id);
            var far = FarOff(node);
            Check.Equal(2, far.Count, $"{id}: far off it is drawn as its face and its back");
            Check.That(far.All(m => m.MaterialOverride is StandardMaterial3D { CullMode: BaseMaterial3D.CullModeEnum.Back }), $"{id}: each shows from its own side only");
            var face = far.Single(m => m.Position.Y > 0);
            var back = far.Single(m => m.Position.Y == 0);
            Check.That(face.Basis.IsEqualApprox(Basis.Identity) && face.Mesh is PlaneMesh { FlipFaces: false }, $"{id}: the face looks out of the piece's top");
            Check.That(back.Basis.Y.Y < -0.99f, $"{id}: its own back looks out of the piece's bottom: {back.Basis}");
            var up = node.GlobalTransform.Basis * back.Basis * Vector3.Up;
            Check.That(up.Y > 0.99f, $"{id}: lying face down, the back is the side that looks up: {up}");
        }
        t.Table.QueueFree();
    }
}
