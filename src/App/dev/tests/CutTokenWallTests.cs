using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CutTokenWallTests
{
    [NodeTest]
    static async Task A_cut_token_s_walls_carry_its_picture_cut_by_the_same_transparency_as_its_face_as_the_game_builds_one_mesh(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: store =>
        {
            var pack = new PackManifest { Id = "test.walls", Name = "Walls" };
            store.RegisterDef(pack, new PieceDef
            {
                Id = "cut", Name = "Cut", Shape = "alpha", Size = new Size2(2, 1), Image = "front", Thickness = 0.2,
                Outline = new() { new() { new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), new Vec2(0, 1) } },
            });
        }, arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance> { new() { Id = "a", Def = "cut", Pos = new Vec2(0, 0) } });
        await r.Frames(3);
        var n = t.Node("a");
        var meshes = n.GetChildren().OfType<MeshInstance3D>().ToList();
        var face = meshes.FirstOrDefault(m => m.Mesh is PlaneMesh && m.Position.Y > 0.1f) ?? throw new TestFailure("the face is drawn on top");
        var walls = meshes.FirstOrDefault(m => m.Mesh is ArrayMesh) ?? throw new TestFailure("the token has walls");
        Check.That(walls.MaterialOverride == face.MaterialOverride, "the walls wear the face's own picture material, cut where the picture is clear");
        var uv = (Vector2[])((ArrayMesh)walls.Mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.TexUV];
        Check.That(uv.Length > 0 && uv.All(u => u.X >= 0 && u.X <= 1 && u.Y >= 0 && u.Y <= 1), "each wall samples the picture where its edge lies");
        t.Table.QueueFree();
    }
}
