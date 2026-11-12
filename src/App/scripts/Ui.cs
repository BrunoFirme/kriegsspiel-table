using System;
using Godot;
using KTable.Core;

namespace KTable.App;

public static class Ui
{
    public static readonly Color Ink = new(0.914f, 0.898f, 0.859f);
    public static readonly Color Muted = new(0.914f, 0.898f, 0.859f, 0.62f);
    public static readonly Color Faint = new(0.914f, 0.898f, 0.859f, 0.40f);

    public static readonly Color Accent = new(0.788f, 0.635f, 0.298f);
    public static readonly Color AccentInk = new(0.867f, 0.733f, 0.427f);
    public static readonly Color OnAccent = new(0.11f, 0.09f, 0.055f);
    public static readonly Color Danger = new(0.898f, 0.404f, 0.353f);
    public static readonly Color Alarm = new(0.725f, 0.141f, 0.118f);
    public static readonly Color Good = new(0.549f, 0.776f, 0.431f);

    public static Color Bar { get; private set; } = Of(InterfaceTint.Bar);
    public static Color Surface { get; private set; } = Of(InterfaceTint.Surface);
    public static Color Raised { get; private set; } = Of(InterfaceTint.Raised);
    public static Color Overlay { get; private set; } = Of(InterfaceTint.Overlay);
    public static string TintInUse { get; private set; } = InterfaceTint.Charcoal;
    public static readonly Color Field = new(0, 0, 0, 0.28f);
    public static readonly Color Line = new(1, 1, 1, 0.08f);
    public static readonly Color LineStrong = new(1, 1, 1, 0.14f);

    const float Rest = 0.06f, Hover = 0.10f, On = 0.15f;

    const string UiFonts = "res://fonts/ui/";
    static readonly string[] SystemSans = { "Segoe UI", "Helvetica Neue", "Arial", "Noto Sans" };
    static readonly string[] SystemSerif = { "Georgia", "Cambria", "Times New Roman", "Noto Serif" };

    public static Font Sans() => _sans ??= Bundled("Barlow-Regular.ttf", new SystemFont { FontNames = SystemSans });
    public static Font SansStrong() => _sansStrong ??= Bundled("Barlow-SemiBold.ttf", new SystemFont { FontNames = SystemSans, FontWeight = 600 });
    public static Font SansItalic() => _sansItalic ??= Bundled("Barlow-Italic.ttf", new SystemFont { FontNames = SystemSans, FontItalic = true });
    public static Font Serif() => _serif ??= Bundled("OldStandard-Regular.ttf", new SystemFont { FontNames = SystemSerif });
    static Font? _sans, _sansStrong, _sansItalic, _serif;

    static Font Bundled(string file, SystemFont system) => ResourceLoader.Load<FontFile>(UiFonts + file, null, ResourceLoader.CacheMode.Ignore) is { } face ? face : system;

    public static Font TableSans() => _tableSans ??= ResourceLoader.Load<FontFile>(UiFonts + "Barlow-Regular.ttf") is { } face ? face : new SystemFont { FontNames = SystemSans };
    static Font? _tableSans;

    internal static void DrawFontsAt(float scale)
    {
        bool betweenPixels = !Mathf.IsEqualApprox(scale, Mathf.Round(scale));
        foreach (var font in new[] { Sans(), SansStrong(), SansItalic(), Serif() })
        {
            if (font is FontFile face) face.MultichannelSignedDistanceField = betweenPixels;
            if (font is SystemFont system) system.MultichannelSignedDistanceField = betweenPixels;
        }
    }

    public static StyleBoxFlat Box(Color bg, int radius = 3, int mx = 8, int my = 3) => new()
    {
        BgColor = bg, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = mx, ContentMarginRight = mx, ContentMarginTop = my, ContentMarginBottom = my,
    };

    public static StyleBoxFlat WindowFill(Color bg, int mx = 0, int my = 0)
    {
        var b = Box(bg, 5, mx, my);
        b.CornerRadiusTopLeft = b.CornerRadiusTopRight = 0;
        return b;
    }

    public static StyleBoxFlat Typing(StyleBoxFlat field)
    {
        var b = (StyleBoxFlat)field.Duplicate();
        b.BorderColor = new Color(Accent, 0.85f);
        b.BorderWidthBottom = 2;
        return b;
    }

    static Color White(float a) => new(1, 1, 1, a);

    static Color Of(Rgb c, float a = 1) => new(c.R, c.G, c.B, a);

    public static Color Chip(string tint) => Of(InterfaceTint.Chip(tint));

    public static Color Tinted(Color own) => Of(InterfaceTint.Tinted(new Rgb(own.R, own.G, own.B), TintInUse), own.A);

