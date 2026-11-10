using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;

namespace KTable.App;

sealed class AgentHelpTools
{
    const int GuidePage = 40_000, FoundMost = 60, LogShown = 100, LogMost = 2000;
    static readonly string[] Logs = { "app", "earlier", "engine" };

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("read_guide",
            "The in-app guides, as Help shows them: with no guide, their list; with a guide, its text a page at a time; with find, only the paragraphs that mention a word.",
            Args.Schema(new Arg("guide", "string", "a guide's file or title, from the list"), new Arg("find", "string", "a word; only the paragraphs with it"), Paging.Offset),
            a => Task.FromResult(ReadGuide(a.Str("guide"), a.Str("find"), (int)Math.Max(0, a.Num("offset") ?? 0))));
        yield return new AgentTool("read_app_log",
            "The app's own log on this PC, newest last: what it did, its warnings and errors (app), the log before it was last rolled over (earlier), or the engine's log of this run (engine). " +
            "Narrow it with find or problems; diagnostics gives the summary Copy diagnostics makes.",
            Args.Schema(new Arg("log", "string", string.Join(", ", Logs) + " (app if left out)"), new Arg("last", "number", $"the newest this many lines (default {LogShown}, at most {LogMost})"),
                new Arg("find", "string", "only lines with this word"), new Arg("problems", "boolean", "only warnings and errors")),
            a => Task.FromResult(ReadLog(a.Str("log"), (int)Math.Clamp(a.Num("last") ?? LogShown, 1, LogMost), a.Str("find"), a.Flag("problems") == true)));
        yield return new AgentTool("diagnostics",
            "What Copy diagnostics puts together, without touching the clipboard: the version, this PC's system and graphics, the table, the latest warnings and errors, the last commands, and a run that closed unexpectedly.",
            Args.Schema(),
            _ => Task.FromResult(new ToolResult(Diagnostics.Report())));
    }

    static ToolResult ReadGuide(string? asked, string? find, int offset)
    {
        if (asked?.Trim() is not { Length: > 0 } name)
            return new ToolResult(new JsonArray(GuideLibrary.Docs.Select(d => (JsonNode?)new JsonObject { ["guide"] = d.File, ["title"] = GuideLibrary.Title(d.File), ["group"] = d.Group.Length > 0 ? d.Group : null })
                .ToArray()).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var doc = GuideLibrary.Docs.FirstOrDefault(d => string.Equals(d.File, name, StringComparison.OrdinalIgnoreCase) || string.Equals(d.File, name + ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(GuideLibrary.Title(d.File), name, StringComparison.OrdinalIgnoreCase) || string.Equals(d.Short, name, StringComparison.OrdinalIgnoreCase));
        if (doc == null || GuideLibrary.Text(doc.File) is not { } text) return ToolResult.Fail($"No guide {name}: leave guide out for the list.");
        if (find?.Trim() is { Length: > 0 } word)
        {
            var hits = text.Split("\n\n").Where(p => p.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) return new ToolResult($"Nothing in {GuideLibrary.Title(doc.File)} mentions {word}.");
            return new ToolResult($"{hits.Count} paragraph{(hits.Count == 1 ? "" : "s")} of {GuideLibrary.Title(doc.File)} mention {word}" + (hits.Count > FoundMost ? $"; the first {FoundMost}" : "") + ":\n\n" +
                string.Join("\n\n", hits.Take(FoundMost)));
        }
        if (offset >= text.Length) return new ToolResult($"The guide ends at {text.Length} characters.");
        int end = Math.Min(text.Length, offset + GuidePage);
        var head = offset == 0 && end == text.Length ? "" : $"characters {offset}–{end} of {text.Length}" + (end < text.Length ? $" (offset {end} for the next page)" : "") + "\n";
        return new ToolResult(head + text[offset..end]);
    }

    static ToolResult ReadLog(string? which, int last, string? find, bool problems)
    {
        var kind = (which ?? "app").Trim().ToLowerInvariant();
        if (!Logs.Contains(kind)) return ToolResult.Fail("log is " + string.Join(", ", Logs) + ".");
        var path = kind switch
        {
            "engine" => Diagnostics.EngineLogPath,
            "earlier" when Diagnostics.LogPath.Length > 0 => Path.ChangeExtension(Diagnostics.LogPath, ".old.log"),
            _ => Diagnostics.LogPath,
        };
        if (path.Length == 0 || !File.Exists(path)) return new ToolResult($"There is no {kind} log on this PC yet.");
        List<string> lines;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        }
        catch (IOException e) { return ToolResult.Fail($"The {kind} log could not be read: {e.Message}"); }
        var kept = lines.Where(l => (!problems || l.Contains(" ERROR ") || l.Contains(" WARN ") || l.Contains("ERROR:") || l.Contains("WARNING:"))
            && (find is not { Length: > 0 } w || l.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
        var shown = kept.TakeLast(last).ToList();
        return new ToolResult($"{path}: {shown.Count} of {kept.Count} line{(kept.Count == 1 ? "" : "s")}{(kept.Count > shown.Count ? " (the newest)" : "")}\n" + string.Join("\n", shown));
    }
}
