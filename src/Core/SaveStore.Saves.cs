using System.Globalization;
using KTable.Core.Model;

namespace KTable.Core;

public sealed record TrashedSave(TrashEntry Entry, string Name, string Item, bool Folder);

public sealed partial class SaveStore
{
    public const string SafetyFolder = ".safety", CurrentFile = "current";
    const string SafetyOwnerFile = "of";
    const string SavesSuffix = " (saves)", TrashedLive = "live", TrashedSaves = "saves", TrashedItem = "item", TrashedSafety = "safety", TrashedFrom = "from.json";
    const string MirrorsFolder = "mirrors";

    public string SavesRoot(string scenarioDir) => Path.Combine(LiveDir, scenarioDir);

    public string? ScenarioOf(string savePath)
    {
        string full;
        try { full = Path.GetFullPath(savePath); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        var live = Path.GetFullPath(LiveDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(live, StringComparison.OrdinalIgnoreCase) || !full.EndsWith(Ext, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = full[live.Length..].Split(Path.DirectorySeparatorChar);
        if (parts.Length < 2 || parts[0].Equals(MirrorsFolder, StringComparison.OrdinalIgnoreCase) || parts.Any(p => p.StartsWith('.'))) return null;
        return parts[0];
    }

    public static bool IsHidden(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith('.') && p != ".");
    }

    public IReadOnlyList<string> SaveFiles(string scenarioDir)
    {
        var root = SavesRoot(scenarioDir);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*" + Ext, SearchOption.AllDirectories).Where(f => !IsHidden(root, f)).ToList();
    }

    public string? CurrentSave(string scenarioDir)
    {
        if (MarkedSave(scenarioDir) is { } marked && (File.Exists(marked) || Checkpoints(marked).Count > 0)) return marked;
        if (SaveFiles(scenarioDir).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() is { } newest) return newest;
        var safety = Path.Combine(SavesRoot(scenarioDir), SafetyFolder);
        if (!Directory.Exists(safety)) return null;
        foreach (var d in Directory.GetDirectories(safety).OrderByDescending(Directory.GetLastWriteTimeUtc))
            if (OwnerOf(d) is { } rel && Path.GetFullPath(Path.Combine(SavesRoot(scenarioDir), rel)) is var full && IsUnder(full, SavesRoot(scenarioDir))
                && Directory.EnumerateFiles(d, "*" + Ext).Any()) return full;
        return null;
    }

    static string? OwnerOf(string safetyDir)
    {
        var owner = Path.Combine(safetyDir, SafetyOwnerFile);
        try { return File.Exists(owner) ? File.ReadAllText(owner).Trim() : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static string? OwnedIn(string safetyRoot, string rel)
    {
        if (!Directory.Exists(safetyRoot)) return null;
        foreach (var d in Directory.GetDirectories(safetyRoot))
            if (OwnerOf(d) is { } owner && string.Equals(Path.GetFullPath(Path.Combine("/", owner)), Path.GetFullPath(Path.Combine("/", rel)), StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(d);
        return null;
    }

    string? MarkedSave(string scenarioDir)
    {
        var root = SavesRoot(scenarioDir);
        try
        {
            var f = Path.Combine(root, CurrentFile);
            if (!File.Exists(f)) return null;
            var rel = File.ReadAllText(f).Trim();
            if (!rel.EndsWith(Ext, StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(rel)) return null;
            var full = Path.GetFullPath(Path.Combine(root, rel));
            return IsUnder(full, root) && !IsHidden(root, full) ? full : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public void SetCurrent(string scenarioDir, string savePath)
    {
        CheckInside(scenarioDir, savePath, allowRoot: false);
        Json.WriteAtomic(Path.Combine(SavesRoot(scenarioDir), CurrentFile), Path.GetRelativePath(SavesRoot(scenarioDir), savePath));
    }

    public bool IsCurrent(string scenarioDir, string savePath) => SamePath(MarkedSave(scenarioDir), savePath);

    public string LivePath(string scenarioDir) => CurrentSave(scenarioDir) ?? NewSavePath(scenarioDir, NextGameName(scenarioDir));

    public bool HasGame(string scenarioDir) => CurrentSave(scenarioDir) != null;

    public string NextGameName(string scenarioDir)
    {
        var root = SavesRoot(scenarioDir);
        for (int n = SaveFiles(scenarioDir).Count + 1; ; n++)
        {
            var name = SaveNames.Game(n);
            if (!File.Exists(Path.Combine(root, name + Ext)) && !Directory.Exists(Path.Combine(root, name + Ext))) return name;
        }
    }

    public string NewSavePath(string scenarioDir, string name, string? folder = null)
    {
        var parent = folder ?? SavesRoot(scenarioDir);
        CheckInside(scenarioDir, parent, allowRoot: true);
        return FreeSaveFile(parent, SaveNames.Clean(name, SaveNames.Game(1)));
    }

    public string? IdOf(string savePath)
    {
        if (File.Exists(savePath) && ReadIdOrNull(savePath) is { } id) return id;
        if (ScenarioOf(savePath) is not { } scenarioDir) return null;
        return OwnedIn(Path.Combine(SavesRoot(scenarioDir), SafetyFolder), Path.GetRelativePath(SavesRoot(scenarioDir), savePath));
    }

    public bool IdTakenElsewhere(string savePath, string id)
    {
        if (ScenarioOf(savePath) is not { } scenarioDir) return false;
        if (SaveFiles(scenarioDir).Any(f => !SamePath(f, savePath) && ReadIdOrNull(f) == id)) return true;
        var owner = Path.Combine(SavesRoot(scenarioDir), SafetyFolder, id, SafetyOwnerFile);
        try
        {
            if (!File.Exists(owner)) return false;
            var other = Path.GetFullPath(Path.Combine(SavesRoot(scenarioDir), File.ReadAllText(owner).Trim()));
            return !SamePath(other, savePath) && File.Exists(other);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    string SafetyOf(string scenarioDir, string savePath) =>
        Path.Combine(SavesRoot(scenarioDir), SafetyFolder, IdOf(savePath) ?? "_" + Slug(Path.GetRelativePath(SavesRoot(scenarioDir), savePath)));

    void WriteSafetyOwner(string scenarioDir, string safetyDir, string savePath)
    {
        Directory.CreateDirectory(safetyDir);
        Json.WriteAtomic(Path.Combine(safetyDir, SafetyOwnerFile), Path.GetRelativePath(SavesRoot(scenarioDir), savePath));
    }

    void FollowSaves(string scenarioDir, IReadOnlyList<(string Id, string From)> moved, string fromRoot, string toRoot)
    {
        foreach (var (id, from) in moved)
        {
            var to = Path.Combine(toRoot, Path.GetRelativePath(fromRoot, from));
            var safety = Path.Combine(SavesRoot(scenarioDir), SafetyFolder, id);
            if (Directory.Exists(safety)) WriteSafetyOwner(scenarioDir, safety, to);
            if (IsCurrent(scenarioDir, from)) SetCurrent(scenarioDir, to);
        }
    }

    static List<(string Id, string Path)> SavesIn(string path, string relativeTo, string safetyRoot, string under)
    {
        var files = Directory.Exists(path) ? Directory.EnumerateFiles(path, "*" + Ext, SearchOption.AllDirectories) : File.Exists(path) ? [path] : [];
        return files.Select(f => (Id: ReadIdOrNull(f) ?? OwnedIn(safetyRoot, Path.Combine(under, Path.GetRelativePath(relativeTo, f))), Path: f))
            .Where(s => s.Id != null).Select(s => (s.Id!, s.Path)).ToList();
    }

    List<(string Id, string Path)> SavesWithIds(string path)
    {
        var files = Directory.Exists(path) ? Directory.EnumerateFiles(path, "*" + Ext, SearchOption.AllDirectories) : File.Exists(path) ? [path] : [];
        return files.Select(f => (Id: IdOf(f), Path: f)).Where(s => s.Id != null).Select(s => (s.Id!, s.Path)).ToList();
    }

    static string FreeFolder(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(parent, $"{name} ({n.ToString(CultureInfo.InvariantCulture)})");
        return path;
    }

    static string FreeSaveFile(string folder, string name)
    {
        var path = Path.Combine(folder, name + Ext);
        for (int n = 2; File.Exists(path) || Directory.Exists(path) || File.Exists(SaveLock.PathFor(path)); n++)
            path = Path.Combine(folder, $"{name} ({n.ToString(CultureInfo.InvariantCulture)}){Ext}");
        return path;
    }

    public static string WriteNewSave(string folder, string name, byte[] bytes)
    {
        Directory.CreateDirectory(folder);
        for (int attempt = 0; ; attempt++)
        {
            var path = FreeSaveFile(folder, SaveNames.Clean(name));
            var tmp = Json.WriteTemp(path, bytes, Guid.NewGuid().ToString("N"));
            try { File.Move(tmp, path, overwrite: false); return path; }
            catch (IOException) when (File.Exists(path) && attempt < 100) { Json.Discard(tmp); }
            catch { Json.Discard(tmp); throw; }
        }
    }

    public string SaveInto(string scenarioDir, string folder, string name, byte[] bytes, bool replace, DateTime nowUtc, string? onTable = null)
    {
        CheckInside(scenarioDir, folder, allowRoot: true);
        var target = Path.Combine(folder, SaveNames.Clean(name) + Ext);
        if (File.Exists(target))
        {
            if (!replace) throw new IOException($"a save named {Path.GetFileNameWithoutExtension(target)} is there already");
            var written = WriteNewSave(folder, SaveNames.Clean(name) + " (new)", bytes);
            TrashSaves(scenarioDir, target, nowUtc, onTable);
            File.Move(written, target);
            return target;
        }
        return WriteNewSave(folder, name, bytes);
    }

    public string MakeFolder(string scenarioDir, string parent, string name)
    {
        CheckInside(scenarioDir, parent, allowRoot: true);
        var folder = FreeFolder(parent, SaveNames.Clean(name, "New folder"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public string Rename(string scenarioDir, string path, string name, string? onTable = null)
    {
        CheckInside(scenarioDir, path, allowRoot: false);
        RefuseOnTable(path, onTable);
        var parent = Path.GetDirectoryName(path)!;
        bool folder = Directory.Exists(path);
        var clean = SaveNames.Clean(name, folder ? "Folder" : "Save");
        var wanted = Path.Combine(parent, folder ? clean : clean + Ext);
        if (string.Equals(wanted, path, StringComparison.Ordinal)) return path;
        var saves = SavesWithIds(path);
        string to;
        if (string.Equals(wanted, path, StringComparison.OrdinalIgnoreCase))
        {
            var step = path + "." + Guid.NewGuid().ToString("N");
            Shift(path, step, folder);
            Shift(step, wanted, folder);
            to = wanted;
        }
        else
        {
            to = folder ? FreeFolder(parent, clean) : FreeSaveFile(parent, clean);
            Shift(path, to, folder);
        }
        FollowSaves(scenarioDir, saves, path, to);
        return to;
    }

    static void Shift(string from, string to, bool folder)
    {
        if (folder) Directory.Move(from, to);
        else MoveSaveWithLogs(from, to);
    }

    public string MoveSave(string scenarioDir, string path, string intoFolder, string? onTable = null)
    {
        CheckInside(scenarioDir, path, allowRoot: false);
        CheckInside(scenarioDir, intoFolder, allowRoot: true);
        RefuseOnTable(path, onTable);
        if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(intoFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return path;
        bool folder = Directory.Exists(path);
        if (folder && IsUnder(intoFolder, path)) throw new IOException("a folder cannot go inside itself");
        var saves = SavesWithIds(path);
        var to = folder ? FreeFolder(intoFolder, Path.GetFileName(path)) : FreeSaveFile(intoFolder, Path.GetFileNameWithoutExtension(path));
        Shift(path, to, folder);
        FollowSaves(scenarioDir, saves, path, to);
        return to;
    }

    static void RefuseOnTable(string path, string? onTable)
    {
        if (onTable != null && (SamePath(path, onTable) || IsUnder(onTable, path))) throw new IOException("the game on the table now is saved in it");
        RefuseTaken(path);
    }

    static void RefuseTaken(string path)
    {
        var saves = Directory.Exists(path) ? Directory.EnumerateFiles(path, "*" + Ext, SearchOption.AllDirectories) : [path];
        if (saves.FirstOrDefault(SaveLock.IsTaken) is { } taken) throw SaveLock.InUse(taken);
    }

    public string TrashSaves(string scenarioDir, string path, DateTime nowUtc, string? onTable = null)
    {
        CheckInside(scenarioDir, path, allowRoot: false);
        RefuseOnTable(path, onTable);
        var root = SavesRoot(scenarioDir);
        var saves = SavesWithIds(path);
        var name = Path.GetFileName(path);
        var entry = NewTrashEntry($"{scenarioDir} · {Path.GetFileNameWithoutExtension(name)}{SavesSuffix}", nowUtc);
        Directory.CreateDirectory(entry);
        Json.WriteAtomic(Path.Combine(entry, TrashedFrom), Json.Serialize(new SavesInTrash
        {
            Scenario = scenarioDir, Parent = Path.GetRelativePath(root, Path.GetDirectoryName(path)!), Name = name,
        }));
        var item = Path.Combine(entry, TrashedItem, name);
        Directory.CreateDirectory(Path.GetDirectoryName(item)!);
        if (Directory.Exists(path)) MoveInto(path, item);
        else MoveSaveWithLogs(path, item);
        foreach (var (id, _) in saves) MoveInto(Path.Combine(root, SafetyFolder, id), Path.Combine(entry, TrashedSafety, id));
        return entry;
    }

    public IReadOnlyList<TrashedSave> SavesTrash(string scenarioDir)
    {
        var list = new List<TrashedSave>();
        foreach (var e in TrashEntries().Where(e => e.Kind == TrashKind.Saves))
        {
            SavesInTrash from;
            try { from = Json.Load<SavesInTrash>(Path.Combine(e.Path, TrashedFrom)); }
            catch (Exception ex) when (IsDamage(ex) || ex is IOException or UnauthorizedAccessException) { continue; }
            if (!string.Equals(from.Scenario, scenarioDir, StringComparison.OrdinalIgnoreCase)) continue;
            var item = Path.Combine(e.Path, TrashedItem, from.Name);
            bool folder = Directory.Exists(item);
            if (!folder && !File.Exists(item)) continue;
            list.Add(new TrashedSave(e, folder ? from.Name : Path.GetFileNameWithoutExtension(from.Name), item, folder));
        }
        return list;
    }

    string RestoreSaves(TrashEntry e, DateTime nowUtc)
    {
        var from = Json.Load<SavesInTrash>(Path.Combine(e.Path, TrashedFrom));
        if (from.Scenario.Length == 0 || from.Scenario.Contains(Path.DirectorySeparatorChar) || from.Scenario.Contains('/')) throw new IOException("this entry does not say where it came from");
        if (IsLegacyEntry(e.Path)) return RestoreLegacySaves(e, from, nowUtc);
        var root = SavesRoot(from.Scenario);
        var parent = Path.GetFullPath(Path.Combine(root, from.Parent));
        if (!IsUnder(parent, root) || IsHidden(root, parent)) parent = root;
        Directory.CreateDirectory(parent);
        var safety = Path.Combine(e.Path, TrashedSafety);
        if (Directory.Exists(safety))
            foreach (var d in Directory.GetDirectories(safety)) MoveTree(d, Path.Combine(root, SafetyFolder, Path.GetFileName(d)));
        var itemDir = Path.Combine(e.Path, TrashedItem);
        string? back = null;
        if (Directory.Exists(itemDir))
            foreach (var item in Directory.EnumerateFileSystemEntries(itemDir).Where(i => !IsLogFile(i)).ToList())
            {
                var saves = SavesIn(item, itemDir, Path.Combine(root, SafetyFolder), from.Parent);
                if (Directory.Exists(item)) { back = FreeFolder(parent, Path.GetFileName(item)); MoveInto(item, back); }
                else { back = FreeSaveFile(parent, Path.GetFileNameWithoutExtension(item)); MoveSaveWithLogs(item, back); }
                FollowSaves(from.Scenario, saves, item, back);
            }
        Directory.Delete(e.Path, true);
        return $"{Path.GetFileNameWithoutExtension(back ?? from.Name)} is back among the saves of {from.Scenario}.";
    }

    static bool IsLogFile(string path) =>
        path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".log.old", StringComparison.OrdinalIgnoreCase);

    public string AddGame(string scenarioDir, string saveFile, string name, string? where = null)
    {
        var root = SavesRoot(scenarioDir);
        string target;
        if (where is { } rel && rel.EndsWith(Ext, StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(rel)
            && Path.GetFullPath(Path.Combine(root, rel)) is var full && IsUnder(full, root) && !IsHidden(root, full))
            target = FreeSaveFile(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full));
        else target = FreeSaveFile(root, SaveNames.Clean(name, SaveNames.Game(1)));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Json.CopyAtomic(saveFile, target, overwrite: false);
        SetCurrent(scenarioDir, target);
        return target;
    }

    public void AddSaves(string scenarioDir, string savesFrom)
    {
        if (!Directory.Exists(savesFrom)) return;
        var root = SavesRoot(scenarioDir);
        foreach (var f in Directory.EnumerateFiles(savesFrom, "*" + Ext, SearchOption.AllDirectories))
        {
            if (IsHidden(savesFrom, f)) continue;
            var full = Path.GetFullPath(Path.Combine(root, Path.GetRelativePath(savesFrom, f)));
            if (!IsUnder(full, root)) continue;
            var to = FreeSaveFile(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Json.CopyAtomic(f, to, overwrite: false);
        }
    }

    void CheckInside(string scenarioDir, string path, bool allowRoot)
    {
        var root = Path.GetFullPath(SavesRoot(scenarioDir)).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        bool isRoot = string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        if (isRoot ? !allowRoot : !IsUnder(full, root) || IsHidden(root, full)) throw new IOException($"{path} is not among the saves of {scenarioDir}");
    }

    static bool IsUnder(string path, string folder)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    static void MoveFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        try { File.Move(from, to, overwrite: false); }
        catch (IOException e) when (e.HResult == ErrorNotSameDevice) { Json.CopyAtomic(from, to, overwrite: false); File.Delete(from); }
    }
}
