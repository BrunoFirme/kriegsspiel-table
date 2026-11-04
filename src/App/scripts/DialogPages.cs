using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class DialogPages
{
    public readonly HBoxContainer Root = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    readonly VBoxContainer _nav = new() { CustomMinimumSize = new Vector2(150, 0) };
    readonly Control _stack = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    readonly List<(string name, Button tab, ScrollContainer page)> _pages = new();
    readonly ButtonGroup _group = new();
    readonly string _key;

    internal static readonly Dictionary<string, string> LastPage = new();

    public DialogPages(string key, Vector2 size)
    {
        _key = key;
        Root.CustomMinimumSize = size;
        Root.AddThemeConstantOverride("separation", 0);
        _nav.AddThemeConstantOverride("separation", 2);
        Root.AddChild(_nav);
        var rule = new VSeparator();
        rule.AddThemeConstantOverride("separation", 20);
        Root.AddChild(rule);
        Root.AddChild(_stack);
    }

    public VBoxContainer Add(string name)
    {
        var tab =
            new Button { Text = name, ToggleMode = true, ButtonGroup = _group, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) };
        NavStyle(tab);
        _nav.AddChild(tab);
        var page = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var pad = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_right", 14);
        pad.AddThemeConstantOverride("margin_bottom", 6);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        pad.AddChild(box);
        page.AddChild(pad);
        _stack.AddChild(page);
        _pages.Add((name, tab, page));
        tab.Pressed += () => Show(name);
        return box;
    }

    public void ShowFirst() => Show(LastPage.TryGetValue(_key, out var n) && _pages.Exists(p => p.name == n) ? n : _pages.Count > 0 ? _pages[0].name : "");

    public void Show(string name)
    {
        foreach (var (n, tab, page) in _pages)
        {
            page.Visible = n == name;
            tab.SetPressedNoSignal(n == name);
        }
        LastPage[_key] = name;
    }

    static void NavStyle(Button b)
    {
        StyleBoxFlat Box(float a) => new()
        {
            BgColor = new Color(1, 1, 1, a),
            ContentMarginLeft = 10, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
        b.AddThemeStyleboxOverride("normal", Box(0));
        b.AddThemeStyleboxOverride("hover", Box(0.06f));
        b.AddThemeStyleboxOverride("pressed", Box(0.13f));
        b.AddThemeStyleboxOverride("hover_pressed", Box(0.15f));
        b.AddThemeColorOverride("font_color", Ui.Muted);
        b.AddThemeColorOverride("font_hover_color", Ui.Ink);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        b.AddThemeColorOverride("font_hover_pressed_color", Colors.White);
    }
}
