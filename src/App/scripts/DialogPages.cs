using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class DialogPages
{
    public readonly HBoxContainer Root = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    readonly VBoxContainer _nav = new() { CustomMinimumSize = Ui.Px(150, 0) };
    readonly Control _stack = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
    readonly List<(string name, Button tab, ScrollContainer page)> _pages = new();
    readonly ButtonGroup _group = new();
    readonly string _key;

    internal static readonly Dictionary<string, string> LastPage = new();
    public const string PageMeta = "uiPage";

    public DialogPages(string key, Vector2 size)
    {
        _key = key;
        Root.CustomMinimumSize = Ui.Px(size);
        Root.AddThemeConstantOverride("separation", Ui.Gap.None);
        _nav.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        Root.AddChild(_nav);
        var rule = new VSeparator();
        rule.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Section));
        Root.AddChild(rule);
        Root.AddChild(_stack);
    }

    public VBoxContainer Add(string name)
    {
        var tab =
            new Button { Text = name, ToggleMode = true, ButtonGroup = _group, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(0, 30) };
        NavStyle(tab);
        _nav.AddChild(tab);
        var page = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        page.SetMeta(PageMeta, name);
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var pad = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_right", Ui.Px(14));
        pad.AddThemeConstantOverride("margin_bottom", Ui.Px(6));
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
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

    const int NavIndent = 10;

    internal static void NavStyle(Button b, int indent = NavIndent)
    {
        StyleBoxFlat Box(float a) => Ui.Scaled(new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, a),
            ContentMarginLeft = indent, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
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
