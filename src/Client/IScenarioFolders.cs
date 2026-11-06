using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

public interface IScenarioFolders
{
    bool SheetsPending(PackageStore store, GameState g, string dir);
    void SaveTable(PackageStore store, GameState g, string name, string dir, CancellationToken stop = default);
    (string file, Scenario scenario) PrepareTable(PackageStore store, GameState g, string name, string dir);
    void KeepSetup(PackageStore store, string dir, TurnRecord first, string id, string name);
    void Release(string folder);
}
