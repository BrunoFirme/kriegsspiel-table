using System;
using System.Linq;
using Godot;

namespace KTable.App;

sealed partial class Measure : Container
{
    public const float Form = 720;

    public float Most { get; init; } = Form;

    public Measure() => SizeFlagsHorizontal = SizeFlags.ExpandFill;

    public override Vector2 _GetMinimumSize() =>
        GetChildren().OfType<Control>().Where(c => c.Visible && !c.TopLevel).Aggregate(Vector2.Zero, (m, c) => m.Max(c.GetCombinedMinimumSize()));

    public override void _Notification(int what)
    {
        if (what != NotificationSortChildren) return;
        float width = Math.Min(Size.X, Most);
        foreach (var c in GetChildren().OfType<Control>().Where(c => c.Visible && !c.TopLevel)) FitChildInRect(c, new Rect2(0, 0, width, Size.Y));
    }
}
