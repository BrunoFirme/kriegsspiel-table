using System;
using Godot;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class NewBlankScenarioDialog
{
    static readonly CalendarDef Calendar = CalendarDef.Gregorian;
    static readonly long Example = Calendar.Seconds(new DateParts(1812, 7, 22, 6, 0, 0)) ?? GameClock.UsualStart;
    internal const string StartsTip = "A date and time as 22 July 1812 06:00, 22/07/1812 06:00 or 1812-07-22 06:00";

    internal static bool Takes(string starts) => starts.Trim().Length == 0 || Calendar.Read(starts) != null;

    public static void Open(Main main)
    {
        var dlg = new ConfirmationDialog { Title = "New blank scenario", OkButtonText = "Create and host" };
        var g = Ui.Form(Ui.DialogLabels);
        g.CustomMinimumSize = Ui.Px(460, 0);
        LineEdit Field(string label, string value, Control? beside = null)
        {
            g.AddChild(new Label { Text = label });
            var le = new LineEdit { Text = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            if (beside == null) g.AddChild(le);
            else
            {
                var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
                row.AddChild(le);
                row.AddChild(beside);
                g.AddChild(row);
            }
            return le;
        }
        var name = Field("Name", "");
        LineEdit start = null!;
        void Checked(string t) => dlg.GetOkButton().Disabled = !Takes(t);
        var pick = CalendarPick.Opener(() => Calendar, () => Calendar.Read(start.Text) ?? Example, at =>
        {
            start.Text = Calendar.Typed(at);
            Checked(start.Text);
        });
        start = Field("Starts", Calendar.Typed(Example), pick);
        start.TooltipText = StartsTip;
        start.TextChanged += Checked;
        g.AddChild(new Label { Text = "Each turn" });
        var step = new TurnLength(GameClock.UsualTurnSeconds);
        g.AddChild(step.Row);
        Ui.AlignForm(g);
        dlg.AddChild(g);
        dlg.Confirmed += () =>
        {
            var store = new PackageStore(PackageStore.DefaultRoot);
            var when = Calendar.Text(Calendar.Read(start.Text) ?? GameClock.UsualStart);
            var dir = ScenarioFiles.CreateBlank(store, name.Text.Trim().Length > 0 ? name.Text.Trim() : "New scenario", when, step.Seconds,
                KTable.Client.Backdrops.NewLook(main.Cfg.NewBackdrop, main.Cfg.NewTableShade));
            main.Title.Shelf.FileNew(false, dir);
            main.DoHost(dir, resume: false);
        };
        Ui.Open(main, dlg, new Vector2I(520, 240));
    }
}
