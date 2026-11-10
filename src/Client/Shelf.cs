using System;
using System.Collections.Generic;
using System.Linq;

namespace KTable.Client;

public sealed record ShelfEntry(string Key, bool Folder)
{
    public string Path => Folder ? Shelf.PathOf(Key) : "";
    public string Name => Folder ? Shelf.NameOf(Path) : Key;
}

public sealed class Shelf
{
    const char Apart = '/';
    const int Last = int.MaxValue;

    public List<string> Folders { get; set; } = new();
    public Dictionary<string, string> Filed { get; set; } = new();
    public Dictionary<string, List<string>> Order { get; set; } = new();

    public const string TrashKey = ":trash";

    public static bool IsFixed(string key) => key.StartsWith(':');
    public static string KeyOf(string folder) => Apart + folder;
    public static bool IsFolderKey(string key) => key.Length > 0 && key[0] == Apart;
    public static string PathOf(string key) => key[1..];
    public static string NameOf(string folder) => folder[(folder.LastIndexOf(Apart) + 1)..];
    public static string ParentOf(string folder) => folder.LastIndexOf(Apart) is var i and >= 0 ? folder[..i] : "";
    public static string Join(string parent, string name) => parent.Length == 0 ? name : parent + Apart + name;
    public static IEnumerable<string> Steps(string folder) => folder.Length == 0 ? Array.Empty<string>() : folder.Split(Apart);
    static bool Within(string folder, string root) => root.Length > 0 && (folder == root || folder.StartsWith(root + Apart, StringComparison.Ordinal));
    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public void Mend()
    {
        Folders = (Folders ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct().ToList();
        foreach (var f in Folders.ToList())
            for (var p = ParentOf(f); p.Length > 0; p = ParentOf(p))
                if (!Folders.Contains(p)) Folders.Add(p);
        Filed ??= new();
        Order ??= new();
        foreach (var k in Order.Keys.ToList()) Order[k] ??= new();
    }

    public bool Has(string folder) => folder.Length == 0 || Folders.Contains(folder);

    public string FolderOf(string id) => Filed.TryGetValue(id, out var f) && Folders.Contains(f) ? f : "";

    public IEnumerable<string> SubFolders(string parent) => Folders.Where(f => ParentOf(f) == parent);

    public List<ShelfEntry> Arranged(string folder, IReadOnlyList<string> present)
    {
        var here = SubFolders(folder).OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase).Select(f => new ShelfEntry(KeyOf(f), true))
            .Concat(present.Distinct().Where(id => FolderOf(id) == folder).Select(id => new ShelfEntry(id, false))).ToList();
        if (!Order.TryGetValue(folder, out var order) || order.Count == 0) return here;
        var rank = new Dictionary<string, int>();
        for (int i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);
        return here.OrderBy(e => rank.TryGetValue(e.Key, out var r) ? r : Last).ToList();
    }

    public List<string> Inside(string folder, IReadOnlyList<string> present, int most)
    {
        var found = new List<string>();
        void Walk(string f)
        {
            foreach (var e in Arranged(f, present))
            {
                if (found.Count >= most) return;
                if (e.Folder) Walk(e.Path);
                else if (!IsFixed(e.Key)) found.Add(e.Key);
            }
        }
        Walk(folder);
        return found;
    }

    public int CountInside(string folder, IReadOnlyList<string> present) => present.Distinct().Count(id => Within(FolderOf(id), folder));

    public List<string> Flattened(IReadOnlyList<string> present) => Inside("", present, Last);

    public string? AddFolder(string parent, string name)
    {
        name = Library.CleanName(name);
        if (name.Length == 0 || !Has(parent)) return null;
        var path = Join(parent, name);
        if (Folders.Any(f => Same(f, path))) return null;
        Folders.Add(path);
        if (Order.TryGetValue(parent, out var order)) order.Insert(0, KeyOf(path));
        else Order[parent] = new List<string> { KeyOf(path) };
        return path;
    }

    public bool RenameFolder(string folder, string name)
    {
        name = Library.CleanName(name);
        if (name.Length == 0 || !Folders.Contains(folder)) return false;
        var to = Join(ParentOf(folder), name);
        if (to == folder) return true;
        if (Folders.Any(f => f != folder && Same(f, to))) return false;
        Repath(folder, to);
        return true;
    }

    public void Drop(string folder, IReadOnlyList<string> present, IReadOnlyList<string> keys, int slot)
    {
        if (!Has(folder)) return;
        var moving = keys.Distinct().Where(k => IsFixed(k) ? folder.Length == 0 : !IsFolderKey(k) || Folders.Contains(PathOf(k)) && !Within(folder, PathOf(k))).ToList();
        if (moving.Count == 0) return;
        var now = Arranged(folder, present).Select(e => e.Key).ToList();
        int cut = Math.Clamp(slot, 0, now.Count);
        int index = cut - now.Take(cut).Count(moving.Contains);
        var landed = new List<string>();
        foreach (var k in moving)
        {
            if (IsFolderKey(k))
            {
                var p = PathOf(k);
                if (ParentOf(p) == folder) { landed.Add(k); continue; }
                Unlist(k);
                var to = Free(folder, NameOf(p));
                Repath(p, to);
                landed.Add(KeyOf(to));
                continue;
            }
            if (FolderOf(k) != folder)
            {
                Unlist(k);
                if (folder.Length == 0) Filed.Remove(k);
                else Filed[k] = folder;
            }
            landed.Add(k);
        }
        var rest = now.Where(k => !moving.Contains(k)).ToList();
        rest.InsertRange(Math.Clamp(index, 0, rest.Count), landed);
        Order[folder] = rest;
    }

    public void RemoveFolder(string folder, IReadOnlyList<string> present)
    {
        if (!Folders.Contains(folder)) return;
        var parent = ParentOf(folder);
        var inside = Arranged(folder, present).Select(e => e.Key).ToList();
        int at = Arranged(parent, present).FindIndex(e => e.Key == KeyOf(folder));
        Folders.Remove(folder);
        Unlist(KeyOf(folder));
        Drop(parent, present, inside, at < 0 ? Last : at);
        Folders.RemoveAll(f => Within(f, folder));
        foreach (var k in Order.Keys.Where(k => Within(k, folder)).ToList()) Order.Remove(k);
        foreach (var id in Filed.Where(kv => Within(kv.Value, folder)).Select(kv => kv.Key).ToList())
        {
            if (parent.Length == 0) Filed.Remove(id);
            else Filed[id] = parent;
        }
    }

    public void Forget(string id)
    {
        Filed.Remove(id);
        Unlist(id);
    }

    string Free(string parent, string name)
    {
        var path = Join(parent, name);
        for (int n = 2; Folders.Any(f => Same(f, path)); n++) path = Join(parent, $"{name} {n}");
        return path;
    }

    void Unlist(string key)
    {
        foreach (var order in Order.Values) order.RemoveAll(k => k == key);
    }

    void Repath(string from, string to)
    {
        string Re(string f) => Within(f, from) ? to + f[from.Length..] : f;
        string ReKey(string k) => IsFolderKey(k) ? KeyOf(Re(PathOf(k))) : k;
        Folders = Folders.Select(Re).Distinct().ToList();
        foreach (var id in Filed.Keys.ToList()) Filed[id] = Re(Filed[id]);
        var order = new Dictionary<string, List<string>>();
        foreach (var (k, list) in Order)
        {
            var moved = list.Select(ReKey).ToList();
            if (order.TryGetValue(Re(k), out var had)) had.AddRange(moved.Where(m => !had.Contains(m)));
            else order[Re(k)] = moved;
        }
        Order = order;
    }
}
