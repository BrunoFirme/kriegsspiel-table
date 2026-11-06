using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Updates;
using KTable.Core;

namespace KTable.App;

sealed class UpdateOffer
{
    const string Uninstaller = "unins000.exe";
    const string VersionSetting = "application/config/version";
    internal const string ExportedBuild = "template";
    const double BytesPerMb = 1024 * 1024;
    static readonly string Folder = Path.Combine(Path.GetTempPath(), "KriegsspielTable-update");
    static readonly Lazy<bool> CanReplaceItself = new(() => File.Exists(Path.Combine(ExeDir, Uninstaller)) && Writable(ExeDir));
    static Task<Release?>? _asked;
    static string? _downloaded;

    readonly Main _main;
    readonly VBoxContainer _row = new();
    CancellationTokenSource? _fetching;
    long _got;
    string _failed = "";

    public static bool Applies(IPlatform platform) => OS.HasFeature(ExportedBuild) && !platform.UpdatesItself;

    public static void Put(VBoxContainer at, Main main, Settings cfg)
    {
        var offer = new UpdateOffer(main);
        at.AddChild(offer._row);
        offer._row.TreeExiting += () => offer._fetching?.Cancel();
        if (!cfg.CheckUpdates) return;
        _asked ??= Ask();
        _asked.ContinueWith(_ => Callable.From(offer.Show).CallDeferred(), TaskScheduler.Default);
    }

    UpdateOffer(Main main) { _main = main; }

    static string ExeDir => Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";

    static Version Running => Releases.NumberOf(ProjectSettings.GetSetting(VersionSetting).AsString()) ?? new Version(0, 0, 0);

    static string UserAgent => $"KriegsspielTable/{Running.ToString(3)}";

    static bool Writable(string dir)
    {
        var probe = Path.Combine(dir, $".write-check-{Guid.NewGuid():N}");
        try { File.WriteAllBytes(probe, Array.Empty<byte>()); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally { BestEffort.DeleteFile(probe); }
    }

    static Task<Release?> Ask() => Task.Run(async () =>
    {
        BestEffort.DeleteFolder(Folder);
        try
        {
            using var http = Releases.NewClient(UserAgent);
            var found = await Releases.Check(http, Running, CancellationToken.None);
            GD.Print(found is { } r ? $"[update] {r.Name} is out" : $"[update] {Running.ToString(3)} is the newest version");
            return found;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or IOException)
        {
            GD.PrintErr("[update] checking GitHub for a new version: " + e.Message);
            return null;
        }
    });

    void Show()
    {
        if (!GodotObject.IsInstanceValid(_row) || _fetching != null) return;
        foreach (var c in _row.GetChildren()) c.QueueFree();
        if (_asked is not { IsCompletedSuccessfully: true, Result: { } found }) return;
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", Ui.Gap.Row);
        bool installs = found.Setup != null && CanReplaceItself.Value;
        bool ready = installs && _downloaded != null && File.Exists(_downloaded);
        line.AddChild(new Label { Text = ready ? $"{found.Name} downloaded" : $"{found.Name} is out", Modulate = Ui.AccentInk, VerticalAlignment = VerticalAlignment.Center });
        if (installs)
        {
            line.AddChild(Ui.Btn(ready ? "Install" : "Update", () => Begin(found)));
            var notes = Ui.Btn("What's new", () => OS.ShellOpen(found.Page));
            Ui.MakeQuiet(notes);
            line.AddChild(notes);
        }
        else line.AddChild(Ui.Btn("Download", () => OS.ShellOpen(found.Page)));
        _row.AddChild(line);
        if (_failed.Length > 0) _row.AddChild(Ui.Hint(_failed));
    }

    void Begin(Release found)
    {
        if (_downloaded is { } done && File.Exists(done)) { Install(done); return; }
        if (found.Setup is not { } setup) return;
        _failed = "";
        Interlocked.Exchange(ref _got, 0);
        var stop = new CancellationTokenSource();
        _fetching = stop;
        foreach (var c in _row.GetChildren()) c.QueueFree();
        int totalMb = (int)Math.Ceiling(setup.Size / BytesPerMb);
        _row.AddChild(new LoadingCard(found.Name, TitleStyle.Serif(),
            () => new LoadingCard.Progress("Downloading MB", (int)(Interlocked.Read(ref _got) / BytesPerMb), totalMb), stop.Cancel));
        Task.Run(async () =>
        {
            string? file = null, why = null;
            try
            {
                using var http = Releases.NewClient(UserAgent);
                file = await Releases.Fetch(http, setup, Folder, n => Interlocked.Exchange(ref _got, n), stop.Token);
                GD.Print($"[update] {setup.Name} downloaded and checked");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { GD.Print("[update] download cancelled"); }
            catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException)
            {
                why = e.Message;
                GD.PrintErr($"[update] downloading {setup.Name}: {e.Message}");
            }
            Callable.From(() => Fetched(stop, file, why)).CallDeferred();
        });
    }

    void Fetched(CancellationTokenSource stop, string? file, string? why)
    {
        if (_fetching != stop) return;
        _fetching = null;
        if (file != null) _downloaded = file;
        _failed = why != null ? $"The update could not be downloaded: {why}" : "";
        if (!GodotObject.IsInstanceValid(_row)) return;
        if (file != null && _main.OpeningTitle == null) { Install(file); return; }
        Show();
    }

    static bool RunsFrom(Process p, string exe)
    {
        try { return p.MainModule is { FileName: { } from } && string.Equals(Path.GetFullPath(from), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { return false; }
    }

    static string? HoldingTheFolder()
    {
        if (ConverterTool.Running) return "A conversion is still running: let it finish, then Install.";
        var helper = Path.Combine(ExeDir, "converter", ConverterTool.ExeName);
        if (!File.Exists(helper)) return null;
        foreach (var other in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(helper)))
        {
            using var _ = other;
            if (RunsFrom(other, helper)) return "An AI agent's helper is still running: close the program you connected, then Install.";
        }
        return null;
    }

    void Install(string file)
    {
        if (HoldingTheFolder() is { } held)
        {
            GD.PrintErr("[update] not installing: " + held);
            _failed = held;
            Show();
            return;
        }
        var start = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var arg in new[] { "/SILENT", "/NORESTART", "/RELAUNCH=1", $"/DIR={ExeDir}" }) start.ArgumentList.Add(arg);
        try { using var _ = Process.Start(start); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            GD.PrintErr($"[update] starting {file}: {e.Message}");
            _downloaded = null;
            _failed = $"The installer could not be started: {e.Message}";
            Show();
            return;
        }
        GD.Print("[update] installer started, closing for it");
        _main.QuitApp();
    }
}
