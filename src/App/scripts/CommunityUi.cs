using System;
using System.IO;
using System.Linq;
using Godot;
using KTable.Core.Model;
using Json = KTable.Core.Json;
using KTable.Core;

namespace KTable.App;

public partial class Hud
{
    public void RefreshPacks() => Pieces.RebuildPacks();

    Window.FilesDroppedEventHandler? _onDrop;

    void WireFileDrops()
    {
        GetWindow().FilesDropped += _onDrop = files =>
        {
            foreach (var p in files.Where(f => f.EndsWith(".ktpack", StringComparison.OrdinalIgnoreCase))) InstallPack(p);
        };
    }

    public override void _ExitTree()
    {
        if (_onDrop != null) GetWindow().FilesDropped -= _onDrop;
        _onDrop = null;
    }

    internal void EnsureInGame(string defId)
    {
        var pack = S.Store.PackOfDef(defId);
        if (pack == null || S.View.Packs.Contains(pack.Id)) return;
        if (!S.IsHost && !PackManifest.IsUserPackId(pack.Id)) { Log($"[color=#f87]{Fmt.Bb(pack.Name)} is only on your PC; ask the host to install it.[/color]"); return; }
        Log($"Sharing your pack '{Fmt.Bb(pack.Name)}' with the game…");
        N.SharePack(pack.Id);
    }

    internal void InstallPack(string file)
    {
        var root = S.Store.Root;
        var worked = new System.Collections.Generic.List<(string id, string name, KTable.Core.PackChange change)>();
        worked = BestEffort.Read(() => ImportDialog.WorkedOn(root, WorkshopContent.PacksItWouldPutInPlace(new KTable.Core.PackageStore(root), file)), fallback: worked);
        if (worked.Count > 0)
        {
            ImportDialog.AskBeforeReplacing(this, worked, $"Installing {Path.GetFileName(file)}", "Install anyway", () => InstallNow(file),
                () => Log($"Not installed: {Fmt.Bb(Path.GetFileName(file))}. Nothing was changed."));
            return;
        }
        InstallNow(file);
    }

    void InstallNow(string file)
    {
        if (!IsInstanceValid(this)) return;
        Log($"Installing {Fmt.Bb(Path.GetFileName(file))}…");
        var root = S.Store.Root;
        System.Threading.Tasks.Task.Run(() =>
        {
            WorkshopItemMeta? meta = null;
            string? error = null;
            try { meta = WorkshopContent.ImportFile(new KTable.Core.PackageStore(root), file); }
            catch (Exception e) { error = e.Message; }
            if (!Assets.Quitting) Callable.From(() => PackInstalled(file, meta, error)).CallDeferred();
        });
    }

    void PackInstalled(string file, WorkshopItemMeta? meta, string? error)
    {
        if (!IsInstanceValid(this)) return;
        GD.Print($"[install] {Path.GetFileName(file)}: " + (meta != null ? $"installed {meta.Kind} {meta.Id}" : "refused: " + error));
        try
        {
            if (meta == null) throw new InvalidOperationException(error);
            foreach (var pid in meta.InstalledPacks.Append(meta.Id)) S.Store.Forget(pid);
            if (meta.Trashed.Count > 0)
                Log($"[color=#ffd75e]What you had under the same name went to the trash first, whole ({Fmt.Bb(string.Join(", ", meta.Trashed))}): restore it from the title screen, at the foot of Piece studio or Scenarios, within {KTable.Core.SaveStore.TrashDays} days.[/color]");
            foreach (var w in meta.Warnings) Log($"[color=#ffd75e]{Fmt.Bb(w)}[/color]");
            if (meta.Kind != "pack")
            {
                Log($"Installed the scenario {Fmt.Bb(meta.Name)} and its packs; host it from the title screen.");
                Pieces.RebuildPacks();
                return;
            }
            var id = meta.Id;
            Log($"Installed pack '{Fmt.Bb(S.Store.Pack(id)?.Name ?? id)}'.");
            if (S.IsHost || PackManifest.IsUserPackId(id)) N.SharePack(id);
            Pieces.RebuildPacks();
            Pieces.SelectPack(id);
        }
        catch (Exception e) { Log($"[color=#f87]Could not install {Fmt.Bb(Path.GetFileName(file))}: {Fmt.Bb(e.Message)}[/color]"); }
    }

    internal void FillModuleMenu(PopupMenu pm)
    {
        pm.AddItem("The umpire board…", 201);
        pm.IdPressed += id => { if (id == 201) ExportToModule(); };
    }

    void ExportToModule()
    {
        var tmp = Path.Combine(OS.GetUserDataDir(), "module_export.kstable");
        Json.Save(tmp, new SaveFile { ScenarioId = S.View.ScenarioId, Snapshot = S.View });
        Log("Converting to a digital tabletop save…");
        string? outFile = null;
        ConverterTool.Run(new[] { "export-save", tmp, "--out", S.Store.Root }, line =>
        {
            if (line.StartsWith("EXPORTED ")) outFile = line[9..];
            else if (line.StartsWith("NOTE ")) Log("[color=#ffd75e]" + Fmt.Bb(line[5..]) + "[/color]");
        }, code =>
        {
            if (code == 0 && outFile != null)
            {
                Log($"Saved: {Fmt.Bb(outFile)}\nIt is with your digital tabletop's saves.");
                OS.ShellShowInFileManager(outFile);
            }
            else Log($"[color=#f87]Export failed (exit {code}).[/color]");
        });
    }
}
