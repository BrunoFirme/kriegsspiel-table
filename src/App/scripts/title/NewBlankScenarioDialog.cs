using System;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class NewBlankScenarioDialog
{
    public static void Open(Main main)
    {
        var dlg = new ConfirmationDialog { Title = "New blank scenario", OkButtonText = "Create and host" };
        var g = Ui.Form(Ui.DialogLabels);
        g.CustomMinimumSize = Ui.Px(460, 0);
        LineEdit Field(string label, string value, string placeholder = "")
        {
            g.AddChild(new Label { Text = label });
            var le = new LineEdit { Text = value, PlaceholderText = placeholder, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            g.AddChild(le);
            return le;
        }
        var name = Field("Name", "", "e.g. Salamanca 1812");
        var start = Field("Starts", "1812-07-22 06:00", "yyyy-MM-dd HH:mm");
        g.AddChild(new Label { Text = "Each turn" });
        var step = new TurnLength(GameClock.UsualTurnSeconds);
        g.AddChild(step.Row);
        Ui.AlignForm(g);
        dlg.AddChild(g);
        dlg.Confirmed += () =>
        {
            var store = new PackageStore(PackageStore.DefaultRoot);
            var when = CalendarDef.Gregorian.Text(CalendarDef.Gregorian.Read(start.Text) ?? GameClock.UsualStart);
            var dir = ScenarioFiles.CreateBlank(store, name.Text.Trim().Length > 0 ? name.Text.Trim() : "New scenario", when, step.Seconds,
                KTable.Client.Backdrops.NewLook(main.Cfg.NewBackdrop, main.Cfg.NewTableShade));
            main.Title.Shelf.FileNew(false, dir);
            main.DoHost(dir, resume: false);
        };
        Ui.Open(main, dlg, new Vector2I(520, 240));
    }
}
