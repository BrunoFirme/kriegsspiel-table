namespace KTable.Client.Panels;

public static class PanelLists
{
    public const string Name = "Dropdown List", Item = "Item", Index = "index";
    const float ItemHeight = 30, ListHeight = 150, Lift = 2, PortCut = 17, BarWidth = 20, Edge = 1, CheckSize = 20, Groove = 20;
    static readonly Paint ItemShade = new(0.96078f, 0.96078f, 0.96078f, 1);

    public static PanelBox Open(PanelScene scene, PanelBox dropdown, int value)
    {
        var options = dropdown.Element?.Options ?? new();
        var ctx = scene.Context;
        string? Look(string key) => dropdown.Extra.GetValueOrDefault(key);
        float itemHeight = Look("itemheight") is { } ih ? PanelValues.Float(ih) : ItemHeight;
        float listHeight = Look("dropdownheight") is { } dh ? PanelValues.Float(dh) : ListHeight;
        var list = new PanelBox
        {
            Name = Name, Tag = "VerticalScrollView", AnchorMin = V2.Zero, AnchorMax = new V2(1, 0), Pivot = new V2(0.5f, 1), Position = new V2(0, Lift), SizeDelta = new V2(0, listHeight),
            Image = new ImageProps { Color = Look("dropdownbackgroundcolor") is { } bc ? ctx.Color(bc) : Paint.White, Sprite = PanelPrefabs.ToolSprite, Type = PanelPrefabs.Sliced, RaycastTarget = true },
        };
        if (Look("dropdownbackgroundimage") is { } bi) list.Image.Sprite = PanelValues.IsNone(bi) ? null : bi;
        list.Extra["vertical"] = "true";
        list.Extra["horizontal"] = "false";
        if (Look("scrollsensitivity") is { } ss) list.Extra["scrollsensitivity"] = ss;
        dropdown.Add(list);
        var port = list.Add(new PanelBox { Name = "Viewport", SizeDelta = new V2(-PortCut, 0), Pivot = new V2(0, 1), Mask = true, ShowMaskGraphic = false,
            Image = new ImageProps { Color = Paint.White, Sprite = PanelPrefabs.MaskSprite, Type = PanelPrefabs.Sliced, RaycastTarget = true } });
        float contentHeight = itemHeight * options.Count - 2 * Edge;
        var content = port.Add(new PanelBox { Name = "Content", AnchorMin = new V2(0, 1), AnchorMax = V2.One, Pivot = new V2(0.5f, 1), SizeDelta = new V2(0, contentHeight) });
        for (int i = 0; i < options.Count; i++) content.Add(Row(dropdown, i, i == value, options.Count, itemHeight, options[i].Value ?? ""));
        Style(list, dropdown, ctx);
        if (listHeight - contentHeight > 0) list.SizeDelta = list.SizeDelta with { Y = contentHeight };
        var bar = list.Add(Bar(dropdown, ctx));
        bool needed = contentHeight > list.SizeDelta.Y;
        if (!needed)
        {
            bar.Active = false;
            port.SizeDelta = port.SizeDelta with { X = 0 };
        }
        PanelLayout.Run(list, scene.Meter);
        for (int axis = 0; axis < 2; axis++) if (Outside(scene, list, axis)) Flip(list, axis);
        PanelLayout.Run(list, scene.Meter);
        PanelScrolls.Move(list, V2.Zero);
        return list;
    }

