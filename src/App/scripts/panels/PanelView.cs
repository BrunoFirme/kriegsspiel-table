using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    public delegate void Sender(List<int> path, string ev, string value);

    readonly Sender _send;
    readonly Func<string, Action<Texture2D>, Texture2D?> _picture;
    readonly Dictionary<string, string> _local;
    readonly Action _redraw;
    public readonly Dictionary<PanelBox, Control> Nodes = new();
    PanelScene? _scene;
    readonly List<PanelBox> _hovered = new();

    public PanelView(Sender send, Func<string, Action<Texture2D>, Texture2D?> picture, Dictionary<string, string> local, Action redraw)
    {
        _send = send;
        _picture = picture;
        _local = local;
        _redraw = redraw;
    }

    public PanelScene? Scene => _scene;

    public Control Show(PanelScene scene, PanelBox from)
    {
        _scene = scene;
        Nodes.Clear();
        _hovered.Clear();
        var c = Node(from, null);
        return c;
    }

    Control Node(PanelBox b, PanelBox? parent)
    {
        var c = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = b.Mask || b.RectMask, Visible = b.Active, Size = new Vector2(b.RectSize.X, b.RectSize.Y) };
        if (parent != null)
        {
            c.Position = new Vector2(b.PivotAt.X + b.RectMin.X - parent.RectMin.X, parent.RectMin.Y + parent.RectSize.Y - (b.PivotAt.Y + b.RectMin.Y + b.RectSize.Y));
            c.PivotOffset = new Vector2(b.Pivot.X * b.RectSize.X, (1 - b.Pivot.Y) * b.RectSize.Y);
            c.RotationDegrees = -b.Rotation.Z;
            c.Scale = new Vector2(b.Scale.X * Mathf.Cos(Mathf.DegToRad(b.Rotation.Y)), b.Scale.Y * Mathf.Cos(Mathf.DegToRad(b.Rotation.X)));
        }
        if (b.Opacity < 1) c.Modulate = Colors.White with { A = Math.Clamp(b.Opacity, 0, 1) };
        Nodes[b] = c;
        if (b.Image != null && !(b.Mask && !b.ShowMaskGraphic)) c.AddChild(Paint(b));
        if (b.Text != null) c.AddChild(Words(b));
        if (Catches(b))
        {
            c.MouseFilter = Control.MouseFilterEnum.Stop;
            c.GuiInput += e => Input(b, e);
            c.MouseEntered += () => Hover(b, true);
            c.MouseExited += () => Hover(b, false);
            if (Owner(b)?.Tooltip is { Length: > 0 } tip) c.TooltipText = tip;
        }
        foreach (var child in b.Children) c.AddChild(Node(child, b));
        return c;
    }

    static bool Catches(PanelBox b) => (b.Image?.RaycastTarget ?? false) || (b.Text?.RaycastTarget ?? false);

    static PanelBox? Owner(PanelBox b)
    {
        for (var at = b; at != null; at = at.Parent) if (at.Element != null) return at;
        return null;
    }

    Control Paint(PanelBox b)
    {
        var img = b.Image!;
        var tint = Tinted(b, ToColor(img.Color));
        Control paint;
        if (img.Sprite != null && Builtin(img.Sprite) is { } radius)
        {
            var p = new Panel();
            p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = tint, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            });
            paint = p;
            if (img.Sprite is PanelPrefabs.Checkmark or PanelPrefabs.Knob or PanelPrefabs.DropdownArrow) paint = Glyph(img.Sprite, tint);
        }
        else if (img.Sprite != null)
        {
            var tr = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, SelfModulate = tint,
                StretchMode = img.PreserveAspect ? TextureRect.StretchModeEnum.KeepAspectCentered : TextureRect.StretchModeEnum.Scale };
            if (_picture(img.Sprite, t => { if (GodotObject.IsInstanceValid(tr)) tr.Texture = t; }) is { } tex) tr.Texture = tex;
            paint = tr;
        }
        else paint = new ColorRect { Color = tint };
        paint.MouseFilter = Control.MouseFilterEnum.Ignore;
        paint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (img.Type == 3) Filled(paint, img);
        return paint;
    }

    static void Filled(Control paint, ImageProps img)
    {
        if (img.FillMethod == 0) paint.AnchorRight = Math.Clamp(img.FillAmount, 0, 1);
        else if (img.FillMethod == 1) paint.AnchorTop = 1 - Math.Clamp(img.FillAmount, 0, 1);
    }

    static Control Glyph(string sprite, Color tint)
    {
        var l = new Label { Text = sprite == PanelPrefabs.Checkmark ? "✔" : sprite == PanelPrefabs.Knob ? "●" : "▼", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeColorOverride("font_color", tint);
        return l;
    }

    static int? Builtin(string sprite) => sprite switch
    {
        PanelPrefabs.UiSprite or PanelPrefabs.Background or PanelPrefabs.MaskSprite => 5,
        PanelPrefabs.ToolSprite or PanelPrefabs.ToolBackground => 3,
        PanelPrefabs.Checkmark or PanelPrefabs.Knob or PanelPrefabs.DropdownArrow => 0,
        "Outline" or PanelPrefabs.OutlineSharp or "ToggleGroup_Background" or "ToggleGroup_Selected" or "InputFieldBackground" => 2,
        _ => null,
    };

    public static Color ToColor(Paint p) => new(Math.Clamp(p.R, 0, 1), Math.Clamp(p.G, 0, 1), Math.Clamp(p.B, 0, 1), Math.Clamp(p.A, 0, 1));

    Color Tinted(PanelBox b, Color c)
    {
        var owner = Owner(b);
        if (owner?.Select is not { Transition: 1 } sel || sel.TargetGraphic != b.Name) return c;
        var state = !sel.Interactable ? sel.Colors.Disabled : _pressed == owner ? sel.Colors.Pressed : _hovered.Contains(owner) ? sel.Colors.Highlighted : sel.Colors.Normal;
        return c * ToColor(state.Scaled(sel.Colors.Multiplier) with { A = state.A * sel.Colors.Multiplier });
    }

    Control Words(PanelBox b)
    {
        var t = b.Text!;
        var text = t.Text;
        int size = Math.Max(1, t.BestFit ? BestFit(b) : t.Size);
        var across = ((int)t.Alignment % 3) switch { 0 => HorizontalAlignment.Left, 2 => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        var down = ((int)t.Alignment / 3) switch { 0 => VerticalAlignment.Top, 2 => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
        var wrap = t.Overflow ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart;
        Control words;
        if (PanelRich.IsRich(text, t.Rich))
        {
            var r = new RichTextLabel { BbcodeEnabled = true, Text = PanelRich.BbCode(text, size), ScrollActive = false, AutowrapMode = wrap, HorizontalAlignment = across, VerticalAlignment = down };
            r.AddThemeColorOverride("default_color", ToColor(t.Color));
            foreach (var key in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" }) r.AddThemeFontSizeOverride(key, size);
            r.AddThemeFontOverride("normal_font", PanelMeter.FontOf(t));
            Effects(r, b, size);
            words = r;
        }
        else
        {
            var l = new Label { Text = text, AutowrapMode = wrap, HorizontalAlignment = across, VerticalAlignment = down, ClipText = !t.VerticalOverflow && !t.Overflow };
            l.AddThemeColorOverride("font_color", ToColor(t.Color));
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeFontOverride("font", PanelMeter.FontOf(t));
            Effects(l, b, size);
            words = l;
        }
        words.MouseFilter = Control.MouseFilterEnum.Ignore;
        words.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return words;
    }

    static void Effects(Control c, PanelBox b, int size)
    {
        if (b.Outline is { } o)
        {
            c.AddThemeColorOverride("font_outline_color", ToColor(o.Color));
            c.AddThemeConstantOverride("outline_size", Math.Max(1, (int)Math.Round(Math.Abs(o.Distance.X) * 2)));
        }
        if (b.Shadow is { } sh)
        {
            c.AddThemeColorOverride("font_shadow_color", ToColor(sh.Color));
            c.AddThemeConstantOverride("shadow_offset_x", (int)Math.Round(sh.Distance.X));
            c.AddThemeConstantOverride("shadow_offset_y", (int)Math.Round(-sh.Distance.Y));
        }
    }

    static int BestFit(PanelBox b)
    {
        var t = b.Text!;
        var meter = new PanelMeter(_ => null);
        for (int s = t.MaxSize; s > t.MinSize; s--)
        {
            var probe = t.Clone();
            probe.Size = s;
            var need = meter.Measure(probe, t.Text, b.RectSize.X);
            if (need.Y <= b.RectSize.Y && (t.Overflow || need.X <= b.RectSize.X + 0.5f)) return s;
        }
        return t.MinSize;
    }
}
