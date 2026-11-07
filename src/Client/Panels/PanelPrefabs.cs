namespace KTable.Client.Panels;

public static class PanelPrefabs
{
    public const string UiSprite = "::UISprite", ToolSprite = "::UISprite_XmlLayout", Background = "::Background", ToolBackground = "::Background_XmlLayout", Checkmark = "::Checkmark",
        DropdownArrow = "::DropdownArrow", Knob = "::Knob", MaskSprite = "::UIMask", OutlineSharp = "::Outline_Sharp", Outline = "::Outline", GroupBackground = "::ToggleGroup_Background",
        GroupSelected = "::ToggleGroup_Selected";

    public static readonly Dictionary<string, (float W, float H, float Border, float PixelsPerUnit)> Sprites = new()
    {
        [UiSprite] = (32, 32, 10, 200), [Background] = (32, 32, 10, 200), [MaskSprite] = (32, 32, 10, 200), [ToolSprite] = (128, 128, 4, 128), [ToolBackground] = (128, 128, 4, 128),
        [Checkmark] = (40, 40, 0, 128), [Knob] = (40, 40, 0, 200), [DropdownArrow] = (40, 40, 0, 128), [Outline] = (512, 512, 64, 512), [OutlineSharp] = (512, 512, 64, 512),
        [GroupBackground] = (512, 512, 0, 100), [GroupSelected] = (512, 512, 0, 100),
    };

    public static readonly Dictionary<string, string> Known = new()
    {
        ["Sprites/Elements/UISprite_XmlLayout"] = ToolSprite, ["Sprites/Elements/Background_XmlLayout"] = ToolBackground, ["Sprites/Elements/Checkmark"] = Checkmark,
        ["Sprites/Elements/DropdownArrow"] = DropdownArrow, ["Sprites/Elements/AccordionCollapsed"] = PanelAccordions.Collapsed, ["Sprites/Elements/AccordionExpanded"] = PanelAccordions.Expanded, ["Sprites/Elements/ToggleGroup_Background"] = GroupBackground, ["Sprites/Elements/ToggleGroup_Selected"] = GroupSelected,
    };

    static readonly string[] KnownBlank =
    {
        "Sprites/Elements/TriangleRight", "Sprites/Elements/Background", "Sprites/HUD/", "Sprites/Icons/",
        "Sprites/Layout/", "Sprites/Shop/", "Sprites/XmlLayout", "Cursors/",
    };

    public static bool KnownToTheGame(string name) => Known.ContainsKey(name) || KnownBlank.Any(k => k.EndsWith('/') ? name.StartsWith(k, StringComparison.Ordinal) : name == k);
    public const int Simple = 0, Sliced = 1;
    static readonly Paint Ink = new(0.19608f, 0.19608f, 0.19608f, 1);
    static readonly Paint Faint = new(1, 1, 1, 0);

    public static PanelBox Create(string tag) => tag switch
    {
        "Panel" => Panel(),
        "Text" => Text(),
        "Image" or "RawImage" or "SVGImage" => Image(),
        "Button" => Button(tag),
        "ToggleButton" => ToggleButton(),
        "Toggle" => Toggle(),
        "ToggleGroup" => Group(),
        "InputField" => Input(),
        "Slider" => Slider(),
        "ProgressBar" => Progress(),
        "Dropdown" => Dropdown(),
        "HorizontalLayout" or "VerticalLayout" => Group(tag, tag == "HorizontalLayout" ? GroupKind.Horizontal : GroupKind.Vertical),
        "GridLayout" => Grid(),
        "TableLayout" => Table(),
        "Row" => Row(),
        "Cell" => Cell(),
        "Mask" => MaskBox(),
        "VerticalScrollView" or "HorizontalScrollView" => Scroll(tag == "VerticalScrollView"),
        "Accordion" or "AccordionSection" or "AccordionSectionHeader" or "AccordionSectionHeaderIcon" or "AccordionSectionContent" => PanelAccordions.Make(tag)!,
        "DataTable" => new PanelBox { Name = tag, Tag = tag, AnchorMin = new V2(0, 1), AnchorMax = V2.One, SizeDelta = new V2(0, DataTableHeight), Pivot = new V2(0.5f, 1),
            Image = new ImageProps { Color = Faint, Type = Sliced }, Layout = new LayoutProps { PreferredHeight = DataTableHeight } },
        "TextMeshPro" => Bare(tag),
        "TextMeshProInputField" => TypedBare(tag),
        "TextMeshProDropdown" => TextlessDropdown(),
        "Canvas" => new PanelBox { Name = tag, Tag = tag, AnchorMax = V2.Zero, Pivot = V2.Zero, Image = new ImageProps { Color = Faint, Type = Sliced, RaycastTarget = true } },
        _ => Plain(tag, Faint, null),
    };

