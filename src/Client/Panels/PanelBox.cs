using KTable.Core.Panels;

namespace KTable.Client.Panels;

public sealed class LayoutProps
{
    public bool Ignore;
    public float MinWidth = -1, MinHeight = -1, PreferredWidth = -1, PreferredHeight = -1, FlexibleWidth = -1, FlexibleHeight = -1;
    public int Priority = 1;
    public LayoutProps Clone() => (LayoutProps)MemberwiseClone();
}

public sealed class ImageProps
{
    public Paint Color = Paint.White;
    public string? Sprite;
    public int Type;
    public bool PreserveAspect, RaycastTarget, FillCenter = true, FillClockwise = true;
    public int FillMethod = 4, FillOrigin;
    public float FillAmount = 1;
    public ImageProps Clone() => (ImageProps)MemberwiseClone();
}

public sealed class TextProps
{
    public string Text = "";
    public Paint Color = new(0.19608f, 0.19608f, 0.19608f, 1);
    public string? Font;
    public int Size = 14, MinSize = 10, MaxSize = 40;
    public FontStyle Style;
    public bool BestFit, AlignByGeometry, Rich = true, RaycastTarget;
    public Anchor Alignment = Anchor.MiddleCenter;
    public bool Overflow, VerticalOverflow;
    public float LineSpacing = 1;
    public TextProps Clone() => (TextProps)MemberwiseClone();
}

public sealed class EffectProps
{
    public Paint Color = new(0, 0, 0, 0.5f);
    public V2 Distance = new(1, -1);
    public bool UseGraphicAlpha = true;
    public EffectProps Clone() => (EffectProps)MemberwiseClone();
}

public enum GroupKind { Horizontal, Vertical, Grid }

public sealed class GroupProps
{
    public GroupKind Kind;
    public bool Enabled = true;
    public Offsets Padding;
    public Anchor ChildAlignment = Anchor.UpperLeft;
    public float Spacing;
    public bool ExpandWidth = true, ExpandHeight = true, ControlWidth = true, ControlHeight = true, ScaleWidth, ScaleHeight, Reverse;
    public int StartCorner, StartAxis, Constraint, ConstraintCount = 2;
    public V2 CellSize = new(100, 100), CellSpacing = V2.Zero;
    public GroupProps Clone() => (GroupProps)MemberwiseClone();
}

public sealed class SelectProps
{
    public ColorBlock Colors = ColorBlock.Default;
    public bool Interactable = true;
    public int Transition = 1;
    public string? TargetGraphic;
    public SelectProps Clone() => (SelectProps)MemberwiseClone();
}

public sealed class PanelBox
{
    public string Name = "";
    public string Tag = "";
    public UiElement? Element;
    public List<int>? Path;
    public PanelBox? Parent;
    public readonly List<PanelBox> Children = new();
    public bool Active = true;
    public V2 AnchorMin = V2.Zero, AnchorMax = V2.One, Position = V2.Zero, SizeDelta = V2.Zero, Pivot = V2.Half, CurrentOffset = V2.Zero;
    public V3 Scale = V3.One, Rotation = V3.Zero;
    public float Depth;
    public LayoutProps? Layout;
    public ImageProps? Image;
    public TextProps? Text;
    public EffectProps? Outline, Shadow;
    public GroupProps? Group;
    public SelectProps? Select;
    public PanelTable? Table;
    public PanelRow? Row;
    public PanelCell? Cell;
    public int FitWidth, FitHeight;
    public float Opacity = 1;
    public bool Mask, ShowMaskGraphic = true, RectMask;
    public string? Tooltip;
    public Dictionary<string, string> Extra = new(StringComparer.OrdinalIgnoreCase);
    public V2 RectMin, RectSize;
    public V2 PivotAt;

    public PanelBox Add(PanelBox child)
    {
        child.Parent = this;
        Children.Add(child);
        return child;
    }

    public PanelBox? Part(string name) => Children.FirstOrDefault(c => c.Name == name) ?? Children.Select(c => c.Part(name)).FirstOrDefault(p => p != null);

    public bool ActiveInHierarchy => Active && (Parent?.ActiveInHierarchy ?? true);

    public void SetSize(int axis, float size)
    {
        var anchorSpan = Parent == null ? 0 : (AnchorMax[axis] - AnchorMin[axis]) * Parent.RectSize[axis];
        SizeDelta = SizeDelta.With(axis, size - anchorSpan);
    }

    public void Place()
    {
        var parentMin = Parent?.RectMin ?? V2.Zero;
        var parentSize = Parent?.RectSize ?? RectSize;
        var aMin = parentMin + AnchorMin * parentSize;
        var aMax = parentMin + AnchorMax * parentSize;
        RectSize = aMax - aMin + SizeDelta;
        PivotAt = aMin + (aMax - aMin) * Pivot + Position;
        RectMin = new V2(-Pivot.X * RectSize.X, -Pivot.Y * RectSize.Y);
    }
}
