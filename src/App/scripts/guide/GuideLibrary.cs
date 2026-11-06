using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KTable.Client.Docs;
using KTable.Client.Prefs;
using KTable.Server.Scripting;

namespace KTable.App;

public sealed record GuideDoc(string File, string Group, string Short, bool Generated = false);

public static class GuideLibrary
{
    public const string Home = "start.md";

    public static readonly IReadOnlyList<GuideDoc> Docs = new[]
    {
        new GuideDoc(Home, "", "Start here"),
        new GuideDoc("umpire-guide.md", "At the table", "The umpire's guide"),
        new GuideDoc("player-guide.md", "At the table", "The player's guide"),
        new GuideDoc(KeysPage.File, "At the table", "Keys and mouse", true),
        new GuideDoc("community-guide.md", "Making", "Modules, pieces, sharing"),
        new GuideDoc("makers.md", "Making", "For makers"),
        new GuideDoc("scripting.md", "Scripting", "Scripting guide"),
        new GuideDoc(ApiDoc.File, "Scripting", "API reference", true),
    };

    public static Func<Keymap>? Keys { get; set; }

    static readonly Dictionary<string, List<MdBlock>> _parsed = new(StringComparer.OrdinalIgnoreCase);
    static List<DocSection>? _fixedSections;

    public static GuideDoc? Doc(string file) => Docs.FirstOrDefault(d => string.Equals(d.File, file, StringComparison.OrdinalIgnoreCase));

    static bool Live(string file) => string.Equals(file, KeysPage.File, StringComparison.OrdinalIgnoreCase);

    public static string? Text(string file)
    {
        if (string.Equals(file, ApiDoc.File, StringComparison.OrdinalIgnoreCase)) return ApiDoc.Markdown();
        if (Live(file)) return KeysPage.Markdown(Keys?.Invoke() ?? new Keymap());
        using var s = typeof(GuideLibrary).Assembly.GetManifestResourceStream("docs/" + file);
        if (s != null) { using var r = new StreamReader(s); return r.ReadToEnd(); }
        if (OnDisk(file) is { } path)
        {
            try { return File.ReadAllText(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }
        return null;
    }

    public static List<MdBlock>? Blocks(string file)
    {
        if (!Live(file) && _parsed.TryGetValue(file, out var b)) return b;
        if (Text(file) is not { } text) return null;
        var parsed = Markdown.Parse(text);
        if (!Live(file)) _parsed[file] = parsed;
        return parsed;
    }

    public static string Title(string file) => Blocks(file) is { } b ? Markdown.Title(b, Doc(file)?.Short ?? file) : file;

    public static string? Folder() => Guides.Folder();

    public static string? OnDisk(string file) => Folder() is { } d && File.Exists(Path.Combine(d, file)) ? Path.Combine(d, file) : null;

    public static IReadOnlyList<DocSection> Sections()
    {
        if (_fixedSections == null)
        {
            _fixedSections = new();
            foreach (var d in Docs.Where(d => !Live(d.File)))
                if (Blocks(d.File) is { } b) _fixedSections.AddRange(DocSearch.Sections(d.File, d.Short, b));
        }
        var all = new List<DocSection>(_fixedSections);
        if (Blocks(KeysPage.File) is { } keys) all.AddRange(DocSearch.Sections(KeysPage.File, "Keys and mouse", keys));
        return all;
    }
}
