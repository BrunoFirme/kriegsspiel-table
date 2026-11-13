using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace KTable.App;

static class FileBrowserTests
{
    sealed class Picked
    {
        public readonly List<string> Paths = new();
    }

    static async Task<(FileDialog Fd, Picked Got)> Open(TestRunner r, FileDialog.FileModeEnum mode, string from)
    {
        var fd = new FileDialog { FileMode = mode, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter } };
        FileBrowser.Fit(fd);
        var got = new Picked();
        fd.FilesSelected += paths => got.Paths.AddRange(paths);
        fd.FileSelected += path => got.Paths.Add(path);
        r.AddChild(fd);
        fd.PopupCentered(new Vector2I(900, 600));
        fd.CurrentDir = from;
        await r.Frames(2);
        return (fd, got);
    }

    static string Folder(string path) => path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

    static LineEdit PathBox(FileDialog fd) => fd.GetVBox().GetChild(0, true).GetChildren(true).OfType<LineEdit>().First();

    static void Press(LineEdit box, Key key) => box.EmitSignal(Control.SignalName.GuiInput, new InputEventKey { Keycode = key, Pressed = true });

    static string Scratch(out string picture, out string notes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-nodetest-browse-" + Guid.NewGuid().ToString("N")[..8], "Random Maps");
        Directory.CreateDirectory(dir);
        picture = Path.Combine(dir, "map one.png");
        notes = Path.Combine(dir, "notes.txt");
        Image.CreateEmpty(4, 4, false, Image.Format.Rgb8).SavePng(picture);
        File.WriteAllText(notes, "x");
        return dir;
    }

    [NodeTest]
    static async Task A_file_explorer_lists_the_pcs_places_on_the_left_and_a_place_opens_its_folder(TestRunner r)
    {
        var (fd, _) = await Open(r, FileDialog.FileModeEnum.OpenFiles, Path.GetTempPath());
        try
        {
            var list = fd.FindChild("PlacesList", true, false) as ItemList;
            Check.That(list != null && list.ItemCount > 0, "the explorer's left column lists the places on this PC");
            var places = KTable.Client.FolderPlaces.OnThisPc();
            Check.Equal(places[0].Name, list!.GetItemText(0), "the first place is the first known folder");
            Check.That(list.GetParent() is VBoxContainer col && col.GetChild(0, true) is Label { Text: "Places:" }, "they sit under a Places heading at the top of the left column");
            list.Select(0);
            list.EmitSignal(ItemList.SignalName.ItemSelected, 0);
            await r.Frames(1);
            Check.Equal(Folder(places[0].Path), Folder(fd.CurrentDir), "picking a place opens its folder");
        }
        finally { fd.QueueFree(); }
    }

    [NodeTest]
    static async Task A_whole_path_pasted_in_quotes_opens_the_file_or_goes_to_the_folder(TestRunner r)
    {
        var dir = Scratch(out var picture, out var notes);
        try
        {
            var (fd, got) = await Open(r, FileDialog.FileModeEnum.OpenFiles, Path.GetTempPath());
            var box = PathBox(fd);
            box.Text = "\"" + dir + "\"";
            Press(box, Key.Enter);
            await r.Frames(1);
            Check.Equal(Folder(dir), Folder(fd.CurrentDir), "a quoted folder path typed in the path box goes to that folder");
            box.Text = "\"" + picture + "\"";
            Press(box, Key.Enter);
            await r.Frames(1);
            Check.Equal(picture, got.Paths.SingleOrDefault() ?? "", "a quoted picture path is picked whole, as Windows' Copy as path gives it");
            Check.That(!fd.Visible, "and the explorer closes on it");
            fd.QueueFree();

            (fd, got) = await Open(r, FileDialog.FileModeEnum.OpenFile, Path.GetTempPath());
            Check.That(FileBrowser.Go(fd, "\"" + notes + "\""), "a path to a file of another kind is still taken");
            await r.Frames(1);
            Check.Equal(Folder(dir), Folder(fd.CurrentDir), "it goes to the file's folder");
            Check.That(got.Paths.Count == 0 && fd.Visible, "without picking a file the explorer does not take");
            Check.That(!FileBrowser.Go(fd, "map one.png"), "a bare name is left to the explorer itself");
            fd.QueueFree();

            (fd, got) = await Open(r, FileDialog.FileModeEnum.SaveFile, Path.GetTempPath());
            FileBrowser.Go(fd, "\"" + picture + "\"");
            await r.Frames(1);
            Check.That(got.Paths.Count == 0 && fd.Visible, "a saving explorer never writes on a pasted path by itself");
            Check.Equal("map one.png", fd.CurrentFile, "it takes the file's folder and name to save over once confirmed");
            fd.QueueFree();
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [ScreenTest]
    static async Task Keys_typed_in_an_explorer_over_the_table_reach_its_path_box(TestRunner r)
    {
        var dir = Scratch(out _, out _);
        try
        {
            var h = await HudTests.Host(r);
            var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFiles, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { ImageFiles.Filter } };
            FileBrowser.Fit(fd);
            h.Hud.AddChild(fd);
            fd.PopupCentered(new Vector2I(900, 600));
            fd.CurrentDir = Path.GetTempPath();
            await r.Frames(2);
            var box = PathBox(fd);
            box.GrabFocus();
            box.Text = "\"" + dir + "\"";
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
            await r.Frames(2);
            Check.Equal(Folder(dir), Folder(fd.CurrentDir), "Enter in the path box over the table goes to the quoted folder");
            fd.QueueFree();
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [NodeTest]
    static async Task A_picture_dropped_on_the_table_becomes_a_map_sheet(TestRunner r)
    {
        var dir = Scratch(out var picture, out var notes);
        try
        {
            var h = await HudTests.Host(r, sheets: 0);
            Check.That(!FileDrops.Route(new[] { notes }), "a dropped file the table has no use for is left alone");
            Check.That(FileDrops.Route(new[] { picture }), "a dropped picture is taken");
            for (int i = 0; i < 100 && h.S.View.Sheets.Count == 0; i++) await r.Seconds(0.05);
            Check.Equal(1, h.S.View.Sheets.Count, "and laid on the table as a map sheet");
            Check.Equal("map one", h.S.View.Sheets.FirstOrDefault()?.Name ?? "", "named after its file");
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [NodeTest]
    static async Task Files_dropped_on_the_window_go_to_the_open_explorer_first(TestRunner r)
    {
        var dir = Scratch(out var picture, out var notes);
        var table = new Picked();
        var under = new Node();
        r.AddChild(under);
        FileDrops.Listen(under, files => { table.Paths.AddRange(files); return true; });
        try
        {
            var (fd, got) = await Open(r, FileDialog.FileModeEnum.OpenFiles, Path.GetTempPath());
            Check.That(FileDrops.Route(new[] { notes, picture }), "a drop is taken");
            Check.Equal(picture, got.Paths.SingleOrDefault() ?? "", "the open explorer picks the dropped files it accepts");
            Check.That(table.Paths.Count == 0, "and nothing under it sees the drop");
            await r.Frames(1);
            FileDrops.Route(new[] { picture });
            Check.Equal(1, table.Paths.Count, "once the explorer is closed, a drop goes to what is under it");
            fd.QueueFree();

            (fd, got) = await Open(r, FileDialog.FileModeEnum.OpenFile, Path.GetTempPath());
            FileDrops.Route(new[] { notes });
            await r.Frames(1);
            Check.That(got.Paths.Count == 0 && fd.Visible, "a dropped file the explorer cannot open is not picked");
            Check.Equal(Folder(dir), Folder(fd.CurrentDir), "the explorer goes to its folder instead");
            fd.QueueFree();
        }
        finally
        {
            under.QueueFree();
            Directory.Delete(Path.GetDirectoryName(dir)!, true);
        }
    }
}
