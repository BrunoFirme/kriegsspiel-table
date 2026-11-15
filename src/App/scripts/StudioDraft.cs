using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed class StudioDraft
{
    public DateTime Since { get; init; }
    public PackManifest Pack { get; init; } = new();
    public Dictionary<string, string>? Line { get; init; }

    static string Dir => Path.Combine(DataPaths.Local, "studio-drafts");
    static string PathOf(string packId) => Path.Combine(Dir, packId + ".json");
    static string SincePath(string packId) => Path.Combine(Dir, packId + ".since");
    static string LinePath(string packId) => Path.Combine(Dir, packId + ".line.json");

    internal static StudioDraft? Find(string packId)
    {
        if (!File.Exists(PathOf(packId)) || BestEffort.Read(() => Json.Load<PackManifest>(PathOf(packId)), null) is not { } pack) return null;
        var since = BestEffort.Read(() => DateTime.Parse(File.ReadAllText(SincePath(packId)), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), File.GetCreationTimeUtc(PathOf(packId)));
        var line = File.Exists(LinePath(packId)) ? BestEffort.Read(() => Json.Load<Dictionary<string, string>>(LinePath(packId)), null) : null;
        return new StudioDraft { Since = since, Pack = pack, Line = line };
    }

    internal static DateTime WrittenAt(string packId) => File.GetLastWriteTime(PathOf(packId));

    internal static void Keep(string packId, DateTime since, PackManifest draft, Dictionary<string, string>? line = null)
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(SincePath(packId))) Json.WriteAtomic(SincePath(packId), since.ToString("O", CultureInfo.InvariantCulture));
        if (line is { Count: > 0 }) Json.Save(LinePath(packId), line);
        else BestEffort.DeleteFile(LinePath(packId));
        Json.SaveBig(PathOf(packId), draft);
    }

    internal static void Drop(string packId)
    {
        BestEffort.DeleteFile(PathOf(packId));
        BestEffort.DeleteFile(SincePath(packId));
        BestEffort.DeleteFile(LinePath(packId));
    }

    internal static void DropFiles(PackageStore store, PackManifest real, PackManifest draft, DateTime since)
    {
        foreach (var (sha, asset) in draft.Assets)
        {
            if (real.Assets.ContainsKey(sha)) continue;
            DropIfNew(Path.Combine(store.PackageDir(draft.Id), asset.File), since);
            DropIfNew(ArtStore.DocPath(store, draft.Id, sha), since);
        }
    }

    static void DropIfNew(string path, DateTime since)
    {
        if (File.Exists(path) && File.GetCreationTimeUtc(path) >= since.AddSeconds(-1)) BestEffort.DeleteFile(path);
    }
}
