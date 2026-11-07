using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public sealed class UiBuilder
{
    readonly Action<string, string, string> _send;
    readonly Func<string, Action<Texture2D>, Texture2D?>? _image;
    public readonly Dictionary<string, Control> ById = new();

    public UiBuilder(Action<string, string, string> send, Func<string, Action<Texture2D>, Texture2D?>? image)
    { _send = send; _image = image; }

    public static Texture2D? AssetTexture(Session s, string id, Action<Texture2D> onReady)
    {
        if (id.Length == 0) return null;
        if (s.View.UiPictures.TryGetValue(id.Trim(), out var named)) id = named;
        foreach (var pk in s.Store.LoadedPacks)
            if (pk.Assets.TryGetValue(id, out var a) && a.Kind == "image") return s.Assets.Texture(pk.Id, a.File, onReady, maxSize: 512);
        return null;
    }

    public static void Place(Control c, UiNode n, Vector2 parentSize, Control? parent)
    {
        var w = Num(n, "width", 0);
        var h = Num(n, "height", 0);
        if (w <= 0 && h <= 0 && parent != null && n.Tag is "VerticalLayout" or "HorizontalLayout" or "TableLayout" or "GridLayout")
        {
            c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var pad = Pad(n);
            c.OffsetLeft = pad.X;
            c.OffsetTop = pad.Y;
            c.OffsetRight = -pad.Z;
            c.OffsetBottom = -pad.W;
            return;
        }
        if (w <= 0) w = n.Tag == "Panel" ? Mathf.Min(parentSize.X, 400) : Mathf.Max(c.GetCombinedMinimumSize().X, 40);
        if (h <= 0) h = n.Tag == "Panel" ? Mathf.Min(parentSize.Y, 300) : Mathf.Max(c.GetCombinedMinimumSize().Y, 24);
        var align = Attr(n, "rectAlignment", "MiddleCenter");
        float ax = align.Contains("Left") ? 0 : align.Contains("Right") ? 1 : 0.5f;
        float ay = align.StartsWith("Upper") ? 0 : align.StartsWith("Lower") ? 1 : 0.5f;
        var off = Vec(n, "offsetXY");
        if (parent == null)
        {
            var anchor = new Vector2(parentSize.X * ax, parentSize.Y * ay);
            c.Size = new Vector2(w, h);
            c.Position = new Vector2(anchor.X + off.X - w * ax, anchor.Y - off.Y - h * ay);
            return;
        }
        c.AnchorLeft = ax;
        c.AnchorRight = ax;
        c.AnchorTop = ay;
        c.AnchorBottom = ay;
        c.OffsetLeft = off.X - w * ax;
        c.OffsetRight = c.OffsetLeft + w;
        c.OffsetTop = -off.Y - h * ay;
        c.OffsetBottom = c.OffsetTop + h;
    }

    const int MaxDepth = 48;
    int _depth;

    public Control? Build(UiNode n, Vector2 avail)
    {
        if (_depth >= MaxDepth) return null;
        _depth++;
        try { return BuildNode(n, avail); }
        finally { _depth--; }
    }

    Control BuildNode(UiNode n, Vector2 avail)
    {
        if (Attr(n, "active", "true").Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            var hidden = new Control { Visible = false };
            Register(n, hidden);
            return hidden;
        }
        Control c = n.Tag switch
        {
            "Panel" => BuildPanel(n, avail),
            "VerticalLayout" => Fill(new VBoxContainer(), n, avail),
            "HorizontalLayout" => Fill(new HBoxContainer(), n, avail),
            "GridLayout" => FillGrid(n, avail),
            "TableLayout" => BuildTable(n, avail),
            "Text" => BuildText(n),
            "Button" => BuildButton(n),
            "InputField" => BuildInput(n),
            "Toggle" => BuildToggle(n),
            "Dropdown" => BuildDropdown(n),
            "ProgressBar" => BuildProgress(n),
            "Slider" => BuildSlider(n),
            "Image" => BuildImage(n, avail),
            "ToggleButton" => BuildToggleButton(n),
            "ToggleGroup" => BuildToggleGroup(n, avail),
            "HorizontalScrollView" or "VerticalScrollView" => BuildScroll(n, avail),
            "Defaults" => new Control { Visible = false },
            _ => Fill(new VBoxContainer(), n, avail),
        };
        if (Attr(n, "tooltip", "") is { Length: > 0 } tip) c.TooltipText = tip;
        if (n.Tag is not ("Panel" or "Image")) SizeHints(c, n);
        if (Attr(n, "raycastTarget", "true").Equals("false", StringComparison.OrdinalIgnoreCase)) c.MouseFilter = Control.MouseFilterEnum.Ignore;
        Register(n, c);
        return c;
    }

    void Register(UiNode n, Control c) { if (n.Id != null) ById[n.Id] = c; }

    static void SizeHints(Control c, UiNode n)
    {
        var pw = Num(n, "preferredWidth", Num(n, "minWidth", 0));
        var ph = Num(n, "preferredHeight", Num(n, "minHeight", 0));
        if (pw > 0 || ph > 0) c.CustomMinimumSize = new Vector2(Mathf.Max(c.CustomMinimumSize.X, pw), Mathf.Max(c.CustomMinimumSize.Y, ph));
        c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (Attr(n, "flexibleHeight", "") == "1" || n.Tag is "Panel" or "VerticalLayout" or "TableLayout") c.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    }

    Control BuildPanel(UiNode n, Vector2 avail)
    {
        var color = Col(n, "color", new Color(0, 0, 0, 0));
        var p = new Panel { MouseFilter = color.A > 0.001f ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Pass };
        p.AddThemeStyleboxOverride("panel", Ui.Box(color, 4, 0, 0));
        var inner = new Vector2(Num(n, "width", Mathf.Min(avail.X, 400)), Num(n, "height", Mathf.Min(avail.Y, 300)));
        foreach (var ch in n.Children)
        {
            var c = Build(ch, inner);
            if (c == null) continue;
            Place(c, ch, inner, p);
            p.AddChild(c);
        }
        if (Attr(n, "allowDragging", "false").Equals("true", StringComparison.OrdinalIgnoreCase)) Draggable(p, n);
        return p;
    }

    static void Draggable(Control p, UiNode n)
    {
        p.MouseFilter = Control.MouseFilterEnum.Stop;
        bool back = Attr(n, "returnToOriginalPositionWhenReleased", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        bool inside = Attr(n, "restrictDraggingToParentBounds", "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        Vector2? grab = null, home = null;
        p.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
            {
                if (mb.Pressed) { grab = mb.GlobalPosition - p.GlobalPosition; home ??= p.Position; }
                else { grab = null; if (back && home is { } h) p.Position = h; }
                p.AcceptEvent();
            }
            else if (e is InputEventMouseMotion mm && grab is { } g)
            {
                var to = mm.GlobalPosition - g;
                if (inside && p.GetParentControl() is { } parent)
                    to = to.Clamp(parent.GlobalPosition, parent.GlobalPosition + parent.Size - p.Size);
                p.GlobalPosition = to;
                p.AcceptEvent();
            }
        };
    }

    Control Fill(BoxContainer box, UiNode n, Vector2 avail)
    {
        box.AddThemeConstantOverride("separation", (int)Num(n, "spacing", 0));
        Control host = box;
        var bg = Col(n, "color", new Color(0, 0, 0, 0));
        if (bg.A > 0.001f)
        {
            var pc = new PanelContainer();
            pc.AddThemeStyleboxOverride("panel", Ui.Box(bg, 0, 0, 0));
            pc.AddChild(box);
            host = pc;
        }
        var pad = Pad(n);
        if (pad != Vector4.Zero)
        {
            var m = new MarginContainer();
            m.AddThemeConstantOverride("margin_left", (int)pad.X);
            m.AddThemeConstantOverride("margin_top", (int)pad.Y);
            m.AddThemeConstantOverride("margin_right", (int)pad.Z);
            m.AddThemeConstantOverride("margin_bottom", (int)pad.W);
            m.AddChild(host);
            host = m;
        }
        var align = Attr(n, "childAlignment", "UpperLeft");
        box.Alignment = align.Contains("Center") && !align.Contains("Upper")
            && !align.Contains("Lower") ? BoxContainer.AlignmentMode.Center : align.StartsWith("Lower") ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin;
        foreach (var ch in n.Children) { var c = Build(ch, avail); if (c != null) box.AddChild(c); }
        return host;
    }

    Control FillGrid(UiNode n, Vector2 avail)
    {
        var cell = Vec(n, "cellSize");
        var spacing = Num(n, "spacing", 0);
        int cols = (int)Num(n, "constraintCount", 0);
        if (cols <= 0) cols = cell.X > 0 && avail.X > 0 ? Math.Max(1, (int)Math.Floor((avail.X + spacing) / (cell.X + spacing))) : 2;
        var g = new GridContainer { Columns = cols };
        g.AddThemeConstantOverride("h_separation", (int)spacing);
        g.AddThemeConstantOverride("v_separation", (int)spacing);
        foreach (var ch in n.Children)
        {
            var c = Build(ch, cell.X > 0 ? cell : avail);
            if (c == null) continue;
            if (cell.X > 0 && c.Visible) c.CustomMinimumSize = cell;
            g.AddChild(c);
        }
        var align = Attr(n, "childAlignment", "UpperLeft");
        if (align.Contains("Center") || align.Contains("Middle"))
        {
            var cc = new CenterContainer();
            cc.AddChild(g);
            return cc;
        }
        return g;
    }

    Control BuildTable(UiNode n, Vector2 avail)
    {
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", (int)Num(n, "spacing", 0));
        var cellBg = Col(n, "cellBackgroundColor", new Color(0, 0, 0, 0));
        foreach (var r in n.Children)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", (int)Num(n, "cellSpacing", 0));
            if (Num(r, "preferredHeight", 0) is var rh && rh > 0) row.CustomMinimumSize = new Vector2(0, rh);
            foreach (var cell in r.Children)
            {
                var pc = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = Mathf.Max(1, Num(cell, "columnSpan", 1)) };
                var bg = Attr(cell, "dontUseTableCellBackground", "false").Equals("true", StringComparison.OrdinalIgnoreCase) ? new Color(0, 0, 0, 0) : Col(cell, "color", cellBg);
                pc.AddThemeStyleboxOverride("panel", Ui.Box(bg, 0, (int)Num(n, "cellPadding", 2), (int)Num(n, "cellPadding", 2)));
                var inner = new VBoxContainer();
                foreach (var ch in cell.Children) { var c = Build(ch, avail); if (c != null) inner.AddChild(c); }
                pc.AddChild(inner);
                row.AddChild(pc);
            }
            rows.AddChild(row);
        }
        return rows;
    }

    Control BuildImage(UiNode n, Vector2 avail)
    {
        var w = Num(n, "width", 0);
        var h = Num(n, "height", 0);
        var id = Attr(n, "image", "");
        var tint = Col(n, "color", Colors.White);
        Control host;
        if (id.Length > 0 && _image != null)
        {
            var keep = Attr(n, "preserveAspect", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
            var tr = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = keep ? TextureRect.StretchModeEnum.KeepAspectCentered : TextureRect.StretchModeEnum.Scale, Modulate = tint,
            };
            var tex = _image(id, t => { if (GodotObject.IsInstanceValid(tr)) tr.Texture = t; });
            if (tex != null) tr.Texture = tex;
            host = tr;
        }
        else host = new ColorRect { Color = id.Length > 0 ? tint : Col(n, "color", new Color(1, 1, 1, 0.15f)) };
        host.CustomMinimumSize = new Vector2(w > 0 ? w : Mathf.Min(avail.X, 40), h > 0 ? h : Mathf.Min(avail.Y, 40));
        var inner = new Vector2(w > 0 ? w : avail.X, h > 0 ? h : avail.Y);
        foreach (var ch in n.Children)
        {
            var c = Build(ch, inner);
            if (c == null) continue;
            Place(c, ch, inner, host);
            host.AddChild(c);
        }
        return host;
    }

    Control BuildText(UiNode n)
    {
        float width = Num(n, "width", Num(n, "preferredWidth", 0));
        var text = Attr(n, "text", "");
        var size = (int)Mathf.Clamp(Num(n, "fontSize", 14) * Vec3(n, "scale", 1).X, 4, 200);
        if (Attr(n, "resizeTextForBestFit", "false").Equals("true", StringComparison.OrdinalIgnoreCase) && width > 0) size = BestFit(n, text, width);
        var a = Attr(n, "alignment", "MiddleCenter");
        var across = a.EndsWith("Left") ? HorizontalAlignment.Left : a.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        var down = a.StartsWith("Upper") ? VerticalAlignment.Top : a.StartsWith("Lower") ? VerticalAlignment.Bottom : VerticalAlignment.Center;
        var wrap = width > 0 ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off;
        if (UiStyles.IsRich(text))
        {
            var r = new RichTextLabel
            {
                BbcodeEnabled = true, Text = UiStyles.BbCode(text), FitContent = true, ScrollActive = false, MouseFilter = Control.MouseFilterEnum.Ignore,
                AutowrapMode = wrap, HorizontalAlignment = across, VerticalAlignment = down,
            };
            r.AddThemeColorOverride("default_color", Col(n, "color", Colors.White));
            foreach (var key in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" }) r.AddThemeFontSizeOverride(key, size);
            Lettering(r, n, "normal_font", "font_outline_color");
            return r;
        }
        var l = new Label { Text = text, AutowrapMode = wrap, MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = across, VerticalAlignment = down };
        l.AddThemeColorOverride("font_color", Col(n, "color", Colors.White));
        l.AddThemeFontSizeOverride("font_size", size);
        Lettering(l, n, "font", "font_outline_color");
        return l;
    }

    const int BestFitLargest = 40, BestFitSmallest = 10;

    static int BestFit(UiNode n, string text, float width)
    {
        int most = (int)Num(n, "resizeTextMaxSize", BestFitLargest), least = (int)Num(n, "resizeTextMinSize", BestFitSmallest);
        var font = ThemeDB.FallbackFont;
        for (int s = most; s > least; s--)
            if (font.GetStringSize(text, HorizontalAlignment.Left, -1, s).X <= width) return s;
        return Math.Max(1, least);
    }

    static void Lettering(Control c, UiNode n, string fontKey, string outlineKey)
    {
        var style = Attr(n, "fontStyle", "Normal");
        bool bold = style.Contains("Bold"), italic = style.Contains("Italic");
        if (bold || italic)
        {
            const float Embolden = 0.6f, Slant = 0.2f;
            var fv = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = bold ? Embolden : 0 };
            if (italic) fv.VariationTransform = new Transform2D(new Vector2(1, 0), new Vector2(Slant, 1), Vector2.Zero);
            c.AddThemeFontOverride(fontKey, fv);
        }
        if (n.Attributes.ContainsKey("outline"))
        {
            c.AddThemeColorOverride(outlineKey, Col(n, "outline", Colors.Black));
            c.AddThemeConstantOverride("outline_size", Math.Max(1, (int)Mathf.Round(Vec(n, "outlineSize").X * 2)));
        }
    }

    Control BuildButton(UiNode n)
    {
        var b = new Button { Text = Attr(n, "text", ""), Disabled = Attr(n, "interactable", "true").Equals("false", StringComparison.OrdinalIgnoreCase) };
        Lettering(b, n, "font", "font_outline_color");
        if (Attr(n, "icon", "") is { Length: > 0 } icon && _image != null)
        {
            var tex = _image(icon, t => { if (GodotObject.IsInstanceValid(b)) b.Icon = t; });
            if (tex != null) b.Icon = tex;
            b.ExpandIcon = true;
        }
        b.AddThemeFontSizeOverride("font_size", (int)Mathf.Clamp(Num(n, "fontSize", 14), 6, 48));
        b.AddThemeColorOverride("font_color", Col(n, "textColor", Colors.White));
        var colors = Attr(n, "colors", "").Split('|');
        var normal = colors.Length > 0 && colors[0].Length > 0 ? Coords.ParseColor(colors[0], Colors.White) : Col(n, "color", new Color(0.85f, 0.85f, 0.85f));
        var hover = colors.Length > 1 ? Coords.ParseColor(colors[1], normal.Lightened(0.15f)) : normal.Lightened(0.15f);
        var pressed = colors.Length > 2 ? Coords.ParseColor(colors[2], normal.Darkened(0.2f)) : normal.Darkened(0.2f);
        var disabled = colors.Length > 3 ? Coords.ParseColor(colors[3], normal.Darkened(0.4f)) : normal.Darkened(0.4f);
        b.AddThemeStyleboxOverride("normal", Ui.Box(normal, 3, 6, 3));
        b.AddThemeStyleboxOverride("hover", Ui.Box(hover, 3, 6, 3));
        b.AddThemeStyleboxOverride("pressed", Ui.Box(pressed, 3, 6, 3));
        b.AddThemeStyleboxOverride("disabled", Ui.Box(disabled, 3, 6, 3));
        if (n.Id is { } id) b.Pressed += () => _send(id, "click", "-1");
        return b;
    }

    Control BuildInput(UiNode n)
    {
        var multi = Attr(n, "lineType", "SingleLine") != "SingleLine";
        if (multi)
        {
            var te = new TextEdit { Text = Attr(n, "text", ""), PlaceholderText = Attr(n, "placeholder", ""), CustomMinimumSize = new Vector2(0, 60) };
            if (n.Id is { } mid) te.FocusExited += () => _send(mid, "end", te.Text);
            return te;
        }
        var e = new LineEdit { Text = Attr(n, "text", ""), PlaceholderText = Attr(n, "placeholder", ""), Editable = !Attr(n, "interactable", "true").Equals("false", StringComparison.OrdinalIgnoreCase) };
        e.AddThemeColorOverride("font_color", Col(n, "textColor", Colors.White));
        Lettering(e, n, "font", "font_outline_color");
        if ((int)Num(n, "characterLimit", 0) is var limit and > 0) e.MaxLength = limit;
        var mode = Attr(n, "characterValidation", "None");
        if (mode != "None")
            e.TextChanged += typed =>
            {
                var ok = UiStyles.Valid(typed, mode);
                if (ok == typed) return;
                int caret = Math.Max(0, e.CaretColumn - (typed.Length - ok.Length));
                e.Text = ok;
                e.CaretColumn = Math.Min(caret, ok.Length);
            };
        if (n.Id is { } id)
        {
            bool sent = false;
            e.TextSubmitted += _ => { sent = true; _send(id, "end", e.Text); };
            e.FocusExited += () => { if (!sent) _send(id, "end", e.Text); sent = false; };
        }
        return e;
    }

    Control BuildToggle(UiNode n)
    {
        var t = new CheckBox { Text = Attr(n, "text", ""), ButtonPressed = Attr(n, "isOn", "false").Equals("true", StringComparison.OrdinalIgnoreCase) };
        t.AddThemeColorOverride("font_color", Col(n, "textColor", Colors.White));
        if (n.Id is { } id) t.Toggled += on => _send(id, "change", on ? "True" : "False");
        return t;
    }

    Control BuildToggleButton(UiNode n)
    {
        var b = (Button)BuildButton(new UiNode { Tag = n.Tag, Attributes = n.Attributes.Where(a => a.Key != "id").ToDictionary(a => a.Key, a => a.Value) });
        b.ToggleMode = true;
        b.ButtonPressed = Attr(n, "isOn", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        if (n.Id is { } id) b.Toggled += on => _send(id, "change", on ? "True" : "False");
        return b;
    }

    Control BuildToggleGroup(UiNode n, Vector2 avail)
    {
        var host = Fill(new VBoxContainer(), n, avail);
        var group = new ButtonGroup { AllowUnpress = Attr(n, "allowSwitchOff", "false").Equals("true", StringComparison.OrdinalIgnoreCase) };
        foreach (var b in Buttons(host)) if (b.ToggleMode || b is CheckBox) { b.ToggleMode = true; b.ButtonGroup = group; }
        return host;
    }

    static IEnumerable<BaseButton> Buttons(Node at)
    {
        foreach (var c in at.GetChildren())
        {
            if (c is BaseButton b) yield return b;
            foreach (var d in Buttons(c)) yield return d;
        }
    }

    Control BuildScroll(UiNode n, Vector2 avail)
    {
        bool across = n.Tag == "HorizontalScrollView";
        var sc = new ScrollContainer
        {
            HorizontalScrollMode = across ? ScrollContainer.ScrollMode.Auto : ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = across ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.Auto,
        };
        var inner = Fill(across ? new HBoxContainer() : new VBoxContainer(), n, avail);
        inner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        sc.AddChild(inner);
        return sc;
    }

    Control BuildDropdown(UiNode n)
    {
        var d = new OptionButton();
        int sel = 0, i = 0;
        foreach (var o in n.Children.Where(c => c.Tag == "Option"))
        {
            d.AddItem(Attr(o, "text", ""));
            if (Attr(o, "selected", "false").Equals("true", StringComparison.OrdinalIgnoreCase)) sel = i;
            i++;
        }
        if (d.ItemCount > 0) d.Selected = sel;
        if (n.Id is { } id) d.ItemSelected += idx => _send(id, "change", d.GetItemText((int)idx));
        return d;
    }

    Control BuildProgress(UiNode n)
    {
        var p = new ProgressBar { MinValue = 0, MaxValue = 100, Value = Num(n, "percentage", 0), ShowPercentage = Attr(n, "showPercentageText", "true").Equals("true", StringComparison.OrdinalIgnoreCase) };
        p.AddThemeStyleboxOverride("background", Ui.Box(Col(n, "color", new Color(0.2f, 0.2f, 0.2f)), 2, 0, 0));
        p.AddThemeStyleboxOverride("fill", Ui.Box(Col(n, "fillImageColor", new Color(0.4f, 0.7f, 1f)), 2, 0, 0));
        p.CustomMinimumSize = new Vector2(Num(n, "preferredWidth", 80), Num(n, "preferredHeight", 18));
        return p;
    }

    Control BuildSlider(UiNode n)
    {
        var s =
            new HSlider { MinValue = Num(n, "minValue", 0), MaxValue = Num(n, "maxValue", 1), Value = Num(n, "value", 0), Step = Attr(n, "wholeNumbers", "false").Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : 0.01,
                CustomMinimumSize = new Vector2(100, 16) };
        if (n.Id is { } id) s.DragEnded += changed => { if (changed) _send(id, "change", s.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)); };
        return s;
    }

    public static string Attr(UiNode n, string key, string fallback) => n.Attributes.TryGetValue(key, out var v) ? v : fallback;
    public static float Num(UiNode n, string key, float fallback) => n.Attributes.TryGetValue(key, out var v)
        && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) && float.IsFinite(f) ? f : fallback;
    public static Color Col(UiNode n, string key, Color fallback) => n.Attributes.TryGetValue(key, out var v)
        && v.Length > 0 ? Coords.ParseColor(v.StartsWith('#') ? v : NamedColor(v), fallback) : fallback;
    static string NamedColor(string name) => name.ToLowerInvariant() switch { "white" => "#FFFFFF", "black" => "#000000", "red" => "#DA1917", "green" => "#30B32B", "blue" => "#1F87FF", "yellow" => "#E7E52C", "orange" => "#F3701F", "grey" or "gray" => "#7F7F7F", "clear" => "#00000000", _ => name };
    public static Vector2 Vec(UiNode n, string key)
    {
        var parts = Attr(n, key, "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float P(int i) => i < parts.Length && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
            && float.IsFinite(f) ? f : 0;
        return new Vector2(P(0), P(1));
    }
    public static Vector3 Vec3(UiNode n, string key, float fallback)
    {
        var parts = Attr(n, key, "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float P(int i) => i < parts.Length && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
            && float.IsFinite(f) ? f : fallback;
        return new Vector3(P(0), P(1), P(2));
    }
    public static Vector4 Pad(UiNode n)
    {
        var parts = Attr(n, "padding", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float P(int i) => i < parts.Length && float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)
            && float.IsFinite(f) ? f : 0;
        if (parts.Length == 0) return Vector4.Zero;
        if (parts.Length == 1) return new Vector4(P(0), P(0), P(0), P(0));
        return new Vector4(P(0), P(2), P(1), P(3));
    }
}
