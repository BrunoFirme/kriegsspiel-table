using System;
using Godot;
using KTable.Client.Effects;
using KTable.Core.Model;

namespace KTable.App;

static class IntensitySlider
{
    const string Tip = "How hard the effect plays beside its kind: shots, volleys and bursts come further apart or closer together; fire at will, tracers, rain, snow and a melee show less or more";
    const double Step = 0.05, Snap = 0.08;
    const int ValueWidth = 48;

    public static (HSlider Slider, Label Shown) Parts(double value, Action<double> chosen)
    {
        var slider = new HSlider
        {
            MinValue = EffectIntensity.Least, MaxValue = EffectIntensity.Most, Step = Step, Value = value, ExpEdit = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None, TooltipText = Tip,
        };
        var shown = new Label { Text = EffectKinds.IntensityText(value), CustomMinimumSize = new Vector2(ValueWidth, 0), HorizontalAlignment = HorizontalAlignment.Right };
        bool dragging = false;
        slider.DragStarted += () => dragging = true;
        slider.DragEnded += changed =>
        {
            dragging = false;
            if (changed) chosen(slider.Value);
        };
        slider.ValueChanged += v =>
        {
            if (Math.Abs(v - EffectIntensity.AsMade) < Snap && v != EffectIntensity.AsMade)
            {
                slider.SetValueNoSignal(EffectIntensity.AsMade);
                v = EffectIntensity.AsMade;
            }
            shown.Text = EffectKinds.IntensityText(v);
            if (!dragging) chosen(v);
        };
        return (slider, shown);
    }
}
