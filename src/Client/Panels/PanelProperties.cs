namespace KTable.Client.Panels;

public static partial class PanelApply
{
    public static void Property(PanelBox b, string key, string v, PanelContext ctx)
    {
        var k = key.ToLowerInvariant();
        if (Primary(b, k, v, ctx)) return;
        if (Rect(b, k, v)) return;
        if (LayoutProperty(b, k, v)) return;
        if (ElementField(b, k, v)) return;
        if (b.Image != null) ImageProperty(b.Image, k, v, ctx);
    }

    static bool Primary(PanelBox b, string k, string v, PanelContext ctx) => b.Tag switch
    {
        "Text" => b.Text != null && TextProperty(b.Text, k, v, ctx),
        "HorizontalLayout" or "VerticalLayout" or "Panel" or "Cell" => b.Group != null && GroupProperty(b.Group, k, v),
        "GridLayout" => b.Group != null && GridProperty(b.Group, k, v),
        "TableLayout" => b.Table != null && TableProperty(b.Table, k, v, ctx),
        "Row" => b.Row != null && RowProperty(b.Row, k, v),
        "Button" or "Toggle" or "ToggleButton" or "InputField" or "Slider" or "Dropdown" => b.Select != null && SelectProperty(b, k, v, ctx),
        "Mask" => k == "showmaskgraphic" && Set(() => b.ShowMaskGraphic = PanelValues.Bool(v)),
        "ToggleGroup" => k is "togglebackgroundimage" or "togglebackgroundcolor" or "toggleselectedimage" or "toggleselectedcolor" or "allowswitchoff" && Set(() => b.Extra[k] = v),
        _ => false,
    } || b.Tag == "Cell" && b.Cell != null && CellProperty(b.Cell, k, v);

    static bool Set(Action a)
    {
        a();
        return true;
    }

    static bool Rect(PanelBox b, string k, string v)
    {
        switch (k)
        {
            case "anchormin":
                b.AnchorMin = PanelValues.Vector2(v);
                return true;
            case "anchormax":
                b.AnchorMax = PanelValues.Vector2(v);
                return true;
            case "anchoredposition":
                b.Position = PanelValues.Vector2(v);
                return true;
            case "sizedelta":
                b.SizeDelta = PanelValues.Vector2(v);
                return true;
            case "pivot":
                b.Pivot = PanelValues.Vector2(v);
                return true;
            case "offsetmin":
            {
                var o = PanelValues.Vector2(v);
                var min = new V2(b.Position.X - b.SizeDelta.X * b.Pivot.X, b.Position.Y - b.SizeDelta.Y * b.Pivot.Y);
                b.SizeDelta -= o - min;
                b.Position += (o - min) * (V2.One - b.Pivot);
                return true;
            }
            case "offsetmax":
            {
                var o = PanelValues.Vector2(v);
                var max = new V2(b.Position.X + b.SizeDelta.X * (1 - b.Pivot.X), b.Position.Y + b.SizeDelta.Y * (1 - b.Pivot.Y));
                b.SizeDelta += o - max;
                b.Position += (o - max) * b.Pivot;
                return true;
            }
            case "localscale":
                b.Scale = PanelValues.Vector3(v);
                return true;
            case "localeulerangles" or "eulerangles":
                b.Rotation = PanelValues.Vector3(v);
                return true;
            case "localposition":
                var p = PanelValues.Vector3(v);
                b.Position = new V2(p.X, p.Y);
                b.Depth = p.Z;
                return true;
        }
        return false;
    }

    static bool LayoutProperty(PanelBox b, string k, string v)
    {
        var l = b.Layout ??= new LayoutProps();
        switch (k)
        {
            case "ignorelayout":
                l.Ignore = PanelValues.Bool(v);
                return true;
            case "minwidth":
                l.MinWidth = PanelValues.Float(v);
                return true;
            case "minheight":
                l.MinHeight = PanelValues.Float(v);
                return true;
            case "preferredwidth":
                l.PreferredWidth = PanelValues.Float(v);
                return true;
            case "preferredheight":
                l.PreferredHeight = PanelValues.Float(v);
                return true;
            case "flexiblewidth":
                l.FlexibleWidth = PanelValues.Float(v);
                return true;
            case "flexibleheight":
                l.FlexibleHeight = PanelValues.Float(v);
                return true;
            case "layoutpriority":
                l.Priority = PanelValues.Int(v);
                return true;
        }
        return false;
    }

    static readonly string[] ElementFields =
    {
        "showanimation", "hideanimation", "animationduration", "showanimationdelay", "hideanimationdelay", "defaultopacity", "onclicksound", "onmouseentersound", "onmouseexitsound",
        "onshowsound", "onhidesound", "audiovolume", "audiomode", "audiomixergroup", "allowdragging", "restrictdraggingtoparentbounds", "returntooriginalpositionwhenreleased", "isdropreceiver",
        "tooltip", "cursor", "cursorclick", "highlightedsprite", "pressedsprite", "selectedsprite", "disabledsprite", "navigation", "selectonup", "selectondown", "selectonleft",
        "selectonright", "hoverclass", "pressclass", "selectclass",
    };