    const float DataTableHeight = 96, BareSize = 100;

    static PanelBox Bare(string tag) => new() { Name = tag, Tag = tag, AnchorMin = V2.Half, AnchorMax = V2.Half, SizeDelta = new V2(BareSize, BareSize), Layout = new LayoutProps() };

    static PanelBox TypedBare(string tag)
    {
        var b = Bare(tag);
        b.Image = new ImageProps { Color = Paint.White, Sprite = ToolSprite, Type = Sliced, RaycastTarget = true };
        b.Select = new SelectProps { TargetGraphic = tag };
        return b;
    }

    static PanelBox TextlessDropdown()
    {
        var d = Centered("TextMeshProDropdown", 160, 30);
        d.Image = new ImageProps { Color = Paint.White, Sprite = UiSprite, Type = Sliced, RaycastTarget = true };
        d.Select = new SelectProps { TargetGraphic = "TextMeshProDropdown" };
        d.Add(new PanelBox { Name = "Label", Position = new V2(-7.5f, -0.5f), SizeDelta = new V2(-35, -13) });
        d.Add(new PanelBox { Name = "Arrow", AnchorMin = new V2(1, 0.5f), AnchorMax = new V2(1, 0.5f), Position = new V2(-15, 0), SizeDelta = new V2(20, 20),
            Image = new ImageProps { Color = Paint.White, Sprite = DropdownArrow, Type = Simple, RaycastTarget = true } });
        return d;
    }

    static PanelBox Plain(string tag, Paint color, string? sprite) =>
        new() { Name = tag, Tag = tag, Image = new ImageProps { Color = color, Sprite = sprite, Type = Sliced }, Layout = new LayoutProps() };

    static PanelBox Panel()
    {
        var p = Plain("Panel", Faint, null);
        p.Group = new GroupProps { Kind = GroupKind.Horizontal, Enabled = false };
        return p;
    }

    static PanelBox Text() => new() { Name = "Text", Tag = "Text", Text = new TextProps { Text = "New Text" }, Layout = new LayoutProps() };

    static PanelBox Image() => new() { Name = "Image", Tag = "Image", Image = new ImageProps { Color = Paint.White, Type = Simple }, Layout = new LayoutProps() };

    static PanelBox Centered(string tag, float w, float h, float y = 0) => new() { Name = tag, Tag = tag, AnchorMin = V2.Half, AnchorMax = V2.Half, SizeDelta = new V2(w, h),
        Position = new V2(0, y), Layout = new LayoutProps() };

