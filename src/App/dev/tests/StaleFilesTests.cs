using System;
using System.IO;
using System.Threading.Tasks;
using KTable.Client;

namespace KTable.App;

static class StaleFilesTests
{
    static string Scratch() => Path.Combine(Path.GetTempPath(), "ktable-node-stale", Guid.NewGuid().ToString("N"));

    static string Aged(string folder, string name, DateTime written)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, name);
        File.WriteAllBytes(file, new byte[64]);
        File.SetLastWriteTimeUtc(file, written);
        return file;
    }

    [NodeTest]
    static async Task The_sweep_at_start_clears_old_work_files_and_a_conversion_cache_unused_for_a_day(TestRunner r)
    {
        var root = Scratch();
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        string downloads = Path.Combine(root, "downloads"), kept = Path.Combine(root, "kept"), work = Path.Combine(root, "work"), pictures = Path.Combine(root, "pictures");
        var forgotten = Aged(pictures, "forgotten.png", now.AddDays(-31));
        var shown = Aged(pictures, "shown.png", now.AddDays(-29));
        var fetched = Aged(Path.Combine(root, "hosts", "user.old"), "pack.json", now.AddDays(-31));
        var gone = Aged(downloads, "a.png", now.AddDays(-3));
        Caches.Used(downloads, now.AddHours(-25));
        var stays = Aged(kept, "b.png", now.AddDays(-3));
        Caches.Used(kept, now.AddHours(-2));
        var oldWork = Aged(work, "old.glb", now.AddHours(-30));
        var newWork = Aged(work, "new.glb", now.AddHours(-1));

        await StaleFiles.Sweep(downloads, new[] { work }, pictures, Path.Combine(root, "hosts"), Array.Empty<string>(), now);
        StaleFiles.Settle();
        await StaleFiles.Sweep(kept, Array.Empty<string>(), pictures, pictures, Array.Empty<string>(), now);
        await r.Frame();

        Check.That(!File.Exists(gone), "downloads no conversion used for a day are cleared");
        Check.That(File.Exists(stays), "downloads used today stay");
        Check.That(!File.Exists(oldWork) && File.Exists(newWork), "only work files over a day old go");
        Check.That(!File.Exists(forgotten) && File.Exists(shown), "a cached picture goes once unused for thirty days");
        Check.That(!File.Exists(fetched), "and so does a file fetched to join a game no mirror needs");
        Directory.Delete(root, true);
    }
}
