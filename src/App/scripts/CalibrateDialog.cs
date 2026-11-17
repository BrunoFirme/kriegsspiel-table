using System;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class CalibrateDialog : HudPart
{
    const double Most = 10000000, FineStep = 0.1;

    CalibrateDialog(HudContext c) : base(c) { }

    public static void Open(HudContext c, double units) => new CalibrateDialog(c).Show(units);

    void Show(double units)
    {
        var cal = S.View.Calibration;
        var measures = Calibration.Lengths;
        string written = cal.Units == Calibration.GridCells ? Calibration.Metres : cal.Units;
        var dlg = new ConfirmationDialog { Title = "Map scale", OkButtonText = "Set scale" };
        var form = Ui.Form(Ui.DialogLabels);
        form.AddChild(new Label { Text = "Measured" });
        form.AddChild(new Label { Text = $"{units:0.000} u" });
        form.AddChild(new Label { Text = "On the ground" });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        var length = new SpinBox { MaxValue = Most, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "What the measured line is on the ground" };
        var measure = new OptionButton { TooltipText = "The measure the map's scale bar is drawn in; distances are then written in it" };
        foreach (var (_, name) in measures) measure.AddItem(name);
        measure.Selected = Math.Max(0, Array.FindIndex(measures, m => m.Id == written));
        bool typed = false, offering = false;
        void Offer()
        {
            string id = measures[measure.Selected].Id;
            offering = true;
            double step = Calibration.MetresIn(id) > 1 ? FineStep : 1;
            length.Step = Math.Min(step, length.Step);
            length.MinValue = step;
            length.Step = step;
            if (!typed) length.Value = Calibration.BarIn(id);
            offering = false;
        }
        Offer();
        length.ValueChanged += _ => typed |= !offering;
        measure.ItemSelected += _ => Offer();
        row.AddChild(length);
        row.AddChild(measure);
        form.AddChild(row);
        Ui.AlignForm(form);
        dlg.AddChild(form);
        dlg.Confirmed += () =>
        {
            string id = measures[measure.Selected].Id;
            double metres = length.Value * Calibration.MetresIn(id);
            string? writtenIn = cal.Units == Calibration.GridCells ? null : id;
            S.Do(new SetCalibration { UnitsPerMeter = units / metres, Units = writtenIn });
            Log($"Scale set: {Fmt.Distance(units, new Calibration { UnitsPerMeter = units / metres, Units = id })} measured over {units:0.000} u");
            T.SetTool(Tool.Select);
        };
        Ui.Open(HudNode, dlg, new Vector2I(420, 150));
        Ui.FreeOnClose(dlg);
    }
}
