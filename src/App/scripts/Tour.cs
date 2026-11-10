using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

static class Tour
{
    sealed record Step(string Title, string Text, Func<Control?> Target);

    public const int AtTableFrom = 4, Steps = 7;
    const float CardWidth = 340, Beside = 16, Edge = 12;
    static readonly Color MarkFill = new(Ui.Accent, 0.22f);

    static PanelContainer? _card;
    static ColorRect? _mark;

    public static bool Pending(Settings cfg) => cfg.Tour >= 0 && cfg.Tour < Steps;

    public static void Restart(Settings cfg)
    {
        cfg.Tour = 0;
        cfg.Save();
    }

    public static void AtTitle(Control root, Settings cfg, Func<string, Control?> nav)
    {
        if (cfg.Tour < 0 || cfg.Tour >= AtTableFrom) return;
        var steps = new List<Step>
        {
            new("Welcome", "A table for umpired Kriegsspiel: map, pieces, dice and turns, live over the network or by post.", () => null),
            new("Scenarios", "Open one to host its table. New blank scenario starts an empty one, and a converted module arrives here as a scenario.", () => nav("Scenarios")),
            new("Piece studio", "Pieces live in packs: make and change them here, or convert a digital tabletop module into a pack and a scenario.", () => nav("Piece studio")),
            new("Help", "Every guide, searchable. F1 opens it from anywhere, at the section of the tool under the pointer.", () => nav("Help")),
        };
        Show(root, cfg, steps, 0, null);
    }

    public static void AtTable(Hud hud, Settings cfg)
    {
        if (!Pending(cfg)) return;
        if (cfg.Tour < AtTableFrom) cfg.Tour = AtTableFrom;
        var keys = cfg.Keys;
        var steps = new List<Step>
        {
            new("Tools", "Select, ruler, formations, drawing, relief and the rest. Each tooltip names its key; F1 over a tool opens its section.", () => hud.Strip.Panel),
            new("Pieces", $"Drag to carry, right-click for everything else. {keys.Label("turnLeft")} and {keys.Label("turnRight")} turn, {keys.Label("larger")} and {keys.Label("smaller")} size, "
                + $"{keys.Label("statePrev")} and {keys.Label("stateNext")} step through looks, and the wheel zooms.", () => null),
            new("The menu", "Saves, the scenario's properties, settings and Help. Esc opens it too.", () => hud.Top.MenuButton),
        };
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hud.AddChild(root);
        Show(root, cfg, steps, AtTableFrom, () => GuideReader.Show(hud.GetTree().Root));
    }

    static void Close()
    {
        if (_card != null && GodotObject.IsInstanceValid(_card)) _card.QueueFree();
        if (_mark != null && GodotObject.IsInstanceValid(_mark)) _mark.QueueFree();
        _card = null;
        _mark = null;
    }

    static void Show(Control root, Settings cfg, List<Step> steps, int first, Action? help)
    {
        Close();
        int i = cfg.Tour - first;
        if (i < 0 || i >= steps.Count) return;
        var step = steps[i];
        bool last = first + i == Steps - 1, toTable = first + i == AtTableFrom - 1;
        void Go(int to)
        {
            cfg.Tour = to;
            cfg.Save();
            if (to - first < steps.Count && to >= 0) Show(root, cfg, steps, first, help);
            else Close();
        }

        var card = new PanelContainer { CustomMinimumSize = new Vector2(CardWidth, 0), MouseFilter = Control.MouseFilterEnum.Stop, Theme = Ui.Theme() };
        card.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Raised, 6, 16, 14));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Gap.Row);
        card.AddChild(box);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var title = new Label { Text = step.Title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        title.AddThemeFontOverride("font", Ui.SansStrong());
        head.AddChild(title);
        head.AddChild(new Label { Text = $"{cfg.Tour + 1} / {Steps}", Modulate = Ui.Muted });
        box.AddChild(head);
        box.AddChild(new Label { Text = step.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(CardWidth - 32, 0) });
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var skip = Ui.Btn(last ? "Done" : "Skip the tour", () => Go(-1));
        skip.Flat = true;
        buttons.AddChild(skip);
        if (last && help != null) buttons.AddChild(Ui.Primary("Open Help", () => { Go(-1); help(); }));
        else buttons.AddChild(Ui.Primary(toTable ? "Continue at a table" : "Next", () => Go(cfg.Tour + 1)));
        box.AddChild(buttons);

        var target = step.Target();
        if (target != null && GodotObject.IsInstanceValid(target) && target.IsVisibleInTree())
        {
            _mark = new ColorRect { Color = MarkFill, MouseFilter = Control.MouseFilterEnum.Ignore };
            root.AddChild(_mark);
        }
        root.AddChild(card);
        _card = card;
        Callable.From(() => Place(root, card, target)).CallDeferred();
    }

    static void Place(Control root, PanelContainer card, Control? target)
    {
        if (!GodotObject.IsInstanceValid(card)) return;
        var view = root.GetViewportRect().Size;
        var size = card.GetCombinedMinimumSize();
        card.Size = size;
        Vector2 at;
        if (target != null && GodotObject.IsInstanceValid(target) && target.IsVisibleInTree())
        {
            var r = target.GetGlobalRect();
            if (_mark != null && GodotObject.IsInstanceValid(_mark)) { _mark.GlobalPosition = r.Position; _mark.Size = r.Size; }
            at = r.End.X + Beside + size.X <= view.X - Edge ? new Vector2(r.End.X + Beside, r.Position.Y)
                : r.Position.X - Beside - size.X >= Edge ? new Vector2(r.Position.X - Beside - size.X, r.Position.Y)
                : new Vector2(r.Position.X, r.End.Y + Beside);
        }
        else at = (view - size) / 2;
        card.GlobalPosition = new Vector2(Mathf.Clamp(at.X, Edge, Mathf.Max(Edge, view.X - size.X - Edge)), Mathf.Clamp(at.Y, Edge, Mathf.Max(Edge, view.Y - size.Y - Edge)));
    }
}