    static bool ElementField(PanelBox b, string k, string v)
    {
        if (!ElementFields.Contains(k)) return false;
        if (k == "tooltip") b.Tooltip = v;
        else if (k == "defaultopacity") b.Opacity = PanelValues.Float(v);
        b.Extra[k] = v;
        return true;
    }

    static bool ImageProperty(ImageProps i, string k, string v, PanelContext ctx)
    {
        switch (k)
        {
            case "color":
                if (ctx.TryColor(v) is { } c) i.Color = c;
                return true;
            case "type":
                if (PanelValues.Enum<ImageTypes>(v) is { } t) i.Type = (int)t;
                return true;
            case "preserveaspect":
                i.PreserveAspect = PanelValues.Bool(v);
                return true;
            case "fillcenter":
                i.FillCenter = PanelValues.Bool(v);
                return true;
            case "fillmethod":
                if (PanelValues.Enum<FillMethods>(v) is { } m) i.FillMethod = (int)m;
                return true;
            case "fillamount":
                i.FillAmount = Math.Clamp(PanelValues.Float(v), 0, 1);
                return true;
            case "fillclockwise":
                i.FillClockwise = PanelValues.Bool(v);
                return true;
            case "fillorigin":
                i.FillOrigin = PanelValues.Int(v);
                return true;
            case "raycasttarget":
                i.RaycastTarget = PanelValues.Bool(v);
                return true;
            case "sprite" or "texture":
                i.Sprite = PanelValues.IsNone(v) ? null : v;
                return true;
        }
        return false;
    }

    enum ImageTypes { Simple, Sliced, Tiled, Filled }

    enum FillMethods { Horizontal, Vertical, Radial90, Radial180, Radial360 }

    enum Overflows { Wrap, Overflow }

    public static bool TextProperty(TextProps t, string k, string v, PanelContext ctx)
    {
        switch (k)
        {
            case "text":
                t.Text = KTable.Core.Panels.PanelText.FromAttribute(v);
                return true;
            case "font":
                t.Font = v;
                return true;
            case "fontsize":
                t.Size = PanelValues.Int(v);
                return true;
            case "fontstyle":
                if (PanelValues.Enum<FontStyle>(v) is { } f) t.Style = f;
                return true;
            case "alignment":
                if (PanelValues.Enum<Anchor>(v) is { } a) t.Alignment = a;
                return true;
            case "alignbygeometry":
                t.AlignByGeometry = PanelValues.Bool(v);
                return true;
            case "linespacing":
                t.LineSpacing = PanelValues.Float(v);
                return true;
            case "supportrichtext":
                t.Rich = PanelValues.Bool(v);
                return true;
            case "resizetextforbestfit":
                t.BestFit = PanelValues.Bool(v);
                return true;
            case "resizetextminsize":
                t.MinSize = PanelValues.Int(v);
                return true;
            case "resizetextmaxsize":
                t.MaxSize = PanelValues.Int(v);
                return true;
            case "horizontaloverflow":
                if (PanelValues.Enum<Overflows>(v) is { } h) t.Overflow = h == Overflows.Overflow;
                return true;
            case "verticaloverflow":
                if (PanelValues.Enum<VerticalOverflows>(v) is { } o) t.VerticalOverflow = o == VerticalOverflows.Overflow;
                return true;
            case "color":
                if (ctx.TryColor(v) is { } c) t.Color = c;
                return true;
            case "raycasttarget":
                t.RaycastTarget = PanelValues.Bool(v);
                return true;
        }
        return false;
    }

    enum VerticalOverflows { Truncate, Overflow }

    static bool GroupProperty(GroupProps g, string k, string v)
    {
        switch (k)
        {
            case "padding":
                g.Padding = PanelValues.Padding(v);
                return true;
            case "childalignment":
                if (PanelValues.Enum<Anchor>(v) is { } a) g.ChildAlignment = a;
                return true;
            case "spacing":
                g.Spacing = PanelValues.Float(v);
                return true;
            case "childforceexpandwidth":
                g.ExpandWidth = PanelValues.Bool(v);
                return true;
            case "childforceexpandheight":
                g.ExpandHeight = PanelValues.Bool(v);
                return true;
            case "childcontrolwidth":
                g.ControlWidth = PanelValues.Bool(v);
                return true;
            case "childcontrolheight":
                g.ControlHeight = PanelValues.Bool(v);
                return true;
            case "childscalewidth":
                g.ScaleWidth = PanelValues.Bool(v);
                return true;
            case "childscaleheight":
                g.ScaleHeight = PanelValues.Bool(v);
                return true;
            case "reversearrangement":
                g.Reverse = PanelValues.Bool(v);
                return true;
            case "enabled":
                g.Enabled = PanelValues.Bool(v);
                return true;
        }
        return false;
    }

