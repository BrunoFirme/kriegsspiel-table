using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client.Tests;

static class TestFolders
{
    static TestFolders()
    {
        var user = Path.Combine(Path.GetTempPath(), "ktable-client-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("KTABLE_USER_DIR", user);
        Environment.SetEnvironmentVariable("KTABLE_PACKAGES", null);
    }

    public static string New()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-client-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

sealed class FakeFolders : IScenarioFolders
{
    public readonly List<string> Calls = new();
    public bool Pending;
    public bool SheetsPending(PackageStore store, GameState g, string dir) { Calls.Add("pending"); return Pending; }
    public void SaveTable(PackageStore store, GameState g, string name, string dir, CancellationToken stop = default) => Calls.Add("save " + name);
    public (string file, Scenario scenario) PrepareTable(PackageStore store, GameState g, string name, string dir)
    {
        Calls.Add("prepare " + name);
        return (Path.Combine(dir, "scenario.json"), new Scenario { Id = "pack.x", Name = name });
    }
    public void KeepSetup(PackageStore store, string dir, TurnRecord first, string id, string name) => Calls.Add("setup");
    public void Release(string folder) => Calls.Add("release " + folder);
}