    static PanelBox Row(PanelBox dropdown, int i, bool on, int count, float height, string text)
    {
        var row = new PanelBox
        {
            Name = Item, Tag = Item, Element = dropdown.Element, AnchorMin = V2.Zero, AnchorMax = new V2(1, 0), SizeDelta = new V2(0, height),
            Position = new V2(0, -Edge + height * (count - 1 - i) + height * 0.5f),
            Group = new GroupProps { Kind = GroupKind.Horizontal, Padding = new Offsets(1, 0, 0, 0), ChildAlignment = Anchor.MiddleLeft, ExpandWidth = false, ExpandHeight = false },
            Select = new SelectProps { TargetGraphic = "Item Background" },
        };
        row.Extra[Index] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        row.Add(new PanelBox { Name = "Item Background", Image = new ImageProps { Color = ItemShade, RaycastTarget = true }, Layout = new LayoutProps { Ignore = true } });
        var check = row.Add(new PanelBox { Name = "Item Checkmark", AnchorMin = new V2(0, 1), AnchorMax = new V2(0, 1), SizeDelta = new V2(CheckSize, CheckSize),
            Image = new ImageProps { Color = Paint.Black, Sprite = PanelPrefabs.Checkmark, PreserveAspect = true, RaycastTarget = true },
            Layout = new LayoutProps { PreferredWidth = CheckSize, PreferredHeight = CheckSize, FlexibleWidth = 0, FlexibleHeight = 0 } });
        if (!on) check.Opacity = 0;
        var label = dropdown.Part("Label")?.Text?.Clone() ?? new TextProps();
        label.Text = text;
        label.Alignment = Anchor.MiddleLeft;
        label.RaycastTarget = true;
        row.Add(new PanelBox { Name = "Item Label", AnchorMin = new V2(0, 1), AnchorMax = new V2(0, 1), Text = label, Layout = new LayoutProps { FlexibleWidth = 1 } });
        return row;
    }

    static void Style(PanelBox list, PanelBox dropdown, PanelContext ctx)
    {
        string? Look(string key) => dropdown.Extra.GetValueOrDefault(key);
        foreach (var row in list.Part("Content")?.Children ?? new())
        {
            if (Look("itembackgroundcolors") is { } colors && row.Select != null) row.Select.Colors = PanelValues.Block(colors, ctx.Colors);
            if (row.Part("Item Checkmark")?.Image is { } check)
            {
                if (Look("checkcolor") is { } cc) check.Color = ctx.Color(cc);
                if (Look("checkimage") is { } ci) check.Sprite = PanelValues.IsNone(ci) ? null : ci;
            }
            if (Look("itemtextcolor") is { } tc && row.Part("Item Label")?.Text is { } t) t.Color = ctx.Color(tc);
        }
    }

    static PanelBox Bar(PanelBox dropdown, PanelContext ctx)
    {
        string? Look(string key) => dropdown.Extra.GetValueOrDefault(key);
        var bar = new PanelBox { Name = "Scrollbar Vertical", AnchorMin = new V2(1, 0), AnchorMax = V2.One, SizeDelta = new V2(BarWidth, 0), Pivot = V2.One,
            Image = new ImageProps { Color = Look("scrollbarbackgroundcolor") is { } bc ? ctx.Color(bc) : Paint.White, Sprite = PanelPrefabs.Background, Type = PanelPrefabs.Sliced, RaycastTarget = true } };
        if (Look("scrollbarbackgroundimage") is { } bi) bar.Image.Sprite = PanelValues.IsNone(bi) ? null : bi;
        bar.Extra["visibility"] = "2";
        var area = bar.Add(new PanelBox { Name = "Sliding Area", SizeDelta = new V2(-Groove, -Groove) });
        var handle = area.Add(new PanelBox { Name = "Handle", SizeDelta = new V2(Groove, Groove),
            Image = new ImageProps { Color = Paint.White, Sprite = PanelPrefabs.UiSprite, Type = PanelPrefabs.Sliced, RaycastTarget = true } });
        if (Look("scrollbarimage") is { } si) handle.Image!.Sprite = PanelValues.IsNone(si) ? null : si;
        bar.Select = new SelectProps { TargetGraphic = "Handle" };
        if (Look("scrollbarcolors") is { } sc) bar.Select.Colors = PanelValues.Block(sc, ctx.Colors);
        return bar;
    }

    static bool Outside(PanelScene scene, PanelBox list, int axis)
    {
        var (x, y, w, h) = scene.Screen(list);
        var root = scene.Root.RectSize;
        float from = axis == 0 ? x : y, size = axis == 0 ? w : h, limit = axis == 0 ? root.X : root.Y;
        return from < -Slack || from + size > limit + Slack;
    }

    const float Slack = 0.001f;

    static void Flip(PanelBox b, int axis)
    {
        float min = b.AnchorMin[axis], max = b.AnchorMax[axis];
        b.AnchorMin = b.AnchorMin.With(axis, 1 - max);
        b.AnchorMax = b.AnchorMax.With(axis, 1 - min);
        b.Pivot = b.Pivot.With(axis, 1 - b.Pivot[axis]);
        b.Position = b.Position.With(axis, -b.Position[axis]);
    }
}