    public static void Tint(string tint)
    {
        if (_theme != null) return;
        TintInUse = InterfaceTint.Kept(tint);
        Bar = Of(InterfaceTint.Tinted(InterfaceTint.Bar, TintInUse));
        Surface = Of(InterfaceTint.Tinted(InterfaceTint.Surface, TintInUse));
        Raised = Of(InterfaceTint.Tinted(InterfaceTint.Raised, TintInUse));
        Overlay = Of(InterfaceTint.Tinted(InterfaceTint.Overlay, TintInUse));
    }

    public static Theme Theme() => _theme ??= MakeTheme();
    static Theme? _theme;
    static bool _installed;

    public static void InstallTheme()
    {
        if (_installed) return;
        _installed = true;
        ThemeDB.FallbackFont = TableSans();
        ThemeDB.FallbackFontSize = 13;
        ThemeDB.GetDefaultTheme().MergeWith(Theme());
        if (Engine.GetMainLoop() is SceneTree tree) tree.NodeAdded += n => { if (n is Window { ForceNative: false } w) w.TransparentBg = true; };
    }

    static Theme MakeTheme()
    {
        var t = new Theme { DefaultFont = Sans(), DefaultFontSize = 13 };
        Buttons(t);
        Fields(t);
        Bars(t);
        Menus(t);
        Lists(t);
        Windows(t);
        Spacing(t);
        return t;
    }

