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
        AgentFence.Mark(this);
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
        string inPlay = "The save on the table";
        try
        {
            if (saves.CurrentSave(scenarioDir) is { } live)
                inPlay += $" ({Path.GetFileNameWithoutExtension(live)}{(Json.ReadSaveHeadline(live) is { } head ? $", turn {head.Preview.Turn}" : "")})";
        }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { BestEffort.Note(e); }
        opt.AddItem(inPlay, (int)SavesMode.InPlay);
        int files = 0;
        try { files = Directory.Exists(saves.SavesRoot(scenarioDir)) ? Directory.EnumerateFiles(saves.SavesRoot(scenarioDir), "*" + SaveStore.Ext,
            SearchOption.AllDirectories).Count(f => !SaveStore.IsHidden(saves.SavesRoot(scenarioDir), Path.GetDirectoryName(f)!)) : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { files = 0; }
        opt.AddItem($"Every save ({files} file{(files == 1 ? "" : "s")}, every folder)", (int)SavesMode.All);
        opt.TooltipText = "Whoever installs the file gets these saves beside their own; the save on the table becomes the one Resume opens.";
        opt.Selected = 0;
        return opt;
    }

    static OptionButton TablePicker(PackageStore store, Scenario sc, string scenarioDir, bool inPlay)
    {
        var opt = new OptionButton { FitToLongestItem = false };
        int pieces = sc.UmpireBoard.Count;
        opt.AddItem($"As it stands now ({pieces} piece{(pieces == 1 ? "" : "s")}, turn {sc.Clock.Turn})", (int)WorkshopContent.TableMode.AsItStands);
        bool setup = ScenarioFiles.StartPath(store, scenarioDir) != null;
        if (setup) opt.AddItem(sc.Id.StartsWith("local.") ? "The setup, as the first turn ended" : "The scenario's start", (int)WorkshopContent.TableMode.Setup);
        opt.AddItem("The map only: no pieces, drawings, briefing or scripts", (int)WorkshopContent.TableMode.MapOnly);
        opt.Selected = opt.GetItemIndex((int)(!inPlay ? WorkshopContent.TableMode.AsItStands : setup ? WorkshopContent.TableMode.Setup : WorkshopContent.TableMode.MapOnly));
        return opt;
    }

    static HBoxContainer Row(string label, OptionButton pick)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(LabelColumn, 0) });
        pick.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(pick);
        return row;
    }

    const int LabelColumn = 48;

    public override void _Ready()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        AddChild(box);
        OptionButton? mode = null;
        OptionButton? savesPick = null;
        OptionButton? tablePick = null;
        if (_kind == "scenario")
        {
            var sc = Json.Load<Scenario>(Path.Combine(_store.Root, "scenarios", _id, "scenario.json"));
            box.AddChild(new Label { Text = $"{sc.Name} uses {sc.Packs.Count} pack{(sc.Packs.Count == 1 ? "" : "s")}. Include them in the file?" });
            mode = PackModePicker(_store, _id, workshop: false);
            box.AddChild(mode);
            box.AddChild(Ui.Hint("Whoever opens the file gets the scenario and every pack inside it. A trimmed pack never replaces a full one they already have."));
            var saves = SaveStore.Default;
            bool inPlay = saves.HasGame(_id);
            box.AddChild(Row("Table", tablePick = TablePicker(_store, sc, _id, inPlay)));
            if (inPlay)
            {
                box.AddChild(Row("Saves", savesPick = SavesPicker(saves, _id)));
                box.AddChild(Ui.Hint("A game is in play here: as it stands now, the file shows whoever opens it every piece where it is, with its notes and the briefing."));
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
                    var table = tablePick == null ? WorkshopContent.TableMode.AsItStands : (WorkshopContent.TableMode)tablePick.GetSelectedId();
                    var bytes = WorkshopContent.ExportFile(_store, _kind, _id, m, f, withSaves, table);
                    var held = table switch { WorkshopContent.TableMode.MapOnly => ": the map only", WorkshopContent.TableMode.Setup => ": the setup", _ => "" };
                    Done?.Invoke($"Saved {Path.GetFileName(f)} ({Mb(bytes)}){held}{withSaves switch { SavesMode.InPlay => " with the save on the table", SavesMode.All => " with every save", _ => "" }}.");
                }
                catch (Exception e) { Done?.Invoke("Export failed: " + e.Message); }
            };
            fd.PopupCentered();
            Ui.FreeOnClose(fd);
        };
    }
}
