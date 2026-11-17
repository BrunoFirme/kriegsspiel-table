using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

static class SheetExportFileTests
{
    const int W = 300, H = 200;

    static Color Colour(int x, int y) => Color.Color8((byte)(x * 3), (byte)(y * 5), (byte)(x + y));

    static string Picture(string root)
    {
        using var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                img.SetPixel(x, y, Colour(x, y));
        var path = Path.Combine(root, "source-picture.png");
        img.SavePng(path);
        return path;
    }

    static (string Sha, TileIndex Index) Imported(string root)
    {
        var (_, sha, _, _) = SheetImporter.Import(Picture(root), root);
        return (sha, Json.Load<TileIndex>(Path.Combine(root, SheetImporter.Package, "tiles", sha, "index.json")));
    }

    static SheetDef Sheet(string sha, string id, double x) =>
        new() { Id = id, Name = id, Image = sha, Package = SheetImporter.Package, Pos = new Vec2(x, 0), RotDeg = 180, Size = new Size2(W / 10.0, H / 10.0), Z = id.Length };

    static void ShowsBoth(Image? read, string what)
    {
        Check.That(read != null, $"the {what} reads back");
        Check.Equal((W * 2, H), (read!.GetWidth(), read.GetHeight()), $"size of the {what}");
        foreach (var (x, y) in new[] { (0, 0), (W - 1, H - 1), (123, 77), (W - 1, 0) })
        {
            Check.Equal(Colour(x, y).ToRgba32(), read.GetPixel(x, y).ToRgba32(), $"{what}, left sheet at {x},{y}");
            Check.Equal(Colour(x, y).ToRgba32(), read.GetPixel(W + x, y).ToRgba32(), $"{what}, right sheet at {x},{y}");
        }
    }

    [NodeTest]
    static Task Two_sheets_side_by_side_are_saved_as_one_png_or_tiff_that_reads_back_as_both_pictures(TestRunner r)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-sheet-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var (sha, index) = Imported(root);
        var sources = new List<SheetExport.Source> { new(Sheet(sha, "a", 15), index), new(Sheet(sha, "bb", 45), index) };
        var frame = SheetExport.FrameOf(sources);
        string png = Path.Combine(root, "out.png"), tiff = Path.Combine(root, "out.tif");

        SheetExportFile.Write(sources, frame, (package, rel) => Path.Combine(root, package, rel), png, () => false);
        SheetExportFile.Write(sources, frame, (package, rel) => Path.Combine(root, package, rel), tiff, () => false);

        using var readPng = Image.LoadFromFile(png);
        using var readTiff = TiffImages.Load(tiff);
        ShowsBoth(readPng, "PNG");
        ShowsBoth(readTiff, "TIFF");
        Check.That(!File.Exists(png + SheetExportFile.Unfinished) && !File.Exists(tiff + SheetExportFile.Unfinished), "no unfinished file is left beside the pictures");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task An_export_that_cannot_read_a_tile_leaves_no_file(TestRunner r)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-sheet-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var (sha, index) = Imported(root);
        var sources = new List<SheetExport.Source> { new(Sheet(sha, "a", 15), index) };
        var png = Path.Combine(root, "out.png");
        File.WriteAllText(png, "kept");

        var thrown = false;
        try { SheetExportFile.Write(sources, SheetExport.FrameOf(sources), (_, rel) => Path.Combine(root, "nowhere", rel), png, () => false); }
        catch (Exception e) when (e is IOException or AggregateException) { thrown = true; }

        Check.That(thrown, "the export fails");
        Check.Equal("kept", File.ReadAllText(png), "the file that was there");
        Check.That(!File.Exists(png + SheetExportFile.Unfinished), "no unfinished file is left");
        return Task.CompletedTask;
    }

    [NodeTest]
    static async Task The_menu_row_lists_the_pictured_sheets_and_saves_the_chosen_one(TestRunner r)
    {
        string sha = "";
        var h = await HudTests.Host(r, prepare: store => sha = Imported(store.Root).Sha);
        h.S.Do(new AddSheet { Sheet = Sheet(sha, "a", 15) });
        for (int i = 0; i < 200 && SheetExportDialog.Exportable(h.Hud.Ctx).Count == 0; i++) await r.Frames(1);
        var sources = SheetExportDialog.Exportable(h.Hud.Ctx);
        Check.Equal(1, sources.Count, "sheets offered");

        SheetExportDialog.Open(h.Hud.Ctx);
        await r.Frames(2);
        var dlg = WindowTests.Descendants(h.Hud.Ctx.Node).OfType<ConfirmationDialog>().Single(d => d.Title == SheetExportDialog.Name);
        var checks = WindowTests.Descendants(dlg).OfType<CheckBox>().ToList();
        Check.Equal(1, checks.Count, "sheets listed");
        Check.That(checks[0].ButtonPressed && !dlg.GetOkButton().Disabled, "the one sheet starts chosen");
        Check.That(WindowTests.Descendants(dlg).OfType<Label>().Any(l => l.Text == $"{W} × {H} px"), "the picture's size is shown");
        checks[0].ButtonPressed = false;
        Check.That(dlg.GetOkButton().Disabled, "nothing chosen, nothing to save");
        dlg.Hide();

        var path = Path.Combine(h.S.Store.Root, "exported.png");
        SheetExportDialog.Export(h.Hud.Ctx, sources, SheetExport.FrameOf(sources), path);
        for (int i = 0; i < 600 && SheetExportDialog.Running is not { IsCompleted: true }; i++) await r.Frames(1);
        await r.Frames(2);
        using var read = Image.LoadFromFile(path);
        Check.Equal((W, H), (read.GetWidth(), read.GetHeight()), "size of the saved picture");
        Check.Equal(Colour(210, 150).ToRgba32(), read.GetPixel(210, 150).ToRgba32(), "a pixel of the saved picture");
    }
}