    static PanelBox Button(string tag)
    {
        var b = Plain(tag, Paint.White, UiSprite);
        b.Image!.RaycastTarget = true;
        b.Select = new SelectProps { TargetGraphic = tag };
        b.Extra["textColors"] = "#000000";
        var table = b.Add(new PanelBox { Name = "TableLayout", Image = new ImageProps { Color = Faint, Type = Sliced }, Layout = new LayoutProps() });
        table.Table = new PanelTable { Padding = new Offsets(5, 5, 0, 0), ChildAlignment = Anchor.UpperCenter, CellColor = Faint, CellSpacing = 10, ColumnWidths = new() { 0 } };
        var row = table.Add(new PanelBox { Name = "Row", AnchorMax = V2.Zero, Pivot = V2.Zero, Image = new ImageProps { Color = Paint.Clear, Type = Sliced }, Row = new PanelRow() });
        var iconCell = row.Add(CellBox("Icon Cell", new V2(0, 1), new V2(32, 30), new V2(0, 1)));
        iconCell.Active = false;
        iconCell.Add(new PanelBox { Name = "Icon", AnchorMin = new V2(0, 1), AnchorMax = new V2(0, 1), Position = new V2(16, -15), SizeDelta = new V2(32, 30),
            Image = new ImageProps { Color = Paint.Black, Type = Simple, PreserveAspect = true }, Layout = new LayoutProps() });
        var textCell = row.Add(CellBox("Text Cell", V2.Zero, V2.Zero, V2.Zero));
        textCell.Add(new PanelBox { Name = "Text", AnchorMax = V2.Zero, Text = new TextProps { Text = "", Color = Paint.Black } });
        return b;
    }

    static PanelBox CellBox(string name, V2 anchor, V2 size, V2 pivot) => new()
    {
        Name = name, AnchorMin = anchor, AnchorMax = anchor, SizeDelta = size, Pivot = pivot, Image = new ImageProps { Color = Faint, Type = Sliced },
        Group = new GroupProps { Kind = GroupKind.Horizontal }, Cell = new PanelCell(),
    };

    static PanelBox ToggleButton()
    {
        var b = Button("ToggleButton");
        b.Select = new SelectProps { Colors = new ColorBlock(Paint.White, Paint.White, Paint.White, Paint.White, ColorBlock.Default.Disabled), TargetGraphic = "ToggleButton" };
        b.Extra["selectedBackgroundColor"] = "#838383";
        b.Extra["deselectedBackgroundColor"] = "#FFFFFF";
        b.Extra["selectedIconColor"] = "#FFFFFF";
        b.Extra["deselectedIconColor"] = "#000000";
        return b;
    }

    static PanelBox Toggle()
    {
        var t = Centered("Toggle", 160, 20, 32);
        t.Select = new SelectProps { TargetGraphic = "Background" };
        t.Group = new GroupProps { Kind = GroupKind.Horizontal, ChildAlignment = Anchor.MiddleCenter, Spacing = 4, ExpandWidth = false, ExpandHeight = false };
        var bg = t.Add(new PanelBox { Name = "Background", AnchorMax = V2.Zero, Image = new ImageProps { Color = Paint.White, Sprite = ToolSprite, Type = Sliced, RaycastTarget = true },
            Layout = new LayoutProps { MinWidth = 20, MinHeight = 20, PreferredWidth = 20, PreferredHeight = 20, FlexibleWidth = 0 } });
        bg.Add(new PanelBox { Name = "Checkmark", Image = new ImageProps { Color = Paint.Black, Sprite = Checkmark, Type = Simple, RaycastTarget = true } });
        t.Add(new PanelBox { Name = "Label", AnchorMax = V2.Zero, Text = new TextProps { Text = "", Alignment = Anchor.MiddleLeft, RaycastTarget = true },
            Layout = new LayoutProps { MinHeight = 20, PreferredHeight = 20, FlexibleWidth = 1 } });
        return t;
    }

    static PanelBox Input()
    {
        var i = Centered("InputField", 160, 30);
        i.Image = new ImageProps { Color = Paint.White, Sprite = ToolSprite, Type = Sliced, RaycastTarget = true };
        i.Select = new SelectProps { TargetGraphic = "InputField" };
        i.Add(new PanelBox { Name = "Placeholder", Position = new V2(0, -0.5f), SizeDelta = new V2(-20, -13),
            Text = new TextProps { Text = "Enter text...", Color = new Paint(Ink.R, Ink.G, Ink.B, 0.5f), Style = FontStyle.Italic, Alignment = Anchor.UpperLeft } });
        i.Add(new PanelBox { Name = "Text", Position = new V2(0, -0.5f), SizeDelta = new V2(-20, -13), Text = new TextProps { Text = "", Alignment = Anchor.UpperLeft, Rich = false, Overflow = true } });
        return i;
    }

