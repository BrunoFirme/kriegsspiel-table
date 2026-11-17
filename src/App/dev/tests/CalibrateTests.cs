using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class CalibrateTests
{
    const double Measured = 12, Miles = 2, MetresInAMile = 1609.344;

    static ConfirmationDialog Dialog(TestRunner r) => r.GetTree().Root.FindChildren("*", "ConfirmationDialog", true, false).OfType<ConfirmationDialog>().Last();

    static T One<T>(Node under) where T : Node => under.FindChildren("*", "", true, false).OfType<T>().Single();

    [NodeTest]
    static async Task The_scale_is_set_in_the_measure_picked_where_the_line_is_typed(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0);
        CalibrateDialog.Open(h.Hud.Ctx, Measured);
        await r.Frames(2);
        var dlg = Dialog(r);
        var measure = One<OptionButton>(dlg);
        var length = One<SpinBox>(dlg);
        Check.Equal("Metres", measure.GetItemText(measure.Selected), "the measure offered is the scenario's");
        Check.Equal(1000.0, length.Value, "with a kilometre's worth of it");
        int miles = Enumerable.Range(0, measure.ItemCount).Single(i => measure.GetItemText(i) == "Miles");
        Check.That(Enumerable.Range(0, measure.ItemCount).All(i => measure.GetItemText(i) != "Grid cells"), "only lengths are offered");
        measure.Select(miles);
        measure.EmitSignal(OptionButton.SignalName.ItemSelected, miles);
        Check.Equal(1.0, length.Value, "picking miles offers one mile");
        length.Value = Miles;
        measure.Select(0);
        measure.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
        Check.Equal(Miles, length.Value, "a typed length stays when the measure changes");
        measure.Select(miles);
        measure.EmitSignal(OptionButton.SignalName.ItemSelected, miles);
        dlg.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var cal = h.S.View.Calibration;
        Check.That(System.Math.Abs(cal.UnitsPerMeter!.Value - Measured / (Miles * MetresInAMile)) < 1e-9, $"twelve units are two miles: {cal.UnitsPerMeter}");
        Check.Equal(Calibration.Miles, cal.Units, "and distances are written in miles");

        h.S.Do(new KTable.Core.Protocol.SetCalibration { UnitsPerMeter = cal.UnitsPerMeter, Units = Calibration.GridCells });
        CalibrateDialog.Open(h.Hud.Ctx, Measured);
        await r.Frames(2);
        var again = Dialog(r);
        Check.Equal("Metres", One<OptionButton>(again).GetItemText(One<OptionButton>(again).Selected), "a scenario counting grid cells is offered metres");
        again.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(Calibration.Metres, h.S.View.Calibration.Units, "and the scenario's measure becomes the one picked");
    }
}
