using KTable.Core.Panels;

namespace KTable.Client.Panels;

public static partial class PanelApply
{
    static readonly string[] TextAttributes = { "text", "fontstyle", "font", "fontsize", "horizontalOverflow", "verticalOverflow", "resizeTextForBestFit", "resizeTextMinSize", "resizeTextMaxSize", "alignByGeometry" };

    static Dictionary<string, string> TextPart(IReadOnlyDictionary<string, string> attrs, bool withAlignment)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in attrs) if (TextAttributes.Contains(k, StringComparer.OrdinalIgnoreCase)) d[k] = v;
        if (attrs.TryGetValue("textshadow", out var sh)) d["shadow"] = sh;
        if (attrs.TryGetValue("textoutline", out var ol)) d["outline"] = ol;
        if (attrs.TryGetValue("textcolor", out var tc)) d["color"] = tc;
        if (withAlignment && attrs.TryGetValue("textalignment", out var ta)) d["alignment"] = ta;
        return d;
    }

    static void ApplyText(PanelBox? target, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        if (target?.Text == null) return;
        foreach (var (k, v) in attrs)
        {
            var key = k.ToLowerInvariant();
            if (key is "shadow" or "outline") Effect(target, attrs, key, key + (key == "shadow" ? "Distance" : "Size"), ctx, key == "outline");
            else TextProperty(target.Text, key, v, ctx);
        }
    }

    static void Tag(PanelBox b, UiElement e, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        switch (b.Tag)
        {
            case "Panel" when attrs.ContainsKey("padding") && b.Group != null:
                b.Group.Padding = PanelValues.Padding(attrs["padding"]);
                b.Group.Enabled = true;
                break;
            case "Text":
                if (attrs.ContainsKey("text") && b.Text != null) b.Text.Text = PanelText.FromAttribute(attrs["text"]);
                break;
            case "Button" or "ToggleButton":
                Button(b, e, attrs, ctx);
                break;
            case "Toggle":
                Toggle(b, e, attrs, ctx);
                break;
            case "InputField":
                Input(b, attrs, ctx);
                break;
            case "Slider":
                Slider(b, attrs, ctx);
                break;
            case "ProgressBar":
                Progress(b, attrs, ctx);
                break;
            case "Dropdown":
                Dropdown(b, e, attrs, ctx);
                break;
            case "VerticalScrollView" or "HorizontalScrollView":
                Scroll(b, attrs, ctx);
                break;
        }
    }

    static void Button(PanelBox b, UiElement e, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        var text = b.Part("Text Cell")?.Part("Text");
        bool ownText = true;
        ColorBlock block = ColorBlock.Black;
        if (attrs.TryGetValue("textcolors", out var tcs)) block = PanelValues.Block(tcs, ctx.Colors);
        else if ((attrs.GetValueOrDefault("textcolor") ?? attrs.GetValueOrDefault("deselectedtextcolor")) is { } tc) block = Same(ctx.Color(tc));
        var first = b.Part("Text Cell")?.Children.FirstOrDefault(c => c.Element != null) ?? b.Children.FirstOrDefault(c => c.Element != null);
        if (first is { Tag: "Text" } t)
        {
            ownText = false;
            if (text != null) text.Active = false;
            if (first.Element!.Attributes.ContainsKey("color") && t.Text != null) block = Same(t.Text.Color);
        }
        if (ownText) ApplyText(text, TextPart(attrs, true), ctx);
        if ((ownText ? text : first)?.Text is { } words) words.Color = block.Normal;
        b.Extra["textColorsNow"] = string.Join("|", new[] { block.Normal, block.Highlighted, block.Pressed, block.Disabled }.Select(Hex));
        if (attrs.TryGetValue("preserveaspect", out var pa) && b.Image != null) b.Image.PreserveAspect = PanelValues.Bool(pa);
        if (attrs.ContainsKey("icon") || e.Attributes.ContainsKey("icon")) Icon(b, e, attrs, ctx);
        if (attrs.TryGetValue("padding", out var pad) && b.Part("TableLayout")?.Table is { } table) table.Padding = PanelValues.Padding(pad);
        if (b.Tag == "ToggleButton") ToggleButton(b, e, attrs, ctx);
    }

    static ColorBlock Same(Paint c) => new(c, c, c, c, c, 1, 0);

    public static string Hex(Paint c) => $"#{(int)Math.Round(Math.Clamp(c.R, 0, 1) * 255):X2}{(int)Math.Round(Math.Clamp(c.G, 0, 1) * 255):X2}{(int)Math.Round(Math.Clamp(c.B, 0, 1) * 255):X2}"
        + $"{(int)Math.Round(Math.Clamp(c.A, 0, 1) * 255):X2}";

    static void Icon(PanelBox b, UiElement e, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        var table = b.Part("TableLayout")!;
        var iconCell = table.Part("Icon Cell")!;
        var textCell = table.Part("Text Cell")!;
        var icon = iconCell.Part("Icon")!;
        var align = attrs.GetValueOrDefault("iconAlignment") ?? e.Get("iconAlignment") ?? "Left";
        float width = PanelValues.Float(attrs.GetValueOrDefault("iconwidth") ?? e.Get("iconwidth"));
        table.Table!.ColumnWidths = new() { 0, 0 };
        var row = iconCell.Parent!;
        row.Children.Remove(iconCell);
        if (align.Equals("Right", StringComparison.OrdinalIgnoreCase))
        {
            row.Children.Add(iconCell);
            table.Table.ColumnWidths[1] = width;
        }
        else
        {
            row.Children.Insert(0, iconCell);
            table.Table.ColumnWidths[0] = width;
        }
        icon.Image!.PreserveAspect = true;
        if (attrs.TryGetValue("icon", out var sprite)) icon.Image.Sprite = PanelValues.IsNone(sprite) ? null : sprite;
        if (attrs.TryGetValue("iconcolor", out var ic)) b.Extra["iconColor"] = ic;
        if (attrs.TryGetValue("iconhovercolor", out var ih)) b.Extra["iconHoverColor"] = ih;
        if (attrs.TryGetValue("icondisabledcolor", out var id)) b.Extra["iconDisabledColor"] = id;
        if (attrs.TryGetValue("iconimagetype", out var it) && PanelValues.Enum<ImageTypes>(it) is { } type) icon.Image.Type = (int)type;
        icon.Image.Color = ctx.Color(b.Extra.GetValueOrDefault("iconColor") ?? "#FFFFFF");
        iconCell.Active = true;
        textCell.Active = !string.IsNullOrEmpty(attrs.GetValueOrDefault("text")) || e.Attributes.ContainsKey("text");
    }

    static void ToggleButton(PanelBox b, UiElement e, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        if (attrs.TryGetValue("colors", out var colors))
        {
            var list = PanelValues.Colors(colors, ctx.Colors);
            if (list.Count > 0) b.Extra["deselectedBackgroundColor"] = Hex(list[0]);
            if (list.Count > 1) b.Extra["selectedBackgroundColor"] = Hex(list[1]);
        }
        if (attrs.TryGetValue("ison", out var on)) b.Extra["ison"] = on;
        if (attrs.TryGetValue("selectedicon", out var si)) b.Extra["selectedIcon"] = si;
    }

    static void Toggle(PanelBox b, UiElement e, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        var bg = b.Part("Background")!;
        var check = bg.Part("Checkmark")!;
        var label = b.Part("Label");
        var first = b.Children.FirstOrDefault(c => c.Element != null);
        if (first is { Tag: "Text" or "TextMeshPro" })
        {
            if (label != null) label.Active = false;
            label = first.Tag == "Text" ? first : null;
        }
        if (attrs.TryGetValue("checkcolor", out var cc)) check.Image!.Color = ctx.Color(cc);
        if (attrs.TryGetValue("togglewidth", out var tw)) bg.Layout = Sized(bg.Layout, 0, PanelValues.Float(tw));
        if (attrs.TryGetValue("toggleheight", out var th)) bg.Layout = Sized(bg.Layout, 1, PanelValues.Float(th));
        if (attrs.TryGetValue("textColor", out var textColor) && (label ?? b.Part("Label"))?.Text is { } lt) lt.Color = ctx.Color(textColor);
        if (attrs.TryGetValue("backgroundColor", out var bc)) bg.Image!.Color = ctx.Color(bc);
        if (label == b.Part("Label") && label != null)
        {
            ApplyText(label, TextPart(attrs, false), ctx);
            label.Active = !string.IsNullOrEmpty(label.Text!.Text);
        }
        if (!attrs.ContainsKey("text") && !e.Attributes.ContainsKey("text") && !attrs.ContainsKey("dontModifyBackground"))
        {
            bg.AnchorMin = bg.AnchorMax = V2.Half;
            bg.Position = V2.Zero;
        }
        if (attrs.TryGetValue("togglebackgroundimage", out var tbi)) bg.Image!.Sprite = PanelValues.IsNone(tbi) ? null : tbi;
        if (attrs.TryGetValue("togglecheckmarkimage", out var tci)) check.Image!.Sprite = PanelValues.IsNone(tci) ? null : tci;
        if (attrs.TryGetValue("togglecheckmarkimagepreserveaspect", out var tcp)) check.Image!.PreserveAspect = PanelValues.Bool(tcp);
        if (attrs.TryGetValue("togglecheckmarksize", out var size))
        {
            float s = PanelValues.Float(size);
            check.AnchorMin = check.AnchorMax = V2.Half;
            check.SizeDelta = new V2(s, s);
        }
        if (attrs.TryGetValue("ison", out var isOn)) b.Extra["ison"] = isOn;
    }

    static LayoutProps Sized(LayoutProps? l, int axis, float v)
    {
        l ??= new LayoutProps();
        if (axis == 0) l.PreferredWidth = l.MinWidth = v;
        else l.PreferredHeight = l.MinHeight = v;
        return l;
    }

    static void Input(PanelBox b, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        var parts = new[] { b.Part("Text"), b.Part("Placeholder") };
        if (attrs.TryGetValue("placeholdertext", out var pt) && parts[1]?.Text is { } ph) ph.Text = PanelText.Decode(pt);
        foreach (var part in parts)
        {
            var d = TextPart(attrs, true);
            d.Remove("text");
            ApplyText(part, d, ctx);
            if (part != null && attrs.TryGetValue("textoffset", out var off))
            {
                var o = PanelValues.Padding(off);
                Property(part, "offsetMin", $"{o.Left} {o.Bottom}", ctx);
                Property(part, "offsetMax", $"-{o.Right} -{o.Top}", ctx);
            }
        }
        if (attrs.TryGetValue("textColor", out var tc) && parts[0]?.Text is { } t) t.Color = ctx.Color(tc);
        if (attrs.TryGetValue("backgroundColor", out var bg) && b.Image != null) b.Image.Color = ctx.Color(bg);
    }

    static void Slider(PanelBox b, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        var bg = b.Part("Background")!;
        var fill = b.Part("Fill")!;
        var handle = b.Part("Handle")!;
        if (attrs.TryGetValue("backgroundcolor", out var bc)) bg.Image!.Color = ctx.Color(bc);
        if (attrs.TryGetValue("backgroundimage", out var bi)) bg.Image!.Sprite = PanelValues.IsNone(bi) ? null : bi;
        if (attrs.TryGetValue("fillcolor", out var fc)) fill.Image!.Color = ctx.Color(fc);
        if (attrs.TryGetValue("fillimage", out var fi)) fill.Image!.Sprite = PanelValues.IsNone(fi) ? null : fi;
        if (attrs.TryGetValue("handlecolor", out var hc)) handle.Image!.Color = ctx.Color(hc);
        if (attrs.TryGetValue("handleimage", out var hi)) handle.Image!.Sprite = PanelValues.IsNone(hi) ? null : hi;
        if (attrs.TryGetValue("handlepreserveaspect", out var hp)) handle.Image!.PreserveAspect = PanelValues.Bool(hp);
    }

    static void Progress(PanelBox b, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        var text = b.Part("PercentageText");
        ApplyText(text, TextPart(attrs, true), ctx);
        var fill = b.Part("Fill")!;
        if (attrs.TryGetValue("fillImage", out var fi)) fill.Image!.Sprite = PanelValues.IsNone(fi) ? null : fi;
        if (attrs.TryGetValue("fillImageColor", out var fc)) fill.Image!.Color = ctx.Color(fc);
        foreach (var k in new[] { "percentage", "showPercentageText", "percentageTextFormat", "horizontal", "reverse" })
            if (attrs.TryGetValue(k, out var v)) b.Extra[k] = v;
    }

    static void Dropdown(PanelBox b, UiElement e, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        ApplyText(b.Part("Label"), TextPart(attrs, true), ctx);
        var arrow = b.Part("Arrow")!;
        if (attrs.TryGetValue("arrowimage", out var ai)) arrow.Image!.Sprite = PanelValues.IsNone(ai) ? null : ai;
        if (attrs.TryGetValue("arrowcolor", out var ac)) arrow.Image!.Color = ctx.Color(ac);
        if (attrs.TryGetValue("arrowOffset", out var ao)) arrow.Position = PanelValues.Vector2(ao);
        foreach (var k in new[] { "dropdownheight", "itemheight", "checkcolor", "itemBackgroundColors", "itemTextColor", "scrollbarColors", "scrollbarImage" })
            if (attrs.TryGetValue(k, out var v)) b.Extra[k] = v;
    }

    static void Scroll(PanelBox b, IReadOnlyDictionary<string, string> attrs, PanelContext ctx)
    {
        foreach (var bar in new[] { b.Part("Scrollbar Vertical"), b.Part("Scrollbar Horizontal") })
        {
            if (bar == null) continue;
            if (attrs.TryGetValue("scrollbarbackgroundcolor", out var bc)) bar.Image!.Color = ctx.Color(bc);
            if (attrs.TryGetValue("scrollbarbackgroundimage", out var bi)) bar.Image!.Sprite = PanelValues.IsNone(bi) ? null : bi;
            if (attrs.TryGetValue("scrollbarimage", out var si) && bar.Part("Handle")?.Image is { } h) h.Sprite = PanelValues.IsNone(si) ? null : si;
        }
        if (attrs.TryGetValue("verticalscrollbarwidth", out var vw) && b.Part("Scrollbar Vertical") is { } v) v.SizeDelta = v.SizeDelta with { X = PanelValues.Float(vw) };
        if (attrs.TryGetValue("horizontalscrollbarheight", out var hh) && b.Part("Scrollbar Horizontal") is { } hz) hz.SizeDelta = hz.SizeDelta with { Y = PanelValues.Float(hh) };
        if (attrs.TryGetValue("noscrollbars", out var none) && PanelValues.Bool(none))
            foreach (var bar in new[] { b.Part("Scrollbar Vertical"), b.Part("Scrollbar Horizontal") }) if (bar != null) bar.Active = false;
        foreach (var k in new[] { "horizontal", "vertical", "movementType", "scrollSensitivity", "scrollbarColors" })
            if (attrs.TryGetValue(k, out var x)) b.Extra[k] = x;
    }
}
