using KTable.Client.Docs;
using KTable.Server.Scripting;

namespace KTable.Client.Tests;

public class DocsTests
{
    static string Repo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "KriegsspielTable.sln"))) return d.FullName;
        throw new InvalidOperationException("the repository is not above the test's folder");
    }

    [Fact]
    public void Blocks_come_out_in_order_with_github_anchors()
    {
        var md = "# Title\n\nSome **bold** text\nthat goes on.\n\n## 1. Before the game\n\n- one\n  more\n  - nested\n2. two\n\n```lua\nprint(1)\n```\n\n| A | B |\n|---|---|\n| `a\\|b` | x |\n\n> a quote\n\n## 1. Before the game\n";
        var b = Markdown.Parse(md);
        Assert.Equal(new[] { BlockKind.Heading, BlockKind.Paragraph, BlockKind.Heading, BlockKind.List, BlockKind.Code, BlockKind.Table, BlockKind.Quote, BlockKind.Heading }, b.Select(x => x.Kind));
        Assert.Equal("Some **bold** text that goes on.", b[1].Text);
        Assert.Equal("1-before-the-game", b[2].Anchor);
        Assert.Equal("1-before-the-game-1", b[7].Anchor);
        Assert.Equal(new[] { "one more", "nested", "two" }, b[3].Items.Select(i => i.Text));
        Assert.Equal(1, b[3].Items[1].Depth);
        Assert.True(b[3].Items[2].Ordered);
        Assert.Equal("lua", b[4].Lang);
        Assert.Equal("print(1)", b[4].Text);
        Assert.Equal("`a|b`", b[5].Rows[1][0]);
        Assert.Equal("Title", Markdown.Title(b, "x"));
    }

    [Fact]
    public void Plain_text_drops_the_markup()
    {
        Assert.Equal("see self.set_name and bold, optional.", Markdown.Plain("see [`self.set_name`](#selfset_name) and **bold**, _optional_."));
    }

    [Fact]
    public void Every_reference_entry_has_its_anchor_on_the_page()
    {
        var anchors = Markdown.Parse(ApiDoc.Markdown()).Where(b => b.Kind == BlockKind.Heading).Select(b => b.Anchor).ToHashSet();
        foreach (var e in ApiDoc.All.Where(e => !ApiDoc.SectionOf(e)!.Title.StartsWith("Lua", StringComparison.Ordinal)))
            Assert.True(anchors.Contains(ApiDoc.AnchorOf(e)), $"{e.Qualified}: #{ApiDoc.AnchorOf(e)} is not a heading on the page");
    }

    [Fact]
    public void Links_inside_the_guides_land_on_a_heading()
    {
        var docs = Path.Combine(Repo(), "docs");
        var pages = Directory.GetFiles(docs, "*.md").ToDictionary(f => Path.GetFileName(f)!, f => Markdown.Parse(File.ReadAllText(f)), StringComparer.OrdinalIgnoreCase);
        pages[ApiDoc.File] = Markdown.Parse(ApiDoc.Markdown());
        pages[KeysPage.File] = Markdown.Parse(KeysPage.Markdown(new KTable.Client.Prefs.Keymap()));
        var link = new System.Text.RegularExpressions.Regex(@"\]\(([^)\s]+)\)");
        var broken = new List<string>();
        foreach (var (name, blocks) in pages)
            foreach (var b in blocks)
            {
                var text = b.Kind == BlockKind.Table ? string.Join(" ", b.Rows.SelectMany(r => r)) : b.Kind == BlockKind.List ? string.Join(" ", b.Items.Select(i => i.Text)) : b.Text;
                foreach (System.Text.RegularExpressions.Match m in link.Matches(text))
                {
                    var href = m.Groups[1].Value;
                    if (href.StartsWith("http", StringComparison.Ordinal)) continue;
                    var parts = href.Split('#', 2);
                    var target = parts[0].Length == 0 ? name : parts[0];
                    if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!pages.TryGetValue(target, out var page)) { broken.Add($"{name}: {href}"); continue; }
                    if (parts.Length == 2 && !page.Any(x => x.Kind == BlockKind.Heading && x.Anchor == parts[1])) broken.Add($"{name}: {href}");
                }
            }
        Assert.True(broken.Count == 0, "broken links: " + string.Join(", ", broken));
    }

    [Fact]
    public void Search_ranks_a_heading_match_first_and_marks_the_words()
    {
        var blocks = Markdown.Parse("# Doc\n\n## Moving\n\nUse set_pos to move a piece.\n\n## `self.set_pos`\n\nMoves it, in map units.\n");
        var hits = DocSearch.Find(DocSearch.Sections("d.md", "Doc", blocks), "set_pos");
        Assert.Equal(2, hits.Count);
        Assert.Equal("selfset_pos", hits[0].Section.Anchor);
        var h = hits[1];
        Assert.Equal("set_pos", h.Snippet.Substring(h.Marks[0].Start, h.Marks[0].Length));
        Assert.Empty(DocSearch.Find(DocSearch.Sections("d.md", "Doc", blocks), "set_pos nothing"));
    }

    [Fact]
    public void A_tts_name_finds_its_entry_in_the_reference()
    {
        var sections = DocSearch.Sections(ApiDoc.File, "API", Markdown.Parse(ApiDoc.Markdown()));
        var hits = DocSearch.Find(sections, "setName");
        Assert.Contains(hits.Take(3), h => h.Section.Anchor == "selfset_name");
    }

    [Fact]
    public void Matches_merge_overlaps_and_ignore_case()
    {
        Assert.Equal(new[] { (0, 5), (6, 5) }, DocSearch.Matches("Piece piece", new[] { "piece", "iec" }));
        Assert.Equal(new[] { (0, 7) }, DocSearch.Matches("set_pos", new[] { "set_", "_pos" }));
    }
}
