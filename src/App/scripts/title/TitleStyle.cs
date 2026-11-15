using System;
using System.IO;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class TitleStyle
{
    public static readonly Color Ink = Ui.Ink;
    public static readonly Color Muted = Ui.Muted;
    public static Color Brass => Ui.Accent;

    public const int ControlRadius = 3, CardRadius = 4, FrameRadius = 5, ColumnWidth = 260;
    const float FloatingAlpha = 0.96f;
    const int PrimaryHeight = 34, PrimaryInset = 18;

    public static StyleBoxFlat Floating(int mx, int my) => Ui.Box(new Color(Ui.Surface, FloatingAlpha), FrameRadius, mx, my);

    public static Font Serif() => Ui.Serif();
    public static Font Sans() => Ui.Sans();
    public static Font SansMedium() => Ui.SansStrong();

    public const int NameSize = 15;

    public static Label ItemName(Label l, int size = NameSize)
    {
        l.AddThemeFontOverride("font", SansMedium());
        l.AddThemeFontSizeOverride("font_size", Ui.Px(size));
        return l;
    }

    public static Theme LobbyTheme() => new() { DefaultFont = Sans(), DefaultFontSize = Ui.Px(15) };

    public static Label Section(string t) => Ui.Header(t);

    public static Button Primary(string text, Action a)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(0, PrimaryHeight), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        Ui.MakePrimary(b, PrimaryInset, 0);
        b.Pressed += a;
        return b;
    }

    public static StyleBoxFlat CardStyle(bool on) => Ui.Box(on ? new Color(Brass, 0.18f) : new Color(1, 1, 1, 0.05f), CardRadius, 12, 10);

    public static Button MoreBtn(string tip)
    {
        var b = Ui.IconBtn("more", tip);
        b.Flat = true;
        b.CustomMinimumSize = Ui.Px(MoreWidth, MoreWidth);
        b.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        b.MouseFilter = Control.MouseFilterEnum.Stop;
        return b;
    }

    public static (PanelContainer row, HBoxContainer line) Row(string name)
    {
        var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        row.AddThemeStyleboxOverride("panel", CardStyle(false));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Group));
        row.AddChild(h);
        var nm = new Label { Text = name, ClipText = true, CustomMinimumSize = Ui.Px(260, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.4f };
        h.AddChild(ItemName(nm));
        return (row, h);
    }

    public static CardGrid Grid() => new();

    public const int CardWidth = 220, CardInset = 24, PlateWidth = CardWidth - CardInset, NameWidth = PlateWidth - MoreWidth - Ui.Gap.Tight;
    public const int MoreWidth = 28;
    const int NameLines = 2;
    const float PlateAspect = 130f / 226f;
    static readonly int PlateHeight = (int)MathF.Round(PlateWidth * PlateAspect);

    public static (PanelContainer card, VBoxContainer face, HBoxContainer nameRow) CardFace(Texture2D? thumb, string name, bool plate)
    {
        var card = new PanelContainer { CustomMinimumSize = Ui.Px(CardWidth, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        card.AddThemeStyleboxOverride("panel", CardStyle(false));
        var cv = new VBoxContainer();
        cv.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        card.AddChild(cv);
        Control? shown = null;
        if (thumb != null) cv.AddChild(shown = new TextureRect { Texture = thumb, CustomMinimumSize = Ui.Px(PlateWidth, PlateHeight), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered });
        else if (plate)
        {
            var blank = new Panel { CustomMinimumSize = Ui.Px(PlateWidth, PlateHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
            shown = blank;
            blank.AddThemeStyleboxOverride("panel",
                Ui.Box(new Color(1, 1, 1, 0.03f), ControlRadius, 0, 0));
            cv.AddChild(blank);
        }
        if (shown != null)
            card.Resized += () => shown.CustomMinimumSize = new Vector2(Ui.Px(PlateWidth), MathF.Round(Math.Max(Ui.Px(PlateWidth), card.Size.X - Ui.Px(CardInset)) * PlateAspect));
        if (shown != null) cv.AddChild(new Control { CustomMinimumSize = Ui.Px(0, Ui.Gap.Tight), MouseFilter = Control.MouseFilterEnum.Ignore });
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var label = new Label { Text = name, AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = NameLines, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = Ui.Px(NameWidth, MoreWidth), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameRow.AddChild(ItemName(label));
        label.Resized += () => label.TooltipText = label.GetLineCount() > NameLines ? name : "";
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
            plus.AddThemeFontSizeOverride("font_size", Ui.Px(PlusCardSize));
            plus.AddThemeColorOverride("font_color", Muted);
            plate.AddChild(plus);
        }
        cv.AddChild(nameRow);
        card.TooltipText = tip;
        card.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        card.MouseEntered += () => card.AddThemeStyleboxOverride("panel", Ui.Box(new Color(1, 1, 1, PlusCardHover), CardRadius, 12, 10));
        card.MouseExited += () => card.AddThemeStyleboxOverride("panel", CardStyle(false));
        card.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) open();
        };
        return card;
    }

    public static Label CardLine(string text, Color colour)
    {
        var l = new Label { Text = text, Modulate = colour, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = Ui.Px(PlateWidth, 0) };
        l.AddThemeFontSizeOverride("font_size", Ui.Px(12));
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
