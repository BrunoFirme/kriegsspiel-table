using KTable.Core;

namespace KTable.Client;

public static class TrashShelves
{
    public static bool IsPackEntry(TrashEntry e) => e.Kind is TrashKind.Pack or TrashKind.PackFiles;

    public static List<TrashEntry> Entries(SaveStore saves, bool packs) => saves.TrashEntries().Where(e => IsPackEntry(e) == packs).ToList();

    public static string? SetsGameAside(SaveStore saves, TrashEntry e)
    {
        var mirror = Path.Combine(saves.MirrorDir, e.Id + SaveStore.Ext);
        return e.Kind switch
        {
            TrashKind.Mirror when File.Exists(mirror) || Directory.Exists(saves.CheckpointDir(mirror)) => $"The mirror of {e.Id} on this PC now goes to the trash in its place, for {SaveStore.TrashDays} days.",
            _ => null,
        };
    }
}
