using System;
using System.Globalization;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class AltitudeSlider
{
    const string Tip = "How high the piece flies over the ground, in table units";
    const double Step = 0.5;
    const int ValueWidth = 48;

    public static string Shown(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    public static (HSlider Slider, Label Shown) Parts(double value, Action<double> chosen)
    {
        var slider = new HSlider
        {
            MinValue = PieceInstance.LeastAltitude, MaxValue = PieceInstance.MostAltitude, Step = Step, Value = value, ExpEdit = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None, TooltipText = Tip,
        };
        var shown = new Label { Text = Shown(value), CustomMinimumSize = new Vector2(ValueWidth, 0), HorizontalAlignment = HorizontalAlignment.Right };
        bool dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.DragEnded += changed =>
        {
            dragging = false;
            if (changed) chosen(slider.Value);
        };
        slider.ValueChanged += v =>
        {
            shown.Text = Shown(v);
            if (!dragging) chosen(v);
        };
        return (slider, shown);
    }
}
