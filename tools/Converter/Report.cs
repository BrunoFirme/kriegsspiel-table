using System.Collections.Concurrent;
using System.Text;
using KTable.Core;

namespace KTable.Converter;

public sealed class Report
{
    public sealed record Unresolved(string Url, string Why, string Where);
    readonly ConcurrentDictionary<string, Unresolved> _unresolved = new();
    public sealed record LeftOutPiece(string Name, string Url, string Why, string Where);
    readonly ConcurrentDictionary<string, LeftOutPiece> _leftOut = new();
    readonly ConcurrentDictionary<string, (int count, string example)> _dropped = new();
    readonly ConcurrentDictionary<string, (int count, string example)> _skipped = new();
    readonly ConcurrentDictionary<string, (int count, string example)> _unknown = new();
    readonly ConcurrentDictionary<string, (int count, string example)> _hostile = new();
    readonly ConcurrentDictionary<string, byte> _hostileGuids = new();
    int _hostileObjects;
    readonly ConcurrentQueue<(string where, int line, string what, string text)> _untranslated = new();
    int _scripts, _direct, _compat, _unparsed;
    readonly ConcurrentQueue<string> _notes = new();
    public readonly Dictionary<string, string> Counts = new();

    public int UnknownTypes => _unknown.Count;

    public void Missing(string url, string why, string where) => _unresolved.TryAdd(url, new Unresolved(url, why, where));
    public void LeftOut(string name, string url, string why, string where, string guid) =>
        _leftOut.TryAdd(guid.Length > 0 ? guid + "|" + name : where, new LeftOutPiece(name, url, why, where));
    public int PiecesLeftOut => _leftOut.Count;