    static void Buttons(Theme t)
    {
        foreach (var type in new[] { "Button", "MenuButton", "OptionButton", "CheckBox", "CheckButton" })
        {
            bool tick = type is "CheckBox" or "CheckButton";
            t.SetStylebox("normal", type, Box(White(tick ? 0f : Rest), 3, 10, 4));
            t.SetStylebox("hover", type, Box(White(tick ? 0.04f : Hover), 3, 10, 4));
            t.SetStylebox("pressed", type, Box(White(tick ? 0f : On), 3, 10, 4));
            t.SetStylebox("hover_pressed", type, Box(White(tick ? 0.04f : On + 0.04f), 3, 10, 4));
            t.SetStylebox("disabled", type, Box(White(tick ? 0f : 0.03f), 3, 10, 4));
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Ink);
            t.SetColor("font_hover_color", type, Colors.White);
            t.SetColor("font_pressed_color", type, Colors.White);
            t.SetColor("font_hover_pressed_color", type, Colors.White);
            t.SetColor("font_focus_color", type, Ink);
            t.SetColor("font_disabled_color", type, Faint);
            t.SetColor("icon_disabled_color", type, Faint);
        }
        ColourChips(t);
        t.SetColor("font_pressed_color", "CheckBox", Ink);
        t.SetColor("font_pressed_color", "CheckButton", Ink);
        foreach (var type in new[] { "CheckBox", "PopupMenu" })
        {
            t.SetIcon("checked", type, Mark(true, false, 1f));
            t.SetIcon("unchecked", type, Mark(false, false, 1f));
            t.SetIcon("radio_checked", type, Mark(true, true, 1f));
            t.SetIcon("radio_unchecked", type, Mark(false, true, 1f));
            t.SetIcon("checked_disabled", type, Mark(true, false, 0.4f));
            t.SetIcon("unchecked_disabled", type, Mark(false, false, 0.4f));
            t.SetIcon("radio_checked_disabled", type, Mark(true, true, 0.4f));
            t.SetIcon("radio_unchecked_disabled", type, Mark(false, true, 0.4f));
        }
    }

    static void Fields(Theme t)
    {
        foreach (var type in new[] { "LineEdit", "TextEdit", "CodeEdit" })
        {
            int mx = type == "LineEdit" ? 8 : 6, my = type == "LineEdit" ? 4 : 3;
            t.SetStylebox("normal", type, Box(Field, 3, mx, my));
            t.SetStylebox("focus", type, Typing(Box(Field, 3, mx, my)));
            t.SetStylebox("read_only", type, Box(new Color(0, 0, 0, 0.14f), 3, mx, my));
            t.SetColor("font_color", type, Ink);
            t.SetColor("font_readonly_color", type, Muted);
            t.SetColor("font_placeholder_color", type, Faint);
            t.SetColor("caret_color", type, AccentInk);
            t.SetColor("selection_color", type, new Color(Accent, 0.35f));
        }
    }

    static void Bars(Theme t)
    {
        StyleBoxFlat Track(Color c) =>
            new() { BgColor = c, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2, ContentMarginTop = 2, ContentMarginBottom = 2 };
        t.SetStylebox("slider", "HSlider", Track(White(0.10f)));
        t.SetStylebox("grabber_area", "HSlider", Track(new Color(Ink, 0.45f)));
        t.SetStylebox("grabber_area_highlight", "HSlider", Track(new Color(Ink, 0.65f)));
        t.SetStylebox("background", "ProgressBar", Box(White(0.08f), 2, 0, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, 2, 0, 0));
        t.SetColor("font_color", "ProgressBar", Ink);
        foreach (var bar in new[] { "VScrollBar", "HScrollBar" })
        {
            bool v = bar == "VScrollBar";
            StyleBoxFlat Grab(float a) => new()
            {
                BgColor = White(a), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                ContentMarginLeft = v ? 3 : 0, ContentMarginRight = v ? 3 : 0, ContentMarginTop = v ? 0 : 3, ContentMarginBottom = v ? 0 : 3,
            };
            t.SetStylebox("scroll", bar,
                new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ContentMarginLeft = v ? 3 : 0, ContentMarginRight = v ? 3 : 0, ContentMarginTop = v ? 0 : 3, ContentMarginBottom = v ? 0 : 3 });
            t.SetStylebox("scroll_focus", bar, new StyleBoxEmpty());
            t.SetStylebox("grabber", bar, Grab(0.16f));
            t.SetStylebox("grabber_highlight", bar, Grab(0.26f));
            t.SetStylebox("grabber_pressed", bar, Grab(0.34f));
        }
    }

    static void Menus(Theme t)
    {
        t.SetStylebox("panel", "PopupMenu", Box(Overlay, 4, 4, 4));
        t.SetStylebox("hover", "PopupMenu", Box(White(Hover), 3, 4, 2));
        t.SetStylebox("separator", "PopupMenu", new StyleBoxLine { Color = Line, Thickness = 1 });
        t.SetColor("font_color", "PopupMenu", Ink);
        t.SetColor("font_hover_color", "PopupMenu", Colors.White);
        t.SetColor("font_accelerator_color", "PopupMenu", Faint);
        t.SetColor("font_disabled_color", "PopupMenu", Faint);
        t.SetColor("font_separator_color", "PopupMenu", Muted);
        t.SetFontSize("font_separator_size", "PopupMenu", 11);
        t.SetFont("font_separator", "PopupMenu", SansStrong());
        t.SetStylebox("labeled_separator_left", "PopupMenu", new StyleBoxLine { Color = Line, Thickness = 1 });
        t.SetStylebox("labeled_separator_right", "PopupMenu", new StyleBoxLine { Color = Line, Thickness = 1 });
        t.SetStylebox("panel", "TooltipPanel", Box(Overlay, 4, 9, 6));
        t.SetColor("font_color", "TooltipLabel", Ink);
        t.SetFontSize("font_size", "TooltipLabel", 12);
        t.SetStylebox("panel", "PopupPanel", Box(Overlay, 4, 10, 8));
    }

    static void Lists(Theme t)
    {
        t.SetColor("font_color", "Label", Ink);
        foreach (var tabs in new[] { "TabContainer", "TabBar" })
        {
            StyleBoxFlat Tab(Color under, float bg = 0) => new()
            {
                BgColor = White(bg), BorderColor = under, BorderWidthBottom = under.A > 0 ? 2 : 0,
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4,
            };
            t.SetStylebox("tab_selected", tabs, Tab(Ink));
            t.SetStylebox("tab_unselected", tabs, Tab(new Color(0, 0, 0, 0)));
            t.SetStylebox("tab_hovered", tabs, Tab(new Color(0, 0, 0, 0), 0.05f));
            t.SetStylebox("tab_focus", tabs, new StyleBoxEmpty());
            t.SetColor("font_selected_color", tabs, Colors.White);
            t.SetColor("font_unselected_color", tabs, Muted);
            t.SetColor("font_hovered_color", tabs, Ink);
        }
        t.SetStylebox("panel", "TabContainer", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ContentMarginTop = 6 });
        t.SetStylebox("tabbar_background", "TabContainer", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Line, BorderWidthBottom = 1 });
        t.SetStylebox("panel", "ItemList", Box(new Color(0, 0, 0, 0.18f), 3, 4, 4));
        t.SetStylebox("focus", "ItemList", new StyleBoxEmpty());
        t.SetStylebox("selected", "ItemList", Box(White(On), 3, 2, 2));
        t.SetStylebox("selected_focus", "ItemList", Box(White(On), 3, 2, 2));
        t.SetStylebox("hovered", "ItemList", Box(White(Rest), 3, 2, 2));
        t.SetColor("font_color", "ItemList", Ink);
        t.SetColor("font_selected_color", "ItemList", Colors.White);
        t.SetStylebox("panel", "Tree", Box(new Color(0, 0, 0, 0.18f), 3, 4, 4));
        t.SetStylebox("focus", "Tree", new StyleBoxEmpty());
        t.SetStylebox("selected", "Tree", Box(White(On), 3, 2, 2));
        t.SetStylebox("selected_focus", "Tree", Box(White(On), 3, 2, 2));
        t.SetColor("font_color", "Tree", Ink);
        t.SetColor("font_selected_color", "Tree", Colors.White);
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        t.SetStylebox("normal", "RichTextLabel", new StyleBoxEmpty());
        t.SetStylebox("focus", "RichTextLabel", new StyleBoxEmpty());
        t.SetColor("default_color", "RichTextLabel", Ink);
    }

    static void Windows(Theme t)
    {
        var frame = Box(Raised, 5, 0, 0);
        frame.ExpandMarginTop = 34;
        t.SetStylebox("embedded_border", "Window", frame);
        t.SetStylebox("embedded_unfocused_border", "Window", frame);
        foreach (var w in new[] { "Window", "AcceptDialog", "ConfirmationDialog", "FileDialog" })
        {
            t.SetFont("title_font", w, Serif());
            t.SetFontSize("title_font_size", w, 16);
            t.SetColor("title_color", w, Ink);
        }
        t.SetConstant("title_height", "Window", 34);
        t.SetConstant("close_h_offset", "Window", 26);
        t.SetConstant("close_v_offset", "Window", 25);
        t.SetStylebox("panel", "AcceptDialog", WindowFill(Raised, 14, 10));
        t.SetStylebox("panel", "ConfirmationDialog", WindowFill(Raised, 14, 10));
        t.SetStylebox("panel", "FileDialog", WindowFill(Raised, 14, 10));
        t.SetConstant("thumbnail_size", "FileDialog", 112);
    }

    static void Spacing(Theme t)
    {
        t.SetConstant("separation", "HBoxContainer", 6);
        t.SetConstant("separation", "VBoxContainer", 6);
        t.SetConstant("h_separation", "GridContainer", 8);
        t.SetConstant("v_separation", "GridContainer", 6);
        t.SetConstant("separation", "HSeparator", 6);
        t.SetStylebox("separator", "VSeparator", new StyleBoxLine { Color = Line, Vertical = true, Thickness = 1, GrowBegin = -4, GrowEnd = -4 });
        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = Line, Thickness = 1 });
    }

    static ImageTexture Mark(bool on, bool radio, float alpha)
    {
        const int n = 16;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var edge = new Color(Ink, 0.55f * alpha);
        var fill = new Color(Ink, alpha);
        var tick = new Color(Surface, alpha);
        static float Cover(float d) => Mathf.Clamp(0.5f - d, 0f, 1f);
        static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float h = Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            return (p - a - ab * h).Length();
        }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var c = new Vector2(n / 2f, n / 2f);
                float d;
                if (radio) d = (p - c).Length() - 6.5f;
                else
                {
                    var q = (p - c).Abs() - new Vector2(6.5f - 3f, 6.5f - 3f);
                    d = new Vector2(Mathf.Max(q.X, 0), Mathf.Max(q.Y, 0)).Length() + Mathf.Min(Mathf.Max(q.X, q.Y), 0) - 3f;
                }
                var col = new Color(0, 0, 0, 0);
                if (on)
                {
                    float inside = Cover(d);
                    col = new Color(fill, fill.A * inside);
                    float mark = radio ? Cover((p - c).Length() - 2.6f)
                        : Cover(Mathf.Min(Seg(p, new Vector2(4.6f, 8.4f), new Vector2(7f, 10.8f)), Seg(p, new Vector2(7f, 10.8f), new Vector2(11.6f, 5.6f))) - 0.95f);
                    if (mark > 0) col = col.Lerp(new Color(tick, col.A), mark);
                }
                else
                {
                    float ring = Cover(Mathf.Abs(d + 0.6f) - 0.6f);
                    col = new Color(edge, edge.A * ring);
                }
                img.SetPixel(x, y, col);
            }
        return ImageTexture.CreateFromImage(img);
    }

    public static StyleBoxFlat Panel(float a = 0.96f) => new()
    {
        BgColor = new Color(Surface, a), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
        CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
    };

    public static StyleBoxFlat Card(int mx = 8, int my = 6)
    {
        return Box(new Color(Overlay, 0.98f), 4, mx, my);
    }

    public static StyleBoxFlat Pill(float a = 0.06f) => new()
    {
        BgColor = new Color(1, 1, 1, a), ContentMarginLeft = 9, ContentMarginRight = 10, ContentMarginTop = 2, ContentMarginBottom = 2,
        CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
    };

    static Texture2D? _dot;
    public static Texture2D Dot => _dot ??= new GradientTexture2D
    {
        Width = 10, Height = 10, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0.05f),
        Gradient = new Gradient { Offsets = new float[] { 0, 0.72f, 1 }, Colors = new[] { Colors.White, Colors.White, new Color(1, 1, 1, 0) } },
    };

    public static StyleBoxFlat TopBar() => new()
    {
        BgColor = new Color(Bar, 0.97f), ContentMarginLeft = 10, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3,
        BorderColor = Line, BorderWidthBottom = 1,
    };

    public static string Spaced(string t) => t.ToUpperInvariant();

    public static Label Header(string t)
    {
        var l = new Label { Text = Spaced(t), VerticalAlignment = VerticalAlignment.Bottom };
        l.SetMeta(HeaderMeta, t);
        HeadStyle(l);
        var rule = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Line, BorderWidthBottom = 1, ContentMarginBottom = 2, ContentMarginTop = Gap.Block };
        l.AddThemeStyleboxOverride("normal", rule);
        return l;
    }

    public static Label HeadLabel(string t)
    {
        var l = new Label { Text = Spaced(t) };
        HeadStyle(l);
        return l;
    }

    const int HeadSize = 11;

    public static void HeadStyle(Label l)
    {
        l.AddThemeColorOverride("font_color", Muted);
        l.AddThemeFontOverride("font", SansStrong());
        l.AddThemeFontSizeOverride("font_size", HeadSize);
    }

    public static void HeadStyle(TreeItem item, string text)
    {
        item.SetText(0, Spaced(text));
        item.SetCustomColor(0, Muted);
        item.SetCustomFont(0, SansStrong());
        item.SetCustomFontSize(0, HeadSize);
    }

    public static GridContainer Form(int labelWidth = 72, int columns = 2)
    {
        var g = new GridContainer { Columns = columns };
        g.AddThemeConstantOverride("h_separation", Gap.Group);
        g.AddThemeConstantOverride("v_separation", Gap.Tight);
        g.SetMeta("labelWidth", labelWidth);
        return g;
    }

    public static void AlignForm(GridContainer g)
    {
        int w = g.HasMeta("labelWidth") ? g.GetMeta("labelWidth").AsInt32() : 72;
        var kids = g.GetChildren();
        for (int i = 0; i < kids.Count; i++)
        {
            if (kids[i] is not Control c) continue;
            if (i % g.Columns == 0)
            {
                if (c is Label l)
                {
                    MarkRowLabel(l, w);
                    l.ClipText = true;
                }
            }
            else if (c.SizeFlagsHorizontal == Control.SizeFlags.Fill) c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        }
    }

    public const string HeaderMeta = "uiHeader", RowLabelMeta = "uiRowLabel";
    public const int DialogLabels = 128, PanelLabels = 76;

    public static class Gap
    {
        public const int None = 0, Hair = 2, Tight = 4, Row = 6, Group = 8, Block = 12, Section = 20;
    }
    static readonly Color RowLabelInk = new(1, 1, 1, 0.74f);

    public static Label RowLabel(string text, int width) => MarkRowLabel(new Label { Text = text }, width);

    public static Label MarkRowLabel(Label l, int width)
    {
        l.CustomMinimumSize = new Vector2(width, l.CustomMinimumSize.Y);
        l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        l.Modulate = RowLabelInk;
        l.SetMeta(RowLabelMeta, width);
        return l;
    }

    const int CheckInset = 10;

    public static MarginContainer UnderControls(Control c, int labels)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", labels + Gap.Group - (c is CheckBox ? CheckInset : 0));
        c.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        m.AddChild(c);
        return m;
    }

    public static HBoxContainer Fact(string name, string value, int width, Control? extra = null)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", Gap.Group);
        r.AddChild(RowLabel(name, width));
        r.AddChild(new Label
        {
            Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, TooltipText = value, MouseFilter = Control.MouseFilterEnum.Pass,
        });
        if (extra != null)
        {
            extra.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            r.AddChild(extra);
        }
        return r;
    }

    public static Button Primary(string text, Action onPress, string tip = "")
    {
        var b = Btn(text, onPress, tip);
        MakePrimary(b);
        return b;
    }

    public static void MakePrimary(Button b, int mx = 12, int my = 4)
    {
        b.AddThemeStyleboxOverride("normal", Box(Accent, 3, mx, my));
        b.AddThemeStyleboxOverride("hover", Box(Accent.Lightened(0.12f), 3, mx, my));
        b.AddThemeStyleboxOverride("pressed", Box(Accent.Darkened(0.12f), 3, mx, my));
        b.AddThemeStyleboxOverride("hover_pressed", Box(Accent.Darkened(0.06f), 3, mx, my));
        b.AddThemeStyleboxOverride("disabled", Box(White(0.05f), 3, mx, my));
        b.AddThemeFontOverride("font", SansStrong());
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            b.AddThemeColorOverride(c, OnAccent);
        b.AddThemeColorOverride("font_disabled_color", Faint);
    }

    public static void MarkTool(Button b)
    {
        b.AddThemeStyleboxOverride("pressed", Box(new Color(Accent, 0.30f), 3, 10, 4));
        b.AddThemeStyleboxOverride("hover_pressed", Box(new Color(Accent, 0.38f), 3, 10, 4));
    }

    public static T Bare<T>(T b) where T : Button
    {
        b.AddThemeStyleboxOverride("normal", Box(new Color(0, 0, 0, 0), 3, 10, 4));
        b.AddThemeStyleboxOverride("hover", Box(White(0.08f), 3, 10, 4));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0, 0, 0, 0), 3, 10, 4));
        return b;
    }

    public static Button Marked(Button b) { MarkTool(b); return b; }

    public static void MakeQuiet(Button b)
    {
        b.AddThemeStyleboxOverride("normal", Box(new Color(0, 0, 0, 0), 3, 10, 4));
        b.AddThemeColorOverride("font_color", Muted);
    }

    public static HBoxContainer Actions(params Button[] buttons)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", Gap.Row);
        foreach (var b in buttons) { b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; h.AddChild(b); }
        return h;
    }

    public static Label Readout(string text, int width)
    {
        var l = new Label { Text = text, CustomMinimumSize = new Vector2(width, 0), HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        l.AddThemeColorOverride("font_color", Muted);
        return l;
    }

    public static void WhenPicked(ColorPickerButton button, Action<Color> picked) =>
        button.PopupClosed += () => { var colour = button.Color; Callable.From(() => picked(colour)).CallDeferred(); };

    const int MostValueSamples = 5000;
    static readonly System.Text.RegularExpressions.Regex Number = new(@"-?\d[\d,]*(\.\d+)?|-?\.\d+");

    public static LineEdit ValueBox(HSlider s, Func<double, string> show, int width = 0)
    {
        var box = new LineEdit
        {
            Text = show(s.Value), Alignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            SelectAllOnFocus = true, ContextMenuEnabled = false, CaretBlink = true,
        };
        box.Editable = NumberIn(show(s.MinValue)) != null || NumberIn(show(s.MaxValue)) != null;
        box.TooltipText = box.Editable ? "Click to type a value" : "";
        var quiet = Box(new Color(0, 0, 0, 0), 3, 4, 1);
        var hover = Box(White(Rest), 3, 4, 1);
        box.AddThemeStyleboxOverride("normal", quiet);
        box.AddThemeStyleboxOverride("read_only", quiet);
        box.AddThemeStyleboxOverride("focus", Typing(Box(Field, 3, 4, 1)));
        box.AddThemeColorOverride("font_color", Muted);
        box.AddThemeColorOverride("font_uneditable_color", Muted);
        box.MouseEntered += () => { if (box.Editable) box.AddThemeStyleboxOverride("normal", hover); };
        box.MouseExited += () => box.AddThemeStyleboxOverride("normal", quiet);
        box.FocusEntered += () => box.AddThemeColorOverride("font_color", Ink);
        void Commit()
        {
            box.AddThemeColorOverride("font_color", Muted);
            if (TypedNumber(box.Text) is { } want && NearestShowing(s, show, want) is { } v && !Mathf.IsEqualApprox(v, s.Value))
            {
                s.Value = v;
                s.EmitSignal(Slider.SignalName.DragEnded, true);
            }
            box.Text = show(s.Value);
        }
        box.TextSubmitted += _ => box.ReleaseFocus();
        box.FocusExited += Commit;
        box.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
            box.Text = show(s.Value);
            box.ReleaseFocus();
            box.AcceptEvent();
        };
        s.ValueChanged += v => { if (!box.HasFocus()) box.Text = show(v); };
        return box;
    }

    static double? TypedNumber(string text) => NumberIn(text.Contains('.') ? text : text.Replace(',', '.'));

    static double? NumberIn(string text)
    {
        var m = Number.Match(text);
        return m.Success && double.TryParse(m.Value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    static double? NearestShowing(Godot.Range s, Func<double, string> show, double want)
    {
        double lo = s.MinValue, hi = s.MaxValue;
        int n = s.Step > 0 ? (int)Math.Min(Math.Ceiling((hi - lo) / s.Step), MostValueSamples) : MostValueSamples;
        if (n <= 0) return null;
        double gap = double.MaxValue;
        int first = -1, last = -1;
        for (int i = 0; i <= n; i++)
        {
            if (NumberIn(show(At(i))) is not { } got) continue;
            double d = Math.Abs(got - want);
            if (d < gap - 1e-9)
            {
                gap = d;
                first = last = i;
            }
            else if (Math.Abs(d - gap) <= 1e-9 && last == i - 1) last = i;
        }
        return first < 0 ? null : At((first + last) / 2);
        double At(int i) => Math.Min(hi, lo + (hi - lo) * i / n);
    }

    public static Button Swatch(Color colour, Vector2 size, string tip, bool chosen = false)
    {
        var b = new Button { CustomMinimumSize = size, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
        var rest = SwatchBox(colour, chosen ? AccentInk : LineStrong, chosen ? 2 : 1);
        var hover = SwatchBox(colour, chosen ? AccentInk : White(0.40f), chosen ? 2 : 1);
        foreach (var st in new[] { "normal", "pressed", "focus", "disabled" }) b.AddThemeStyleboxOverride(st, rest);
        foreach (var st in new[] { "hover", "hover_pressed" }) b.AddThemeStyleboxOverride(st, hover);
        return b;
    }

    static StyleBoxFlat SwatchBox(Color colour, Color edge, int width, int radius = 3)
    {
        var b = Box(colour, radius, width, width);
        b.BorderColor = edge;
        b.SetBorderWidthAll(width);
        return b;
    }

    static void ColourChips(Theme t)
    {
        const string Type = "ColorPickerButton";
        const int Edge = 1, Corner = 2;
        var clear = new Color(0, 0, 0, 0);
        foreach (var st in new[] { "normal", "pressed", "disabled" }) t.SetStylebox(st, Type, SwatchBox(clear, LineStrong, Edge, Corner));
        foreach (var st in new[] { "hover", "hover_pressed" }) t.SetStylebox(st, Type, SwatchBox(clear, White(0.40f), Edge, Corner));
        t.SetStylebox("focus", Type, new StyleBoxEmpty());
    }

    public static Label Hint(string text)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = Muted, CustomMinimumSize = new Vector2(200, 0) };
        l.AddThemeFontSizeOverride("font_size", 12);
        return l;
    }

    public static Button Btn(string text, Action onPress, string tip = "")
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += onPress;
        return b;
    }

    public static Button IconBtn(string kind, string tip, string detail = "")
    {
        var b = new Button { CustomMinimumSize = new Vector2(30, 30), FocusMode = Control.FocusModeEnum.None, TooltipText = detail.Length > 0 ? $"{tip}\n{detail}" : tip };
        b.AddChild(new ToolIcon { Kind = kind });
        return b;
    }

    public static void Open(Node parent, Window dlg, Vector2I? size = null)
    {
        dlg.WrapControls = false;
        parent.AddChild(dlg);
        bool ownSize = dlg.Size.X > 120 || dlg.Size.Y > 120;
        dlg.PopupCentered(size ?? (ownSize ? dlg.Size : new Vector2I(200, 20)));
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(dlg) || !dlg.Visible) return;
            if (dlg is AcceptDialog ad) Slim(ad);
            dlg.WrapControls = true;
            int titled = (int)dlg.GetThemeFont("title_font").GetStringSize(dlg.Title, HorizontalAlignment.Left, -1, dlg.GetThemeFontSize("title_font_size")).X + 2 * dlg.GetThemeConstant("close_h_offset") + 28;
            if (dlg.Size.X < titled) dlg.Size = dlg.Size with { X = titled };
            dlg.MoveToCenter();
        }).CallDeferred();
    }

    public static Question Confirm(Node parent, string title, string okText, Action onOk, string? text = null, int width = 360) =>
        InPlace.Ask(parent, title, okText, onOk, text, width: width);

    static readonly Texture2D NoIcon = new PlaceholderTexture2D { Size = Vector2.Zero };

    public static void NoCloseButton(Window w)
    {
        w.AddThemeIconOverride("close", NoIcon);
        w.AddThemeIconOverride("close_pressed", NoIcon);
    }

    public static void FreeOnClose(Window w) =>
        w.VisibilityChanged += () => { if (GodotObject.IsInstanceValid(w) && !w.Visible) w.QueueFree(); };

    public const string CloseWord = "Close";

    public static void Slim(AcceptDialog dlg)
    {
        if (dlg.GetOkButton().GetParent() is HBoxContainer bar)
        {
            bool first = true;
            foreach (var c in bar.GetChildren())
                if (c is Control ctl and not Button) { ctl.Visible = first; first = false; }
            bar.AddThemeConstantOverride("separation", Gap.Group);
        }
        dlg.GetOkButton().CustomMinimumSize = new Vector2(80, 0);
        if (dlg.OkButtonText != CloseWord) MakePrimary(dlg.GetOkButton());
        if (dlg is ConfirmationDialog cd) cd.GetCancelButton().CustomMinimumSize = new Vector2(80, 0);
    }

    static string LastFolderFile => ProjectSettings.GlobalizePath("user://last-folder.txt");

    internal static Settings? FoldersIn { get; set; }

    public static void KeepFolderLists(Settings cfg)
    {
        FoldersIn = cfg;
        FileDialog.SetFavoriteList(cfg.FolderFavourites.ToArray());
        FileDialog.SetRecentList(cfg.RecentFolders.ToArray());
    }

    public static void SaveFolderLists()
    {
        if (FoldersIn is { } cfg && cfg.TakeFolders(FileDialog.GetFavoriteList(), FileDialog.GetRecentList())) cfg.Save();
    }

    public static void RememberFolder(FileDialog fd)
    {
        BestEffort.Try(() => { if (System.IO.File.Exists(LastFolderFile) && System.IO.File.ReadAllText(LastFolderFile).Trim() is { Length: > 0 } d
            && System.IO.Directory.Exists(d)) fd.CurrentDir = d; });
        void Save()
        {
            BestEffort.Try(() => { var d = fd.CurrentDir; if (!string.IsNullOrEmpty(d) && System.IO.Directory.Exists(d)) System.IO.File.WriteAllText(LastFolderFile, d); });
            SaveFolderLists();
        }
        fd.Confirmed += Save;
        fd.Canceled += Save;
        fd.VisibilityChanged += () => { if (!fd.Visible) Save(); };
    }

    static readonly Vector2 LeastRoom = new(1100, 600);
    const float PlainDpi = 96f, WindowsScaleStep = 0.05f;
    static Settings? _scaleFrom;
    static Window? _scaled;

    public static void ApplyScale(Window root, Settings cfg)
    {
        _scaleFrom = cfg;
        if (_scaled != root)
        {
            _scaled = root;
            root.SizeChanged += Rescale;
            root.GetTree().NodeAdded += OnScreen.Watch;
        }
        Rescale();
    }

    public static float WindowsScale(Window root)
    {
        float dpi = DisplayServer.ScreenGetDpi(root.CurrentScreen);
        return dpi > 0 ? Mathf.Max(Mathf.Snapped(dpi / PlainDpi, WindowsScaleStep), 1f) : 1f;
    }

    public static float Fitted(Window root) => Settings.Fitted(WindowsScale(root), root.Size.Y);

    public static float WantedScale(Window root, Settings cfg) => cfg.UiScaleFor(WindowsScale(root), root.Size.Y);

    public static float ScaleThatFits(Window root, float wanted)
    {
        var room = (Vector2)root.Size;
        float fits = Mathf.Max(Mathf.Min(room.X / LeastRoom.X, room.Y / LeastRoom.Y), Settings.UiScaleMin);
        return Mathf.Clamp(Mathf.Min(wanted, fits), Settings.UiScaleMin, Settings.FittedMost * Settings.UiScaleMax);
    }

    static void Rescale()
    {
        if (_scaled == null || !GodotObject.IsInstanceValid(_scaled)) return;
        if (_scaleFrom == null) return;
        float scale = ScaleThatFits(_scaled, WantedScale(_scaled, _scaleFrom));
        var root = _scaled;
        Callable.From(() => { if (GodotObject.IsInstanceValid(root)) OnScreen.FitAll(root); }).CallDeferred();
        if (Mathf.IsEqualApprox(_scaled.ContentScaleFactor, scale)) return;
        _scaled.ContentScaleFactor = scale;
        _scaled.OversamplingOverride = scale;
        DrawFontsAt(scale);
    }

    public static void SwallowWheel(Control panel) =>
        panel.GuiInput += e =>
            { if (e is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight }) panel.AcceptEvent(); };

    static Texture2D? _loadingIcon;
    public static Texture2D LoadingIcon
    {
        get
        {
            if (_loadingIcon != null) return _loadingIcon;
            var img = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
            img.Fill(new Color(1, 1, 1, 0.06f));
            for (int i = 0; i < 3; i++)
                for (int y = 29; y < 35; y++) for (int x = 18 + i * 14; x < 24 + i * 14; x++) img.SetPixel(x, y, new Color(1, 1, 1, 0.35f));
            return _loadingIcon = ImageTexture.CreateFromImage(img);
        }
    }

    public static void Place(Control c, Control.LayoutPreset anchors, float l, float t, float r, float b)
    {
        c.SetAnchorsPreset(anchors);
        c.OffsetLeft = l;
        c.OffsetTop = t;
        c.OffsetRight = r;
        c.OffsetBottom = b;
    }
}

