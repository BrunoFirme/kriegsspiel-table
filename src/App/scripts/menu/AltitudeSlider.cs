using System;
using System.Globalization;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class AltitudeSlider
{
    const string Tip = "How high the piece flies over the ground, in table units";
    const double Step = 0.05;
    const int ValueWidth = 48;

    public static string Shown(double value) => value.ToString("0.0#", CultureInfo.InvariantCulture);

    public static (HSlider Slider, LineEdit Shown) Parts(double value, Action<double> chosen)
    {
        var slider = new HSlider
        {
            MinValue = PieceInstance.LeastAltitude, MaxValue = PieceInstance.MostAltitude, Step = Step, Value = value, ExpEdit = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None, TooltipText = Tip,
        };
        var shown = Ui.ValueBox(slider, Shown, ValueWidth);
        bool dragging = false;
        double sent = value;
        void Choose(double v)
        {
            if (v == sent) return;
            sent = v;
            chosen(v);
        }
        slider.DragStarted += () => dragging = true;
        slider.DragEnded += changed =>
        {
            dragging = false;
            if (changed) Choose(slider.Value);
        };
        slider.ValueChanged += v =>
        {
            if (!dragging) Choose(v);
        };
        return (slider, shown);
    }
}
