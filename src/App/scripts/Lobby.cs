using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public partial class Main
{
    bool _wsHooked;

    internal void BuildLobby()
    {
        Title.Build();
        Dev.TitleShown(Title);
        if (_platform.Available)
        {
            if (!_wsHooked)
            {
                _wsHooked = true;
                _platform.Changed += m => { if (Title.Status != null && IsInstanceValid(Title.Status)) Title.Status.Text = m; };
            }
            if (_platform.SyncInstalled() > 0) { Callable.From(RebuildLobby).CallDeferred(); }
        }
    }

    internal void RebuildLobby()
    {
        Title.Close();
        BuildLobby();
    }

    internal void PutOpeningIn(Control slot)
    {
        _loadSlot = slot;
        if (_openingNow is { } opening) ShowCard(opening);
    }

    internal static (TurnRecord? setup, KTable.Server.GameServer? keep, string text) StartOverPlan(PackageStore store, string dir)
    {
        bool local = (Json.PeekString(Path.Combine(store.Root, "scenarios", dir, "scenario.json"), "id") ?? "local.").StartsWith("local.");
        if (ScenarioFiles.StartPath(store, dir) != null)
            return (null, null, local ? "The table goes back to its setup, as it stood when the first turn was ended." : "The table goes back to the scenario's start.");
        var game = local ? ScenarioFiles.GameInPlay(Saves, dir) : null;
        if (game != null && ScenarioFiles.FirstTurn(game) is { } first)
            return (first, null, $"The table goes back to its setup, as it stood when turn {first.Turn} was ended.");
        return (null, game, local
            ? "No turn of this game has been ended, so there is no setup to go back to: the pieces stay where they are."
            : "This scenario's start was not kept, so the pieces stay where they are.");
    }

    internal void StartOver(PackageStore store, string dir, TurnRecord? setup, KTable.Server.GameServer? keep, Action<string> say)
    {
        CancelOpening();
        if (InUse.BeingReplaced(InUse.Scenario(store.Root, dir)) is { } busy) { say($"The scenario {dir} is {busy} right now, so nothing was changed: start over once that has finished."); return; }
        if (setup != null)
            try { ScenarioFiles.KeepSetup(store, dir, setup); }
            catch (Exception e) { say("Could not keep the setup, so nothing was changed: " + e.Message); return; }
        if (keep != null)
            try { ScenarioFiles.WriteFromGame(store, dir, keep); }
            catch (Exception e) { say("Could not write the table as the game left it, so nothing was changed: " + e.Message); return; }
        if (ScenarioFiles.StartPath(store, dir) is { } start)
        {
            var file = Path.Combine(store.Root, "scenarios", dir, "scenario.json");
            if (ScenarioFiles.TryLoad(start) == null) { say($"The scenario's start ({start}) cannot be read, so nothing was changed."); return; }
            try { Json.CopyAtomic(start, file); }
            catch (Exception e) { say("Could not put the start back, so nothing was changed: " + e.Message); return; }
        }
        say("Hosting from the start.");
        DoHost(dir, resume: false);
    }

    internal async void DoHost(string dir, bool resume)
    {
        CancelOpening();
        _cfg.Save();
        bool mirror = dir.EndsWith(SaveStore.Ext, StringComparison.OrdinalIgnoreCase);
        string? password = resume || mirror ? null : "";
        if (mirror) { StartHost(dir, HostName, resume: true, password); return; }
        var store = new PackageStore(PackageStore.DefaultRoot);
        var path = Path.Combine(store.Root, "scenarios", dir, "scenario.json");
        var op = BeginOpening(Json.PeekString(path, "name") ?? dir);
        try
        {
            var sc = await System.Threading.Tasks.Task.Run(() => File.Exists(path) ? ScenarioFiles.TryLoad(path) : null);
            if (GivenUp(op)) { EndOpening(op); return; }
            if (sc == null && resume && File.Exists(path)) KeepDamagedScenario(store, dir);
            if (sc != null && WorkshopContent.MissingPacks(store, sc) is { Count: > 0 } missing)
            {
                EndOpening(op);
                Ui.Confirm(this, "Packs missing", "Host anyway", () => StartHost(dir, HostName, resume, password, sc), string.Join('\n', missing.Select(m => "·  " + m)));
                return;
            }
            StartHost(dir, HostName, resume, password, sc, op);
        }
        catch (Exception e)
        {
            EndOpening(op);
            if (IsInstanceValid(this) && !Assets.Quitting) Fail("Cannot open: " + e.Message);
        }
    }

    static void KeepDamagedScenario(PackageStore store, string dir)
    {
        var file = Path.Combine(store.Root, "scenarios", dir, "scenario.json");
        try
        {
            File.Copy(file, $"{file}.bad-{DateTime.UtcNow:yyyyMMdd-HHmmss}", overwrite: true);
            if (ScenarioFiles.StartPath(store, dir) is { } start && ScenarioFiles.TryLoad(start) != null) File.Copy(start, file, overwrite: true);
        }
        catch (Exception e) { GD.PushWarning($"could not keep the damaged scenario file of {dir} aside: {e.Message}"); }
    }

    string HostName => Title.HostName;

    public static SaveStore Saves => SaveStore.Default;

    public static string AutosavePath(string scenarioDir) => Saves.LivePath(scenarioDir);

    internal void InstallFile(string f)
    {
        var worked = new List<(string id, string name, PackChange change)>();
        worked = BestEffort.Read(() => ImportDialog.WorkedOn(PackageStore.DefaultRoot, WorkshopContent.PacksItWouldPutInPlace(new PackageStore(PackageStore.DefaultRoot), f)), fallback: worked);
        if (worked.Count > 0)
        {
            ImportDialog.AskBeforeReplacing(this, worked, $"Installing {Path.GetFileName(f)}", "Install anyway", () => InstallNow(f),
                () => { if (Title.Root != null && IsInstanceValid(Title.Status)) Title.Status.Text = $"Not installed: {Path.GetFileName(f)}. Nothing was changed."; });
            return;
        }
        InstallNow(f);
    }

    void InstallNow(string f)
    {
        if (Title.Root == null) return;
        Title.Status.Text = $"Installing {Path.GetFileName(f)}…";
        System.Threading.Tasks.Task.Run(() =>
        {
            WorkshopItemMeta? m = null;
            string? error = null;
            try { m = WorkshopContent.ImportFile(new PackageStore(PackageStore.DefaultRoot), f); }
            catch (Exception e) { error = e.Message; }
            if (!Assets.Quitting) Callable.From(() => InstallDone(m, error)).CallDeferred();
        });
    }

    void InstallDone(WorkshopItemMeta? m, string? error)
    {
        if (m == null) { if (Title.Root != null && IsInstanceValid(Title.Status)) Title.Status.Text = "Could not install: " + error; return; }
        try
        {
            var msg = m.Kind == "scenario"
                ? $"Installed the scenario {m.Name}" + (m.InstalledPacks.Count > 0 ? $" and {m.InstalledPacks.Count} pack{(m.InstalledPacks.Count == 1 ? "" : "s")}." : ".")
                : $"Installed the pack {m.Name}.";
            if (m.InstalledGame != null)
                msg += $" It brought its saves{(m.InstalledGame.Length > 0 ? $" ({m.InstalledGame})" : "")}: Resume continues its game with the same password and the people already let in. The saves that were here stay.";
            if (m.Trashed.Count > 0)
                msg += $" What you had under the same name went to the trash first, whole ({string.Join(", ", m.Trashed)}): restore it from the foot of Piece studio or Host a game within {SaveStore.TrashDays} days.";
            foreach (var w in m.Warnings) msg += " " + w;
            if (Title.Root == null) { GD.Print("[install] " + msg); return; }
            RebuildLobby();
            Title.Status.Text = msg;
        }
        catch (Exception e) { if (Title.Root != null && IsInstanceValid(Title.Status)) Title.Status.Text = "Could not install: " + e.Message; }
    }
}