public partial class LoadingStrip : PanelContainer
{
    Assets? _assets;
    Label _text = null!;
    ProgressBar _bar = null!;
    double _idle;

    public void Track(Assets a) => _assets = a;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", Ui.Panel(0.94f));
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        AddChild(row);
        _text = new Label { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _text.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(_text);
        _bar = new ProgressBar { CustomMinimumSize = new Vector2(160, 8), ShowPercentage = false, SizeFlagsVertical = SizeFlags.ShrinkCenter, MinValue = 0, MaxValue = 1 };
        _bar.AddThemeStyleboxOverride("background", Ui.Box(Ui.Line, 2, 0, 0));
        _bar.AddThemeStyleboxOverride("fill", Ui.Box(Ui.Accent, 2, 0, 0));
        row.AddChild(_bar);
        SetAnchorsPreset(LayoutPreset.CenterBottom);
        OffsetTop = -64;
        OffsetBottom = -36;
        OffsetLeft = -200;
        OffsetRight = 200;
    }

    public override void _Process(double delta)
    {
        if (_assets == null) return;
        if (_assets.Shows)
        {
            _idle = 0;
            int total = Math.Max(1, _assets.BatchTotal), done = Math.Min(_assets.BatchDone, total);
            var jobs = _assets.Jobs;
            double jp = _assets.JobProgress;
            var what = jobs.Count > 0 ? jobs[^1] + (jp >= 0 ? $"   ·   {jp * 100:0} %" : total > done ? $"   ·   {done} / {total}" : "")
                : _assets.PendingFiles > 0 ? $"Loading… {done} / {total}   ({_assets.PendingFiles} files from the host)" : $"Loading pictures and map tiles… {done} / {total}";
            _text.Text = what;
            _bar.Value = jobs.Count > 0 && jp >= 0 ? jp : jobs.Count > 0 && total <= done ? 0.5 : done / (double)total;
            if (!Visible) { Visible = true; ResetSize(); }
            return;
        }
        if (!Visible) return;
        _idle += delta;
        _bar.Value = 1;
        if (_idle > 0.6) Visible = false;
    }
}
