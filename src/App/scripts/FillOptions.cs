using Godot;
using KTable.Client.Prefs;
using KTable.Core.Model;

namespace KTable.App;

static class FillOptions
{
    const int ReachWidth = 36;

    public static void Reach(GridContainer form, Settings cfg)
    {
        form.AddChild(new Label { Text = "Reach" });
        var reach = new HSlider
        {
            MinValue = 0, MaxValue = Settings.MostFillReach, Step = 0.01, Value = cfg.FillReach, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "How different a colour may be and still be flooded",
        };
        reach.ValueChanged += v => cfg.FillReach = (float)v;
        reach.DragEnded += _ => cfg.Save();
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        row.AddChild(reach);
        row.AddChild(Ui.ValueBox(reach, v => $"{v * 100:0}%", ReachWidth));
        form.AddChild(row);
    }

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