    static string NameList(IEnumerable<string> names, int most)
    {
        var groups = names.GroupBy(n => n).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key).ToList();
        return groups.Count <= most ? string.Join(", ", groups) : string.Join(", ", groups.Take(most)) + $" and {groups.Count - most} more";
    }
    public void DroppedScript(string variant, string where) => _dropped.AddOrUpdate(variant, (1, where), (_, v) => (v.count + 1, v.example));
    public void Skipped(string type, string where) => _skipped.AddOrUpdate(type, (1, where), (_, v) => (v.count + 1, v.example));
    public void UnknownType(string type, string where) => _unknown.AddOrUpdate(type, (1, where), (_, v) => (v.count + 1, v.example));
    public void Note(string s) => _notes.Enqueue(s);
    public void HostileScript(IEnumerable<string> reasons, string where, string guid)
    {
        if (!_hostileGuids.TryAdd(guid.Length > 0 ? guid : where, 0)) return;
        Interlocked.Increment(ref _hostileObjects);
        foreach (var r in reasons) _hostile.AddOrUpdate(r, (1, where), (_, v) => (v.count + 1, v.example));
    }
    public int HostileObjects => _hostileObjects;
    public void ScriptTranslated(KTable.Server.Scripting.Translate.TranslateResult tr, string where)
    {
        Interlocked.Increment(ref _scripts);
        Interlocked.Add(ref _direct, tr.DirectCalls);
        Interlocked.Add(ref _compat, tr.CompatCalls);
        if (!tr.Parsed) Interlocked.Increment(ref _unparsed);
        foreach (var u in tr.Untranslated) _untranslated.Enqueue((where, u.Line, u.What, u.Text));
    }
    public int ScriptsTranslated => _scripts;

    public void Write(string path, string section)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {section}");
        sb.AppendLine();
        sb.AppendLine($"Generated {Repeatable.Now:yyyy-MM-dd HH:mm}.");
        sb.AppendLine();
        foreach (var (k, v) in Counts) sb.AppendLine($"- {k}: {v}");
        if (_scripts > 0) sb.AppendLine($"- scripts translated: {_scripts} ({_direct} direct calls, {_compat} through compat{(_unparsed > 0 ? $"; {_unparsed} could not be parsed and run as they are" : "")})");
        if (Counts.Count > 0 || _scripts > 0) sb.AppendLine();
        if (!_untranslated.IsEmpty)
        {
            var groups = _untranslated.GroupBy(u => u.what).OrderByDescending(g => g.Count()).ToList();
            sb.AppendLine($"### Script lines not translated ({_untranslated.Count})");
            sb.AppendLine("No rule in the translator yet: the call runs through `compat.todo` and does nothing. One line per kind of call, with where it was first seen.");
            foreach (var g in groups) { var e = g.First(); sb.AppendLine($"- {g.Count()}× {g.Key} (e.g. {e.where}, line {e.line}: `{e.text}`)"); }
            sb.AppendLine();
        }
        if (!_hostile.IsEmpty)
        {
            sb.AppendLine($"### HOSTILE SCRIPTS REMOVED ({_hostileObjects} objects)");
            sb.AppendLine("Cut before anything was read from them; nothing of them is in the pack. A module that carries these spreads them to every object saved from it in TTS.");
            foreach (var (k, v) in _hostile.OrderByDescending(x => x.Value.count)) sb.AppendLine($"- {v.count}× {k} (e.g. {v.example})");
            sb.AppendLine();
            Console.WriteLine($"HOSTILE {_hostileObjects} objects: {string.Join("; ", _hostile.OrderByDescending(x => x.Value.count).Select(x => x.Key))}");
        }
        if (!_leftOut.IsEmpty)
        {
            const int NamesOnTheLine = 12;
            sb.AppendLine($"### PIECES LEFT OUT ({_leftOut.Count})");
            sb.AppendLine("Their picture or model is gone from the web and is not in TTS's own cache on this computer, so TTS cannot show them either.");
            foreach (var p in _leftOut.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)) sb.AppendLine($"- {p.Name} — `{p.Url}` — {p.Why} — {p.Where}");
            sb.AppendLine();
            Console.WriteLine($"LEFTOUT {_leftOut.Count} {NameList(_leftOut.Values.Select(p => p.Name), NamesOnTheLine)}");
        }
        if (!_notes.IsEmpty) { sb.AppendLine("### Notes"); foreach (var n in _notes) sb.AppendLine("- " + n); sb.AppendLine(); }
        sb.AppendLine($"### Unresolved assets ({_unresolved.Count}) — placeholder image used");
        foreach (var u in _unresolved.Values.OrderBy(u => u.Why).ThenBy(u => u.Url)) sb.AppendLine($"- `{u.Url}` — {u.Why} — {u.Where}");
        sb.AppendLine();
        sb.AppendLine($"### Dropped scripts ({_dropped.Values.Sum(v => v.count)})");
        foreach (var (k, v) in _dropped.OrderByDescending(x => x.Value.count)) sb.AppendLine($"- {v.count}× {k} (e.g. {v.example})");
        sb.AppendLine();
        sb.AppendLine("### Skipped object types (by design)");
        foreach (var (k, v) in _skipped.OrderByDescending(x => x.Value.count)) sb.AppendLine($"- {v.count}× {k} (e.g. {v.example})");
        sb.AppendLine();
        sb.AppendLine($"### Unmapped object types ({_unknown.Count})");
        foreach (var (k, v) in _unknown.OrderByDescending(x => x.Value.count)) sb.AppendLine($"- {v.count}× {k} (e.g. {v.example})");
        sb.AppendLine();

        var sections = new SortedDictionary<string, string>();
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path);
            foreach (var part in text.Split("\n## ").Skip(1))
            {
                var title = part[..part.IndexOf('\n')].Trim();
                sections[title] = "## " + part.TrimEnd() + "\n";
            }
        }
        sections[section] = sb.ToString().TrimEnd() + "\n";
        var outText = new StringBuilder("# Conversion report\n\nRegenerated by `Converter` on every run (one section per pack/scenario).\n\n");
        foreach (var s in sections.Values) outText.AppendLine(s);
        File.WriteAllText(path, outText.ToString());
    }
}
