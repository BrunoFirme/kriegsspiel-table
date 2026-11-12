using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class FaceDownPickTests
{
    const int Wide = 64, High = 32;
    const float Quarter = 0.5f;

    static void HalfCut(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "test.half", "assets");
        Directory.CreateDirectory(dir);
        var img = Image.CreateEmpty(Wide, High, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);
        img.FillRect(new Rect2I(0, 0, Wide / 2, High), Colors.Red);
        img.SavePng(Path.Combine(dir, "half.png"));
        var pack = new PackManifest { Id = "test.half", Name = "Half" };
        pack.Assets["half"] = new AssetEntry { File = "assets/half.png", Kind = "image" };
        store.RegisterDef(pack, new PieceDef { Id = "half", Name = "Half", Shape = "alpha", Size = new Size2(2, 1), Image = "half" });
    }

    static MeshInstance3D? Walls(PieceNode n) => n.GetChildren().OfType<MeshInstance3D>().SingleOrDefault(m => m.Mesh is ArrayMesh);

    [NodeTest]
    static async Task A_cut_out_lying_face_down_is_picked_on_the_side_it_is_drawn_and_its_landing_mark_lies_there(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: HalfCut, arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
        {
            new() { Id = "up", Def = "half", Pos = new Vec2(0, 0) },
            new() { Id = "down", Def = "half", Pos = new Vec2(0, 4), FaceDown = true },
        });
        for (int i = 0; i < 300 && (Walls(t.Node("up")) == null || Walls(t.Node("down")) == null); i++) await r.Frame();
        foreach (var id in new[] { "up", "down" })
        {
            var n = t.Node(id);
            var walls = Walls(n) ?? throw new TestFailure($"{id}: its cut-out walls were never drawn");
            float side = Mathf.Sign((walls.GlobalTransform * walls.Mesh.GetAabb()).GetCenter().X - n.GlobalPosition.X);
            Check.That(side != 0, $"{id}: the drawn half lies to one side");
            var solid = n.GlobalPosition + new Vector3(side * Quarter, 0, 0);
            var empty = n.GlobalPosition - new Vector3(side * Quarter, 0, 0);
            Check.That(n.Contains(Coords.FromWorld(solid)), $"{id}: a click on the drawn half picks it");
            Check.That(!n.Contains(Coords.FromWorld(empty)), $"{id}: a click on the cut-away half misses it");
            var mark = TopShadow.Painted(n);
            Vector2 Texel(Vector3 world)
            {
                var flat = new Basis(Vector3.Up, n.Heading).Inverse() * (world - n.GlobalPosition) / n.Scale.X;
                return (new Vector2(flat.X, flat.Z) - mark.area.Position) / mark.area.Size * new Vector2(mark.image.GetWidth(), mark.image.GetHeight());
            }
            var (at, off) = (Texel(solid), Texel(empty));
            Check.That(mark.image.GetPixel((int)at.X, (int)at.Y).A > 0.4f && mark.image.GetPixel((int)off.X, (int)off.Y).A < 0.02f, $"{id}: its landing mark lies under the drawn half");
        }
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
