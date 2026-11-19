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
            string where = $"{title} / {(region.HasMeta(DialogPages.PageMeta) ? region.GetMeta(DialogPages.PageMeta).AsString() : "window")}";
            var widths = WindowTests.Descendants(region).Where(n => n.HasMeta(Ui.RowLabelMeta)).Select(n => n.GetMeta(Ui.RowLabelMeta).AsInt32()).Distinct().ToList();
            if (widths.Count > 1)
                yield return $"{where}: labels in {widths.Count} columns ({string.Join(", ", widths)} px)";
            foreach (var problem in SliderRows(region, where)) yield return problem;
        }
    }

    const int AlignSlack = 1;

    static IEnumerable<string> SliderRows(Node region, string where)
    {
        var rows = WindowTests.Descendants(region).OfType<HSlider>().Where(s => s.IsVisibleInTree() && s.GetParent() is HBoxContainer)
            .Select(s => (Slider: s, Row: (HBoxContainer)s.GetParent())).ToList();
        foreach (var (slider, _) in rows.Where(r => r.Row.GetChildren().OfType<SpinBox>().Any()))
            yield return $"{where}: the slider \"{slider.TooltipText}\" has a spin box beside it, not a value box";
        foreach (var slider in WindowTests.Descendants(region).OfType<HSlider>().Where(s => s.IsVisibleInTree() && s.GetParent() is GridContainer))
        {
            var grid = (GridContainer)slider.GetParent();
            var cells = grid.GetChildren().OfType<Control>().Where(c => c.Visible).ToList();
            int at = cells.IndexOf(slider), start = at - at % Math.Max(1, grid.Columns);
            if (cells.Skip(start).Take(grid.Columns).OfType<SpinBox>().Any())
                yield return $"{where}: the slider \"{slider.TooltipText}\" has a spin box beside it, not a value box";
        }
        var boxes = rows.Select(r => r.Row.GetChildren().OfType<LineEdit>().FirstOrDefault()).OfType<LineEdit>().Where(b => b.IsVisibleInTree()).ToList();
        var widths = boxes.Select(b => Mathf.RoundToInt(b.Size.X)).Distinct().ToList();
        if (widths.Count > 1) yield return $"{where}: slider values in {widths.Count} widths ({string.Join(", ", widths)} px)";
        foreach (var form in boxes.GroupBy(b => b.GetParent()?.GetParent()))
        {
            var ends = form.Select(b => b.GlobalPosition.X + b.Size.X).ToList();
            if (ends.Max() - ends.Min() > AlignSlack) yield return $"{where}: slider rows in one form end {ends.Max() - ends.Min():0} px apart";
        }
    }

    [NodeTest]
    static async Task The_slider_rule_sees_rows_that_do_not_line_up(TestRunner r)
    {
        var host = new PanelContainer { Size = Ui.Px(300, 200) };
        r.AddChild(host);
        var form = Ui.Form(Ui.PanelLabels);
        host.AddChild(form);
        foreach (var (label, wide, spin) in new[] { ("One", 40, false), ("Two", 140, false), ("Three", 40, true) })
        {
            var s = new HSlider { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = label };
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddChild(s);
            row.AddChild(spin ? new SpinBox() : Ui.ValueBox(s, v => $"{v:0}", wide));
            form.AddChild(new Label { Text = label });
            form.AddChild(row);
        }
        Ui.AlignForm(form);
        await r.Frames(2);
        var seen = Problems(host, "made").ToList();
        Check.That(seen.Any(p => p.Contains("2 widths")) && seen.Any(p => p.Contains("spin box")), "the rule reports two value widths and a spin box: " + string.Join(" | ", seen));
        host.QueueFree();
    }

    [NodeTest]
    static async Task The_slider_rule_sees_a_spin_box_in_a_grid_row(TestRunner r)
    {
        var host = new PanelContainer { Size = Ui.Px(300, 120) };
        r.AddChild(host);
        var form = Ui.Form(Ui.PanelLabels, 3);
        host.AddChild(form);
        form.AddChild(new Label { Text = "Size" });
        form.AddChild(new HSlider { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Size" });
        form.AddChild(new SpinBox());
        Ui.AlignForm(form);
        await r.Frames(2);
        var seen = Problems(host, "made").ToList();
        Check.That(seen.Any(p => p.Contains("spin box")), "the rule sees a spin box in the slider's grid row: " + string.Join(" | ", seen));
        host.QueueFree();
    }

    [NodeTest]
    static async Task Panels_opened_at_their_own_spots_cover_neither_each_other_nor_the_chat_log(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1);
        IHudShell shell = h.Hud;
        h.Hud.Cfg.PanelPos.Clear();
        var keys = new[] { "log", "relief", "dice" };
        foreach (var key in keys)
        {
            if (!shell.Panels[key].Visible) shell.OpenPanel(key);
            await r.Frames(3);
        }
        var vp = h.Hud.GetViewport().GetVisibleRect().Size;
        var log = shell.Panels["log"].GetRect();
        Check.That(log.End.X > vp.X / 2 && log.End.Y > vp.Y / 2, $"the chat log opens in the bottom right ({log})");
        var problems = new List<string>();
        for (int i = 0; i < keys.Length; i++)
            for (int k = i + 1; k < keys.Length; k++)
                if (shell.Panels[keys[i]].GetRect().Intersects(shell.Panels[keys[k]].GetRect()))
                    problems.Add($"{keys[i]} {shell.Panels[keys[i]].GetRect()} covers {keys[k]} {shell.Panels[keys[k]].GetRect()}");
        Check.That(problems.Count == 0, string.Join("\n", problems));
    }

    [NodeTest]
    static async Task The_studio_tool_panels_line_their_rows_up(TestRunner r)
    {
        var h = await StudioTests.Open(r);
        h.Studio.Select(h.Studio.Pack.Pieces[0], frame: true);
        await h.Studio.SetViewAsync("paint");
        await r.Frames(6);
        var problems = new List<string>();
        foreach (var tool in new[] { "brush", "erase", "shape", "fill", "text", "crop", "outline", "measure" })
        {
            h.Studio.Canvas.SetTool(tool);
            await r.Frames(3);
            problems.AddRange(Problems(h.Studio.ToolPanel.Body, "studio " + tool));
        }
        Check.That(problems.Count == 0, string.Join("\n", problems));
    }

    [NodeTest]
    static async Task The_draw_panel_lines_its_rows_up_with_every_tool(TestRunner r)
    {
        var h = await HudTests.Host(r);
        IHudShell shell = h.Hud;
        shell.OpenPanel("paint");
        h.S.Do(new KTable.Core.Protocol.SetGrid { Grid = new GridDef { Kind = "hex", Pitch = 10 } });
        await r.Frames(2);
        var body = shell.Panels["paint"].Body;
        var problems = new List<string>();
        foreach (var tip in new[] { "Freehand", "Line", "Ellipse", "Area", "Fill", "Erase a drawing", "Name" })
        {
            var tile = WindowTests.Descendants(body).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.TooltipText.StartsWith(tip, StringComparison.Ordinal));
            Check.That(tile != null, "the Draw panel has the tile " + tip);
            tile!.EmitSignal(BaseButton.SignalName.Pressed);
            await r.Frames(3);
            problems.AddRange(Problems(shell.Panels["paint"], "draw panel, " + tip));
            var layers = WindowTests.Descendants(body).OfType<Button>().Where(b => b.IsVisibleInTree() && b.Flat && b.GetChildren().OfType<ToolIcon>().Any()).Select(b => b.Size).Distinct().ToList();
            if (layers.Count > 1) problems.Add($"draw panel, {tip}: its icon buttons come in {layers.Count} sizes ({string.Join(", ", layers)})");
        }
        Check.That(problems.Count == 0, string.Join("\n", problems));
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
            ("settings away from a table", () => SettingsDialog.Open(h.Hud, ctx.Cfg)),
            ("invite", () => InviteDialog.Open(ctx)),
            ("properties", () => PropertiesDialog.Open(ctx, node)),
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
        var h = await HudTests.Host(r, sheets: 1, pictured: true);
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