    enum Corners { UpperLeft, UpperRight, LowerLeft, LowerRight }

    enum Axes { Horizontal, Vertical }

    enum Constraints { Flexible, FixedColumnCount, FixedRowCount }

    static bool GridProperty(GroupProps g, string k, string v)
    {
        switch (k)
        {
            case "padding":
                g.Padding = PanelValues.Padding(v);
                return true;
            case "childalignment":
                if (PanelValues.Enum<Anchor>(v) is { } a) g.ChildAlignment = a;
                return true;
            case "cellsize":
                g.CellSize = PanelValues.Vector2(v);
                return true;
            case "spacing":
                g.CellSpacing = PanelValues.Vector2(v);
                return true;
            case "startcorner":
                if (PanelValues.Enum<Corners>(v) is { } c) g.StartCorner = (int)c;
                return true;
            case "startaxis":
                if (PanelValues.Enum<Axes>(v) is { } x) g.StartAxis = (int)x;
                return true;
            case "constraint":
                if (PanelValues.Enum<Constraints>(v) is { } n) g.Constraint = (int)n;
                return true;
            case "constraintcount":
                g.ConstraintCount = PanelValues.Int(v);
                return true;
        }
        return false;
    }

    static bool TableProperty(PanelTable t, string k, string v, PanelContext ctx)
    {
        switch (k)
        {
            case "padding":
                t.Padding = PanelValues.Padding(v);
                return true;
            case "childalignment":
                if (PanelValues.Enum<Anchor>(v) is { } a) t.ChildAlignment = a;
                return true;
            case "cellspacing":
                t.CellSpacing = PanelValues.Float(v);
                return true;
            case "columnwidths":
                t.ColumnWidths = PanelValues.Floats(v);
                return true;
            case "cellpadding":
                t.CellPadding = PanelValues.Padding(v);
                return true;
            case "usegloballcellpadding" or "useglobalcellpadding":
                t.GlobalCellPadding = PanelValues.Bool(v);
                return true;
            case "rowbackgroundimage":
                t.RowImage = PanelValues.IsNone(v) ? null : v;
                return true;
            case "rowbackgroundcolor":
                t.RowColor = ctx.Color(v);
                return true;
            case "rowbackgroundcoloralternate":
                t.RowColorAlternate = ctx.Color(v);
                return true;
            case "usealternaterowbackgroundcolors":
                t.AlternateRows = PanelValues.Bool(v);
                return true;
            case "cellbackgroundimage":
                t.CellImage = PanelValues.IsNone(v) ? null : v;
                return true;
            case "cellbackgroundcolor":
                t.CellColor = ctx.Color(v);
                return true;
            case "cellbackgroundcoloralternate":
                t.CellColorAlternate = ctx.Color(v);
                return true;
            case "usealternatecellbackroundcolors":
                t.AlternateCells = PanelValues.Bool(v);
                return true;
            case "automaticallyaddcolumns":
                t.AddColumns = PanelValues.Bool(v);
                return true;
            case "automaticallyremoveemptycolumns":
                t.RemoveEmptyColumns = PanelValues.Bool(v);
                return true;
            case "autocalculateheight":
                t.AutoHeight = PanelValues.Bool(v);
                return true;
        }
        return false;
    }

    static bool RowProperty(PanelRow r, string k, string v)
    {
        switch (k)
        {
            case "preferredheight":
                r.PreferredHeight = PanelValues.Float(v);
                return true;
            case "dontusetablerowbackground":
                r.OwnBackground = PanelValues.Bool(v);
                return true;
        }
        return false;
    }

    static bool CellProperty(PanelCell c, string k, string v)
    {
        switch (k)
        {
            case "columnspan":
                c.ColumnSpan = Math.Max(1, PanelValues.Int(v));
                return true;
            case "dontusetablecellbackground":
                c.OwnBackground = PanelValues.Bool(v);
                return true;
            case "overrideglobalpadding":
                c.OwnPadding = PanelValues.Bool(v);
                return true;
        }
        return false;
    }

    static bool SelectProperty(PanelBox b, string k, string v, PanelContext ctx)
    {
        var s = b.Select!;
        switch (k)
        {
            case "interactable":
                s.Interactable = PanelValues.Bool(v);
                return true;
            case "colors" when b.Tag != "ToggleButton":
                s.Colors = PanelValues.Block(v, ctx.Colors);
                return true;
            case "text" when b.Tag == "InputField":
                b.Extra[k] = v;
                return true;
            case "ison" or "value" or "minvalue" or "maxvalue" or "wholenumbers" or "direction" or "characterlimit" or "charactervalidation" or "contenttype" or "linetype" or "readonly"
                or "caretcolor" or "selectioncolor" or "caretblinkrate" or "caretwidth" or "customcaretcolor" or "inputtype" or "keyboardtype" or "asteriskchar" or "hidemobileinput":
                b.Extra[k] = v;
                return true;
        }
        return false;
    }
}
