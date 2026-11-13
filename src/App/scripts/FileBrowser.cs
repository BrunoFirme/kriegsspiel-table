using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

public static class FileBrowser
{
    static string LastFolderFile => ProjectSettings.GlobalizePath("user://last-folder.txt");

    public static void Fit(FileDialog fd)
    {
        RememberFolder(fd);
        if (Ui.FoldersIn?.SystemFileDialog == true) { fd.UseNativeDialog = true; return; }
        AddPlaces(fd);
        foreach (var box in OwnBoxes(fd)) TakePaths(fd, box);
        FileDrops.Listen(fd, files => Dropped(fd, files));
    }

    static void RememberFolder(FileDialog fd)
    {
        BestEffort.Try(() => { if (File.Exists(LastFolderFile) && File.ReadAllText(LastFolderFile).Trim() is { Length: > 0 } d
            && Directory.Exists(d)) fd.CurrentDir = d; });
        void Save()
        {
            BestEffort.Try(() => { var d = fd.CurrentDir; if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) File.WriteAllText(LastFolderFile, d); });
            Ui.SaveFolderLists();
        }
        fd.Confirmed += Save;
        fd.Canceled += Save;
        fd.VisibilityChanged += () => { if (!fd.Visible) Save(); };
    }

    static void AddPlaces(FileDialog fd)
    {
        var split = Find<HSplitContainer>(fd.GetVBox());
        var column = split?.GetChildren(true).OfType<SplitContainer>().FirstOrDefault()?.GetChildren(true).OfType<VBoxContainer>().FirstOrDefault();
        if (column == null) return;
        var places = FolderPlaces.OnThisPc();
        if (places.Count == 0) return;
        var head = new Label { Text = "Places:", Name = "Places" };
        var list = new ItemList { AutoHeight = true, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, Name = "PlacesList" };
        var icon = fd.GetThemeIcon("folder", "FileDialog");
        foreach (var p in places) list.SetItemTooltip(list.AddItem(p.Name, icon), p.Path);
        list.ItemSelected += i => { fd.CurrentDir = places[(int)i].Path; list.DeselectAll(); };
        fd.AboutToPopup += () =>
        {
            if (column.GetChildren(true).OfType<HBoxContainer>().SelectMany(h => h.GetChildren(true).OfType<Label>()).FirstOrDefault() is not { } like) return;
            head.AddThemeFontOverride("font", like.GetThemeFont("font"));
            head.AddThemeFontSizeOverride("font_size", like.GetThemeFontSize("font_size"));
            head.AddThemeColorOverride("font_color", like.GetThemeColor("font_color"));
        };
        column.AddChild(head);
        column.MoveChild(head, 0);
        column.AddChild(list);
        column.MoveChild(list, 1);
    }

    static T? Find<T>(Node n) where T : Node
    {
        foreach (var c in n.GetChildren(true))
        {
            if (c is T t) return t;
            if (Find<T>(c) is { } deep) return deep;
        }
        return null;
    }

    static IEnumerable<LineEdit> OwnBoxes(FileDialog fd)
    {
        static IEnumerable<Node> Below(Node n) => n.GetChildren(true).SelectMany(c => Below(c).Prepend(c));
        static bool InPopup(Node n, Node top) => n != top && (n is Window || n.GetParent() is { } up && InPopup(up, top));
        return Below(fd.GetVBox()).OfType<LineEdit>().Where(le => !InPopup(le, fd)).ToList();
    }

    static void TakePaths(FileDialog fd, LineEdit box) =>
        box.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true } k) return;
            var paste = k.IsCommandOrControlPressed() && k.Keycode == Key.V || k.ShiftPressed && k.Keycode == Key.Insert;
            var enter = k.Keycode is Key.Enter or Key.KpEnter;
            if (!paste && !enter) return;
            if (Go(fd, paste ? DisplayServer.ClipboardGet() : box.Text)) box.AcceptEvent();
        };

    public static bool Go(FileDialog fd, string typed)
    {
        if (FolderPlaces.TypedPath(typed) is not { } path) return false;
        if (Directory.Exists(path)) { fd.CurrentDir = path; return true; }
        if (!File.Exists(path) || Path.GetDirectoryName(path) is not { } folder) return false;
        if (Opens(fd) && Accepts(fd, path)) { Pick(fd, new[] { path }); return true; }
        fd.CurrentDir = folder;
        if (fd.FileMode == FileDialog.FileModeEnum.SaveFile) fd.CurrentFile = Path.GetFileName(path);
        return true;
    }

    public static bool Dropped(FileDialog fd, string[] files)
    {
        if (!fd.Visible || files.Length == 0) return false;
        var taken = files.Where(f => File.Exists(f) && Accepts(fd, f)).ToArray();
        if (Opens(fd) && taken.Length > 0) { Pick(fd, fd.FileMode == FileDialog.FileModeEnum.OpenFiles ? taken : taken[..1]); return true; }
        Go(fd, files[0]);
        return true;
    }

    static bool Opens(FileDialog fd) => fd.FileMode is FileDialog.FileModeEnum.OpenFile or FileDialog.FileModeEnum.OpenFiles;

    public static bool Accepts(FileDialog fd, string path)
    {
        var patterns = fd.Filters.SelectMany(f => f.Split(';')[0].Split(',')).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var name = Path.GetFileName(path);
        return patterns.Count == 0 || patterns.Any(p => name.MatchN(p));
    }

    static void Pick(FileDialog fd, string[] paths)
    {
        var folder = Path.GetDirectoryName(paths[0])!;
        fd.CurrentDir = folder;
        FileDialog.SetRecentList(FileDialog.GetRecentList().Where(r => !SameFolder(r, folder)).Prepend(folder.Replace('\\', '/')).Take(Settings.RecentFoldersMost).ToArray());
        fd.Hide();
        if (fd.FileMode == FileDialog.FileModeEnum.OpenFiles) fd.EmitSignal(FileDialog.SignalName.FilesSelected, paths);
        else fd.EmitSignal(FileDialog.SignalName.FileSelected, paths[0]);
    }

    static bool SameFolder(string a, string b) =>
        string.Equals(a.Replace('\\', '/').TrimEnd('/'), b.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