    static PanelBox Slider()
    {
        var s = Centered("Slider", 160, 20);
        s.Select = new SelectProps { TargetGraphic = "Handle" };
        s.Add(new PanelBox { Name = "Background", AnchorMin = new V2(0, 0.25f), AnchorMax = new V2(1, 0.75f),
            Image = new ImageProps { Color = Paint.White, Sprite = ToolBackground, Type = Sliced, RaycastTarget = true } });
        var area = s.Add(new PanelBox { Name = "Fill Area", AnchorMin = new V2(0, 0.25f), AnchorMax = new V2(1, 0.75f), SizeDelta = new V2(-10, 0) });
        area.Add(new PanelBox { Name = "Fill", AnchorMax = V2.Zero, SizeDelta = new V2(10, 0), Image = new ImageProps { Color = Paint.White, Sprite = ToolSprite, Type = Sliced, RaycastTarget = true } });
        var slide = s.Add(new PanelBox { Name = "Handle Slide Area", SizeDelta = new V2(-20, 0) });
        slide.Add(new PanelBox { Name = "Handle", AnchorMax = V2.Zero, SizeDelta = new V2(20, 0), Image = new ImageProps { Color = Paint.White, Sprite = Knob, Type = Simple, RaycastTarget = true } });
        s.Extra["minValue"] = "0";
        s.Extra["maxValue"] = "1";
        s.Extra["value"] = "0";
        return s;
    }

    static PanelBox Progress()
    {
        var p = Centered("ProgressBar", 320, 32);
        p.Image = new ImageProps { Color = Paint.White, Sprite = ToolBackground, Type = Simple, RaycastTarget = true };
        p.Add(new PanelBox { Name = "Fill", AnchorMin = V2.Zero, AnchorMax = new V2(0, 1), Pivot = new V2(0, 0.5f),
            Image = new ImageProps { Color = Paint.White, Sprite = ToolSprite, Type = Sliced, RaycastTarget = true } });
        p.Add(new PanelBox { Name = "PercentageText", Text = new TextProps { Text = "0.00%", Color = Paint.Black, RaycastTarget = true } });
        return p;
    }

    static PanelBox Dropdown()
    {
        var d = Centered("Dropdown", 160, 30);
        d.Image = new ImageProps { Color = Paint.White, Sprite = ToolSprite, Type = Sliced, RaycastTarget = true };
        d.Select = new SelectProps { TargetGraphic = "Dropdown" };
        d.Add(new PanelBox { Name = "Label", Position = new V2(-7.5f, -0.5f), SizeDelta = new V2(-35, -13), Text = new TextProps { Text = "", Alignment = Anchor.MiddleLeft, RaycastTarget = true } });
        d.Add(new PanelBox { Name = "Arrow", AnchorMin = new V2(1, 0.5f), AnchorMax = new V2(1, 0.5f), Position = new V2(-15, 0), SizeDelta = new V2(20, 20),
            Image = new ImageProps { Color = Paint.Black, Sprite = DropdownArrow, Type = Simple, RaycastTarget = true } });
        return d;
    }

    static PanelBox Group()
    {
        var g = Plain("ToggleGroup", Faint, Background);
        g.Extra["toggleBackgroundImage"] = GroupBackground;
        g.Extra["toggleSelectedImage"] = GroupSelected;
        return g;
    }

    static PanelBox Group(string tag, GroupKind kind)
    {
        var g = Plain(tag, Faint, null);
        g.Group = new GroupProps { Kind = kind };
        return g;
    }

