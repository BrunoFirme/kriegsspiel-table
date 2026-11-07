using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed record PanelHooks(PanelView.Sender Send, Func<string, Action<Texture2D>, Texture2D?> Picture, Dictionary<string, string> Local, Action Redraw)
{
    public Action<string, float> Sound { get; init; } = (_, _) => { };
    public Action<string> Notice { get; init; } = _ => { };
    public Func<string, bool> Known { get; init; } = _ => false;
    public Func<string, Texture2D?> CursorPicture { get; init; } = _ => null;
    public SubViewport? Port { get; init; }
    public Func<Vector2, Vector2?> PlatePixel { get; init; } = _ => null;
    public Func<Vector2, Vector3?> PlateWorld { get; init; } = _ => null;
}

sealed partial class PanelView
{
    public delegate void Sender(List<int> path, string ev, string value);

    readonly PanelHooks _hooks;
    readonly Dictionary<string, string> _local;
    public readonly Dictionary<PanelBox, Control> Nodes = new();
    PanelScene? _scene;
    Control? _top;
    readonly List<PanelBox> _hovered = new();

    public PanelView(PanelHooks hooks)
    {
        _hooks = hooks;
        _local = hooks.Local;
    }

    public PanelScene? Scene => _scene;

    public bool Live => _top != null && GodotObject.IsInstanceValid(_top) && _top.IsInsideTree();

    void Redraw() => _hooks.Redraw();

    public Control Show(PanelScene scene, PanelBox from)
    {
        _scene = scene;
        Nodes.Clear();
        _hovered.Clear();
        _top = Node(from, null);
        PanelFocus.Showing(this);
        Restore(scene);
        return _top;
    }

    void Restore(PanelScene scene)
    {
        RestoreList(scene);
        RestoreHover(scene);
        if (_local.GetValueOrDefault(PanelFocus.Selected) is not { } key) return;
        _selected = scene.Elements.FirstOrDefault(e => e.Path != null && PanelScene.Key(e.Path) == key && Nodes.ContainsKey(e));
        if (_selected == null) return;
        PanelFocus.Shown(this, _local);
        if (_local.GetValueOrDefault(PanelFocus.Editing) == key) Callable.From(() => OpenEditor(_selected)).CallDeferred();
    }

    static void Fit(Control c, PanelBox b, PanelBox? parent)
    {
        c.Size = new Vector2(b.RectSize.X, b.RectSize.Y);
        c.Visible = b.Active;
        if (parent == null) return;
        c.Position = new Vector2(b.PivotAt.X + b.RectMin.X - parent.RectMin.X, parent.RectMin.Y + parent.RectSize.Y - (b.PivotAt.Y + b.RectMin.Y + b.RectSize.Y));
        c.PivotOffset = new Vector2(b.Pivot.X * b.RectSize.X, (1 - b.Pivot.Y) * b.RectSize.Y);
    }

    void Refit(PanelBox b)
    {
        if (Nodes.TryGetValue(b, out var c) && GodotObject.IsInstanceValid(c)) Fit(c, b, b.Parent);
        foreach (var child in b.Children) Refit(child);
    }

    Control Node(PanelBox b, PanelBox? parent)
    {
        var c = b.Mask && b.Image != null ? MaskShape(b) : new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = b.RectMask };
        Fit(c, b, parent);
        if (b.Tag == "Canvas" && b.Extra.TryGetValue("sortingorder", out var order))
        {
            c.ZAsRelative = false;
            c.ZIndex = (int)Math.Clamp(PanelValues.Int(order), RenderingServer.CanvasItemZMin, RenderingServer.CanvasItemZMax);
        }
        if (parent != null)
        {
            c.RotationDegrees = -b.Rotation.Z;
            c.Scale = new Vector2(b.Scale.X * Mathf.Cos(Mathf.DegToRad(b.Rotation.Y)), b.Scale.Y * Mathf.Cos(Mathf.DegToRad(b.Rotation.X)));
        }
        if (b.Opacity < 1) c.Modulate = Colors.White with { A = Math.Clamp(b.Opacity, 0, 1) };
        Nodes[b] = c;
        if (b.Image != null && !b.Mask) c.AddChild(Paint(b));
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

    Control MaskShape(PanelBox b)
    {
        var shape = Paint(b);
        shape.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        shape.ClipContents = true;
        shape.ClipChildren = b.ShowMaskGraphic ? CanvasItem.ClipChildrenMode.AndDraw : CanvasItem.ClipChildrenMode.Only;
        return shape;
    }

    static bool Catches(PanelBox b) => (b.Image?.RaycastTarget ?? false) || (b.Text?.RaycastTarget ?? false);

    static PanelBox? Owner(PanelBox b)
    {
        for (var at = b; at != null; at = at.Parent) if (at.Element != null) return at;
        return null;
    }

    const string NoSprite = "[XmlLayout] Unable to load sprite '{0}'. Please ensure that it is located within a Resources folder or XmlLayout Resource Database.";

