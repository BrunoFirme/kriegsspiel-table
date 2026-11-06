using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using KTable.Core;

namespace KTable.Client;

public sealed class Library
{
    public List<string> Folders { get; set; } = new();
    public Dictionary<string, string> Scenarios { get; set; } = new();
    public Dictionary<string, string> Packs { get; set; } = new();

    static string PathOf => Path.Combine(DataPaths.Local, "library.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Library Load()
    {
        if (!File.Exists(PathOf)) return new();
        try
        {
            var lib = JsonSerializer.Deserialize<Library>(File.ReadAllText(PathOf), Opts) ?? throw new JsonException("the file is empty");
            lib.Folders = (lib.Folders ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
            lib.Scenarios ??= new();
            lib.Packs ??= new();
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

    public void Save()
    {
        try { KTable.Core.Json.WriteAtomic(PathOf, JsonSerializer.Serialize(this, Opts)); }
        catch (Exception e) { Problems.TellOnce("library", $"Your folders on the title screen could not be saved ({e.Message}): what you filed lasts until the app closes."); }
    }

    public string FolderOf(bool pack, string id) => (pack ? Packs : Scenarios).TryGetValue(id, out var f) && Folders.Contains(f) ? f : "";

    public void Move(bool pack, string id, string folder)
    {
        var map = pack ? Packs : Scenarios;
        if (folder.Length == 0) map.Remove(id);
        else map[id] = folder;
        Save();
    }

    public void Forget(bool pack, string id) { (pack ? Packs : Scenarios).Remove(id); Save(); }

    public static string CleanName(string name) => name.Trim().Replace('/', '·').Replace('\\', '·');

    public bool AddFolder(string name)
    {
        name = CleanName(name);
        if (name.Length == 0 || Folders.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
        Folders.Add(name);
        Folders.Sort(StringComparer.OrdinalIgnoreCase);
        Save();
        return true;
    }

    public bool RenameFolder(string from, string to)
    {
        to = CleanName(to);
        if (to.Length == 0 || !Folders.Contains(from) || (to != from && Folders.Contains(to, StringComparer.OrdinalIgnoreCase))) return false;
        Folders[Folders.IndexOf(from)] = to;
        foreach (var k in Scenarios.Where(kv => kv.Value == from).Select(kv => kv.Key).ToList()) Scenarios[k] = to;
        foreach (var k in Packs.Where(kv => kv.Value == from).Select(kv => kv.Key).ToList()) Packs[k] = to;
        Folders.Sort(StringComparer.OrdinalIgnoreCase);
        Save();
        return true;
    }

    public void RemoveFolder(string name)
    {
        Folders.Remove(name);
        foreach (var k in Scenarios.Where(kv => kv.Value == name).Select(kv => kv.Key).ToList()) Scenarios.Remove(k);
        foreach (var k in Packs.Where(kv => kv.Value == name).Select(kv => kv.Key).ToList()) Packs.Remove(k);
        Save();
    }

    public IEnumerable<(string folder, List<T> items)> Grouped<T>(IEnumerable<T> items, Func<T, string> idOf, bool pack, string view)
    {
        var list = items.ToList();
        if (view.Length > 0) { yield return (view, list.Where(i => FolderOf(pack, idOf(i)) == view).ToList()); yield break; }
        var unfiled = list.Where(i => FolderOf(pack, idOf(i)) == "").ToList();
        if (unfiled.Count > 0 || Folders.Count == 0) yield return ("", unfiled);
        foreach (var f in Folders)
        {
            var inF = list.Where(i => FolderOf(pack, idOf(i)) == f).ToList();
            if (inF.Count > 0) yield return (f, inF);
        }
    }
}
