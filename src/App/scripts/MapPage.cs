using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class MapPage : HudPart
{
    VBoxContainer _box = null!;
    string? _cropShownFor;
    Action? _cropSync;

    public readonly ReliefSheets Reliefs;

    public MapPage(HudContext c) : base(c) { Reliefs = new ReliefSheets(c); }

    public Control Build()
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Reserve };
        _box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_box);
        return scroll;
    }

    public void OnCropChanged()
    {
        if (_cropShownFor != T.CropSheetId) Rebuild();
        else _cropSync?.Invoke();
    }

    public void Rebuild()
    {
        if (!S.IsUmpire) return;
        foreach (var c in _box.GetChildren()) c.QueueFree();
        AddSheetList();
        var sel = S.View.Sheets.FirstOrDefault(s => s.Id == T.SelectedSheet);
        if (sel == null) { _toldAbout = null; return; }
        TellHandling(sel);
        AddSelectedForm(sel);
        AddTurn(sel);
        Reliefs.AddRelief(_box, sel);
        _cropShownFor = T.Tool == Tool.Crop ? T.CropSheetId : null;
        _cropSync = null;
        if (_cropShownFor == sel.Id) AddCropForm();
        else AddActions(sel);
    }

    void AddSheetList()
    {
        _box.AddChild(Header("Map sheets"));
        foreach (var sh in S.View.Sheets.OrderBy(s => s.Z))
        {
            var id = sh.Id;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 2);
            var b =
                new Button { Text = sh.Name.Length > 0 ? sh.Name : sh.Id, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, ToggleMode = true,
                    ButtonPressed = T.SelectedSheet == id, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    TooltipText = $"{sh.Name}\nOn the table, Alt+click picks a sheet out too" };
            b.Pressed += () => { T.SelectSheet(T.SelectedSheet == id ? null : id); Rebuild(); };
            row.AddChild(b);
            var locked = sh.Locked;
            var lk = Ui.Bare(new Button { CustomMinimumSize = new Vector2(28, 0), FocusMode = Control.FocusModeEnum.None, TooltipText = locked ? "Locked: it cannot be dragged, turned or resized · click to free it" : "Free · click to lock it in place" });
            lk.AddChild(new StudioIcon { Kind = locked ? "lock" : "unlock", Color = locked ? Ui.Ink : Ui.Faint });
            lk.Pressed += () => S.Do(new LockSheet { Id = id, Locked = !locked });
            row.AddChild(lk);
            _box.AddChild(row);
        }
        if (S.View.Sheets.Count == 0) _box.AddChild(Hint("No map yet"));
        _box.AddChild(Ui.Btn("New blank ground…", Reliefs.AskBlank, "Plain ground to shape and paint with the Relief tool"));
        _box.AddChild(Ui.Btn("Add map sheets…", ImportSheet, "Pictures, 3D terrain (glTF, FBX, OBJ, STL, PLY, Collada, Blender) and heightmaps (PNG, GeoTIFF, ASC, HGT, XYZ); several are laid side by side"));
    }

    string? _toldAbout;

    void TellHandling(SheetDef sel)
    {
        if (_toldAbout == sel.Id) return;
        _toldAbout = sel.Id;
        Say(sel.Locked ? $"{sel.Name} is locked · its padlock in the Map page frees it"
            : $"Drag {sel.Name} to move it (Alt: no snapping) · arrow keys nudge it (Shift: 1 u) · the ring tool ({Cfg.Keys.Label("gizmo")}) turns and resizes it");
    }

    void AddSelectedForm(SheetDef sel)
    {
        _box.AddChild(Header("Selected sheet"));
        var grid = Ui.Form(64);
        var nameEd = new LineEdit { Text = sel.Name, CustomMinimumSize = new Vector2(60, 0) };
        void CommitName() { var n = nameEd.Text.Trim(); if (n.Length > 0 && n != sel.Name) S.Do(new RenameSheet { Id = sel.Id, Name = n }); }
        nameEd.TextSubmitted += _ => CommitName();
        nameEd.FocusExited += CommitName;
        grid.AddChild(new Label { Text = "Name" });
        grid.AddChild(nameEd);
        SpinBox Num(string label, double v, double step, string suffix = "")
        {
            grid.AddChild(new Label { Text = label });
            var sb = new SpinBox { MinValue = -100000, MaxValue = 100000, Step = step, Value = v, Suffix = suffix, Editable = !sel.Locked, CustomMinimumSize = new Vector2(80, 0) };
            grid.AddChild(sb);
            return sb;
        }
        var x = Num("X", sel.Pos.X, 0.01);
        var z = Num("Z", sel.Pos.Z, 0.01);
        var r = Num("Turn", sel.RotDeg, 0.5, "°");
        var zo = Num("Layer", sel.Z, 1);
        var wd = Num("Width", sel.Size.W, 0.1, " u");
        wd.MinValue = 0.5;
        double aspect = sel.Size.H / Math.Max(1e-6, sel.Size.W);
        wd.ValueChanged += v => S.Do(new MoveSheet { Id = sel.Id, Pos = sel.Pos, RotDeg = sel.RotDeg, Z = sel.Z, Size = new Size2(v, v * aspect) });
        void Push() => S.Do(new MoveSheet { Id = sel.Id, Pos = new Vec2(x.Value, z.Value), RotDeg = r.Value, Z = (int)zo.Value });
        foreach (var sb in new[] { x, z, r, zo }) sb.ValueChanged += _ => Push();
        Ui.AlignForm(grid);
        _box.AddChild(grid);
        var ground = S.View.Calibration.UnitsPerMeter is { } upm && upm > 0 ? " · " + S.View.Calibration.DescribeSize(sel.Size.W, sel.Size.H) : "";
        _box.AddChild(Hint($"{sel.Size.W:0.#} × {sel.Size.H:0.#} u{ground}"));
    }

    void AddTurn(SheetDef sel)
    {
        var group = MapTurn.Joined(S.View.Sheets, sel.Id);
        _box.AddChild(Header(group.Count > 1 ? $"Turn with its {group.Count - 1} joined sheets" : "Turn with its pieces"));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        foreach (var (text, deg) in new[] { ("Left 90°", -90.0), ("180°", 180.0), ("Right 90°", 90.0) })
        {
            var b = Btn(text, () => S.Do(MapTurn.Turned(MapTurn.Joined(S.View.Sheets, sel.Id), S.View.Layer(Layers.Umpire), Layers.Umpire, deg)),
                "Turns this sheet, every sheet joined to it and the pieces standing on them as one, about the middle of the map; Ctrl+Z turns them back");
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(b);
        }
        _box.AddChild(row);
    }

    void AddCropForm()
    {
        _box.AddChild(Header("Crop"));
        var cg = Ui.Form(64);
        SpinBox Trim(string label, double v)
        {
            cg.AddChild(new Label { Text = label });
            var sb = new SpinBox { MinValue = 0, MaxValue = 98, Step = 0.1, Value = v, Suffix = "%", CustomMinimumSize = new Vector2(80, 0) };
            cg.AddChild(sb);
            return sb;
        }
        var cr = T.CropRect;
        var left = Trim("Left", cr.X * 100);
        var top = Trim("Top", cr.Y * 100);
        var right = Trim("Right", (1 - cr.X - cr.W) * 100);
        var bottom = Trim("Bottom", (1 - cr.Y - cr.H) * 100);
        bool sync = false;
        void PushCrop() { if (sync) return; T.SetCropRect(new FaceBox { X = left.Value / 100, Y = top.Value / 100, W = 1 - left.Value / 100 - right.Value / 100,
            H = 1 - top.Value / 100 - bottom.Value / 100 }); }
        foreach (var sb in new[] { left, top, right, bottom }) sb.ValueChanged += _ => PushCrop();
        Ui.AlignForm(cg);
        _box.AddChild(cg);
        _box.AddChild(Hint("Or drag the knobs on the table"));
        var row = new HBoxContainer();
        row.AddChild(Ui.Primary("Apply", T.ApplyCrop, "Keep only the part inside the frame"));
        row.AddChild(Btn("Cancel", T.CancelCrop));
        _box.AddChild(row);
        _cropSync = () =>
        {
            if (!GodotObject.IsInstanceValid(left)) return;
            sync = true;
            var c = T.CropRect;
            left.Value = c.X * 100;
            top.Value = c.Y * 100;
            right.Value = (1 - c.X - c.W) * 100;
            bottom.Value = (1 - c.Y - c.H) * 100;
            sync = false;
        };
    }

    void AddActions(SheetDef sel)
    {
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        if (sel.HasPicture) actions.AddChild(Btn("Crop…", () => T.BeginCrop(sel.Id), "Keep only part of the picture: trim borders and legends so sheets fit together"));
        if (sel.Crop != null) actions.AddChild(Btn("Uncrop", () => S.Do(new CropSheet { Id = sel.Id, Crop = null }), "Show the whole picture again"));
        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        actions.AddChild(Btn("Remove", () => { S.Do(new RemoveSheet { Id = sel.Id }); T.SelectSheet(null); }, "Take this sheet off the table"));
        _box.AddChild(actions);
    }

    public static string SheetName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        stem = System.Text.RegularExpressions.Regex.Replace(stem, @"^[0-9A-Fa-f]{32,}[_\- ]*", "");
        stem = stem.Replace('_', ' ').Replace('-', ' ').Trim();
        while (stem.Contains("  ")) stem = stem.Replace("  ", " ");
        return stem.Length == 0 ? "Map sheet" : stem;
    }

    void ImportSheet()
    {
        var fd = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFiles, Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { ReliefImport.Filter, SheetImporter.Filter }, Title = "Add map sheets (select several to lay them side by side)", Size = new Vector2I(900, 600),
        };
        Ui.RememberFolder(fd);
        FileThumbs.Show(fd);
        HudNode.AddChild(fd);
        fd.FilesSelected += paths => AddSheets(paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList());
        PictureLinks.Offer(fd, S.Store.Root, path => AddSheets(new List<string> { path }));
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    public void AddSheets(List<string> paths)
    {
        var reliefs = paths.Where(ReliefImport.IsRelief).ToList();
        if (reliefs.Count > 0) { Reliefs.Pair(reliefs, paths.Except(reliefs).ToList(), AddPictures); return; }
        AddPictures(paths);
    }

    public void AddPictures(List<string> paths)
    {
        if (paths.Count == 0) return;
        var path = paths[0];
        var job = $"Preparing map sheet {SheetName(path)}…";
        Log($"Slicing {Fmt.Bb(SheetName(path))} into map tiles…");
        S.Assets.BeginJob(job);
        Say(job);
        Task.Run(() =>
        {
            try
            {
                var (pkg, sha, w, h) = SheetImporter.Import(path, S.Store.Root,
                    note => Callable.From(() => Log($"[color=#e0b060]{Fmt.Bb(SheetName(path))}: {Fmt.Bb(note)}[/color]")).CallDeferred(),
                    p => S.Assets.JobProgress = p);
                Callable.From(() =>
                {
                    S.Assets.EndJob(job);
                    double right = S.View.Sheets.Count == 0 ? 0 : S.View.Sheets.Max(s => s.Pos.X + s.Size.W / 2);
                    double sw = w * 0.05, shh = h * 0.05;
                    var first = S.View.Sheets.FirstOrDefault();
                    if (first != null)
                    {
                        var k = first.Size.H / shh;
                        sw *= k;
                        shh *= k;
                    }
                    S.Do(new AddSheet { Sheet = new SheetDef { Name = SheetName(path), Image = sha, Package = pkg, Pos = new Vec2(right + sw / 2, first?.Pos.Z ?? 0), Size = new Size2(sw, shh),
                        RotDeg = 180, Locked = false, Z = S.View.Sheets.Count } });
                    if (paths.Count > 1) AddPictures(paths.Skip(1).ToList());
                    else
                    {
                        Log("Sheets added, unlocked: drag them into place, size them in the Map page, then lock them.");
                        Say("Sheet added: drag it into place, then lock it in the Map page.");
                        T.FrameAll();
                    }
                }).CallDeferred();
            }
            catch (Exception e)
            {
                Callable.From(() => { S.Assets.EndJob(job); Log($"[color=#ff7060]Could not add {Fmt.Bb(SheetName(path))}: {Fmt.Bb(e.Message)}[/color]"); }).CallDeferred();
            }
        });
    }
}
