namespace KTable.Client.Panels;

public interface IPanelMeter
{
    V2 Measure(TextProps text, string words, float wrapWidth);
    V2? PictureSize(string sprite);
}

public static class PanelLayout
{
    const int Horizontal = 0, Vertical = 1;

    public static void Run(PanelBox root, IPanelMeter meter)
    {
        PlaceAll(root);
        foreach (var axis in new[] { Horizontal, Vertical })
        {
            var inputs = new Dictionary<PanelBox, (float Min, float Pref, float Flex)>();
            Inputs(root, axis, meter, inputs);
            Control(root, axis, meter, inputs);
            PlaceAll(root);
        }
    }

    static void PlaceAll(PanelBox b)
    {
        b.Place();
        foreach (var c in b.Children) PlaceAll(c);
    }

    static void Inputs(PanelBox b, int axis, IPanelMeter meter, Dictionary<PanelBox, (float, float, float)> inputs)
    {
        foreach (var c in b.Children) if (c.ActiveInHierarchy) Inputs(c, axis, meter, inputs);
        if (b.Group is { Enabled: true } g) inputs[b] = g.Kind == GroupKind.Grid ? GridInput(b, g, axis) : AlongInput(b, g, axis, meter, inputs);
    }

    public static List<PanelBox> RectChildren(PanelBox b) => b.Children.Where(c => c.Active && c.Layout is not { Ignore: true }).ToList();

    static (float, float, float) AlongInput(PanelBox b, GroupProps g, int axis, IPanelMeter meter, Dictionary<PanelBox, (float, float, float)> inputs)
    {
        float pad = axis == Horizontal ? g.Padding.Horizontal : g.Padding.Vertical;
        bool control = axis == Horizontal ? g.ControlWidth : g.ControlHeight;
        bool useScale = axis == Horizontal ? g.ScaleWidth : g.ScaleHeight;
        bool expand = axis == Horizontal ? g.ExpandWidth : g.ExpandHeight;
        float min = pad, pref = pad, flex = 0;
        bool across = (g.Kind == GroupKind.Vertical) ^ (axis == Vertical);
        var kids = RectChildren(b);
        foreach (var c in kids)
        {
            var (cm, cp, cf) = ChildSizes(c, axis, control, expand, meter, inputs);
            if (useScale)
            {
                float s = axis == Horizontal ? c.Scale.X : c.Scale.Y;
                cm *= s;
                cp *= s;
                cf *= s;
            }
            if (across)
            {
                min = Math.Max(cm + pad, min);
                pref = Math.Max(cp + pad, pref);
                flex = Math.Max(cf, flex);
            }
            else
            {
                min += cm + g.Spacing;
                pref += cp + g.Spacing;
                flex += cf;
            }
        }
        if (!across && kids.Count > 0)
        {
            min -= g.Spacing;
            pref -= g.Spacing;
        }
        return (min, Math.Max(min, pref), flex);
    }

    static (float, float, float) GridInput(PanelBox b, GroupProps g, int axis)
    {
        int count = RectChildren(b).Count;
        if (axis == Horizontal)
        {
            int least, most;
            if (g.Constraint == 1) least = most = g.ConstraintCount;
            else if (g.Constraint == 2) least = most = (int)Math.Ceiling(count / (float)g.ConstraintCount - 0.001f);
            else
            {
                least = 1;
                most = (int)Math.Ceiling(Math.Sqrt(count));
            }
            return (g.Padding.Horizontal + (g.CellSize.X + g.CellSpacing.X) * least - g.CellSpacing.X, g.Padding.Horizontal + (g.CellSize.X + g.CellSpacing.X) * most - g.CellSpacing.X, -1);
        }
        int rows;
        if (g.Constraint == 1) rows = (int)Math.Ceiling(count / (float)g.ConstraintCount - 0.001f);
        else if (g.Constraint == 2) rows = g.ConstraintCount;
        else
        {
            int perRow = Math.Max(1, (int)Math.Floor((b.RectSize.X - g.Padding.Horizontal + g.CellSpacing.X + 0.001f) / (g.CellSize.X + g.CellSpacing.X)));
            rows = (int)Math.Ceiling(count / (float)perRow);
        }
        float h = g.Padding.Vertical + (g.CellSize.Y + g.CellSpacing.Y) * rows - g.CellSpacing.Y;
        return (h, h, -1);
    }

