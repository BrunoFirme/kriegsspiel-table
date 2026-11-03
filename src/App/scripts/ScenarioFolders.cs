using System.Threading;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

public sealed class ScenarioFolders : IScenarioFolders
{
    public bool SheetsPending(PackageStore store, GameState g, string dir) => ScenarioFiles.SheetsPending(store, g, dir);

    public void SaveTable(PackageStore store, GameState g, string name, string dir, CancellationToken stop = default) => ScenarioFiles.SaveTable(store, g, name, dir, stop);

    public (string file, Scenario scenario) PrepareTable(PackageStore store, GameState g, string name, string dir)
    {
        var (_, file, sc) = ScenarioFiles.PrepareTable(store, g, name, dir);
        return (file, sc);
    }

    public void KeepSetup(PackageStore store, string dir, TurnRecord first, string id, string name) => ScenarioFiles.KeepSetup(store, dir, first, id, name);

    public void Release(string folder) => InUse.Close(folder);
}
