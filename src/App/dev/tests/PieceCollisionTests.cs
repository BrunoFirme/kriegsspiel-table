using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class PieceCollisionTests
{
    const int Side = 64;
    const float Thick = 0.5f;

    static void DiamondToken(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "test.diamond", "assets");
        Directory.CreateDirectory(dir);
        var img = Image.CreateEmpty(Side, Side, false, Image.Format.Rgba8);
        img.Fill(Colors.Transparent);
        for (int x = 0; x < Side; x++)
            for (int y = 0; y < Side; y++)
                if (Mathf.Abs(x + 0.5f - Side / 2f) + Mathf.Abs(y + 0.5f - Side / 2f) <= Side / 2f) img.SetPixel(x, y, Colors.Blue);
        img.SavePng(Path.Combine(dir, "diamond.png"));
        var pack = new PackManifest { Id = "test.diamond", Name = "Diamond" };
        pack.Assets["diamond"] = new AssetEntry { File = "assets/diamond.png", Kind = "image" };
        store.RegisterDef(pack, new PieceDef { Id = "diamond", Name = "Diamond token", Shape = "alpha", Size = new Size2(2, 2), Thickness = Thick, Image = "diamond" });
    }

    [NodeTest]
    static async Task A_cut_out_token_is_met_by_its_picture_from_the_side_never_by_the_box_around_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: DiamondToken, arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance> { new() { Id = "d", Def = "diamond", Pos = new Vec2(0, 0) } });
        var n = t.Node("d");
        for (int i = 0; i < 300 && n.Contains(new Vec2(0.9, 0.9)); i++) await r.Frame();
        Check.That(!n.Contains(new Vec2(0.9, 0.9)) && n.Contains(new Vec2(0, 0)), "the token's picture is read as its shape");
        float h = Thick / 2;
        Vector3 At(float x, float z) => n.ToGlobal(new Vector3(x, h, z));
        var corner = At(2.5f, -0.6f);
        Check.That(PieceLayer.RayMeets(n, corner, (At(-1.5f, 3.4f) - corner).Normalized()) == null, "a ray through the empty corner of its picture passes it by");
        var side = At(3, 3);
        var inward = (At(0, 0) - side).Normalized();
        var met = PieceLayer.RayMeets(n, side, inward) ?? throw new TestFailure("a ray from the side at the picture misses it");
        float edge = (At(0.5f, 0.5f) - side).Length(), box = (At(1, 1) - side).Length();
        Check.That(Mathf.Abs(met - edge) < 0.1f,$"it meets the picture's edge at {edge:0.00}, not the box at {box:0.00}: met at {met:0.00}");
        var above = n.ToGlobal(new Vector3(0, 3, 0));
        Check.That(PieceLayer.RayMeets(n, above, Vector3.Down) is { } top && top > 0 && top < 3,"a ray from above meets its top face");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_piece_turned_where_it_lies_on_another_stays_on_it_and_one_moved_onto_another_climbs_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance>
        {
            new() { Id = "base", Def = "x", Pos = new Vec2(0, 0) },
            new() { Id = "top", Def = "x", Pos = new Vec2(0, 0), Y = 0.25 },
            new() { Id = "loose", Def = "x", Pos = new Vec2(4, 0) },
        });
        await r.Frames(2);
        var top = t.Node("top");
        t.Table.Poses.Turn(new[] { top }, 30);
        t.Table.Poses.Flush();
        await r.Frames(1);
        Check.That(Mathf.IsEqualApprox((float)top.Piece.Y, 0.25f) && Mathf.IsEqualApprox((float)top.Piece.RotDeg, 30), $"turned where it lies it keeps its height: y {top.Piece.Y}, turned to {top.Piece.RotDeg}");
        var loose = t.Node("loose");
        t.Table.Poses.Set(loose, new Vec2(0.1, 0), loose.Piece.RotDeg, loose.Piece.Scale);
        t.Table.Poses.Flush();
        await r.Frames(1);
        Check.That(loose.Piece.Y > 0.15, $"moved onto another it climbs it: y {loose.Piece.Y}");
        t.Table.Poses.Set(loose, new Vec2(6, 0), loose.Piece.RotDeg, loose.Piece.Scale);
        t.Table.Poses.Flush();
        await r.Frames(1);
        Check.That(loose.Piece.Y == 0, $"moved off onto the bare table it comes down: y {loose.Piece.Y}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }
}