    Control Paint(PanelBox b)
    {
        var img = b.Image!;
        var tint = Tinted(b, ToColor(img.Color));
        var sprite = StateSprite(b) ?? img.Sprite;
        if (sprite != null && PanelPrefabs.Known.TryGetValue(sprite, out var builtin)) sprite = builtin;
        Control paint;
        if (sprite != null && Builtin(sprite) is { } radius) paint = Rounded(sprite, tint, radius);
        else if (sprite != null && !PanelPrefabs.KnownToTheGame(sprite)) paint = Picture(sprite, tint, img.PreserveAspect);
        else paint = new ColorRect { Color = sprite != null ? Colors.Transparent : tint };
        paint.MouseFilter = Control.MouseFilterEnum.Ignore;
        paint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        if (img.Type == 3) Filled(paint, img);
        return paint;
    }

    Control Picture(string sprite, Color tint, bool keep)
    {
        var tr = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, SelfModulate = tint,
            StretchMode = keep ? TextureRect.StretchModeEnum.KeepAspectCentered : TextureRect.StretchModeEnum.Scale };
        var tex = _hooks.Picture(sprite, t => { if (GodotObject.IsInstanceValid(tr)) tr.Texture = t; });
        if (tex != null) tr.Texture = tex;
        else if (!IsLink(sprite) && !_hooks.Known(sprite)) _hooks.Notice(string.Format(System.Globalization.CultureInfo.InvariantCulture, NoSprite, sprite));
        return tr;
    }

    static bool IsLink(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" or "file";

    static Control Rounded(string sprite, Color tint, int radius)
    {
        if (sprite is PanelPrefabs.Checkmark or PanelPrefabs.Knob or PanelPrefabs.DropdownArrow or PanelPrefabs.GroupSelected or PanelAccordions.Collapsed or PanelAccordions.Expanded)
            return Glyph(sprite, tint);
        var p = new Panel();
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = tint, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        });
        return p;
    }

    static void Filled(Control paint, ImageProps img)
    {
        if (img.FillMethod == 0) paint.AnchorRight = Math.Clamp(img.FillAmount, 0, 1);
        else if (img.FillMethod == 1) paint.AnchorTop = 1 - Math.Clamp(img.FillAmount, 0, 1);
    }

    const string Tick = "✔", Dot = "●", Down = "▼", Right = "▶";

    static Control Glyph(string sprite, Color tint)
    {
        var l = new Label { Text = sprite == PanelPrefabs.Checkmark ? Tick : sprite is PanelPrefabs.Knob or PanelPrefabs.GroupSelected ? Dot : sprite == PanelAccordions.Collapsed ? Right : Down,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeColorOverride("font_color", tint);
        return l;
    }

    const int Round = 999;

    static int? Builtin(string sprite) => sprite switch
    {
        PanelPrefabs.UiSprite or PanelPrefabs.Background or PanelPrefabs.MaskSprite => 5,
        PanelPrefabs.ToolSprite or PanelPrefabs.ToolBackground => 3,
        PanelPrefabs.Checkmark or PanelPrefabs.Knob or PanelPrefabs.DropdownArrow or PanelPrefabs.GroupSelected or PanelAccordions.Collapsed or PanelAccordions.Expanded => 0,
        PanelPrefabs.GroupBackground => Round,
        PanelPrefabs.Outline or PanelPrefabs.OutlineSharp => 2,
        _ => null,
    };

    public static Color ToColor(Paint p) => new(Math.Clamp(p.R, 0, 1), Math.Clamp(p.G, 0, 1), Math.Clamp(p.B, 0, 1), Math.Clamp(p.A, 0, 1));

    enum Look { Normal, Highlighted, Pressed, Selected, Disabled }

    Look StateOf(PanelBox owner) => owner.Select is { Interactable: false } ? Look.Disabled : _pressed == owner ? Look.Pressed : _selected == owner ? Look.Selected
        : _hovered.Contains(owner) ? Look.Highlighted : Look.Normal;

    static bool IsTarget(PanelBox b, PanelBox? owner) => owner?.Select is { } sel && sel.TargetGraphic == b.Name;

    Color Tinted(PanelBox b, Color c)
    {
        var owner = Owner(b);
        if (owner?.Select is not { Transition: 1 } sel || !IsTarget(b, owner)) return c;
        var state = StateOf(owner) switch
        {
            Look.Disabled => sel.Colors.Disabled, Look.Pressed => sel.Colors.Pressed, Look.Highlighted => sel.Colors.Highlighted, Look.Selected => sel.Colors.Selected,
            _ => sel.Colors.Normal,
        };
        return c * ToColor(state.Scaled(sel.Colors.Multiplier) with { A = state.A * sel.Colors.Multiplier });
    }

    string? StateSprite(PanelBox b)
    {
        var owner = Owner(b);
        if (owner?.Select is not { Transition: 2 } || !IsTarget(b, owner)) return null;
        var key = StateOf(owner) switch { Look.Disabled => "disabledsprite", Look.Pressed => "pressedsprite", Look.Highlighted => "highlightedsprite", Look.Selected => "selectedsprite", _ => null };
        return key != null && owner.Extra.TryGetValue(key, out var s) && !PanelValues.IsNone(s) ? s : null;
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
