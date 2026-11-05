using System;
using System.IO;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class TitleStyle
{
    public static readonly Color Ink = Ui.Ink;
    public static readonly Color Muted = Ui.Muted;
    public static readonly Color Brass = Ui.Accent;
    public static readonly Color Paper = new(Ui.Surface, 0.88f);

    public static Font Serif() => Ui.Serif();
    public static Font Sans() => Ui.Sans();
    public static Font SansMedium() => Ui.SansStrong();

    static void CheckBoxesAsTheAppDraws(Theme t)
    {
        var app = Ui.Theme();
        foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" }) t.SetStylebox(st, "CheckBox", app.GetStylebox(st, "CheckBox"));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_disabled_color" }) t.SetColor(c, "CheckBox", app.GetColor(c, "CheckBox"));
    }

    public static Theme LobbyTheme()
    {
        var t = new Theme { DefaultFont = Sans(), DefaultFontSize = 15 };
        StyleBoxFlat Box(Color bg) => Ui.Box(bg, 6, 12, 8);
        var field = new Color(1, 1, 1, 0.045f);
        t.SetStylebox("normal", "LineEdit", Box(field));
        t.SetStylebox("focus", "LineEdit", Ui.Typing(Box(field)));
        t.SetColor("font_color", "LineEdit", Ink);
        t.SetColor("font_placeholder_color", "LineEdit", Ui.Faint);
        t.SetStylebox("normal", "Button", Box(new Color(1, 1, 1, 0.06f)));
        t.SetStylebox("hover", "Button", Box(new Color(1, 1, 1, 0.10f)));
        t.SetStylebox("pressed", "Button", Box(new Color(1, 1, 1, 0.15f)));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetStylebox("disabled", "Button", Box(new Color(1, 1, 1, 0.03f)));
        t.SetColor("font_color", "Button", Ink);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_pressed_color", "Button", Colors.White);
        t.SetColor("font_disabled_color", "Button", Ui.Faint);
        CheckBoxesAsTheAppDraws(t);
        t.SetColor("font_color", "Label", Ink);
        t.SetStylebox("panel", "ScrollContainer", new StyleBoxEmpty());
        return t;
    }

    public static Label Caps(string t)
    {
        var l = new Label { Text = t.ToUpperInvariant(), Modulate = Muted };
        l.AddThemeFontSizeOverride("font_size", 11);
        return l;
    }

    public static Label Section(string t) => Ui.Header(t);

    public static Button Primary(string text, Action a)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(180, 40), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        b.AddThemeStyleboxOverride("normal",
            new StyleBoxFlat { BgColor = Brass, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, ContentMarginLeft = 18,
                ContentMarginRight = 18 });
        b.AddThemeStyleboxOverride("hover",
            new StyleBoxFlat { BgColor = Brass.Lightened(0.12f), CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, ContentMarginLeft = 18,
                ContentMarginRight = 18 });
        b.AddThemeStyleboxOverride("disabled",
            new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.05f), CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, ContentMarginLeft = 18,
                ContentMarginRight = 18 });
        b.AddThemeFontOverride("font", SansMedium());
        b.AddThemeColorOverride("font_color", Ui.OnAccent);
        b.AddThemeColorOverride("font_hover_color", Ui.OnAccent);
        b.AddThemeFontSizeOverride("font_size", 16);
        b.Pressed += a;
        return b;
    }

    public static StyleBoxFlat CardStyle(bool on) => Ui.Box(on ? new Color(Brass, 0.18f) : new Color(1, 1, 1, 0.05f), 8, 12, 10);

    public static Button MoreBtn(string tip)
    {
        var b = Ui.IconBtn("more", tip);
        b.Flat = true;
        b.CustomMinimumSize = new Vector2(28, 28);
        b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        b.MouseFilter = Control.MouseFilterEnum.Stop;
        return b;
    }

    public static (PanelContainer row, HBoxContainer line) Row(string name)
    {
        var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        row.AddThemeStyleboxOverride("panel", CardStyle(false));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 10);
        row.AddChild(h);
        var nm = new Label { Text = name, ClipText = true, CustomMinimumSize = new Vector2(260, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f };
        nm.AddThemeFontOverride("font", Serif());
        nm.AddThemeFontSizeOverride("font_size", 16);
        h.AddChild(nm);
        return (row, h);
    }

    public static Label CardLine(string text, Color colour)
    {
        var l = new Label { Text = text, Modulate = colour, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(226, 0) };
        l.AddThemeFontSizeOverride("font_size", 12);
        return l;
    }

    public static readonly Color Warning = Ui.Danger;

    public static string PieceCount(int n) => n == 1 ? "1 piece" : n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " pieces";

    public static string ShortClock(GameClock c) =>
        DateTime.TryParse(c.GameTime, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) ? t.ToString("d MMM HH:mm") : c.GameTime;

    public static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 2 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : d.TotalDays < 14 ? $"{(int)d.TotalDays} days ago" : t.ToString("d MMM yyyy");
    }

    public static string BuildInfo()
    {
        var f = Path.Combine(ProjectSettings.GlobalizePath("res://"), "BUILD.txt");
        return File.Exists(f) ? File.ReadAllText(f).Trim() : "development build";
    }
}
