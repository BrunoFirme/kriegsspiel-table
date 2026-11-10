using System;
using System.Linq;
using Godot;
using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.App;

static class SmokeSlider
{
    public const string Tip = "How thick the smoke is: very fine and fine leave the pieces under it readable, dense and very dense hang and drift downwind as far as powder smoke goes in the scenario's wind on the map's scale";
    const double Step = 0.05, Snap = 0.12;
    const int ValueWidth = 72;

    public static HBoxContainer Make(double value, Action<double> chosen, bool whileDragging, string tip = Tip)
    {
        var (slider, shown) = Parts(value, chosen, whileDragging, tip);
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddChild(slider);
        row.AddChild(shown);
        return row;
    }

    public static (HSlider Slider, Label Shown) Parts(double value, Action<double> chosen, bool whileDragging, string tip = Tip)
    {
        var slider = new HSlider
        {
            MinValue = SmokeDensity.VeryFine, MaxValue = SmokeDensity.VeryDense, Step = Step, TickCount = SmokeDensity.Notches.Length, TicksOnBorders = true, Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None, TooltipText = tip,
        };
        var shown = new Label { Text = EffectKinds.DensityText(value), CustomMinimumSize = new Vector2(ValueWidth, 0), HorizontalAlignment = HorizontalAlignment.Right };
        bool dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.DragEnded += changed =>
        {
            dragging = false;
            if (changed && !whileDragging) chosen(slider.Value);
        };
        slider.ValueChanged += v =>
        {
            double notch = SmokeDensity.Notches.MinBy(n => Math.Abs(n - v));
            if (Math.Abs(notch - v) < Snap && notch != v)
            {
                slider.SetValueNoSignal(notch);
                v = notch;
            }
            shown.Text = EffectKinds.DensityText(v);
            if (whileDragging || !dragging) chosen(v);
        };
        return (slider, shown);
    }
}
