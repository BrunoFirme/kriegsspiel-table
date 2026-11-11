using System;
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

    static string Dir => Path.Combine(DataPaths.Local, "studio-drafts");
    static string PathOf(string packId) => Path.Combine(Dir, packId + ".json");
    static string SincePath(string packId) => Path.Combine(Dir, packId + ".since");

    internal static StudioDraft? Find(string packId)
    {
        if (!File.Exists(PathOf(packId)) || BestEffort.Read(() => Json.Load<PackManifest>(PathOf(packId)), null) is not { } pack) return null;
        var since = BestEffort.Read(() => DateTime.Parse(File.ReadAllText(SincePath(packId)), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), File.GetCreationTimeUtc(PathOf(packId)));
        return new StudioDraft { Since = since, Pack = pack };
    }

    internal static DateTime WrittenAt(string packId) => File.GetLastWriteTime(PathOf(packId));

    internal static void Keep(string packId, DateTime since, PackManifest draft)
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(SincePath(packId))) Json.WriteAtomic(SincePath(packId), since.ToString("O", CultureInfo.InvariantCulture));
        Json.SaveBig(PathOf(packId), draft);
    }

    internal static void Drop(string packId)
    {
        BestEffort.DeleteFile(PathOf(packId));
        BestEffort.DeleteFile(SincePath(packId));
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
