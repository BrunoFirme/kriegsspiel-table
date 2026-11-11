using System.Linq;
using System.Text;
using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

static class BriefingWindow
{
    public static string ToBb(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith('#'))
            {
                int level = line.TakeWhile(c => c == '#').Count();
                var head = line[level..].Trim();
                sb.Append($"[font_size={(level == 1 ? 22 : level == 2 ? 18 : 15)}][b]{MarkdownView.Inline(head, false)}[/b][/font_size]\n");
                continue;
            }
            if (line.StartsWith("- ") || line.StartsWith("* ")) { sb.Append("  •  ").Append(MarkdownView.Inline(line[2..], false)).Append('\n'); continue; }
            sb.Append(MarkdownView.Inline(line, false)).Append('\n');
        }
        return sb.ToString();
    }

    public static void Open(Node parent, Session s)
    {
        var w = new Window { Title = "Briefing · " + s.View.Name, Transient = true, Exclusive = false, WrapControls = false, MinSize = new Vector2I(480, 320) };
        var bg = new PanelContainer();
        bg.AddThemeStyleboxOverride("panel", Ui.WindowFill(Ui.Surface));
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        w.AddChild(bg);
        var pad = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" }) pad.AddThemeConstantOverride("margin_" + side, 14);
        bg.AddChild(pad);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Group);
        pad.AddChild(box);
        var text = new RichTextLabel { BbcodeEnabled = true, SelectionEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, ScrollActive = true };
        text.AddThemeFontSizeOverride("normal_font_size", 14);
        string bb = "";
        void Fill() => text.Text = bb = s.View.Briefing is { Length: > 0 } b ? ToBb(b) : "[color=#9a968c]No briefing yet[/color]";
        var find = FindBar.ForLabel(text, () => bb);
        box.AddChild(find);
        box.AddChild(text);
        Fill();
        void Changed(Command c) { if (c is SetBriefing && GodotObject.IsInstanceValid(text)) { Fill(); if (find.Terms.Count > 0) find.Refresh(); } }
        void Reset() { if (GodotObject.IsInstanceValid(text)) Fill(); }
        s.Applied += Changed;
        s.Reset += Reset;
        w.TreeExiting += () => { s.Applied -= Changed; s.Reset -= Reset; };
        w.CloseRequested += w.QueueFree;
        w.WindowInput += e => { if (find.Handles(e)) w.SetInputAsHandled(); };
        parent.AddChild(w);
        w.PopupCentered(new Vector2I(720, 560));
    }
}
