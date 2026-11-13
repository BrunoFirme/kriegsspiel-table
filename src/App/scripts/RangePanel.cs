using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class RangePanel : HudPart
{
    public readonly FloatPanel Panel;
    SpinBox? _reach;

    public RangePanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel("range", "Ranging");
    }

    bool Scaled => S.View.Calibration.UnitsPerMeter is { } u && u > 0;

    double PerMetre => Scaled ? S.View.Calibration.PerMetre : 1;

    public void Rebuild()
    {
        if (T == null || !Panel.Visible) return;
        foreach (var c in Panel.Body.GetChildren()) { Panel.Body.RemoveChild(c); c.QueueFree(); }
        var form = Ui.Form(Ui.PanelLabels);
        form.TooltipText = "How far a piece reaches when it carries no \"range\" counter · the wheel over the selection sets it too";
        form.AddChild(new Label { Text = "Reach" });
        var reach = ReachBox(S.View.Calibration, T.RangeMetres, v => T.RangeMetres = v);
        form.AddChild(reach);
        _reach = reach;
        Ui.AlignForm(form);
        Panel.Body.AddChild(form);
        Panel.Fit();
    }

    public static SpinBox ReachBox(Calibration cal, double reach, Action<double> set)
    {
        bool scaled = cal.UnitsPerMeter is { } u && u > 0;
        double perMetre = scaled ? cal.PerMetre : 1;
        var box = new SpinBox
        {
            MinValue = RangeRings.LeastReach * perMetre, MaxValue = RangeRings.MostReach * perMetre, Step = scaled ? RangeRings.ScaledStep : RangeRings.UnscaledStep,
            Value = reach * perMetre,
            Suffix = cal.UnitName,
            CustomMinimumSize = new Vector2(96, 0),
        };
        box.ValueChanged += v => set(v / perMetre);
        box.GetLineEdit().TextSubmitted += _ => box.GetLineEdit().ReleaseFocus();
        return box;
    }

    public void Sync()
    {
        if (_reach != null && GodotObject.IsInstanceValid(_reach)) _reach.SetValueNoSignal(T.RangeMetres * PerMetre);
    }
}