    static (float, float, float) ChildSizes(PanelBox c, int axis, bool control, bool expand, IPanelMeter meter, Dictionary<PanelBox, (float, float, float)> inputs)
    {
        float min, pref, flex;
        if (!control)
        {
            min = pref = c.SizeDelta[axis];
            flex = 0;
        }
        else
        {
            min = MinSize(c, axis, meter, inputs);
            pref = PreferredSize(c, axis, meter, inputs);
            flex = FlexibleSize(c, axis, meter, inputs);
        }
        if (expand) flex = Math.Max(flex, 1);
        return (min, pref, flex);
    }

    sealed record Provider(int Priority, float Min, float Pref, float Flex);

    static IEnumerable<Provider> Providers(PanelBox b, int axis, IPanelMeter meter, Dictionary<PanelBox, (float Min, float Pref, float Flex)> inputs)
    {
        if (b.Layout is { } l)
            yield return axis == Horizontal ? new Provider(l.Priority, l.MinWidth, l.PreferredWidth, l.FlexibleWidth) : new Provider(l.Priority, l.MinHeight, l.PreferredHeight, l.FlexibleHeight);
        if (b.Text is { } t)
        {
            var size = meter.Measure(t, ShownText(b), axis == Horizontal ? 0 : b.RectSize.X);
            yield return new Provider(0, 0, size[axis], -1);
        }
        if (b.Image is { } img && b.Text == null) yield return new Provider(0, 0, ImagePreferred(img, axis, meter), -1);
        if (b.Group is { Enabled: true } && inputs.TryGetValue(b, out var g)) yield return new Provider(0, g.Min, g.Pref, g.Flex);
        if (b.Tag == "InputField" && b.Part("Text")?.Text is { } it)
        {
            var size = meter.Measure(it, b.Extra.GetValueOrDefault("typed") ?? it.Text, axis == Horizontal ? 0 : b.RectSize.X);
            yield return new Provider(1, 0, size[axis], -1);
        }
    }

    public static string ShownText(PanelBox b) => b.Text?.Text ?? "";

    static float ImagePreferred(ImageProps img, int axis, IPanelMeter meter)
    {
        if (img.Sprite == null) return 0;
        var name = PanelPrefabs.Known.GetValueOrDefault(img.Sprite) ?? img.Sprite;
        if (PanelPrefabs.Sprites.TryGetValue(name, out var s))
        {
            float scale = s.PixelsPerUnit / 100;
            return img.Type is PanelPrefabs.Sliced or 2 ? 2 * s.Border / scale : (axis == 0 ? s.W : s.H) / scale;
        }
        if (meter.PictureSize(img.Sprite) is not { } px) return 0;
        return img.Type is PanelPrefabs.Sliced or 2 ? 0 : px[axis];
    }

    static float Property(PanelBox b, Func<Provider, float> pick, float fallback, int axis, IPanelMeter meter, Dictionary<PanelBox, (float, float, float)> inputs)
    {
        float value = fallback;
        int best = int.MinValue;
        foreach (var p in Providers(b, axis, meter, inputs))
        {
            if (p.Priority < best) continue;
            float v = pick(p);
            if (v < 0) continue;
            if (p.Priority > best)
            {
                value = v;
                best = p.Priority;
            }
            else if (v > value) value = v;
        }
        return value;
    }

    public static float MinSize(PanelBox b, int axis, IPanelMeter meter, Dictionary<PanelBox, (float, float, float)> inputs) => Property(b, p => p.Min, 0, axis, meter, inputs);

    public static float PreferredSize(PanelBox b, int axis, IPanelMeter meter, Dictionary<PanelBox, (float, float, float)> inputs) =>
        Math.Max(MinSize(b, axis, meter, inputs), Property(b, p => p.Pref, 0, axis, meter, inputs));

    public static float FlexibleSize(PanelBox b, int axis, IPanelMeter meter, Dictionary<PanelBox, (float, float, float)> inputs) => Property(b, p => p.Flex, 0, axis, meter, inputs);

    static void Control(PanelBox b, int axis, IPanelMeter meter, Dictionary<PanelBox, (float Min, float Pref, float Flex)> inputs)
    {
        if (b.AspectMode != 0 && b.ActiveInHierarchy) PanelFits.Fit(b);
        int fit = axis == Horizontal ? b.FitWidth : b.FitHeight;
        if (fit != 0 && b.ActiveInHierarchy)
        {
            b.SetSize(axis, fit == 1 ? MinSize(b, axis, meter, inputs) : PreferredSize(b, axis, meter, inputs));
            b.Place();
        }
        if (b.Table != null && axis == Horizontal) PanelTables.Lay(b);
        if (b.Group is { Enabled: true } g)
        {
            if (g.Kind == GroupKind.Grid) GridCells(b, g, axis);
            else Along(b, g, axis, meter, inputs);
        }
        foreach (var c in b.Children)
        {
            c.Place();
            if (c.ActiveInHierarchy) Control(c, axis, meter, inputs);
        }
    }

