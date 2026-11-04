using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using KTable.Client.Docs;
using KTable.Server.Scripting;

namespace KTable.App;

public sealed class RenderedBlock
{
    public required MdBlock Block;
    public required Control Control;
    public RichTextLabel? Label;
    public string Bbcode = "";
    public bool Marked;
}

public static partial class MarkdownView
{
    public const int BodySize = 16;
    public const int CodeSize = 13;
    public const int Column = 760;

    static Font? _mono, _italic;
    public static Font Mono() => _mono ??= new SystemFont { FontNames = new[] { "Cascadia Mono", "Consolas", "DejaVu Sans Mono", "Noto Sans Mono" } };
    static Font Italic() => _italic ??= new SystemFont { FontNames = new[] { "Segoe UI", "Inter", "Helvetica Neue", "Arial", "Noto Sans" }, FontItalic = true };

    static string Hex(Color c) => "#" + c.ToHtml(c.A < 0.999f);
    static readonly string Link = Hex(Colors.White);
    static readonly string CodeInk = Hex(new Color(0.89f, 0.81f, 0.60f));
    static readonly string CodeBg = Hex(new Color(1, 1, 1, 0.055f));

    [GeneratedRegex(@"\[lb\]((?:(?!\]\().)+?)\]\(([^)\s]+)\)")] private static partial Regex LinkSyntax();
    [GeneratedRegex(@"(?<![=\w/\]])(https?://[^\s\[\]()<>]*[^\s\[\]()<>.,;:])")] private static partial Regex BareUrl();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Strong();
    [GeneratedRegex(@"(?<![\w*])[*_](?![\s*_])(.+?)(?<![\s*_])[*_](?![\w*])")] private static partial Regex Em();

    public static string Esc(string s) => s.Replace("[", "[lb]");

    public static string Inline(string s) => Inline(s, true);

    public static string Inline(string s, bool boxCode)
    {
        var codes = new List<string>();
        s = Regex.Replace(s, "`([^`]+)`", m => { codes.Add(m.Groups[1].Value); return $"\u0001{codes.Count - 1}\u0001"; });
        s = Esc(s);
        s = LinkSyntax().Replace(s, m => $"[url={m.Groups[2].Value}][color={Link}]{m.Groups[1].Value}[/color][/url]");
        s = BareUrl().Replace(s, m => $"[url={m.Groups[1].Value}][color={Link}]{m.Groups[1].Value}[/color][/url]");
        s = Strong().Replace(s, "[b]$1[/b]");
        s = Em().Replace(s, "[i]$1[/i]");
        return Regex.Replace(s, "\u0001(\\d+)\u0001", m => boxCode
            ? $"[bgcolor={CodeBg}][color={CodeInk}][code]{Esc(codes[int.Parse(m.Groups[1].Value)])}[/code][/color][/bgcolor]"
            : $"[code]{Esc(codes[int.Parse(m.Groups[1].Value)])}[/code]");
    }

