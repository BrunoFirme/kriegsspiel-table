using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class CalibrateDialog : HudPart
{
    CalibrateDialog(HudContext c) : base(c) { }

    public static void Open(HudContext c, double units) => new CalibrateDialog(c).Show(units);

    void Show(double units)
    {
        var cal = S.View.Calibration;
        string unit = cal.MeasuredIn;
        double perMetre = cal.PerMetre;
        var dlg = new ConfirmationDialog { Title = "Calibrate map scale", OkButtonText = "Set scale" };
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = $"Measured {units:0.000} map units. What is that on the ground, in {unit}?" });
        var m = new SpinBox { MinValue = 1, MaxValue = 10000000, Value = cal.ScaleBar.Length, Suffix = unit, Step = 1 };
        box.AddChild(m);
        dlg.AddChild(box);
        dlg.Confirmed += () =>
        {
            double metres = m.Value / perMetre;
            S.Do(new SetCalibration { UnitsPerMeter = units / metres });
            Log($"Scale set: {Fmt.Distance(units, new Calibration { UnitsPerMeter = units / metres, Units = cal.Units })} measured over {units:0.000} u");
            T.SetTool(Tool.Select);
        };
        Ui.Open(HudNode, dlg, new Vector2I(420, 150));
        Ui.FreeOnClose(dlg);
    }
}
