using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class SheetExportDialog : HudPart
{
    static Task? _exporting;
    const int ListRows = 10, RowHeight = 30, Megabyte = 1 << 20;
    public const string Name = "Export map sheets";

    SheetExportDialog(HudContext c) : base(c) { }

    public static void Open(HudContext c) => new SheetExportDialog(c).Show();

    public static List<SheetExport.Source> Exportable(HudContext c) =>
        SheetStack.TopDown(c.S.View.Sheets).Select(sh => c.T.Sheets.TryGet(sh.Id, out var node) && node.Picture is { } picture ? new SheetExport.Source(sh, picture.Index) : null)
            .OfType<SheetExport.Source>().ToList();

    static string Named(SheetDef sh) => sh.Name.Length > 0 ? sh.Name : sh.Id;

    static string Pixels(SheetExport.Frame f) => string.Format(CultureInfo.InvariantCulture, "{0} × {1} px", f.W, f.H);

    void Show()
    {
        var sheets = Exportable(Ctx);
        var dlg = AgentFence.Mark(new ConfirmationDialog { Title = Name, OkButtonText = "Choose file…" });
        var box = new VBoxContainer { CustomMinimumSize = Ui.Px(420, 0) };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = Ui.Px(0, Math.Min(sheets.Count, ListRows) * RowHeight) };
        scroll.AddChild(list);
        var size = Ui.Hint("");
        var picks = new List<(CheckBox Box, SheetExport.Source Source)>();
        bool one = sheets.Any(s => s.Sheet.Id == T.SelectedSheet);
        List<SheetExport.Source> Chosen() => picks.Where(p => p.Box.ButtonPressed).Select(p => p.Source).ToList();
        void Sized()
        {
            var chosen = Chosen();
            dlg.GetOkButton().Disabled = chosen.Count == 0;
            size.Text = chosen.Count == 0 ? "No sheet chosen" : Pixels(SheetExport.FrameOf(chosen));
        }
        foreach (var s in sheets)
        {
            var check = new CheckBox { Text = Named(s.Sheet), ButtonPressed = !one || s.Sheet.Id == T.SelectedSheet, FocusMode = Control.FocusModeEnum.None, ClipText = true };
            check.Toggled += _ => Sized();
            list.AddChild(check);
            picks.Add((check, s));
        }
        box.AddChild(scroll);
        box.AddChild(size);
        dlg.AddChild(box);
        Sized();
        dlg.Confirmed += () => ChooseFile(Chosen());
        Ui.Open(HudNode, dlg);
        Ui.FreeOnClose(dlg);
        Say("The chosen sheets as they lie on the table, in one picture at their full detail");
    }

    void ChooseFile(List<SheetExport.Source> chosen)
    {
        if (chosen.Count == 0) return;
        var frame = SheetExport.FrameOf(chosen);
        bool large = Math.Max(frame.W, frame.H) > SheetImporter.GodotImageMax;
        string[] png = ["*.png ; PNG picture"], tiff = ["*.tif, *.tiff ; TIFF picture"];
        var stem = string.Concat((chosen.Count == 1 ? Named(chosen[0].Sheet) : S.View.Name).Split(Path.GetInvalidFileNameChars())).Trim();
        var fd = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile, Access = FileDialog.AccessEnum.Filesystem, Title = Name, Size = Ui.PxI(820, 520),
            Filters = large ? [.. tiff, .. png] : [.. png, .. tiff], CurrentFile = (stem.Length > 0 ? stem : "map") + (large ? SheetExportFile.Tiff : SheetExportFile.Png),
        };
        FileBrowser.Fit(fd);
        HudNode.AddChild(fd);
        fd.FileSelected += file => Export(chosen, frame, SheetExportFile.Named(file));
        fd.PopupCentered();
        Ui.FreeOnClose(fd);
        if (large) Say($"Over {SheetImporter.GodotImageMax} px a side: only a TIFF this large comes back in as a map sheet");
    }

    internal static void Export(HudContext c, List<SheetExport.Source> sources, SheetExport.Frame frame, string path) => new SheetExportDialog(c).Export(sources, frame, path);

    void Export(List<SheetExport.Source> sources, SheetExport.Frame frame, string path)
    {
        if (_exporting is { IsCompleted: false }) { Log("A map is still being exported; wait for it to finish before exporting another."); return; }
        int waiting = sources.Sum(s => s.Full.Cols * s.Full.Rows), missing = 0;
        void One(bool there)
        {
            if (!there) missing++;
            if (--waiting > 0) return;
            if (missing == 0) _exporting = Start(sources, frame, path);
            else Log($"[color=#ff7060]Could not export {Fmt.Bb(Path.GetFileName(path))}: {missing} map tile{(missing == 1 ? " is" : "s are")} not on this PC[/color]");
        }
        foreach (var s in sources.ToList())
            for (int y = 0; y < s.Full.Rows; y++)
                for (int x = 0; x < s.Full.Cols; x++)
                    S.Assets.EnsureFile(s.Sheet.Package, SheetExportFile.TileRel(s, x, y), () => One(true), () => One(false));
    }

    const double BuiltShare = 0.3;

    async Task Start(List<SheetExport.Source> sources, SheetExport.Frame frame, string path)
    {
        var assets = S.Assets;
        string file = Path.GetFileName(path), job = $"Exporting {file}…", built = Path.Combine(Path.GetTempPath(), "ktable-export-" + Guid.NewGuid().ToString("N"));
        assets.BeginJob(job);
        Say(job);
        string done;
        try
        {
            await SheetBuiltOverlay.Render(HudNode, S, sources, built, () => Assets.Quitting, p => assets.JobProgress = p * BuiltShare);
            long bytes = await Task.Run(() => SheetExportFile.Write(sources, frame, assets.Abs, path, () => Assets.Quitting, p => assets.JobProgress = BuiltShare + p * (1 - BuiltShare), built));
            done = $"Saved {Fmt.Bb(file)} ({Pixels(frame)}, {Math.Max(1, bytes / Megabyte)} MB).";
        }
        catch (Exception e) { done = $"[color=#ff7060]Could not export {Fmt.Bb(file)}: {Fmt.Bb(e.Message)}[/color]"; }
        finally { KTable.Core.BestEffort.DeleteFolder(built); }
        if (Assets.Quitting) return;
        assets.EndJob(job);
        Log(done);
    }

    internal static Task? Running => _exporting;
}