    static float Alignment(Anchor a, int axis) => axis == Horizontal ? (int)a % 3 * 0.5f : (int)a / 3 * 0.5f;

    static float StartOffset(PanelBox b, Offsets pad, Anchor align, int axis, float required)
    {
        float total = required + (axis == Horizontal ? pad.Horizontal : pad.Vertical);
        float surplus = b.RectSize[axis] - total;
        return (axis == Horizontal ? pad.Left : pad.Top) + surplus * Alignment(align, axis);
    }

    static void SetChild(PanelBox c, int axis, float pos, float size, float scale)
    {
        c.AnchorMin = c.AnchorMax = new V2(0, 1);
        c.SizeDelta = c.SizeDelta.With(axis, size);
        float at = axis == Horizontal ? pos + size * c.Pivot.X * scale : -pos - size * (1 - c.Pivot.Y) * scale;
        c.Position = c.Position.With(axis, at);
    }

    static void SetChildKeep(PanelBox c, int axis, float pos, float scale)
    {
        c.AnchorMin = c.AnchorMax = new V2(0, 1);
        float size = c.SizeDelta[axis];
        float at = axis == Horizontal ? pos + size * c.Pivot.X * scale : -pos - size * (1 - c.Pivot.Y) * scale;
        c.Position = c.Position.With(axis, at);
    }

    static void Along(PanelBox b, GroupProps g, int axis, IPanelMeter meter, Dictionary<PanelBox, (float Min, float Pref, float Flex)> inputs)
    {
        float size = b.RectSize[axis];
        bool control = axis == Horizontal ? g.ControlWidth : g.ControlHeight;
        bool useScale = axis == Horizontal ? g.ScaleWidth : g.ScaleHeight;
        bool expand = axis == Horizontal ? g.ExpandWidth : g.ExpandHeight;
        float align = Alignment(g.ChildAlignment, axis);
        var kids = RectChildren(b);
        if (g.Reverse) kids.Reverse();
        if ((g.Kind == GroupKind.Vertical) ^ (axis == Vertical))
        {
            float inner = size - (axis == Horizontal ? g.Padding.Horizontal : g.Padding.Vertical);
            foreach (var c in kids)
            {
                var (min, pref, flex) = ChildSizes(c, axis, control, expand, meter, inputs);
                float scale = useScale ? (axis == Horizontal ? c.Scale.X : c.Scale.Y) : 1;
                float s = Math.Clamp(inner, min, flex > 0 ? size : pref);
                float start = StartOffset(b, g.Padding, g.ChildAlignment, axis, s * scale);
                if (control) SetChild(c, axis, start, s, scale);
                else SetChildKeep(c, axis, start + (s - c.SizeDelta[axis]) * align, scale);
            }
            return;
        }
        AlongMain(b, g, axis, kids, control, useScale, expand, align, meter, inputs);
    }

    static void AlongMain(PanelBox b, GroupProps g, int axis, List<PanelBox> kids, bool control, bool useScale, bool expand, float align, IPanelMeter meter,
        Dictionary<PanelBox, (float Min, float Pref, float Flex)> inputs)
    {
        float size = b.RectSize[axis];
        var (totalMin, totalPref, totalFlex) = inputs.TryGetValue(b, out var t) ? t : (0, 0, 0);
        float pos = axis == Horizontal ? g.Padding.Left : g.Padding.Top;
        float perFlex = 0;
        float surplus = size - totalPref;
        if (surplus > 0)
        {
            if (totalFlex == 0) pos = StartOffset(b, g.Padding, g.ChildAlignment, axis, totalPref - (axis == Horizontal ? g.Padding.Horizontal : g.Padding.Vertical));
            else if (totalFlex > 0) perFlex = surplus / totalFlex;
        }
        float lerp = totalMin != totalPref ? Math.Clamp((size - totalMin) / (totalPref - totalMin), 0, 1) : 0;
        foreach (var c in kids)
        {
            var (min, pref, flex) = ChildSizes(c, axis, control, expand, meter, inputs);
            float scale = useScale ? (axis == Horizontal ? c.Scale.X : c.Scale.Y) : 1;
            float s = min + (pref - min) * lerp + flex * perFlex;
            if (control) SetChild(c, axis, pos, s, scale);
            else SetChildKeep(c, axis, pos + (s - c.SizeDelta[axis]) * align, scale);
            pos += s * scale + g.Spacing;
        }
    }

