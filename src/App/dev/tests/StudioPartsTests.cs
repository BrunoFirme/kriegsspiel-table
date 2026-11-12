using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class StudioPartsTests
{
    const double Wide = 5, Deep = 2, FileGap = 1, RankGap = 1, Taller = 0.5;

    static PieceDef Stand(StudioTests.Rig h)
    {
        var s = h.Studio;
        var stand = PackEdit.NewDef(s.Store, s.Pack, "Stand", s.Pack.Pieces[0].Image!, 8, 8, Wide, "box");
        stand.Size = new Size2(Wide, Deep);
        s.SavePack();
        s.Paper.LayOutAll();
        s.RebuildList();
        return stand;
    }

    static PieceNode OnPaper(PieceStudio s, PieceDef d) =>
        s.Paper.Instance(d) is { } p && s.T.NodeOf(Layers.Umpire, p.Id) is { } n ? n : throw new TestFailure($"{d.Name} is not on the studio's table");

    [ScreenTest]
    static async Task A_stand_is_built_from_ten_of_another_piece_and_follows_what_happens_to_that_piece(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var hussars = s.Pack.Pieces.Single();
        var stand = Stand(h);
        s.Select(stand, frame: false);
        await r.Frames(5);
        s.SetView("model");
        await r.Frames(20);
        var m = s.Model ?? throw new TestFailure("the 3D view opens");
        Check.That(m.Holds(stand), "the 3D view shows the stand");
        Check.That(PartRules.Takes(stand, s.Pack.Pieces, s.Store.Def).Contains(hussars), "the stand can take the hussars");
        Check.That(!PartRules.Takes(stand, s.Pack.Pieces, s.Store.Def).Contains(stand), "but not itself");

        m.AddPiece(hussars);
        await r.Frames(3);
        Check.Equal(1, stand.Parts?.Count ?? 0, "the hussars are a part of the stand");
        Check.That(Mathf.IsEqualApprox((float)stand.Parts![0].Y, (float)stand.Thickness), $"they stand on its top: {stand.Parts[0].Y}");
        Check.Equal((0, 1), (m.SelPiece, m.PieceNodes.Count), "the new row is chosen and drawn in the 3D view");

        m.RepeatPiece(0, 10, 2);
        await r.Frames(3);
        Check.Equal(10, stand.Parts.Count, "ten of them after Repeat");
        Check.Equal(10, m.PieceNodes.Count, "all ten drawn in the 3D view");
        Check.Equal((-2 * FileGap, RankGap / 2), (stand.Parts[0].X, -stand.Parts[0].Z), "the first stands front left, a file and a rank apart from the next");
        s.FlushPack();
        Check.Equal(10, h.OnDisk().Pieces.First(p => p.Id == stand.Id).Parts?.Count ?? 0, "pack.json carries the ten");

        var placed = s.Paper.Instance(stand)!;
        var node = OnPaper(s, stand);
        Check.Equal(10, node.Parts.Count, "the stand on the table is drawn with its ten");
        var frontLeft = MarkerPlace.World(placed.Pos, placed.RotDeg, new Vec2(-2 * FileGap, RankGap / 2));
        var seen = Coords.FromWorld(node.Parts[0].GlobalPosition);
        Check.That((seen - frontLeft).Length < 0.01, $"across and ahead mean what they mean for markers: {seen} against {frontLeft}");

        m.SetPiece(0, p => p.Rot = 90);
        m.DuplicatePiece(0);
        await r.Frames(3);
        Check.Equal((11, 1, 90.0), (stand.Parts.Count, m.SelPiece, stand.Parts[1].Rot), "a duplicate stands beside the one it copies, turned as it is, and is chosen");
        m.RemovePiece(1);
        await r.Frames(3);
        Check.Equal((10, 1), (stand.Parts.Count, m.SelPiece), "taking it off leaves ten and the next one chosen");
        s.Undo();
        await r.Frames(10);
        Check.Equal(11, stand.Parts?.Count ?? 0, "undo puts the piece back");
        s.Redo();
        await r.Frames(10);
        Check.Equal(10, stand.Parts?.Count ?? 0, "redo takes it off again");

        s.Select(hussars, frame: false);
        await r.Frames(5);
        s.Apply(x => x.Thickness = Taller, what: "the thickness");
        await r.Frames(5);
        Check.That(Mathf.IsEqualApprox(OnPaper(s, stand).Parts[0].Thickness, (float)Taller), "a change to the hussars shows on the stand built from them");

        s.DeleteNow(hussars);
        await r.Frames(5);
        Check.That(stand.Parts == null, "deleting the hussars takes them off the stand");
        Check.Equal(0, OnPaper(s, stand).Parts.Count, "and off the stand on the table");
        s.FlushPack();
        Check.That(h.OnDisk().Pieces.First(p => p.Id == stand.Id).Parts == null, "and out of pack.json");
    }

    [NodeTest]
    static async Task A_piece_copied_to_another_pack_brings_its_parts_once(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        var s = h.Studio;
        var hussars = s.Pack.Pieces.Single();
        var stand = Stand(h);
        stand.Parts = new() { new PartDef { Def = hussars.Id, X = 1 }, new PartDef { Def = hussars.Id, X = -1 } };
        var colours = PackEdit.Duplicate(s.Store, s.Pack, stand);

        var otherId = PackEdit.CreatePack(new PackageStore(h.Store.Root), "Other pieces", "Tester");
        var fresh = new PackageStore(h.Store.Root);
        var other = fresh.Pack(otherId) ?? throw new TestFailure("the second pack opens");
        var copy = PackEdit.CopyInto(fresh, s.Pack, stand, other);

        Check.Equal(2, other.Pieces.Count, "the stand and one copy of its hussars arrive");
        var brought = other.Pieces.First(p => p != copy);
        Check.That(brought.Name == hussars.Name && brought.Id != hussars.Id, "the hussars are a kind of the new pack");
        Check.That(copy.Parts!.All(p => p.Def == brought.Id), "and the copy's parts are that kind");
        Check.That(File.Exists(fresh.AssetPath(other, brought.Image)), "with their picture");
        Check.That(stand.Parts.All(p => p.Def == hussars.Id), "the stand copied from keeps its own");

        PackEdit.CopyInto(fresh, s.Pack, colours, other);
        Check.Equal(3, other.Pieces.Count, "a second stand of the same hussars brings no second copy of them");
        await r.Frame();
    }
}
