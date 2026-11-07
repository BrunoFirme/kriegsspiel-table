using Godot;

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
        var reach = new SpinBox
        {
            MinValue = RangeRings.LeastReach * PerMetre, MaxValue = RangeRings.MostReach * PerMetre, Step = Scaled ? RangeRings.ScaledStep : RangeRings.UnscaledStep,
            Value = T.RangeMetres * PerMetre,
            Suffix = S.View.Calibration.UnitName,
            CustomMinimumSize = new Vector2(96, 0),
        };
        reach.ValueChanged += v => T.RangeMetres = v / PerMetre;
        reach.GetLineEdit().TextSubmitted += _ => reach.GetLineEdit().ReleaseFocus();
        form.AddChild(reach);
        _reach = reach;
        Ui.AlignForm(form);
        Panel.Body.AddChild(form);
        Panel.Fit();
    }

    public void Sync()
    {
        if (_reach != null && GodotObject.IsInstanceValid(_reach)) _reach.SetValueNoSignal(T.RangeMetres * PerMetre);
    }
}
