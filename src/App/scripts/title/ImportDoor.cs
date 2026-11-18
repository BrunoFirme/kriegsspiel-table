using System;
using System.Linq;
using KTable.Core;

namespace KTable.App;

static class ImportDoor
{
    public const string Name = "Import", SharedExt = ".ktpack", ModuleExt = ".json";
    public const string Takes = "A digital tabletop module or save (a file, or a Workshop link) or a shared .ktpack file becomes a pack, a scenario or both";

    public static bool IsShared(string source) => source.Trim().EndsWith(SharedExt, StringComparison.OrdinalIgnoreCase);

    public static bool IsModule(string file) => file.Trim().EndsWith(ModuleExt, StringComparison.OrdinalIgnoreCase);

    public static string? Taken(string[] files) => files.FirstOrDefault(IsShared) ?? files.FirstOrDefault(IsModule);

    public static ImportPage Open(Main main, string? source = null, bool keepLog = false)
    {
        main.Title.Select(main.Title.ImportAt);
        var page = new ImportPage(PackageStore.DefaultRoot, (scenario, pack) => Landed(main, scenario, pack), keepLog) { Installs = main.InstallFile, Cfg = main.Cfg };
        main.Title.NewPage(Name, "", wide: true).AddChild(page);
        if (source != null) page.Prefill(source);
        return page;
    }

    public static bool Dropped(Main main, string[] files)
    {
        var shared = files.Where(IsShared).ToList();
        foreach (var f in shared) main.InstallFile(f);
        if (shared.Count > 0) return true;
        if (files.FirstOrDefault(IsModule) is not { } module) return false;
        Open(main, module);
        return true;
    }

    static void Landed(Main main, string scenario, string pack)
    {
        main.RebuildLobby();
        if (scenario.Length > 0) { main.Title.Status.Text = $"Converted '{scenario}': its pack is under Piece studio."; return; }
        main.Title.ShowPacks();
        main.Title.Status.Text = $"Installed the pack {pack}.";
    }
}
