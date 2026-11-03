namespace KTable.Client.Docs;

public sealed record DocSection(string Doc, string DocTitle, string Anchor, string Heading, string Text);

public sealed record SearchHit(DocSection Section, int Score, string Snippet, IReadOnlyList<(int Start, int Length)> Marks);

public static class DocSearch
{
    public static IReadOnlyList<string> Terms(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim('"')).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static List<DocSection> Sections(string doc, string title, IEnumerable<MdBlock> blocks)
    {
        var list = new List<DocSection>();
        string anchor = "", heading = title;
        var text = new List<string>();
        void Close()
        {
            if (text.Count > 0 || anchor.Length > 0) list.Add(new DocSection(doc, title, anchor, heading, string.Join("\n", text)));
            text.Clear();
        }
        foreach (var b in blocks)
        {
            if (b.Kind == BlockKind.Heading && b.Level >= 2)
            {
                Close();
                anchor = b.Anchor; heading = Markdown.Plain(b.Text);
                continue;
            }
            if (b.Kind != BlockKind.Heading) text.Add(b.Plain);
        }
        Close();
        return list;
    }

    public static List<(int Start, int Length)> Matches(string text, IReadOnlyList<string> terms)
    {
        var marks = new List<(int Start, int Length)>();
        foreach (var t in terms)
            for (int i = text.IndexOf(t, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(t, i + t.Length, StringComparison.OrdinalIgnoreCase))
                marks.Add((i, t.Length));
        marks.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        var merged = new List<(int Start, int Length)>();
        foreach (var m in marks)
        {
            if (merged.Count > 0 && m.Start <= merged[^1].Start + merged[^1].Length)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.Start + last.Length, m.Start + m.Length) - last.Start);
            }
            else merged.Add(m);
        }
        return merged;
    }

    static int Count(string text, string term)
    {
        int n = 0;
        for (int i = text.IndexOf(term, StringComparison.OrdinalIgnoreCase); i >= 0 && n < 20; i = text.IndexOf(term, i + term.Length, StringComparison.OrdinalIgnoreCase)) n++;
        return n;
    }

    public static List<SearchHit> Find(IEnumerable<DocSection> sections, string query, int max = 80)
    {
        var terms = Terms(query);
        if (terms.Count == 0) return new();
        var phrase = query.Trim().Trim('"');
        var hits = new List<SearchHit>();
        foreach (var s in sections)
        {
            int score = 0;
            bool all = true;
            foreach (var t in terms)
            {
                int inHead = Count(s.Heading, t), inText = Count(s.Text, t);
                if (inHead + inText == 0) { all = false; break; }
                score += inHead * 12 + Math.Min(inText, 8);
                if (s.Heading.Equals(t, StringComparison.OrdinalIgnoreCase) || s.Heading.Trim('`').EndsWith("." + t, StringComparison.OrdinalIgnoreCase)) score += 30;
            }
            if (!all) continue;
            if (terms.Count > 1 && (s.Heading + "\n" + s.Text).Contains(phrase, StringComparison.OrdinalIgnoreCase)) score += 15;
            var (snippet, marks) = Snippet(s.Text, terms);
            hits.Add(new SearchHit(s, score, snippet, marks));
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Section.DocTitle, StringComparer.Ordinal).Take(max).ToList();
    }

    public static (string Text, List<(int Start, int Length)> Marks) Snippet(string text, IReadOnlyList<string> terms, int width = 180)
    {
        var flat = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var all = Matches(flat, terms);
        int start = 0;
        if (all.Count > 0) start = Math.Max(0, all[0].Start - width / 3);
        if (start > 0) { int sp = flat.IndexOf(' ', start); if (sp > 0 && sp < all[0].Start) start = sp + 1; }
        int end = Math.Min(flat.Length, start + width);
        if (end < flat.Length) { int sp = flat.LastIndexOf(' ', end); if (sp > start + width / 2) end = sp; }
        var cut = flat[start..end];
        var prefix = start > 0 ? "…" : "";
        var snippet = prefix + cut + (end < flat.Length ? "…" : "");
        var marks = all.Where(m => m.Start >= start && m.Start + m.Length <= end).Select(m => (m.Start - start + prefix.Length, m.Length)).ToList();
        return (snippet, marks);
    }
}
