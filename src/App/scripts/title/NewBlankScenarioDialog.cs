using System;
using Godot;
using KTable.Core;

namespace KTable.App;

static class NewBlankScenarioDialog
{
    public static void Open(Main main)
    {
        var dlg = new ConfirmationDialog { Title = "New blank scenario", OkButtonText = "Create and host" };
        var g = new GridContainer { Columns = 2, CustomMinimumSize = new Vector2(460, 0) };
        g.AddThemeConstantOverride("v_separation", 8);
        LineEdit Field(string label, string value, string placeholder = "")
        {
            g.AddChild(new Label { Text = label });
            var le = new LineEdit { Text = value, PlaceholderText = placeholder, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            g.AddChild(le);
            return le;
        }
        var name = Field("Name", "", "e.g. Salamanca 1812");
        var start = Field("Starts", "1812-07-22 06:00", "yyyy-MM-dd HH:mm");
        var mins = Field("Minutes per turn", "15");
        dlg.AddChild(g);
        dlg.Confirmed += () =>
        {
            var store = new PackageStore(PackageStore.DefaultRoot);
            var when = DateTime.TryParse(start.Text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t) ? t.ToString("yyyy-MM-ddTHH:mm") : "1800-01-01T06:00";
            var dir = ScenarioFiles.CreateBlank(store, name.Text.Trim().Length > 0 ? name.Text.Trim() : "New scenario", when, int.TryParse(mins.Text, out var m) ? m : 15);
            main.DoHost(dir, resume: false);
        };
        Ui.Open(main, dlg, new Vector2I(520, 240));
    }
}
