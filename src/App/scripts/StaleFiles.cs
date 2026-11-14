using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

static class StaleFiles
{
    static readonly object Gate = new();
    static Task _sweep = Task.CompletedTask;

    static IEnumerable<string> WorkFolders => new[] { Caches.Relief, Path.Combine(Path.GetTempPath(), Captures.GifFolder) };

    public static void Sweep() => Sweep(Caches.ModuleDownloads, WorkFolders, Caches.Display, Caches.FromHosts, SaveStore.Default.Mirrors(), DateTime.UtcNow, PackageStore.DefaultRoot);

    internal static Task Sweep(string downloads, IEnumerable<string> work, string pictures, string fromHosts, IReadOnlyList<string> mirrors, DateTime utcNow, string? maps = null)
    {
        lock (Gate)
        {
            if (_sweep.IsCompleted) _sweep = Task.Run(() => Go(downloads, work, pictures, fromHosts, mirrors, utcNow, maps));
            return _sweep;
        }
    }

    public static void Settle()
    {
        Task sweep;
        lock (Gate) sweep = _sweep;
        sweep.Wait();
    }

    static bool Stop() => ConverterTool.Running || Assets.Quitting;

    static void Go(string downloads, IEnumerable<string> work, string pictures, string fromHosts, IReadOnlyList<string> mirrors, DateTime utcNow, string? maps)
    {
        try
        {
            foreach (var folder in work) Said("work files over a day old", Caches.ClearOlderThan(folder, utcNow - Caches.WorkFilesKept, Stop));
            Said($"cached pictures unused for {Caches.UnusedDays} days", Caches.ClearUnused(pictures, utcNow, Stop));
            Said($"fetched files unused for {Caches.UnusedDays} days", Caches.ClearUnused(fromHosts, utcNow, Stop, () => FetchedFiles.NeededBy(mirrors, fromHosts)));
            Said("conversion downloads unused for a day", Caches.Expire(downloads, utcNow, Caches.ModuleDownloadsKept, Stop));
            if (maps != null) Said(SharedMaps.Join(maps, SharedMaps.RecordBeside(maps), Stop));
        }
        catch (Exception e) { Diagnostics.Write(LogLevel.Warning, "[storage] clearing stale files: " + e.Message); }
    }

    static void Said(Joined maps)
    {
        if (maps.Files > 0) Diagnostics.Write(LogLevel.Info, $"[storage] {maps.Files} map tiles held twice now share one copy, {ContentLimits.Describe(maps.Freed)} freed");
    }

    static void Said(string what, Cleared? gone)
    {
        if (gone is { Freed: > 0 }) Diagnostics.Write(LogLevel.Info, $"[storage] cleared {ContentLimits.Describe(gone.Freed)} of {what}");
    }
}
