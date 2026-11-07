using System.Globalization;

namespace KTable.Client.Panels;

public static class PanelAccordions
{
    public const string Accordion = "Accordion", Section = "AccordionSection", Header = "AccordionSectionHeader", Icon = "AccordionSectionHeaderIcon", Content = "AccordionSectionContent";
    public const string Holder = "ContentContainer", Opening = "#open:", Collapsed = "::AccordionCollapsed", Expanded = "::AccordionExpanded";
    public const float Duration = 0.25f;
    const float HeaderHeight = 64, ContentHeight = 128, Gap = 8, IconSize = 8;
    const int Pad = 16;
    static readonly Paint Clear = new(1, 1, 1, 0), Grey = new(0.5f, 0.5f, 0.5f, 1), Unset = new(0, 0, 0, 0);
    public static readonly string[] Looks = { "collapsible", "accordionAnimationDuration", "allowSectionReordering", "expanded", "contentHeight", "collapsedSprite", "collapsedColor", "expandedSprite", "expandedColor" };

    public static PanelBox? Make(string tag) => tag switch
    {
        Accordion => new PanelBox { Name = tag, Tag = tag, Image = new ImageProps { Color = Clear, Type = PanelPrefabs.Sliced, RaycastTarget = true }, Layout = new LayoutProps(),
            Group = new GroupProps { Kind = GroupKind.Vertical, ExpandHeight = false } },
        Section => new PanelBox { Name = tag, Tag = tag, Image = new ImageProps { Color = Clear, Type = PanelPrefabs.Sliced },
            Layout = new LayoutProps { PreferredHeight = 0, FlexibleHeight = 0 }, Group = new GroupProps { Kind = GroupKind.Vertical, ExpandHeight = false } },
        Header => new PanelBox { Name = tag, Tag = tag, Pivot = new V2(0.5f, 1),
            Image = new ImageProps { Color = Grey, Sprite = PanelPrefabs.ToolSprite, Type = PanelPrefabs.Sliced, RaycastTarget = true },
            Layout = new LayoutProps { PreferredHeight = HeaderHeight, FlexibleHeight = 0 },
            Group = new GroupProps
            {
                Kind = GroupKind.Horizontal, Padding = new Offsets(Pad, Pad, Pad, Pad), ChildAlignment = Anchor.MiddleLeft, Spacing = Gap, ExpandWidth = false, ExpandHeight = false,
            } },
        Icon => new PanelBox { Name = tag, Tag = tag, Pivot = new V2(0.5f, 1), Image = new ImageProps { Color = Paint.White, Sprite = Collapsed, PreserveAspect = true },
            Layout = new LayoutProps { PreferredWidth = IconSize, PreferredHeight = IconSize, FlexibleWidth = 0, FlexibleHeight = 0 } },
        Content => ContentBox(),
        _ => null,
    };

    static PanelBox ContentBox()
    {
        var c = new PanelBox { Name = Content, Tag = Content, AnchorMin = V2.Zero, AnchorMax = V2.Zero, Pivot = new V2(0.5f, 1), RectMask = true,
            Image = new ImageProps { Color = Paint.White, Sprite = PanelPrefabs.ToolSprite, Type = PanelPrefabs.Sliced, RaycastTarget = true },
            Layout = new LayoutProps { PreferredHeight = ContentHeight, FlexibleHeight = 0 } };
        c.Add(new PanelBox { Name = Holder, AnchorMin = new V2(0, 0.5f), AnchorMax = new V2(1, 0.5f), Layout = new LayoutProps(), Group = new GroupProps { Kind = GroupKind.Horizontal } });
        return c;
    }

    public static string Key(PanelBox section) => PanelScene.Key(section.Path!);

    public static bool Open(PanelBox section, IReadOnlyDictionary<string, string>? local) =>
        local != null && section.Path != null && local.TryGetValue(Key(section), out var v) ? PanelValues.Bool(v) : PanelValues.Bool(section.Extra.GetValueOrDefault("expanded"));

    public static float Shown(PanelBox section, IReadOnlyDictionary<string, string>? local) =>
        local != null && section.Path != null && local.TryGetValue(Opening + Key(section), out var f) ? Math.Clamp(PanelValues.Float(f), 0, 1) : Open(section, local) ? 1 : 0;

    public static string Text(float f) => f.ToString(CultureInfo.InvariantCulture);

    public static PanelBox? OwnerOf(PanelBox b)
    {
        for (var at = b.Parent; at != null; at = at.Parent) if (at.Tag == Accordion && at.Element != null) return at;
        return null;
    }

    public static List<PanelBox> Sections(PanelBox accordion, IEnumerable<PanelBox> elements) => elements.Where(e => e.Tag == Section && OwnerOf(e) == accordion).ToList();

    public static float Height(PanelBox section) => section.Extra.TryGetValue("contentheight", out var h) ? PanelValues.Float(h) : ContentHeight;

    public static void Settle(IEnumerable<PanelBox> elements, IReadOnlyDictionary<string, string>? local, PanelContext ctx)
    {
        foreach (var section in elements.Where(e => e.Tag == Section && e.Path != null && OwnerOf(e) != null))
        {
            var header = section.Children.FirstOrDefault(c => c.Tag == Header && c.Element != null);
            var content = section.Children.FirstOrDefault(c => c.Tag == Content && c.Element != null);
            if (header == null || content == null) continue;
            bool open = Open(section, local);
            float height = Height(section), shown = Shown(section, local) * height;
            content.Layout ??= new LayoutProps();
            content.Layout.PreferredHeight = shown;
            if (content.Part(Holder) is { } holder) holder.SizeDelta = holder.SizeDelta with { Y = height };
            section.Layout ??= new LayoutProps();
            section.Layout.PreferredHeight = Math.Max(0, header.Layout?.PreferredHeight ?? 0) + shown;
            if (Find(header, Icon) is { Image: { } icon } iconBox) Look(iconBox, icon, open, ctx);
            if (!open) Disable(content);
        }
    }

    static PanelBox? Find(PanelBox b, string tag)
    {
        foreach (var c in b.Children)
        {
            if (c.Tag == tag && c.Element != null) return c;
            if (Find(c, tag) is { } d) return d;
        }
        return null;
    }

    static void Look(PanelBox box, ImageProps icon, bool open, PanelContext ctx)
    {
        string? Look(string key) => box.Extra.GetValueOrDefault(key);
        var collapsed = Look("collapsedsprite") is { } cs ? (PanelValues.IsNone(cs) ? null : cs) : Collapsed;
        if (collapsed != null) icon.Sprite = collapsed;
        if (Look("collapsedcolor") is { } cc && ctx.Color(cc) is var c1 && c1 != Unset) icon.Color = c1;
        if (!open) return;
        var expanded = Look("expandedsprite") is { } es ? (PanelValues.IsNone(es) ? null : es) : Expanded;
        if (expanded != null) icon.Sprite = expanded;
        if (Look("expandedcolor") is { } ec && ctx.Color(ec) is var c2 && c2 != Unset) icon.Color = c2;
    }

    static void Disable(PanelBox b)
    {
        foreach (var c in b.Children)
        {
            if (c.Select != null) c.Select.Interactable = false;
            Disable(c);
        }
    }
}
