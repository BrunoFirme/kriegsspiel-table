using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

static class SheetReliefTests
{
    const int TileSide = 256, LoadFrames = 120;

    static void OneTilePicture(PackageStore store)
    {
        var dir = Path.Combine(store.Root, "test", "tiles", "pic");
        Directory.CreateDirectory(Path.Combine(dir, "0"));
        var index = new TileIndex { TileSize = TileSide, SourceW = TileSide, SourceH = TileSide, Ext = "png" };
        index.Levels.Add(new TileLevel { Level = 0, W = TileSide, H = TileSide, Cols = 1, Rows = 1 });
        Json.Save(Path.Combine(dir, "index.json"), index);
        using var img = Image.CreateEmpty(TileSide, TileSide, false, Image.Format.Rgba8);
        img.Fill(new Color(0.4f, 0.6f, 0.3f));
        img.SavePng(Path.Combine(dir, "0", "0_0.png"));
    }

    [NodeTest]
    static async Task Changing_a_pictured_sheet_s_relief_keeps_its_picture_on_the_table(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1, pictured: true, prepare: OneTilePicture);
        var id = h.S.View.Sheets[0].Id;
        Check.That(h.Table.Sheets.TryGet(id, out var node), "the sheet has a node");
        for (int i = 0; i < LoadFrames && !node.Loaded; i++) await r.Frame();
        Check.That(node.Loaded && node.Picture != null, "the sheet's picture loaded");
        var picture = node.Picture;
        int built = node.Built;
        var flat = ReliefGrid.Flat(20, 20);
        var (sha, _) = ReliefImport.Save(flat, h.S.Store.Root);
        h.S.Relief.Remember(sha, flat);
        h.S.Do(new SetSheetRelief { Id = id, Relief = sha, Height = 1 });
        await r.Frames(4);
        Check.That(h.Table.Sheets.TryGet(id, out var after) && after == node, "the relief change keeps the sheet's node");
        Check.That(node.Loaded && node.Picture == picture, "and its picture, with no loading");
        Check.That(node.Built > built, "the ground under the picture is rebuilt for the new relief");
        Check.Equal(sha, node.Def.Relief, "the node reads the new relief");
        built = node.Built;
        h.S.UndoLast();
        await r.Frames(4);
        Check.That(h.Table.Sheets.TryGet(id, out after) && after == node && node.Picture == picture, "undo keeps the picture too");
        Check.That(node.Built > built && node.Def.Relief == null, "and lays the sheet flat again");
    }

    [NodeTest]
    static async Task Finer_on_a_sheet_s_Relief_doubles_its_cells_and_leaves_the_ground_as_it_was(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1);
        var id = h.S.View.Sheets[0].Id;
        var ground = ReliefGrid.Flat(20, 20);
        for (int i = 0; i < ground.H.Length; i++) ground.H[i] = (i % ground.Cols) * 0.0001f;
        ground.Kind[ground.Cols * 100 + 100] = GroundKinds.Water;
        var (sha, _) = ReliefImport.Save(ground, h.S.Store.Root);
        h.S.Relief.Remember(sha, ground);
        h.S.Do(new SetSheetRelief { Id = id, Relief = sha, Height = 1 });
        await r.Frames(4);
        h.Table.SelectSheet(id);
        h.Hud.Drawer.ShowPage("map");
        h.Hud.Map.Rebuild();
        await r.Frames(2);
        Button Finer() => WindowTests.Descendants(h.Hud.Drawer.Pages["map"].page!).OfType<Button>().Single(b => b.Text == "Finer" && !b.IsQueuedForDeletion());
        Check.That(!Finer().Disabled, "a sheet with relief offers Finer");
        Finer().EmitSignal(BaseButton.SignalName.Pressed);
        for (int i = 0; i < LoadFrames && h.S.View.Sheets[0].Relief == sha; i++) await r.Frame();
        var fine = h.S.Relief.Grid(h.S.View.Sheets[0]);
        Check.That(fine != null && fine.Cols == (ground.Cols - 1) * 2 + 1 && fine.Rows == (ground.Rows - 1) * 2 + 1, $"the ground has twice the cells each way: {fine?.Cols} × {fine?.Rows}");
        Check.That(System.Math.Abs(fine!.HeightAt(0.37, 0.61) - ground.HeightAt(0.37, 0.61)) < 1e-6, "and lies as it lay");
        Check.That(fine.KindAt(100.0 / (ground.Cols - 1), 100.0 / (ground.Rows - 1)) == GroundKinds.Water, "with its paint");
        h.S.UndoLast();
        await r.Frames(4);
        Check.Equal(sha, h.S.View.Sheets[0].Relief, "one undo brings the coarser ground back");
        h.S.Do(new SetSheetRelief { Id = id, Relief = null, Height = 1 });
    }
}
