using System.Text;
using System.Text.RegularExpressions;
using KTable.Core;

namespace KTable.Client.Docs;

public enum BlockKind { Heading, Paragraph, List, Table, Quote, Code }

public sealed record ListItem(int Depth, bool Ordered, string Number, string Text);

public sealed record MdBlock(BlockKind Kind, string Text = "", int Level = 0, string Anchor = "", string Lang = "")
{
    public IReadOnlyList<string[]> Rows { get; init; } = Array.Empty<string[]>();
    public IReadOnlyList<ListItem> Items { get; init; } = Array.Empty<ListItem>();

    public string Plain => Kind switch
    {
        BlockKind.Code => Text,
        BlockKind.Table => string.Join("\n", Rows.Select(r => string.Join("  ", r.Select(Markdown.Plain)))),
        BlockKind.List => string.Join("\n", Items.Select(i => Markdown.Plain(i.Text))),
        BlockKind.Quote => string.Join("\n", Markdown.Parse(Text).Select(b => b.Plain)),
        _ => Markdown.Plain(Text),
    };
}

public static partial class Markdown
{
    [GeneratedRegex(@"^(\s*)[-*]\s+(.*)$")] private static partial Regex BulletLine();
    [GeneratedRegex(@"^(\s*)(\d+)[.)]\s+(.*)$")] private static partial Regex NumberLine();
    [GeneratedRegex(@"^:?-{2,}:?$")] private static partial Regex RuleCell();
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")] private static partial Regex LinkSyntax();
    [GeneratedRegex(@"(\*\*|__)(.+?)\1")] private static partial Regex StrongSyntax();
    [GeneratedRegex(@"(?<![\w*])[*_](?!\s)(.+?)(?<!\s)[*_](?![\w*])")] private static partial Regex EmSyntax();

    public static List<MdBlock> Parse(string md)
    {
        var lines = md.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<MdBlock>();
        var slugs = new HeadingSlugs();
        var para = new List<string>();
        void Flush()
        {
            if (para.Count == 0) return;
            blocks.Add(new MdBlock(BlockKind.Paragraph, string.Join(" ", para)));
            para.Clear();
        }
        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var t = raw.Trim();
            if (t.Length == 0) { Flush(); continue; }
            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                Flush();
                var lang = t[3..].Trim();
                var code = new List<string>();
                int indent = raw.Length - raw.TrimStart().Length;
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++)
                    code.Add(lines[i].Length >= indent && lines[i][..indent].Trim().Length == 0 ? lines[i][indent..] : lines[i].TrimStart());
                blocks.Add(new MdBlock(BlockKind.Code, string.Join("\n", code).TrimEnd(), Lang: lang));
                continue;
            }
            if (t.StartsWith('#'))
            {
                int level = t.TakeWhile(c => c == '#').Count();
                if (level <= 6 && t.Length > level && t[level] == ' ')
                {
                    Flush();
                    var text = t[level..].Trim();
                    blocks.Add(new MdBlock(BlockKind.Heading, text, level, slugs.Next(text)));
                    continue;
                }
            }
            if (t.StartsWith('|'))
            {
                Flush();
                var rows = new List<string[]>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('|'); i++)
                {
                    var cells = Cells(lines[i].Trim());
                    if (!cells.All(c => RuleCell().IsMatch(c))) rows.Add(cells);
                }
                i--;
                blocks.Add(new MdBlock(BlockKind.Table) { Rows = rows });
                continue;
            }
            if (t.StartsWith('>'))
            {
                Flush();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith('>'); i++)
                {
                    var q = lines[i].Trim()[1..];
                    quote.Add(q.StartsWith(' ') ? q[1..] : q);
                }
                i--;
                blocks.Add(new MdBlock(BlockKind.Quote, string.Join("\n", quote).Trim()));
                continue;
            }
            if (BulletLine().IsMatch(raw) || NumberLine().IsMatch(raw))
            {
                Flush();
                var items = new List<ListItem>();
                for (; i < lines.Length; i++)
                {
                    var b = BulletLine().Match(lines[i]);
                    var n = NumberLine().Match(lines[i]);
                    if (!b.Success && !n.Success)
                    {
                        if (lines[i].Trim().Length > 0 && lines[i].StartsWith(' ') && items.Count > 0)
                        {
                            var last = items[^1];
                            items[^1] = last with { Text = last.Text + " " + lines[i].Trim() };
                            continue;
                        }
                        break;
                    }
                    var m = b.Success ? b : n;
                    int depth = m.Groups[1].Value.Replace("\t", "  ").Length / 2;
                    items.Add(b.Success ? new ListItem(depth, false, "", b.Groups[2].Value.Trim()) : new ListItem(depth, true, n.Groups[2].Value, n.Groups[3].Value.Trim()));
                }
                i--;
                blocks.Add(new MdBlock(BlockKind.List) { Items = items });
                continue;
            }
            para.Add(t);
        }
        Flush();
        return blocks;
    }

    static string[] Cells(string row)
    {
        var inner = row.Trim();
        if (inner.StartsWith('|')) inner = inner[1..];
        if (inner.EndsWith('|') && !inner.EndsWith("\\|", StringComparison.Ordinal)) inner = inner[..^1];
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool code = false;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '\\' && i + 1 < inner.Length && inner[i + 1] == '|') { sb.Append('|'); i++; continue; }
            if (c == '`') code = !code;
            if (c == '|' && !code) { cells.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(c);
        }
        cells.Add(sb.ToString().Trim());
        return cells.ToArray();
    }

    public static string Plain(string inline)
    {
        var s = LinkSyntax().Replace(inline, "$1");
        s = StrongSyntax().Replace(s, "$2");
        s = EmSyntax().Replace(s, "$1");
        return s.Replace("`", "");
    }

    public static string Title(IEnumerable<MdBlock> blocks, string fallback) =>
        blocks.FirstOrDefault(b => b.Kind == BlockKind.Heading && b.Level == 1) is { } h ? Plain(h.Text) : fallback;
}
