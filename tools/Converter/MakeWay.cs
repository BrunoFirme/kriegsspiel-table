using KTable.Core;

namespace KTable.Converter;

public sealed class MakeWay
{
    readonly string _root;
    readonly SaveStore _saves;
    readonly HashSet<string> _cleared = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _packs = new();
    InstallJournal? _journal;

    public MakeWay(Config cfg) { _root = cfg.Output; _saves = SaveStore.ForRoot(_root); }

    public string What { get; set; } = "an import";

    public string? Pack(string id, Report rep) => Clear("pack", id, rep);

    public string? Scenario(string dir, Report rep) => Clear("scenario", dir, rep);

    string? Clear(string kind, string id, Report rep)
    {
        var target = kind == "pack" ? Path.Combine(_root, id) : Path.Combine(_root, "scenarios", id);
        if (!_cleared.Add(Path.GetFullPath(target))) return null;
        if (kind == "pack") _packs.Add(id);
        if (_journal == null)
        {
            _journal = InstallJournal.Begin(_root, _saves, What, Repeatable.UtcNow);
            Console.WriteLine($"JOURNAL {_journal.FilePath}");
        }
        var entry = _journal.MakeWay(kind, id, Repeatable.UtcNow);
        if (entry == null) return null;
        rep.Note($"The {kind} {id} that was already here went to the trash first, whole ({Path.GetFileName(entry)}): restore it from there within {SaveStore.TrashDays} days if the new one is not what you wanted.");
        Console.WriteLine($"TRASHED {entry}");
        return entry;
    }

    public void Done()
    {
        foreach (var id in _packs)
        {
            var dir = Path.Combine(_root, id);
            if (!File.Exists(Path.Combine(dir, "pack.json"))) continue;
            try { PackChanges.Seal(dir); }
            catch (Exception e) { Console.Error.WriteLine($"Could not keep a record of what was written to {id} ({e.Message}); importing it again will ask whether it was changed."); }
        }
        if (_journal?.Finish() is { } unfinished) Console.Error.WriteLine(char.ToUpperInvariant(unfinished[0]) + unfinished[1..] + ".");
        _journal = null;
    }

    public void PutBack()
    {
        if (_journal == null) return;
        var u = _journal.RollBack(Repeatable.UtcNow);
        foreach (var b in u.Back) Console.WriteLine("PUTBACK " + b);
        foreach (var p in u.Problems) Console.Error.WriteLine(char.ToUpperInvariant(p[0]) + p[1..] + ".");
        if (u.Notes.Count > 0) Console.Error.WriteLine("The record of this import: " + string.Join(", ", u.Notes) + ".");
        _journal = null;
    }
}
