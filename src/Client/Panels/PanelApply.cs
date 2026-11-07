using KTable.Core.Panels;

namespace KTable.Client.Panels;

public sealed class PanelContext(IReadOnlyDictionary<string, string> colors, string viewerColor, bool viewerHost, bool viewerAdmin)
{
    public IReadOnlyDictionary<string, string> Colors => colors;
    public Paint Color(string s) => PanelValues.Color(s, colors);
    public Paint? TryColor(string s) => PanelValues.TryColor(s, colors);

    public bool Sees(string visibility)
    {
        foreach (var part in visibility.Contains('|') ? visibility.Split('|') : new[] { visibility })
        {
            if (string.IsNullOrEmpty(part)) return true;
            if (part.Equals("None", StringComparison.OrdinalIgnoreCase) || part.Equals(viewerColor, StringComparison.OrdinalIgnoreCase)) return true;
            if (part.Equals("admin", StringComparison.OrdinalIgnoreCase) && viewerAdmin) return true;
            if (part.Equals("host", StringComparison.OrdinalIgnoreCase) && viewerHost) return true;
        }
        return false;
    }
}

public static partial class PanelApply
{
    static readonly string[] CustomOrder = { "rectAlignment", "width", "height", "offsetXY", "position", "rotation", "scale", "opacity", "outline", "outlineSize", "shadow", "shadowDistance",
        "image", "active", "visibility", "contentSizeFitter", "allowDragging", "childControlWidth", "childControlHeight", "childScaleWidth", "childScaleHeight", "transition", "placeholder" };

    static bool IsCustom(string k) => CustomOrder.Contains(k, StringComparer.OrdinalIgnoreCase);

    static readonly string[] Events = { "onClick", "onMouseEnter", "onMouseExit", "onElementDropped", "onBeginDrag", "onEndDrag", "onDrag", "onSubmit", "onShow", "onHide", "onMouseUp", "onMouseDown",
        "onEndEdit" };

    public static bool Catches(IReadOnlyDictionary<string, string> attrs) =>
        attrs.Keys.Any(k => !k.Equals("onValueChanged", StringComparison.OrdinalIgnoreCase) && Events.Contains(k, StringComparer.OrdinalIgnoreCase)) || !string.IsNullOrEmpty(attrs.GetValueOrDefault("tooltip"));

    public static void Element(PanelBox b, UiElement e, IReadOnlyDictionary<string, string> given, PanelContext ctx)
    {
        PlaceChain(b);
        var attrs = given;
        if (Catches(given) && !given.ContainsKey("raycastTarget"))
        {
            var more = new Dictionary<string, string>(given, StringComparer.OrdinalIgnoreCase) { ["raycastTarget"] = "true" };
            attrs = more;
        }
        Custom(b, e, attrs, ctx);
        foreach (var (k, v) in attrs)
            if (!IsCustom(k) || k.Equals("image", StringComparison.OrdinalIgnoreCase)) Property(b, k, v, ctx);
        Tag(b, e, attrs, ctx);
        b.Active = Shown(e, ctx);
    }

    public static bool Shown(UiElement e, PanelContext ctx) => e.Active switch
    {
        PanelOps.On => true,
        PanelOps.Off => false,
        PanelOps.Seen => (e.Get("active") is not { } a || PanelValues.Bool(a)) && ctx.Sees(e.Get("visibility") ?? ""),
        _ => true,
    };

    static void PlaceChain(PanelBox b)
    {
        if (b.Parent != null) PlaceChain(b.Parent);
        b.Place();
    }

    static void Custom(PanelBox b, UiElement e, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        foreach (var (k, v) in attrs)
        {
            switch (k.ToLowerInvariant())
            {
                case "width":
                    Size(b, 0, v, e, attrs);
                    break;
                case "height":
                    Size(b, 1, v, e, attrs);
                    break;
                case "rectalignment":
                    Align(b, v, e, attrs);
                    break;
                case "offsetxy":
                    Offset(b, v);
                    break;
                case "position":
                    var p = PanelValues.Vector3(v);
                    b.Position = new V2(p.X, p.Y);
                    b.Depth = p.Z;
                    break;
                case "rotation":
                    b.Rotation = PanelValues.Vector3(v);
                    break;
                case "scale":
                    b.Scale = PanelValues.Vector3(v);
                    break;
                case "opacity":
                    b.Opacity = PanelValues.Float(v);
                    break;
                case "outline" or "outlinesize":
                    Effect(b, attrs, "outline", "outlineSize", ctx, true);
                    break;
                case "shadow" or "shadowdistance":
                    Effect(b, attrs, "shadow", "shadowDistance", ctx, false);
                    break;
                case "image":
                    Picture(b, v, e);
                    break;
                case "contentsizefitter":
                    b.FitWidth = v is "horizontal" or "both" ? 2 : 0;
                    b.FitHeight = v is "vertical" or "both" ? 2 : 0;
                    break;
                case "childcontrolwidth" or "childcontrolheight" or "childscalewidth" or "childscaleheight":
                    GroupFlag(b, k.ToLowerInvariant(), v);
                    break;
                case "transition":
                    if (b.Select != null && PanelValues.Enum<Transitions>(v) is { } tr) b.Select.Transition = (int)tr;
                    break;
                case "aspectratio":
                    b.AspectRatio = PanelFits.Ratio(v);
                    break;
                case "aspectmode":
                    b.AspectMode = PanelValues.Enum<PanelFits.Mode>(v) is { } mode ? (int)mode : b.AspectMode;
                    break;
                case "setnativesize" or "sortingorder":
                    b.Extra[k] = v;
                    break;
                case "placeholder" when b.Tag == "InputField":
                    if (b.Part("Placeholder")?.Text is { } ph) ph.Text = v;
                    break;
            }
        }
    }

