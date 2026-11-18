using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceHighlightTests
{
    const int Crowd = 300;

    [NodeTest]
    static async Task Picked_pieces_wear_a_ring_only_where_it_shows(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: Crowd);
        await r.Frames(2);
        if (t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        await r.Frames(2);
        t.Table.SetSelection(t.S.View.Layer(Layers.Umpire).Select(p => p.Id));
        var nodes = t.Table.Pieces.Nodes.ToList();
        Check.That(t.Table.RingedCount() == Crowd && nodes.All(n => n.Masked), $"every piece picked is in the aura's mask: {t.Table.RingedCount()} of {Crowd} picked");
        Check.That(nodes.All(n => n.SelectionMesh == null), "seen in 3D none carries a ring that would not show");
        t.Table.Rig.ToggleTopDown();
        await r.Frames(2);
        Check.That(nodes.All(n => n.SelectionMesh != null), "seen from above each has its ring");
        t.Table.SetSelection([]);
        Check.That(t.Table.RingedCount() == 0 && nodes.All(n => n.SelectionMesh == null && !n.Masked), "and none is left when nothing is picked");
        t.Table.QueueFree();
    }

    const int FrameSide = 64;

    static void HollowSquare(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "test.hollow", "assets");
        Directory.CreateDirectory(dir);
        var img = Image.CreateEmpty(FrameSide, FrameSide, false, Image.Format.Rgba8);
        img.Fill(Colors.Red);
        img.FillRect(new Rect2I(FrameSide / 4, FrameSide / 4, FrameSide / 2, FrameSide / 2), Colors.Transparent);
        img.SavePng(Path.Combine(dir, "hollow.png"));
        var pack = new PackManifest { Id = "test.hollow", Name = "Hollow" };
        pack.Assets["hollow"] = new AssetEntry { File = "assets/hollow.png", Kind = "image" };
        store.RegisterDef(pack, new PieceDef { Id = "hollow", Name = "Hollow square", Shape = "alpha", Size = new Size2(2, 2), Image = "hollow" });
    }

    [NodeTest]
    static async Task A_hollow_cut_out_is_outlined_round_its_outside_never_round_its_hole(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, prepare: HollowSquare, arrange: g => g.Layers[Layers.Umpire] = new List<PieceInstance> { new() { Id = "h", Def = "hollow", Pos = new Vec2(0, 0) } });
        if (t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        var n = t.Node("h");
        for (int i = 0; i < 300 && n.HoleCoverMesh == null; i++) await r.Frame();
        var cover = n.HoleCoverMesh ?? throw new TestFailure("the hollow square's hole was never covered");
        Check.That(!cover.Visible, "unpicked, nothing fills the hole");
        t.Table.SetSelection(["h"]);
        await r.Frames(1);
        Check.That(cover.Visible && (cover.Layers & SelectionAura.MaskLayer) != 0, "picked, the hole is filled in the outline's mask");
        t.Table.SetSelection([]);
        n.SetHovered(true);
        Check.That(cover.Visible && (cover.Layers & SelectionAura.HoverLayer) != 0 && (cover.Layers & SelectionAura.MaskLayer) == 0, "under the pointer, only the hover outline's mask is filled");
        n.SetHovered(false);
        Check.That(!cover.Visible, "and nothing is left once the pointer moves off");
        t.Table.Rig.ToggleTopDown();
        t.Table.SetSelection(["h"]);
        await r.Frames(2);
        var ring = n.SelectionMesh?.MaterialOverride as ShaderMaterial ?? throw new TestFailure("seen from above the picked piece has no ring");
        Check.That(ring.GetShaderParameter("holed").AsBool() && ring.GetShaderParameter("holes").As<Texture2D>() != null, "its ring from above counts the hole as part of the piece");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_highlight_rings_the_piece_and_a_piece_hands_cannot_take_is_passed_over_by_the_pointer(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(2);
        var node = t.Table.Pieces.Nodes.First();
        var id = node.Piece.Id;
        t.S.Do(new SetHighlight { Layer = Layers.Umpire, Id = id, Color = "#DA1A18" });
        await r.Frames(1);
        Check.That(node.Lit, "a highlighted piece wears a ring");
        Check.That(t.Table.Pieces.Pick(node.Piece.Pos) == node, "the pointer finds a piece hands can take");
        t.S.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.NotInteractable, Value = true });
        await r.Frames(1);
        Check.That(t.Table.Pieces.Pick(node.Piece.Pos) == null, "the pointer passes over a piece hands cannot take");
        t.S.Do(new SetHighlight { Layer = Layers.Umpire, Id = id, Color = null });
        await r.Frames(1);
        Check.That(!node.Lit, "the ring goes when the highlight does");
        t.Table.QueueFree();
    }
}
