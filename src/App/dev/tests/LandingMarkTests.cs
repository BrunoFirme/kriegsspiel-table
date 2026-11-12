using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class LandingMarkTests
{
    const string U = Layers.Umpire;
    const float Clear = 0.02f, Dark = 0.4f;

    static void Shapes(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "shapes", "assets");
        Directory.CreateDirectory(dir);
        var corners = new (double x, double z)[] { (0, 0), (2, 0), (2, 0.5), (0.5, 0.5), (0.5, 2), (0, 2) };
        var lines = corners.Select(c => FormattableString.Invariant($"v {c.x} 0 {c.z}")).Concat(corners.Select(c => FormattableString.Invariant($"v {c.x} 0.2 {c.z}"))).ToList();
        for (int i = 2; i < corners.Length; i++)
        {
            lines.Add($"f 1 {i + 1} {i}");
            lines.Add($"f 7 {i + 6} {i + 7}");
        }
        File.WriteAllLines(Path.Combine(dir, "ell.obj"), lines);
        var pack = new PackManifest { Id = "shapes", Name = "Shapes" };
        pack.Assets["ell"] = new AssetEntry { File = "assets/ell.obj", Kind = "mesh" };
        store.RegisterDef(pack, new PieceDef { Id = "ell", Name = "Ell", Kind = "model", Mesh = "ell", Size = new Size2(2, 2), Thickness = 0.2, Shape = "box" });
        store.RegisterDef(pack, new PieceDef { Id = "disc", Name = "Disc", Size = new Size2(1, 1), Shape = "circle" });
    }

    static float Alpha((Image image, Rect2 area) shadow, Vector2 local)
    {
        var (img, area) = shadow;
        var at = (local - area.Position) / area.Size * new Vector2(img.GetWidth(), img.GetHeight());
        return img.GetPixel(Mathf.Clamp((int)at.X, 0, img.GetWidth() - 1), Mathf.Clamp((int)at.Y, 0, img.GetHeight() - 1)).A;
    }

    static float Texel((Image image, Rect2 area) shadow) => shadow.area.Size.X / shadow.image.GetWidth();

    static async Task<OfflineTable> Table(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, prepare: Shapes, arrange: g => g.Layers[U].AddRange(new[]
        {
            new PieceInstance { Id = "ell", Def = "ell", Pos = new Vec2(-4, 0) },
            new PieceInstance { Id = "down", Def = "ell", Pos = new Vec2(-4, 4), FaceDown = true },
            new PieceInstance { Id = "disc", Def = "disc", Pos = new Vec2(4, 0) },
        }));
        for (int i = 0; i < 120 && (t.Node("ell").Model?.Mesh == null || t.Node("down").Model?.Mesh == null); i++) await r.Frame();
        await r.Frames(2);
        return t;
    }

    [NodeTest]
    static async Task A_block_s_mark_is_its_footprint_filled_evenly_with_a_crisp_darker_edge(TestRunner r)
    {
        var t = await Table(r);
        var n = t.Node("p1");
        var shadow = TopShadow.Painted(n);
        float half = n.Footprint.X / 2, texel = Texel(shadow);
        Check.That(shadow.area.HasPoint(-n.Footprint / 2) && shadow.area.HasPoint(n.Footprint / 2 - Vector2.One * 0.001f), $"the mark covers the footprint {n.Footprint}: {shadow.area}");
        float middle = Alpha(shadow, Vector2.Zero);
        Check.That(middle > Dark * 0.9f && middle < 0.7f, $"the middle is an even shade: {middle}");
        float rim = Alpha(shadow, new Vector2(half - texel * 0.5f, 0));
        Check.That(rim > middle * 1.3f, $"the edge is drawn darker than the fill: {rim} against {middle}");
        Check.That(Alpha(shadow, new Vector2(half - texel * 3, 0)) > Dark, "three texels inside the edge is full shade");
        Check.That(Alpha(shadow, new Vector2(half + texel * 1.5f, 0)) < Clear, "a texel and a half outside it is clear");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_round_piece_throws_a_round_mark(TestRunner r)
    {
        var t = await Table(r);
        var shadow = TopShadow.Painted(t.Node("disc"));
        Check.That(Alpha(shadow, Vector2.Zero) > Dark, "the middle of the disc is dark");
        Check.That(Alpha(shadow, new Vector2(0.45f, 0.45f)) < Clear, "the corner of its square footprint is clear");
        Check.That(Alpha(shadow, new Vector2(0.45f, 0)) > Dark, "inside its rim is dark");
    }

    [NodeTest]
    static async Task A_model_throws_its_own_shape_seen_from_straight_above_mirrored_when_face_down(TestRunner r)
    {
        var t = await Table(r);
        foreach (var id in new[] { "ell", "down" })
        {
            var piece = t.Node(id);
            var shadow = TopShadow.Painted(piece);
            Vector2 Below(Vector3 inMesh)
            {
                var flat = new Basis(Vector3.Up, piece.Heading).Inverse() * (piece.Model!.GlobalTransform * inMesh - piece.GlobalPosition) / piece.Scale.X;
                return new Vector2(flat.X, flat.Z);
            }
            Check.That(Alpha(shadow, Below(new Vector3(1.75f, 0.1f, 0.25f))) > Dark && Alpha(shadow, Below(new Vector3(0.25f, 0.1f, 1.75f))) > Dark,
                $"{id}: the ground straight below both drawn arms of the L is dark");
            Check.That(Alpha(shadow, Below(new Vector3(1.5f, 0.1f, 1.5f))) < Clear, $"{id}: below the corner between them it is clear");
        }
        var up = TopShadow.Painted(t.Node("ell"));
        var down = TopShadow.Painted(t.Node("down"));
        Check.That(Alpha(up, new Vector2(-1.5f, -0.25f)) > Dark && Alpha(down, new Vector2(-1.5f, -0.25f)) < Clear, "turned face down, the L lies the other way round");

        t.Table.Selection.Set(new[] { "ell" });
        t.Table.Hand.Start(new Vec2(-4, 0));
        t.Table.Hand.Move(new Vec2(-4, 2));
        await r.Frames(3);
        var n = t.Node("ell");
        var mark = n.GetChildren().OfType<LandingMark>().SingleOrDefault() ?? throw new TestFailure("the held model has no mark");
        Check.That(mark.TextureAlbedo != null && mark.Area.IsEqualApprox(up.area), $"the mark under the held model is its shadow from above: {mark.Area} against {up.area}");
        Check.That(Mathf.IsEqualApprox(mark.Size.X, up.area.Size.X * n.Scale.X) && Mathf.IsEqualApprox(mark.Size.Z, up.area.Size.Y * n.Scale.X), $"at its size: {mark.Size}");
        t.Table.Hand.Drop();
        await r.Frames(5);
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