    enum Transitions { None, ColorTint, SpriteSwap, Animation }

    static void GroupFlag(PanelBox b, string k, string v)
    {
        if (b.Tag is not ("HorizontalLayout" or "VerticalLayout") || b.Group == null) return;
        bool on = PanelValues.Bool(v);
        switch (k)
        {
            case "childcontrolwidth":
                b.Group.ControlWidth = on;
                break;
            case "childcontrolheight":
                b.Group.ControlHeight = on;
                break;
            case "childscalewidth":
                b.Group.ScaleWidth = on;
                break;
            default:
                b.Group.ScaleHeight = on;
                break;
        }
    }

    static Anchor RectAlignment(string? s) => PanelValues.Enum<Anchor>(s) ?? Anchor.MiddleCenter;

    public static void NativeSize(PanelBox b, UiElement e, V2 size)
    {
        var attrs = new Dictionary<string, string>(e.Attributes, StringComparer.OrdinalIgnoreCase)
        {
            ["width"] = PanelValues.FloatText(size.X), ["height"] = PanelValues.FloatText(size.Y),
        };
        Size(b, 0, attrs["width"], e, attrs);
        Size(b, 1, attrs["height"], e, attrs);
    }

    static void Size(PanelBox b, int axis, string value, UiElement e, IReadOnlyDictionary<string, string> attrs)
    {
        var align = RectAlignment(e.Attributes.GetValueOrDefault("rectAlignment") ?? attrs.GetValueOrDefault("rectAlignment"));
        if (attrs.TryGetValue("position", out var pos))
        {
            var p = PanelValues.Vector3(pos);
            b.Position = new V2(p.X, p.Y);
        }
        float otherSize = b.RectSize[1 - axis];
        if (!float.TryParse(value.Replace("%", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) return;
        if (value.Contains('%'))
        {
            b.SizeDelta = b.SizeDelta.With(axis, 0);
            float share = n / 100f;
            var at = ShareStart(share, align, axis);
            b.AnchorMin = b.AnchorMin.With(axis, at);
            b.AnchorMax = b.AnchorMax.With(axis, at + share);
        }
        else
        {
            var (anchor, pivot) = Point(align);
            b.AnchorMin = b.AnchorMin.With(axis, anchor[axis]);
            b.AnchorMax = b.AnchorMax.With(axis, anchor[axis]);
            b.Pivot = b.Pivot.With(axis, pivot[axis]);
            b.SizeDelta = b.SizeDelta.With(axis, n);
        }
        b.Place();
        b.SetSize(1 - axis, otherSize);
        b.Place();
    }

    static float ShareStart(float share, Anchor a, int axis)
    {
        float h = (int)a % 3, v = (int)a / 3;
        if (axis == 0) return h == 0 ? 0 : h == 2 ? 1 - share : (1 - share) / 2;
        return v == 0 ? 1 - share : v == 2 ? 0 : (1 - share) / 2;
    }

    static (V2 Anchor, V2 Pivot) Point(Anchor a)
    {
        float x = (int)a % 3 * 0.5f, y = 1 - (int)a / 3 * 0.5f;
        return (new V2(x, y), new V2(x, y));
    }

    static void Align(PanelBox b, string value, UiElement e, IReadOnlyDictionary<string, string> attrs)
    {
        bool across = !(attrs.ContainsKey("width") || e.Attributes.ContainsKey("width"));
        bool down = !(attrs.ContainsKey("height") || e.Attributes.ContainsKey("height"));
        if ((!across || !down) && (attrs.GetValueOrDefault("contentSizeFitter") ?? e.Get("contentSizeFitter")) is { } fit)
        {
            if (fit is "horizontal" or "both") across = true;
            if (fit is "vertical" or "both") down = true;
        }
        var (_, pivot) = Point(RectAlignment(value));
        if (across ^ down)
        {
            b.AnchorMin = new V2(across ? 0 : b.AnchorMin.X, down ? 0 : b.AnchorMin.Y);
            b.AnchorMax = new V2(across ? 1 : b.AnchorMax.X, down ? 1 : b.AnchorMax.Y);
        }
        b.Pivot = new V2(across ? pivot.X : b.Pivot.X, down ? pivot.Y : b.Pivot.Y);
    }

    static void Offset(PanelBox b, string value)
    {
        if (b.Parent == null) return;
        var now = PanelValues.Vector2(value);
        var was = b.CurrentOffset;
        b.CurrentOffset = now;
        var delta = was != V2.Zero ? now - was : now;
        b.Position += delta;
    }

    static void Effect(PanelBox b, IReadOnlyDictionary<string, string> attrs, string colorKey, string sizeKey, PanelContext ctx, bool outline)
    {
        var target = b.Tag == "Slider" ? b.Children.FirstOrDefault() ?? b : b;
        if (!attrs.TryGetValue(colorKey, out var c)) return;
        if (c.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            if (outline) target.Outline = null;
            else target.Shadow = null;
            return;
        }
        var fx = new EffectProps { Color = ctx.Color(c) };
        if (attrs.TryGetValue(sizeKey, out var size)) fx.Distance = PanelValues.Vector2(size);
        if (outline) target.Outline = fx;
        else target.Shadow = fx;
    }

    static void Picture(PanelBox b, string value, UiElement e)
    {
        if (b.Image == null) return;
        if (!e.Attributes.ContainsKey("color")) b.Image.Color = Paint.White;
        b.Image.Sprite = PanelValues.IsNone(value) ? null : value;
    }
}