    public static RichTextLabel Label(int size = BodySize, bool strong = false)
    {
        var l = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, SelectionEnabled = true, ContextMenuEnabled = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MetaUnderlined = true,
        };
        l.AddThemeFontOverride("normal_font", strong ? Ui.SansStrong() : Ui.Sans());
        l.AddThemeFontOverride("bold_font", Ui.SansStrong());
        l.AddThemeFontOverride("italics_font", Italic());
        l.AddThemeFontOverride("mono_font", Mono());
        l.AddThemeFontSizeOverride("normal_font_size", size);
        l.AddThemeFontSizeOverride("bold_font_size", size);
        l.AddThemeFontSizeOverride("italics_font_size", size);
        l.AddThemeFontSizeOverride("mono_font_size", Math.Max(11, size - 2));
        l.AddThemeConstantOverride("line_separation", size >= BodySize ? 5 : 3);
        l.AddThemeConstantOverride("table_h_separation", 0);
        l.AddThemeConstantOverride("table_v_separation", 0);
        l.AddThemeColorOverride("default_color", Ui.Ink);
        l.AddThemeColorOverride("selection_color", new Color(Ui.Accent, 0.35f));
        l.MetaHoverStarted += _ => l.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        l.MetaHoverEnded += _ => l.MouseDefaultCursorShape = Control.CursorShape.Arrow;
        return l;
    }

    public static List<RenderedBlock> Build(IReadOnlyList<MdBlock> blocks, VBoxContainer into, Action<string> onLink)
    {
        var list = new List<RenderedBlock>();
        foreach (var b in blocks)
        {
            var r = b.Kind switch
            {
                BlockKind.Heading => Heading(b),
                BlockKind.Code => Code(b),
                BlockKind.Quote => Quote(b),
                _ => Prose(b),
            };
            if (r.Label != null) r.Label.MetaClicked += meta => onLink(meta.AsString());
            into.AddChild(r.Control);
            list.Add(r);
        }
        return list;
    }

    static RenderedBlock Heading(MdBlock b)
    {
        int size = b.Level switch { 1 => 28, 2 => 20, 3 => 16, _ => BodySize };
        var l = Label(size, true);
        if (b.Level == 2) l.AddThemeColorOverride("default_color", Colors.White);
        var bb = Inline(b.Text, false);
        l.Text = bb;
        if (b.Text.StartsWith('`')) l.AddThemeFontSizeOverride("mono_font_size", size);
        var pad = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_top", b.Level switch { 1 => 4, 2 => 22, 3 => 14, _ => 8 });
        pad.AddThemeConstantOverride("margin_bottom", b.Level == 1 ? 6 : 0);
        pad.AddChild(l);
        if (b.Level == 2)
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 6);
            pad.RemoveChild(l);
            box.AddChild(l);
            box.AddChild(new HSeparator());
            pad.AddChild(box);
        }
        return new RenderedBlock { Block = b, Control = pad, Label = l, Bbcode = bb };
    }

    static RenderedBlock Prose(MdBlock b)
    {
        var l = Label();
        var bb = b.Kind switch
        {
            BlockKind.List => ListText(b.Items),
            BlockKind.Table => TableText(b.Rows),
            _ => Inline(b.Text),
        };
        l.Text = bb;
        return new RenderedBlock { Block = b, Control = l, Label = l, Bbcode = bb };
    }

    static string ListText(IReadOnlyList<ListItem> items)
    {
        var sb = new StringBuilder();
        var open = new Stack<bool>();
        bool fresh = false;
        foreach (var it in items)
        {
            int want = Math.Min(it.Depth, open.Count) + 1;
            while (open.Count > want) { sb.Append(open.Pop() ? "[/ol]" : "[/ul]"); fresh = false; }
            if (open.Count == want && !fresh) sb.Append('\n');
            while (open.Count < want)
            {
                if (sb.Length > 0 && !fresh) sb.Append('\n');
                bool ordered = it.Ordered && open.Count + 1 == want;
                sb.Append(ordered ? "[ol type=1]" : "[ul bullet=•]");
                open.Push(ordered);
                fresh = true;
            }
            sb.Append('\u200B').Append(Inline(it.Text));
            fresh = false;
        }
        while (open.Count > 0) sb.Append(open.Pop() ? "[/ol]" : "[/ul]");
        return sb.ToString();
    }

    static string TableText(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return "";
        int cols = rows.Max(r => r.Length);
        bool header = rows.Count > 1 && rows[0].Any(c => c.Length > 0);
        var sb = new StringBuilder($"[table={cols}]");
        for (int r = 0; r < rows.Count; r++)
        {
            if (r == 0 && rows[0].All(c => c.Length == 0)) continue;
            bool head = r == 0 && header;
            string bg = head ? Hex(new Color(1, 1, 1, 0.07f)) : (r % 2 == 0 ? Hex(new Color(1, 1, 1, 0.025f)) : Hex(new Color(0, 0, 0, 0)));
            for (int c = 0; c < cols; c++)
            {
                var text = c < rows[r].Length ? Inline(rows[r][c]) : "";
                var expand = c == cols - 1 ? " expand=1" : "";
                sb.Append($"[cell bg={bg} padding=8,5,14,5{expand}]");
                sb.Append(head ? $"[b][color={Hex(Ui.Muted)}]{text}[/color][/b]" : text);
                sb.Append("[/cell]");
            }
        }
        sb.Append("[/table]");
        return sb.ToString();
    }

    static RenderedBlock Quote(MdBlock b)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(new Color(Ui.Accent, 0.10f), 4, 16, 12));
        var l = Label();
        var bb = string.Join("\n\n", Markdown.Parse(b.Text).Select(x => x.Kind == BlockKind.List ? ListText(x.Items) : Inline(x.Text)));
        l.Text = bb;
        panel.AddChild(l);
        return new RenderedBlock { Block = b, Control = panel, Label = l, Bbcode = bb };
    }

    static RenderedBlock Code(MdBlock b)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Ui.Box(new Color(0, 0, 0, 0.32f), 4, 14, 10));
        var row = new HBoxContainer();
        panel.AddChild(row);
        var l = Label(CodeSize);
        l.AddThemeFontOverride("normal_font", Mono());
        l.AddThemeFontOverride("bold_font", Mono());
        l.AddThemeConstantOverride("line_separation", 3);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var bb = b.Lang is "lua" or "" && LooksLikeLua(b.Text) ? Lua(b.Text) : Esc(b.Text);
        l.Text = bb;
        row.AddChild(l);
        var copy = Ui.Bare(Ui.Btn("Copy", () => AppClipboard.SetText(b.Text), "Copy this code"));
        copy.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        copy.AddThemeColorOverride("font_color", Ui.Faint);
        copy.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(copy);
        return new RenderedBlock { Block = b, Control = panel, Label = l, Bbcode = bb };
    }

    static bool LooksLikeLua(string code) => code.Contains("function") || code.Contains("self.") || code.Contains("local ") || code.Contains("--") || Regex.IsMatch(code, @"^\w[\w.]*[({]", RegexOptions.Multiline);

    static readonly HashSet<string> Keywords = new() { "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while" };
    static readonly Lazy<HashSet<string>> Ours = new(() => ApiDoc.Keywords.Concat(new[] { "string", "table", "math", "os", "coroutine", "pairs", "ipairs", "tostring", "tonumber", "type", "pcall", "error" }).ToHashSet());
    static readonly Lazy<HashSet<string>> Events = new(() => ApiDoc.All.Where(e => e.Kind == "event").Select(e => e.Name).ToHashSet());

    public static string Lua(string code)
    {
        string kw = Hex(new Color(1f, 0.6f, 0.45f)), ours = Hex(new Color(0.6f, 0.9f, 0.8f)), ev = Hex(new Color(0.95f, 0.75f, 0.95f));
        string num = Hex(new Color(0.78f, 0.92f, 0.65f)), str = Hex(new Color(0.95f, 0.85f, 0.55f)), com = Hex(new Color(0.55f, 0.6f, 0.55f));
        var sb = new StringBuilder();
        void Span(string color, string text) => sb.Append("[color=").Append(color).Append(']').Append(Esc(text)).Append("[/color]");
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            if (c == '-' && i + 1 < code.Length && code[i + 1] == '-')
            {
                int end = code.StartsWith("--[[", StringComparison.Ordinal) ? code.IndexOf("]]", i + 4, StringComparison.Ordinal) : -1;
                end = end >= 0 ? end + 2 : code.IndexOf('\n', i) is var nl && nl >= 0 ? nl : code.Length;
                Span(com, code[i..end]);
                i = end;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                int j = i + 1;
                while (j < code.Length && code[j] != c && code[j] != '\n') j += code[j] == '\\' ? 2 : 1;
                j = Math.Min(code.Length, j + 1);
                Span(str, code[i..j]);
                i = j;
                continue;
            }
            if (c == '[' && i + 1 < code.Length && code[i + 1] == '[')
            {
                int end = code.IndexOf("]]", i + 2, StringComparison.Ordinal);
                end = end < 0 ? code.Length : end + 2;
                Span(str, code[i..end]);
                i = end;
                continue;
            }
            if (char.IsDigit(c))
            {
                int j = i;
                while (j < code.Length && (char.IsLetterOrDigit(code[j]) || code[j] == '.')) j++;
                Span(num, code[i..j]);
                i = j;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < code.Length && (char.IsLetterOrDigit(code[j]) || code[j] == '_')) j++;
                var word = code[i..j];
                bool member = i > 0 && code[i - 1] == '.';
                if (!member && Keywords.Contains(word)) Span(kw, word);
                else if (!member && Ours.Value.Contains(word)) Span(ours, word);
                else if (Events.Value.Contains(word)) Span(ev, word);
                else sb.Append(word);
                i = j;
                continue;
            }
            sb.Append(c == '[' ? "[lb]" : c.ToString());
            i++;
        }
        return sb.ToString();
    }
}
