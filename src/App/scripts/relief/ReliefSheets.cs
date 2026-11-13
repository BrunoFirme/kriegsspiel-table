using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Relief;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class ReliefSheets : HudPart
{
    const double UnitsPerPixel = 0.05;
    const double UnitsPerCell = 0.2;
    const int OwnPicture = -1, NoPicture = -2, ChooseFile = -3;

    public ReliefSheets(HudContext c) : base(c) { }

    public static List<ReliefJob> Paired(List<string> reliefs, List<string> pictures)
    {
        var free = pictures.ToList();
        var jobs = new List<ReliefJob>();
        foreach (var r in reliefs)
        {
            var match = BestMatch(r, free);
            if (match != null) free.Remove(match);
            jobs.Add(new ReliefJob(r, match, match == null && ReliefImport.IsModel(r)));
        }
        return jobs;
    }

    static string? BestMatch(string relief, List<string> pictures)
    {
        var stem = Path.GetFileNameWithoutExtension(relief);
        return pictures.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), stem, StringComparison.OrdinalIgnoreCase))
            ?? pictures.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Contains(stem, StringComparison.OrdinalIgnoreCase));
    }

    public void Pair(List<string> reliefs, List<string> pictures, Action<List<string>> addPictures)
    {
        var jobs = Paired(reliefs, pictures);
        var dlg = new ConfirmationDialog { Title = "Relief and picture", OkButtonText = "Add" };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Row);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Group);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Tight);
        var choices = new List<Func<ReliefJob>>();
        foreach (var job in jobs)
        {
            grid.AddChild(new Label { Text = Path.GetFileName(job.Relief), ClipText = true, CustomMinimumSize = new Vector2(200, 0), TooltipText = job.Relief });
            var pick = new OptionButton { CustomMinimumSize = new Vector2(260, 0), FitToLongestItem = false };
            var extra = new List<string>();
            void Fill(string? selected)
            {
                pick.Clear();
                if (ReliefImport.IsModel(job.Relief)) pick.AddItem("Its own texture", OwnPicture);
                for (int i = 0; i < pictures.Count; i++) pick.AddItem(Path.GetFileName(pictures[i]), i);
                for (int i = 0; i < extra.Count; i++) pick.AddItem(Path.GetFileName(extra[i]), pictures.Count + i);
                pick.AddItem("None · plain ground", NoPicture);
                pick.AddItem("Choose a file…", ChooseFile);
                int want = selected == null ? (job.OwnPicture ? OwnPicture : NoPicture)
                    : pictures.IndexOf(selected) is var pi and >= 0 ? pi : pictures.Count + extra.IndexOf(selected);
                pick.Select(Math.Max(0, pick.GetItemIndex(want)));
            }
            Fill(job.Picture);
            int last = pick.Selected;
            pick.ItemSelected += idx =>
            {
                if (pick.GetItemId((int)idx) != ChooseFile) { last = (int)idx; return; }
                pick.Select(last);
                var fd =
                    new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { SheetImporter.Filter },
                        Title = "Picture for " + Path.GetFileName(job.Relief), Size = new Vector2I(900, 600) };
                FileBrowser.Fit(fd);
                FileThumbs.Show(fd);
                HudNode.AddChild(fd);
                void Use(string f)
                {
                    extra.Add(f);
                    Fill(f);
                    last = pick.Selected;
                }
                fd.FileSelected += Use;
                PictureLinks.Offer(fd, S.Store.Root, Use);
                fd.PopupCentered();
                Ui.FreeOnClose(fd);
            };
            grid.AddChild(pick);
            choices.Add(() =>
            {
                int id = pick.GetSelectedId();
                return id switch
                {
                    OwnPicture => job with { Picture = null, OwnPicture = true },
                    NoPicture => job with { Picture = null, OwnPicture = false },
                    _ when id >= 0 && id < pictures.Count => job with { Picture = pictures[id], OwnPicture = false },
                    _ when id >= pictures.Count && id - pictures.Count < extra.Count => job with { Picture = extra[id - pictures.Count], OwnPicture = false },
                    _ => job with { Picture = null, OwnPicture = false },
                };
            });
        }
        box.AddChild(grid);
        var placed = new CheckBox { Text = "Lay them out as the files place them", ButtonPressed = true, TooltipText = "Tiles cut from one map keep their places; otherwise they go side by side" };
        var zero = new CheckBox { Text = "One zero for all", ButtonPressed = true, TooltipText = "Their lowest point together is the table, so neighbouring tiles meet without a step" };
        var scale = new CheckBox { Text = "Set the map's scale from the file", ButtonPressed = true, TooltipText = "A GeoTIFF, .asc or .hgt knows its metres; the Terrain tool needs the map's scale" };
        if (jobs.Count > 1) { box.AddChild(placed); box.AddChild(zero); }
        if (!ReliefTool.Scaled(S)) box.AddChild(scale);
        dlg.AddChild(box);
        dlg.Confirmed += () =>
        {
            var chosen = choices.Select(c => c()).ToList();
            var used = chosen.Where(j => j.Picture != null).Select(j => j.Picture!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rest = pictures.Where(p => !used.Contains(p)).ToList();
            Import(chosen, placed.ButtonPressed, zero.ButtonPressed, scale.ButtonPressed && !ReliefTool.Scaled(S), () => { if (rest.Count > 0) addPictures(rest); });
        };
        Ui.Open(HudNode, dlg, new Vector2I(560, Math.Min(640, 150 + jobs.Count * 34)));
        Ui.FreeOnClose(dlg);
    }

    public void Import(List<ReliefJob> jobs, bool placed, bool sharedZero, bool setScale, Action? done = null)
    {
        if (jobs.Count == 0) { done?.Invoke(); return; }
        var job = $"Building relief {MapPage.SheetName(jobs[0].Relief)}{(jobs.Count > 1 ? $" and {jobs.Count - 1} more" : "")}…";
        S.Assets.BeginJob(job);
        Say(job);
        var cfg = Cfg;
        var root = S.Store.Root;
        var sources = new ReliefSource?[jobs.Count];
        var engine = new List<(int i, string path)>();
        void Problem(string relief, string why) => Callable.From(() => Log($"[color=#ff7060]Could not add {Fmt.Bb(MapPage.SheetName(relief))}: {Fmt.Bb(why)}[/color]")).CallDeferred();
        Task.Run(() =>
        {
            for (int i = 0; i < jobs.Count; i++)
                try
                {
                    var path = jobs[i].Relief;
                    if (Path.GetExtension(path).Equals(".blend", StringComparison.OrdinalIgnoreCase)) path = ReliefImport.FromBlend(path, cfg);
                    if (ReliefImport.NeedsEngine(path)) engine.Add((i, path));
                    else sources[i] = ReliefImport.Read(path);
                }
                catch (Exception e) { Problem(jobs[i].Relief, e.Message); }
            Callable.From(() =>
            {
                foreach (var (i, path) in engine)
                {
                    try { sources[i] = ReliefImport.FromEngine(path, MapPage.SheetName(jobs[i].Relief)); }
                    catch (Exception e) { Problem(jobs[i].Relief, e.Message); }
                    finally { ReliefImport.Forget(path); }
                }
                Task.Run(() => Build(jobs, sources, sharedZero, root, job, placed, setScale, done));
            }).CallDeferred();
        });
    }

    void Build(List<ReliefJob> jobs, ReliefSource?[] sources, bool sharedZero, string root, string job, bool placed, bool setScale, Action? done)
    {
        var made = new List<ReliefMade>();
        double zero = ReliefSource.SharedZero(sources.Where(s => s != null)!);
        for (int i = 0; i < jobs.Count; i++)
        {
            if (sources[i] is not { } src) continue;
            try
            {
                made.Add(ReliefImport.Make(src, jobs[i], sharedZero ? zero : src.Low, root,
                    note => Callable.From(() => Log($"[color=#e0b060]{Fmt.Bb(src.Name)}: {Fmt.Bb(note)}[/color]")).CallDeferred(),
                    p => S.Assets.JobProgress = p));
            }
            catch (Exception e)
            {
                var r = jobs[i].Relief;
                var why = e.Message;
                Callable.From(() => Log($"[color=#ff7060]Could not add {Fmt.Bb(MapPage.SheetName(r))}: {Fmt.Bb(why)}[/color]")).CallDeferred();
            }
        }
        Callable.From(() =>
        {
            S.Assets.EndJob(job);
            Place(made, placed, setScale);
            done?.Invoke();
        }).CallDeferred();
    }

    const double MetresPerKilometre = 1000;

    void Place(List<ReliefMade> made, bool placed, bool setScale)
    {
        if (made.Count == 0) return;
        var first = made[0];
        double w0 = first.PictureW > 0 ? first.PictureW * UnitsPerPixel : first.Source.Cols * UnitsPerCell;
        var existing = S.View.Sheets.FirstOrDefault();
        double h0 = w0 * first.Source.Depth / first.Source.Width;
        if (existing != null && h0 > 0) w0 *= existing.Size.H / h0;
        double k = w0 / first.Source.Width;
        if (setScale && first.Source.RealUnits && !ReliefTool.Scaled(S) && first.Source.Width > 0)
        {
            S.Do(new SetCalibration { UnitsPerMeter = k, Units = S.View.Calibration.Units });
            Log($"Map scale set from {Fmt.Bb(first.Name)}: {first.Source.Width / MetresPerKilometre:0.##} km across.");
        }
        bool byFiles = placed && made.Count > 1 && made.All(m => m.Source.RealUnits)
            && made.Select(m => (Math.Round(m.Source.Left, 3), Math.Round(m.Source.Top, 3))).Distinct().Count() == made.Count;
        double right = S.View.Sheets.Count == 0 ? 0 : S.View.Sheets.Max(s => s.Pos.X + s.Size.W / 2);
        double left0 = made.Min(m => m.Source.Left), top0 = made.Min(m => m.Source.Top);
        double z = existing?.Pos.Z ?? 0;
        int layer = S.View.Sheets.Count;
        foreach (var m in made)
        {
            double w = k * m.Source.Width, h = k * m.Source.Depth;
            var pos = byFiles
                ? new Vec2(right + k * (m.Source.Left - left0) + w / 2, z - k * (m.Source.Top - top0) - h / 2)
                : new Vec2(right + w / 2, z);
            if (!byFiles) right += w;
            var sheet = new SheetDef
            {
                Name = m.Name, Image = m.Image ?? "", Package = UploadAsset.ReliefPackage, Pos = pos, Size = new Size2(w, h), RotDeg = 180,
                Locked = false, Z = layer++, Relief = m.Relief, Height = 1,
            };
            T.Sheets.PickTheNextToArrive();
            Share(m.Relief, new AddSheet { Sheet = sheet });
        }
        Log($"Relief added, unlocked: drag {(made.Count == 1 ? "it" : "them")} into place, then lock {(made.Count == 1 ? "it" : "them")} in the Map page.");
        T.FrameAll();
    }

    public const double BlankSide = 100, LeastBlank = 1, MostBlank = 100000;

    public (double width, double depth) BlankSize()
    {
        var first = S.View.Sheets.FirstOrDefault();
        return first == null ? (BlankSide, BlankSide) : (first.Size.W, first.Size.H);
    }

    public void AskBlank()
    {
        var (w0, d0) = BlankSize();
        var dlg = new ConfirmationDialog { Title = "New blank ground", OkButtonText = "Add" };
        var grid = Ui.Form(Ui.DialogLabels);
        SpinBox Num(string label, double v)
        {
            grid.AddChild(new Label { Text = label });
            var sb = new SpinBox { MinValue = LeastBlank, MaxValue = MostBlank, Step = 0.5, Value = v, Suffix = "u", CustomMinimumSize = new Vector2(110, 0) };
            grid.AddChild(sb);
            return sb;
        }
        var w = Num("Width", w0);
        var d = Num("Depth", d0);
        grid.AddChild(new Label { Text = "Detail" });
        var detail = new OptionButton { TooltipText = "How fine the ground's cells are, for shaping and paint; a small close map wants it finer", CustomMinimumSize = new Vector2(110, 0) };
        foreach (var (times, name) in Details) detail.AddItem(name, times);
        grid.AddChild(detail);
        SpinBox? metres = null;
        Label? across = null;
        if (!ReliefTool.Scaled(S))
        {
            grid.AddChild(new Label { Text = "Metres to the unit" });
            metres = new SpinBox { MinValue = LeastMetresPerUnit, MaxValue = MostMetresPerUnit, Step = 1, Value = UsualMetresPerUnit, Suffix = "m", CustomMinimumSize = new Vector2(110, 0) };
            grid.AddChild(metres);
            across = Hint("");
        }
        Ui.AlignForm(grid);
        dlg.AddChild(grid);
        if (metres != null && across != null)
        {
            void Read() => across.Text = $"{w.Value * metres.Value / MetresPerKilometre:0.#} × {d.Value * metres.Value / MetresPerKilometre:0.#} km";
            Read();
            w.ValueChanged += _ => Read();
            d.ValueChanged += _ => Read();
            metres.ValueChanged += _ => Read();
            dlg.AddChild(across);
        }
        dlg.Confirmed += () =>
        {
            if (metres != null && !ReliefTool.Scaled(S)) S.Do(new SetCalibration { UnitsPerMeter = 1 / metres.Value, Units = S.View.Calibration.Units });
            AddBlank(w.Value, d.Value, finer: detail.GetSelectedId());
        };
        Ui.Open(HudNode, dlg, new Vector2I(300, metres == null ? 186 : 246));
        Ui.FreeOnClose(dlg);
    }

    const double LeastMetresPerUnit = 1, MostMetresPerUnit = 10000, UsualMetresPerUnit = 50;

    static readonly (int Times, string Name)[] Details = { (1, "Usual"), (2, "Fine"), (4, "Finest") };

    public void AddBlank(double width, double depth, Action<string?>? done = null, int finer = 1)
    {
        width = Math.Clamp(width, LeastBlank, MostBlank);
        depth = Math.Clamp(depth, LeastBlank, MostBlank);
        var root = S.Store.Root;
        Task.Run(() =>
        {
            try
            {
                var (sha, _) = ReliefImport.Save(ReliefGrid.Flat(width, depth, finer), root);
                Callable.From(() =>
                {
                    var first = S.View.Sheets.FirstOrDefault();
                    double right = S.View.Sheets.Count == 0 ? 0 : S.View.Sheets.Max(s => s.Pos.X + s.Size.W / 2);
                    var sheet = new SheetDef
                    {
                        Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Pos = new Vec2(S.View.Sheets.Count == 0 ? 0 : right + width / 2, first?.Pos.Z ?? 0),
                        Size = new Size2(width, depth), RotDeg = 180, Locked = false, Z = S.View.Sheets.Count, Relief = sha, Height = 1,
                    };
                    T.Sheets.PickTheNextToArrive();
                    Share(sha, new AddSheet { Sheet = sheet });
                    T.FrameAll();
                    done?.Invoke(null);
                }).CallDeferred();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                var why = e.Message;
                Callable.From(() => { Log($"[color=#ff7060]Could not make the ground: {Fmt.Bb(why)}[/color]"); done?.Invoke(why); }).CallDeferred();
            }
        });
    }

    public void Share(string relief, Command c)
    {
        var dir = Path.Combine(S.Store.Root, UploadAsset.ReliefPackage);
        var files = new List<string>();
        try { files.AddRange(ReliefFiles.Files(relief, ReliefFiles.LoadIndex(Path.Combine(dir, ReliefFiles.HeaderRel(relief))))); }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException) { Log($"[color=#ff7060]The relief could not be read back: {Fmt.Bb(e.Message)}[/color]"); return; }
        N.ShareThen(files, c);
    }

    void Finer(SheetDef sel)
    {
        if (S.Relief.Grid(sel) is not { } grid || !grid.CanBeFiner() || !S.CanEdit(Layers.Umpire)) return;
        ReliefTool.Commit(S, sel, grid.Finer(), Share, T.Say);
    }

    public void AddRelief(VBoxContainer box, SheetDef sel)
    {
        box.AddChild(Header("Relief"));
        if (sel.Relief is { Length: > 0 })
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ui.Gap.Group);
            row.AddChild(Ui.RowLabel("Height ×", Ui.PanelLabels));
            var slider =
                new HSlider { MinValue = 0, MaxValue = 5, Step = 0.05, Value = sel.Height, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    TooltipText = "0 lies flat; 1 keeps the model's own proportions" };
            var value = Ui.ValueBox(slider, v => $"{v:0.##}", 34);
            slider.DragEnded += changed => { if (changed) S.Do(new SetSheetRelief { Id = sel.Id, Relief = sel.Relief, Height = slider.Value }); };
            row.AddChild(slider);
            row.AddChild(value);
            box.AddChild(row);
            if (S.Relief.Grid(sel) is not { } grid) box.AddChild(Hint("Loading"));
            else
            {
                double perMetre = S.View.Calibration.UnitsPerMeter ?? 0, cell = Ground.FullWidth(sel) / Math.Max(1, grid.Cols - 1);
                var finer = Btn("Finer", () => Finer(sel), "Twice the cells each way, for finer shaping and paint; the ground itself stays as it is");
                finer.Disabled = !grid.CanBeFiner() || !S.CanEdit(Layers.Umpire);
                box.AddChild(Ui.Fact("Cells", perMetre > 0 ? $"{grid.Cols - 1} × {grid.Rows - 1} · {cell / perMetre:0.#} m" : $"{grid.Cols - 1} × {grid.Rows - 1}", Ui.PanelLabels, finer));
            }
        }
        var actions = new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation", Ui.Gap.Row);
        actions.AddChild(Btn(sel.Relief is { Length: > 0 } ? "Replace relief…" : "Add relief…", () => PickRelief(sel), "A 3D model, heightmap or elevation file, stretched over this sheet"));
        if (sel.Relief is { Length: > 0 } && sel.HasPicture) actions.AddChild(Btn("Remove relief", () => S.Do(new SetSheetRelief { Id = sel.Id, Relief = null, Height = 1 })));
        actions.AddChild(Btn(sel.HasPicture ? "Picture…" : "Add a picture…", () => PickPicture(sel), "A map picture laid over this sheet"));
        box.AddChild(actions);
    }

    void PickRelief(SheetDef sel)
    {
        var filter = string.Join(", ", ReliefImport.ModelExtensions.Concat(HeightFiles.Extensions).Concat(new[] { ".tif", ".tiff", ".png" }).Select(e => "*" + e)) + " ; 3D terrain and heightmaps";
        var fd =
            new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { filter }, Title = "Relief for " + sel.Name, Size = new Vector2I(900, 600) };
        FileBrowser.Fit(fd);
        HudNode.AddChild(fd);
        var id = sel.Id;
        fd.FileSelected += path => ReliefOnto(path, id, null);
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    public void ReliefOnto(string path, string onto, Action<string?>? done)
    {
        var job = $"Building relief {MapPage.SheetName(path)}…";
        S.Assets.BeginJob(job);
        var cfg = Cfg;
        var root = S.Store.Root;
        void Finish(ReliefSource src)
        {
            Task.Run(() =>
            {
                try
                {
                    var (sha, _) = ReliefImport.Save(src.ToGrid(src.Low), root);
                    Callable.From(() =>
                    {
                        S.Assets.EndJob(job);
                        if (S.View.Sheets.FirstOrDefault(s => s.Id == onto) is not { } sheet) { done?.Invoke("the sheet is gone"); return; }
                        Share(sha, new SetSheetRelief { Id = onto, Relief = sha, Height = sheet.Relief is { Length: > 0 } ? sheet.Height : 1 });
                        done?.Invoke(null);
                    }).CallDeferred();
                }
                catch (Exception e)
                {
                    var why = e.Message;
                    Callable.From(() =>
                    {
                        S.Assets.EndJob(job);
                        Log($"[color=#ff7060]Could not add the relief: {Fmt.Bb(why)}[/color]");
                        done?.Invoke(why);
                    }).CallDeferred();
                }
            });
        }
        Task.Run(() =>
        {
            try
            {
                var p = Path.GetExtension(path).Equals(".blend", StringComparison.OrdinalIgnoreCase) ? ReliefImport.FromBlend(path, cfg) : path;
                if (ReliefImport.NeedsEngine(p))
                    Callable.From(() =>
                    {
                        try { Finish(ReliefImport.FromEngine(p, MapPage.SheetName(path))); }
                        catch (Exception e)
                        {
                            S.Assets.EndJob(job);
                            Log($"[color=#ff7060]Could not add the relief: {Fmt.Bb(e.Message)}[/color]");
                            done?.Invoke(e.Message);
                        }
                        finally { ReliefImport.Forget(p); }
                    }).CallDeferred();
                else Finish(ReliefImport.Read(p));
            }
            catch (Exception e)
            {
                var why = e.Message;
                Callable.From(() =>
                {
                    S.Assets.EndJob(job);
                    Log($"[color=#ff7060]Could not add the relief: {Fmt.Bb(why)}[/color]");
                    done?.Invoke(why);
                }).CallDeferred();
            }
        });
    }

    void PickPicture(SheetDef sel)
    {
        var fd =
            new FileDialog { FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { SheetImporter.Filter }, Title = "Picture for " + sel.Name,
                Size = new Vector2I(900, 600) };
        FileBrowser.Fit(fd);
        FileThumbs.Show(fd);
        HudNode.AddChild(fd);
        var id = sel.Id;
        fd.FileSelected += path => SetPicture(id, path);
        PictureLinks.Offer(fd, S.Store.Root, path => SetPicture(id, path));
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
    }

    public void SetPicture(string id, string path, Action<string?>? done = null)
    {
        var job = $"Preparing map sheet {MapPage.SheetName(path)}…";
        S.Assets.BeginJob(job);
        var root = S.Store.Root;
        Task.Run(() =>
        {
            try
            {
                var (pkg, sha, w, h) = SheetImporter.Import(path, root, null, p => S.Assets.JobProgress = p);
                Callable.From(() =>
                {
                    S.Assets.EndJob(job);
                    if (S.View.Sheets.FirstOrDefault(s => s.Id == id) is not { } sheet) { done?.Invoke("the sheet is gone"); return; }
                    var size = sheet.Relief is { Length: > 0 } ? sheet.Size : new Size2(sheet.Size.W, sheet.Size.W * h / Math.Max(1, w));
                    S.Do(new SetSheetPicture { Id = id, Image = sha, Package = pkg, Size = size });
                    done?.Invoke(null);
                }).CallDeferred();
            }
            catch (Exception e)
            {
                var why = e.Message;
                Callable.From(() =>
                {
                    S.Assets.EndJob(job);
                    Log($"[color=#ff7060]Could not add {Fmt.Bb(MapPage.SheetName(path))}: {Fmt.Bb(why)}[/color]");
                    done?.Invoke(why);
                }).CallDeferred();
            }
        });
    }
}
