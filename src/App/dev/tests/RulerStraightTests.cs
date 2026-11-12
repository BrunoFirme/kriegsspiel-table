using System;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class RulerStraightTests
{
    const double Near = 1e-9;

    static bool At(Vec2? p, double x, double z) => p is { } at && Math.Abs(at.X - x) < Near && Math.Abs(at.Z - z) < Near;

    [NodeTest]
    static async Task With_shift_held_the_ruler_runs_straight_and_every_length_reads_in_the_scenario_s_one_measure(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        t.Table.SetTool(Tool.Ruler);
        var ruler = t.Table.Ruler;
        Check.That(ruler.Hint().Contains("Shift", StringComparison.Ordinal) && ruler.Hint().Contains("metres", StringComparison.Ordinal),
            "the top line names the Shift key and the measure: " + ruler.Hint());
        ruler.Press(new Vec2(0, 0));
        ruler.Drag(new Vec2(10, 1), straight: false);
        Check.That(At(ruler.B, 10, 1), "a plain drag ends under the pointer");
        ruler.Drag(new Vec2(10, 1), straight: true);
        Check.That(At(ruler.B, 10, 0), $"with Shift it runs due east: {ruler.B}");
        ruler.Drag(new Vec2(7, 6), straight: true);
        Check.That(At(ruler.B, 6.5, 6.5), $"or along the diagonal nearest the pointer: {ruler.B}");
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02, Units = Calibration.Kilometres });
        await r.Frames(2);
        Check.Equal("0.46 km", t.Table.Labels.TextOf("ruler"), "under a kilometre it still reads in kilometres");
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02, Units = Calibration.Metres });
        ruler.Drag(new Vec2(100, 0), straight: true);
        await r.Frames(2);
        Check.Equal("5,000 m", t.Table.Labels.TextOf("ruler"), "and over a kilometre it stays in metres when metres are asked for");
        t.S.Do(new SetGrid { Grid = new GridDef { Kind = "square", Pitch = 8 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02, Units = Calibration.GridCells });
        ruler.Drag(new Vec2(20, 0), straight: true);
        await r.Frames(2);
        Check.Equal("2.50 cells", t.Table.Labels.TextOf("ruler"), "grid cells count the scenario's grid, fractions kept");
        t.Table.SetTool(Tool.Select);
        t.Table.QueueFree();
    }
}
