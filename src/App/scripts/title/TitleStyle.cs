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
        h.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(h);
        var nm = new Label { Text = name, ClipText = true, CustomMinimumSize = new Vector2(260, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f };
        nm.AddThemeFontOverride("font", Serif());
        nm.AddThemeFontSizeOverride("font_size", 16);
        h.AddChild(nm);
        return (row, h);
    }

    public static HFlowContainer Grid()
    {
        var grid = new HFlowContainer();
        grid.AddThemeConstantOverride("h_separation", Ui.Gap.Block);
        grid.AddThemeConstantOverride("v_separation", Ui.Gap.Block);
        return grid;
    }

    public static (PanelContainer card, VBoxContainer face, HBoxContainer nameRow) CardFace(Texture2D? thumb, string name, bool plate)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2(250, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", CardStyle(false));
        var cv = new VBoxContainer();
        card.AddChild(cv);
        if (thumb != null) cv.AddChild(new TextureRect { Texture = thumb, CustomMinimumSize = new Vector2(226, 130), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered });
        else if (plate)
        {
            var blank = new Panel { CustomMinimumSize = new Vector2(226, 130), MouseFilter = Control.MouseFilterEnum.Ignore };
            blank.AddThemeStyleboxOverride("panel",
                new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.03f), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 });
            cv.AddChild(blank);
        }
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        var label = new Label { Text = name, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(196, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontOverride("font", Serif());
        label.AddThemeFontSizeOverride("font_size", 18);
        nameRow.AddChild(label);
        return (card, cv, nameRow);
    }

    const int PlusCardSize = 44;
    const float PlusCardHover = 0.09f;

    public static PanelContainer PlusCard(string name, string tip, Action open)
    {
        var (card, cv, nameRow) = CardFace(null, name, plate: true);
        if (cv.GetChild(0) is Panel plate)
        {
            var plus = new Label { Text = "+", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            plus.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            plus.AddThemeFontSizeOverride("font_size", PlusCardSize);
            plus.AddThemeColorOverride("font_color", Muted);
            plate.AddChild(plus);
        }
        cv.AddChild(nameRow);
        card.TooltipText = tip;
        card.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        card.MouseEntered += () => card.AddThemeStyleboxOverride("panel", Ui.Box(new Color(1, 1, 1, PlusCardHover), 8, 12, 10));
        card.MouseExited += () => card.AddThemeStyleboxOverride("panel", CardStyle(false));
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) open();
        };
        return card;
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