    static void GridCells(PanelBox b, GroupProps g, int axis)
    {
        var kids = RectChildren(b);
        int count = kids.Count;
        if (axis == Horizontal)
        {
            foreach (var c in kids)
            {
                c.AnchorMin = c.AnchorMax = new V2(0, 1);
                c.SizeDelta = g.CellSize;
            }
            return;
        }
        var (cols, rows) = GridShape(b, g, count);
        int corner = g.StartCorner;
        int perLine = g.StartAxis == 0 ? cols : rows;
        int usedCols = g.StartAxis == 0 ? Math.Clamp(cols, 1, Math.Max(1, count))
            : g.Constraint != 1 ? Math.Clamp(cols, 1, (int)Math.Ceiling(count / (float)Math.Max(1, perLine))) : Math.Min(cols, count);
        int usedRows = g.StartAxis == 0 ? (g.Constraint != 2 ? Math.Clamp(rows, 1, (int)Math.Ceiling(count / (float)Math.Max(1, perLine))) : Math.Min(rows, count))
            : Math.Clamp(rows, 1, Math.Max(1, count));
        var required = new V2(usedCols * g.CellSize.X + (usedCols - 1) * g.CellSpacing.X, usedRows * g.CellSize.Y + (usedRows - 1) * g.CellSpacing.Y);
        var start = new V2(StartOffset(b, g.Padding, g.ChildAlignment, 0, required.X), StartOffset(b, g.Padding, g.ChildAlignment, 1, required.Y));
        int shift = Shift(g, count, perLine);
        for (int j = 0; j < count; j++)
        {
            var (x, y) = Cell(g, count, perLine, shift, j);
            if (corner % 2 == 1) x = usedCols - 1 - x;
            if (corner / 2 == 1) y = usedRows - 1 - y;
            SetChild(kids[j], 0, start.X + (g.CellSize.X + g.CellSpacing.X) * x, g.CellSize.X, 1);
            SetChild(kids[j], 1, start.Y + (g.CellSize.Y + g.CellSpacing.Y) * y, g.CellSize.Y, 1);
        }
    }

    static (int Cols, int Rows) GridShape(PanelBox b, GroupProps g, int count)
    {
        int cols = 1, rows = 1;
        if (g.Constraint == 1)
        {
            cols = g.ConstraintCount;
            if (count > cols) rows = count / cols + (count % cols > 0 ? 1 : 0);
        }
        else if (g.Constraint != 2)
        {
            cols = g.CellSize.X + g.CellSpacing.X <= 0 ? int.MaxValue
                : Math.Max(1, (int)Math.Floor((b.RectSize.X - g.Padding.Horizontal + g.CellSpacing.X + 0.001f) / (g.CellSize.X + g.CellSpacing.X)));
            rows = g.CellSize.Y + g.CellSpacing.Y <= 0 ? int.MaxValue : Math.Max(1, (int)Math.Floor((b.RectSize.Y - g.Padding.Vertical + g.CellSpacing.Y + 0.001f) / (g.CellSize.Y + g.CellSpacing.Y)));
        }
        else
        {
            rows = g.ConstraintCount;
            if (count > rows) cols = count / rows + (count % rows > 0 ? 1 : 0);
        }
        return (cols, rows);
    }

    static int Shift(GroupProps g, int count, int perLine)
    {
        int lines = (int)Math.Ceiling(count / (float)Math.Max(1, perLine));
        if (count <= g.ConstraintCount || lines >= g.ConstraintCount) return 0;
        int shift = g.ConstraintCount - lines;
        shift += (int)Math.Floor(shift / (perLine - 1f));
        if (count % perLine == 1) shift++;
        return shift;
    }

    static (int X, int Y) Cell(GroupProps g, int count, int perLine, int shift, int j)
    {
        if (g.StartAxis == 0)
        {
            if (g.Constraint == 2 && count - j <= shift) return (0, g.ConstraintCount - (count - j));
            return (j % perLine, j / perLine);
        }
        if (g.Constraint == 1 && count - j <= shift) return (g.ConstraintCount - (count - j), 0);
        return (j / perLine, j % perLine);
    }
}
