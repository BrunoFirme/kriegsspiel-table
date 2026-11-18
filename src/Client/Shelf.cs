using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KTable.Client;

public sealed record ShelfEntry(string Key, bool Folder)
{
    public string Path => Folder ? Shelf.PathOf(Key) : "";
    public string Name => Folder ? Shelf.NameOf(Path) : Key;
}

public enum ShelfOrder { Yours, Opened, Name, Newest, Oldest }

public sealed record ShelfSort(ShelfOrder Order, Func<string, string> Name, Func<string, DateTime?> Played)
{
    public static readonly ShelfSort Yours = new(ShelfOrder.Yours, id => id, _ => null);
    public bool ByHand => Order == ShelfOrder.Yours;
}

public sealed class Shelf
{
    const char Apart = '/';
    const int Last = int.MaxValue;
    const string StampFormat = "o";
    const DateTimeStyles StampRead = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
    static readonly StringComparer Names = StringComparer.CurrentCultureIgnoreCase;

    public List<string> Folders { get; set; } = new();
    public Dictionary<string, string> Filed { get; set; } = new();
    public Dictionary<string, List<string>> Order { get; set; } = new();
    public Dictionary<string, string> Opened { get; set; } = new();
    public Dictionary<string, string> Added { get; set; } = new();

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
        Opened ??= new();
        Added ??= new();
        foreach (var k in Order.Keys.ToList()) Order[k] ??= new();
    }

    public bool Has(string folder) => folder.Length == 0 || Folders.Contains(folder);

    public string FolderOf(string id) => Filed.TryGetValue(id, out var f) && Folders.Contains(f) ? f : "";

    public IEnumerable<string> SubFolders(string parent) => Folders.Where(f => ParentOf(f) == parent);

    public List<ShelfEntry> Arranged(string folder, IReadOnlyList<string> present)
    {
        var here = SubFolders(folder).OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase).Select(f => new ShelfEntry(KeyOf(f), true))
            .Concat(present.Distinct().Where(id => FolderOf(id) == folder).Select(id => new ShelfEntry(id, false))).ToList();
        var rank = new Dictionary<string, int>();
        if (Order.TryGetValue(folder, out var order))
            for (int i = 0; i < order.Count; i++) rank.TryAdd(order[i], i);
        return here.OrderBy(e => e.Key == TrashKey).ThenBy(e => rank.TryGetValue(e.Key, out var r) ? r : Last).ToList();
    }

    public List<ShelfEntry> Arranged(string folder, IReadOnlyList<string> present, ShelfSort sort)
    {
        if (sort.ByHand) return Arranged(folder, present);
        var here = present.Distinct().Where(id => FolderOf(id) == folder).ToList();
        return SubFolders(folder).OrderBy(NameOf, Names).Select(f => new ShelfEntry(KeyOf(f), true))
            .Concat(Sorted(here.Where(id => !IsFixed(id)), sort).Select(id => new ShelfEntry(id, false)))
            .Concat(here.Where(IsFixed).Select(id => new ShelfEntry(id, false))).ToList();
    }

    public List<string> Ordered(IReadOnlyList<string> present, IEnumerable<string> ids, ShelfSort sort)
    {
        var wanted = ids.Where(id => !IsFixed(id)).Distinct().ToList();
        if (!sort.ByHand) return Sorted(wanted, sort);
        var rank = new Dictionary<string, int>();
        var flat = Flattened(present);
        for (int i = 0; i < flat.Count; i++) rank.TryAdd(flat[i], i);
        return wanted.OrderBy(id => rank.TryGetValue(id, out var r) ? r : Last).ToList();
    }

    List<string> Sorted(IEnumerable<string> ids, ShelfSort sort) => (sort.Order switch
    {
        ShelfOrder.Opened => ids.OrderByDescending(id => LastOpened(id, sort)).ThenByDescending(AddedAt),
        ShelfOrder.Newest => ids.OrderByDescending(AddedAt),
        ShelfOrder.Oldest => ids.OrderBy(AddedAt),
        _ => ids.OrderBy(sort.Name, Names),
    }).ThenBy(sort.Name, Names).ThenBy(id => id, StringComparer.Ordinal).ToList();

    DateTime LastOpened(string id, ShelfSort sort)
    {
        var stamped = Stamp(Opened, id) ?? DateTime.MinValue;
        var played = sort.Played(id) ?? DateTime.MinValue;
        return stamped > played ? stamped : played;
    }

    DateTime AddedAt(string id) => Stamp(Added, id) ?? DateTime.MinValue;

    static DateTime? Stamp(Dictionary<string, string> stamps, string id) =>
        stamps.TryGetValue(id, out var s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, StampRead, out var at) ? at : null;

    static string Stamped(DateTime at) => at.ToUniversalTime().ToString(StampFormat, CultureInfo.InvariantCulture);

    public void NoteOpened(string id, DateTime at) => Opened[id] = Stamped(at);

    public bool NoteArrivals(IEnumerable<string> present, Func<string, DateTime> arrived)
    {
        bool any = false;
        foreach (var id in present.Where(id => !IsFixed(id) && !Added.ContainsKey(id)).Distinct().ToList())
        {
            Added[id] = Stamped(arrived(id));
            any = true;
        }
        return any;
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
        Opened.Remove(id);
        Added.Remove(id);
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
