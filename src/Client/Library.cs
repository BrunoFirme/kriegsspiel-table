using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using KTable.Core;

namespace KTable.Client;

public sealed class Library
{
    public Shelf ScenarioShelf { get; set; } = new();
    public Shelf PackShelf { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Folders { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Scenarios { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Packs { get; set; }

    static string PathOf => Path.Combine(DataPaths.Local, "library.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    static Library? _current;
    public static Library Current => _current ??= Load();

    public static Library Load()
    {
        if (!File.Exists(PathOf)) return new();
        try
        {
            var lib = JsonSerializer.Deserialize<Library>(File.ReadAllText(PathOf), Opts) ?? throw new JsonException("the file is empty");
            lib.ScenarioShelf ??= new();
            lib.PackShelf ??= new();
            lib.TakeOldFolders();
            lib.ScenarioShelf.Mend();
            lib.PackShelf.Mend();
            return lib;
        }
        catch (Exception e)
        {
            string kept;
            try { File.Copy(PathOf, $"{PathOf}.bad-{DateTime.UtcNow:yyyyMMdd-HHmmss}", overwrite: true); kept = "a copy of it is kept beside it"; }
            catch (Exception c) { kept = $"no copy of it could be kept ({c.Message})"; }
            Problems.Tell($"Your folders on the title screen (library.json) could not be read ({e.Message}), so they start empty; {kept}.");
            return new();
        }
    }

    void TakeOldFolders()
    {
        if (Folders == null && Scenarios == null && Packs == null) return;
        var folders = (Folders ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).Select(CleanName).Distinct().ToList();
        var scenarios = Scenarios ?? new();
        var packs = Packs ?? new();
        void Into(Shelf shelf, Dictionary<string, string> filed, Dictionary<string, string> other)
        {
            foreach (var f in folders)
                if ((filed.ContainsValue(f) || !other.ContainsValue(f)) && !shelf.Folders.Contains(f)) shelf.Folders.Add(f);
            foreach (var (id, f) in filed)
                if (folders.Contains(f)) shelf.Filed.TryAdd(id, f);
        }
        Into(ScenarioShelf, scenarios, packs);
        Into(PackShelf, packs, scenarios);
        Folders = null;
        Scenarios = null;
        Packs = null;
    }

    public void Save()
    {
        try { KTable.Core.Json.WriteAtomic(PathOf, JsonSerializer.Serialize(this, Opts)); }
        catch (Exception e) { Problems.TellOnce("library", $"Your folders on the title screen could not be saved ({e.Message}): what you filed lasts until the app closes."); }
    }

    public Shelf Of(bool pack) => pack ? PackShelf : ScenarioShelf;

    public static string CleanName(string name) => name.Trim().Replace('/', '·').Replace('\\', '·');

    public string FolderOf(bool pack, string id) => Of(pack).FolderOf(id);

    public List<ShelfEntry> Arranged(bool pack, string folder, IReadOnlyList<string> present) => Of(pack).Arranged(folder, present);

    public List<string> Ordered(bool pack, IEnumerable<string> ids) => Of(pack).Flattened(ids.ToList());

    public string? AddFolder(bool pack, string parent, string name) => Saved(Of(pack).AddFolder(parent, name));

    public bool RenameFolder(bool pack, string folder, string name) => Saved(Of(pack).RenameFolder(folder, name));

    public void RemoveFolder(bool pack, string folder, IReadOnlyList<string> present)
    {
        Of(pack).RemoveFolder(folder, present);
        Save();
    }

    public void Drop(bool pack, string folder, IReadOnlyList<string> present, IReadOnlyList<string> keys, int slot)
    {
        Of(pack).Drop(folder, present, keys, slot);
        Save();
    }

    public void MoveInto(bool pack, string folder, IReadOnlyList<string> present, IReadOnlyList<string> keys) => Drop(pack, folder, present, keys, int.MaxValue);

    public void Forget(bool pack, string id)
    {
        Of(pack).Forget(id);
        Save();
    }

    T Saved<T>(T result)
    {
        Save();
        return result;
    }
}
