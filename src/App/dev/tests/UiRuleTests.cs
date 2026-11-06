using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class UiRuleTests
{
    internal static IEnumerable<string> Problems(Window w) => Problems(w, w.Title);

    internal static IEnumerable<string> Problems(Node w, string title)
    {
        var nodes = WindowTests.Descendants(w).ToList();
        var pages = nodes.Where(n => n.HasMeta(DialogPages.PageMeta)).ToList();
        var pageNames = pages.Select(p => p.GetMeta(DialogPages.PageMeta).AsString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var heads = nodes.Where(n => n.HasMeta(Ui.HeaderMeta)).Select(n => n.GetMeta(Ui.HeaderMeta).AsString()).ToList();
        foreach (var twice in heads.GroupBy(h => h, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            yield return $"{title}: the section \"{twice.Key}\" is there {twice.Count()} times";
        foreach (var head in heads.Where(pageNames.Contains))
            yield return $"{title}: the section \"{head}\" has the name of a page";
        foreach (var label in nodes.OfType<Label>().Where(n => n.HasMeta(Ui.RowLabelMeta)))
        {
            float needs = label.GetThemeFont("font").GetStringSize(label.Text, HorizontalAlignment.Left, -1, label.GetThemeFontSize("font_size")).X;
            if (needs > label.GetMeta(Ui.RowLabelMeta).AsInt32())
                yield return $"{title}: the label \"{label.Text}\" needs {needs:0} px, its column is {label.GetMeta(Ui.RowLabelMeta).AsInt32()}";
        }
        IEnumerable<Node> regions = pages.Count > 0 ? pages : new Node[] { w };
        foreach (var region in regions)
        {
            var widths = WindowTests.Descendants(region).Where(n => n.HasMeta(Ui.RowLabelMeta)).Select(n => n.GetMeta(Ui.RowLabelMeta).AsInt32()).Distinct().ToList();
            if (widths.Count > 1)
                yield return $"{title} / {(region.HasMeta(DialogPages.PageMeta) ? region.GetMeta(DialogPages.PageMeta).AsString() : "window")}: labels in {widths.Count} columns ({string.Join(", ", widths)} px)";
        }
    }

    static List<Window> OpenWindows(Node root) => WindowTests.Descendants(root).OfType<Window>().Where(w => w.Visible).ToList();

    [NodeTest]
    static async Task Every_window_names_each_section_once_and_lines_its_labels_up_in_one_column(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1);
        var ctx = h.Hud.Ctx;
        var root = h.Hud.GetTree().Root;
        var node = h.Table.Pieces.NodeOf(Layers.Umpire, "p1")!;
        var opens = new (string name, Action open)[]
        {
            ("scenario", () => ScenarioDialog.Open(ctx)),
            ("settings", () => ((IHudShell)h.Hud).ShowSettings()),
            ("app settings", () => AppSettings.OpenWindow(h.Hud, ctx.Cfg)),
            ("invite", () => InviteDialog.Open(ctx)),
            ("properties", () => PropertiesDialog.Open(ctx, node)),
            ("clock", () => ClockDialog.Open(ctx)),
            ("calibrate", () => CalibrateDialog.Open(ctx, 100)),
            ("leave", () => LeaveDialog.Open(ctx)),
        };
        var problems = new List<string>();
        foreach (var (name, open) in opens)
        {
            var before = OpenWindows(root);
            open();
            await r.Frames(2);
            var opened = OpenWindows(root).Except(before).ToList();
            Check.That(opened.Count > 0, $"the {name} window opens");
            foreach (var w in opened) problems.AddRange(Problems(w));
            foreach (var w in opened) w.QueueFree();
            await r.Frames(1);
        }
        Check.That(problems.Count == 0, string.Join("\n", problems));
    }

    [NodeTest]
    static async Task Every_drawer_page_and_panel_names_each_section_once_and_lines_its_labels_up_in_one_column(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1);
        IHudShell shell = h.Hud;
        var problems = new List<string>();
        h.Table.SelectSheet(h.S.View.Sheets[0].Id);
        foreach (var key in h.Hud.Drawer.Pages.Keys.ToList())
        {
            h.Hud.Drawer.ShowPage(key);
            if (key == "map") h.Hud.Map.Rebuild();
            await r.Frames(2);
            if (h.Hud.Drawer.Pages[key].page is { } page) problems.AddRange(Problems(page, "drawer " + key));
        }
        foreach (var key in shell.Panels.Keys.ToList())
        {
            shell.OpenPanel(key);
            await r.Frames(2);
            problems.AddRange(Problems(shell.Panels[key], "panel " + key));
            shell.ClosePanel(key);
        }
        Check.That(problems.Count == 0, string.Join("\n", problems));
    }
}
