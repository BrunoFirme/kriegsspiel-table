using System.Globalization;

namespace KTable.Client.Panels;

public static class PanelFits
{
    public enum Mode { None, WidthControlsHeight, HeightControlsWidth, FitInParent, EnvelopeParent }

    public static float Ratio(string value)
    {
        if (string.IsNullOrEmpty(value)) return 1;
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)) return r;
        var parts = value.Split(':');
        return parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b != 0 ? a / b : 1;
    }

    public static void Fit(PanelBox b)
    {
        float ratio = b.AspectRatio;
        switch ((Mode)b.AspectMode)
        {
            case Mode.WidthControlsHeight:
                b.SetSize(1, b.RectSize.X / ratio);
                break;
            case Mode.HeightControlsWidth:
                b.SetSize(0, b.RectSize.Y * ratio);
                break;
            case Mode.FitInParent or Mode.EnvelopeParent when b.Parent != null:
                var parent = b.Parent.RectSize;
                b.AnchorMin = V2.Zero;
                b.AnchorMax = V2.One;
                b.Position = V2.Zero;
                bool wide = parent.Y * ratio < parent.X;
                b.SizeDelta = wide ^ (b.AspectMode == (int)Mode.FitInParent) ? new V2(0, parent.X / ratio - parent.Y) : new V2(parent.Y * ratio - parent.X, 0);
                break;
        }
        b.Place();
    }

    public static void SetSize(PanelBox b, int axis, float size)
    {
        float parent = b.Parent == null ? 0 : Rect(b.Parent, axis);
        b.SizeDelta = b.SizeDelta.With(axis, size - (b.AnchorMax[axis] - b.AnchorMin[axis]) * parent);
    }

    static float Rect(PanelBox b, int axis)
    {
        float span = b.AnchorMax[axis] - b.AnchorMin[axis];
        return b.Parent == null || span == 0 ? b.SizeDelta[axis] : span * Rect(b.Parent, axis) + b.SizeDelta[axis];
    }

    public static void Offsets(PanelBox b, V2 min, V2 max)
    {
        b.SizeDelta = max - min;
        b.Position = min + b.SizeDelta * b.Pivot;
    }
}