    static PanelBox Grid()
    {
        var g = Plain("GridLayout", Faint, null);
        g.Group = new GroupProps { Kind = GroupKind.Grid };
        return g;
    }

    static PanelBox Table()
    {
        var t = Plain("TableLayout", Faint, null);
        t.Table = new PanelTable { ChildAlignment = Anchor.UpperCenter, CellImage = OutlineSharp, CellColor = new Paint(1, 1, 1, 0.39216f) };
        return t;
    }

    static PanelBox Row() => new() { Name = "Row", Tag = "Row", AnchorMin = new V2(0, 1), AnchorMax = new V2(0, 1), SizeDelta = new V2(718, 32), Pivot = new V2(0, 1),
        Image = new ImageProps { Color = Faint, Type = Sliced }, Row = new PanelRow() };

    static PanelBox Cell()
    {
        var c = CellBox("Cell", new V2(0, 1), new V2(718, 32), new V2(0, 1));
        c.Tag = "Cell";
        return c;
    }

    static PanelBox MaskBox()
    {
        var m = Plain("Mask", Paint.White, MaskSprite);
        m.Mask = true;
        m.ShowMaskGraphic = false;
        return m;
    }

    static PanelBox Scroll(bool vertical)
    {
        var tag = vertical ? "VerticalScrollView" : "HorizontalScrollView";
        var s = Centered(tag, 768, 512);
        s.Image = new ImageProps { Color = new Paint(1, 1, 1, 0.392f), Sprite = Background, Type = Sliced, RaycastTarget = !vertical };
        s.RectMask = true;
        s.Extra["vertical"] = vertical ? "true" : "false";
        s.Extra["horizontal"] = vertical ? "false" : "true";
        var view = s.Add(new PanelBox { Name = "Viewport", SizeDelta = vertical ? new V2(-17, 0) : new V2(0, -17), Pivot = new V2(0, 1), Mask = true, ShowMaskGraphic = false,
            Image = new ImageProps { Color = Paint.White, Sprite = MaskSprite, Type = Sliced } });
        view.Add(new PanelBox { Name = "Content", Pivot = new V2(0, 1), AnchorMin = vertical ? new V2(0, 1) : V2.Zero, AnchorMax = vertical ? V2.One : new V2(0, 1),
            SizeDelta = vertical ? new V2(0, 512) : new V2(768, 0) });
        var bars = new[]
        {
            s.Add(Bar("Scrollbar Vertical", new V2(1, 0), V2.One, new V2(20, 0), V2.One, new Paint(0.5f, 0.5f, 0.5f, 1))),
            s.Add(Bar("Scrollbar Horizontal", V2.Zero, new V2(1, 0), new V2(0, 20), V2.Zero, Paint.White)),
        };
        bars[0].Extra["visibility"] = vertical ? "0" : "1";
        bars[1].Extra["visibility"] = vertical ? "1" : "0";
        return s;
    }

    static PanelBox Bar(string name, V2 min, V2 max, V2 size, V2 pivot, Paint color)
    {
        var bar = new PanelBox { Name = name, AnchorMin = min, AnchorMax = max, SizeDelta = size, Pivot = pivot,
            Image = new ImageProps { Color = color, Sprite = Background, Type = Sliced, RaycastTarget = true } };
        var area = bar.Add(new PanelBox { Name = "Sliding Area", SizeDelta = new V2(-20, -20) });
        area.Add(new PanelBox { Name = "Handle", AnchorMax = V2.Zero, SizeDelta = new V2(20, 20), Image = new ImageProps { Color = Paint.White, Sprite = UiSprite, Type = Sliced, RaycastTarget = true } });
        return bar;
    }

    public static PanelBox ContentOf(PanelBox element) => element.Tag switch
    {
        "VerticalScrollView" or "HorizontalScrollView" => element.Part("Content")!,
        PanelAccordions.Content => element.Part(PanelAccordions.Holder)!,
        _ => element,
    };
}
