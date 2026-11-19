using Godot;
using KTable.Client.Prefs;
using KTable.Core.Model;

namespace KTable.App;

static class FillOptions
{
    const double ReachStep = 0.01;

    public static void Reach(GridContainer form, Settings cfg) =>
        ReliefPanel.Row(form, cfg, "Reach", 0, Settings.MostFillReach, ReachStep, cfg.FillReach, v => $"{v * 100:0}%", v => cfg.FillReach = (float)v,
            "How different a colour may be and still be flooded");

    public static Control? StopAtGrid(Settings cfg, GridDef? grid) => GridSnap.On(grid)
        ? Tick("Stop at the grid", cfg.FillStopsAtGrid, "The fill stays inside the cell clicked · off: it runs on past the cells' edges", on => cfg.FillStopsAtGrid = on, cfg)
        : null;

    static Control Tick(string text, bool on, string tip, System.Action<bool> set, Settings cfg)
    {
        var tick = new CheckBox { Text = text, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
        tick.Toggled += v =>
        {
            set(v);
            cfg.Save();
        };
        return Ui.UnderControls(tick, Ui.PanelLabels);
    }
}
