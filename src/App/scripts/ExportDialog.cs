using System;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public partial class ExportDialog : ConfirmationDialog
{
    readonly PackageStore _store;
    readonly string _kind, _id;
    public event Action<string>? Done;

    public ExportDialog(PackageStore store, string kind, string id)
    {
        _store = store;
        _kind = kind;
        _id = id;
        Title = kind == "pack" ? "Export pack" : "Export scenario";
        OkButtonText = "Choose file…";
    }

    static string Mb(long b) => b >= 1 << 30 ? $"{b / (double)(1 << 30):0.0} GB" : $"{Math.Max(1, b / (1 << 20))} MB";

    public static OptionButton PackModePicker(PackageStore store, string scenarioId, bool workshop)
    {
        var opt = new OptionButton();
        var sc = Json.Load<Scenario>(Path.Combine(store.Root, "scenarios", scenarioId, "scenario.json"));
        var names = string.Join(", ", sc.Packs.Select(p => store.Pack(p)?.Name ?? p));
        opt.AddItem($"Only the pieces it uses (smallest, ≈ {Mb(WorkshopContent.EstimateBytes(store, scenarioId, WorkshopContent.PackMode.Used))})", (int)WorkshopContent.PackMode.Used);
        opt.AddItem($"Whole packs (≈ {Mb(WorkshopContent.EstimateBytes(store, scenarioId, WorkshopContent.PackMode.Whole))})", (int)WorkshopContent.PackMode.Whole);
        opt.AddItem(workshop ? "No packs: require them as separate Workshop items" : $"No packs: recipients need them already (≈ {Mb(WorkshopContent.EstimateBytes(store, scenarioId, WorkshopContent.PackMode.None))})", (int)WorkshopContent.PackMode.None);
        opt.TooltipText = "Packs used: " + names;
        opt.Selected = 0;
        return opt;
    }

    static OptionButton SavesPicker(SaveStore saves, string scenarioDir)
    {
        var opt = new OptionButton { FitToLongestItem = false };
        opt.AddItem("None", (int)SavesMode.None);
        string inPlay = "The game in play";
        try
        {
            var live = saves.LivePath(scenarioDir);
            var c = Json.Load<SaveFile>(live).Snapshot.Clock;
            var folder = saves.CurrentRun(scenarioDir) is { } run ? saves.RunFolderOf(scenarioDir, run) : null;
            inPlay += $" ({(folder != null ? Path.GetFileName(folder) + ", " : "")}turn {c.Turn}) and its saves";
        }
        catch (Exception e) when (SaveStore.IsDamage(e) || e is IOException or UnauthorizedAccessException) { inPlay += " and its saves"; }
        opt.AddItem(inPlay, (int)SavesMode.InPlay);
        int files = 0;
        try { files = Directory.Exists(saves.SavesRoot(scenarioDir)) ? Directory.EnumerateFiles(saves.SavesRoot(scenarioDir), "*" + SaveStore.Ext,
            SearchOption.AllDirectories).Count(f => !SaveStore.IsHidden(saves.SavesRoot(scenarioDir), Path.GetDirectoryName(f)!)) : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { files = 0; }
        opt.AddItem($"Every save ({files} file{(files == 1 ? "" : "s")}, every folder)", (int)SavesMode.All);
        opt.TooltipText = "Whoever installs the file gets these saves beside their own; the game in play becomes the one Resume opens.";
        opt.Selected = 0;
        return opt;
    }

    public override void _Ready()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        AddChild(box);
        OptionButton? mode = null;
        OptionButton? savesPick = null;
        if (_kind == "scenario")
        {
            var sc = Json.Load<Scenario>(Path.Combine(_store.Root, "scenarios", _id, "scenario.json"));
            box.AddChild(new Label { Text = $"{sc.Name} uses {sc.Packs.Count} pack{(sc.Packs.Count == 1 ? "" : "s")}. Include them in the file?" });
            mode = PackModePicker(_store, _id, workshop: false);
            box.AddChild(mode);
            box.AddChild(Ui.Hint("Whoever opens the file gets the scenario and every pack inside it. A trimmed pack never replaces a full one they already have."));
            var saves = SaveStore.Default;
            if (saves.HasGame(_id))
            {
                savesPick = SavesPicker(saves, _id);
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 8);
                row.AddChild(new Label { Text = "Saves" });
                savesPick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(savesPick);
                box.AddChild(row);
            }
        }
        else box.AddChild(new Label { Text = $"{_store.Pack(_id)?.Name ?? _id}: one file anyone can install (drop it on the window)." });

        Confirmed += () =>
        {
            var fd = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.SaveFile, Access = FileDialog.AccessEnum.Filesystem, Filters = new[] { "*.ktpack ; Kriegsspiel Table file" },
                CurrentFile = _id + ".ktpack", Size = new Vector2I(820, 520),
            };
            Ui.RememberFolder(fd);
            GetParent().AddChild(fd);
            fd.FileSelected += f =>
            {
                try
                {
                    var m = mode == null ? WorkshopContent.PackMode.None : (WorkshopContent.PackMode)mode.GetSelectedId();
                    var withSaves = savesPick == null ? SavesMode.None : (SavesMode)savesPick.GetSelectedId();
                    var bytes = WorkshopContent.ExportFile(_store, _kind, _id, m, f, withSaves);
                    Done?.Invoke($"Saved {Path.GetFileName(f)} ({Mb(bytes)}){withSaves switch { SavesMode.InPlay => " with the game in play", SavesMode.All => " with every save", _ => "" }}.");
                }
                catch (Exception e) { Done?.Invoke("Export failed: " + e.Message); }
            };
            fd.PopupCentered();
            Ui.FreeOnClose(fd);
        };
    }
}
