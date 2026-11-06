using Godot;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class ReportPage(TitleScreen t)
{
    static bool _toldOfClose;

    public bool TellOfUnexpectedClose()
    {
        if (_toldOfClose || Diagnostics.ClosedUnexpectedly == null) return false;
        _toldOfClose = true;
        Show();
        return true;
    }

    public void Show()
    {
        t.Select(-1);
        var closed = Diagnostics.ClosedUnexpectedly;
        var v = closed != null
            ? t.NewPage("It closed unexpectedly", $"Kriegsspiel Table stopped without being quit (the run started {closed.Started:d MMM, HH:mm}). Games save themselves as you play: host it again to carry on where it stopped.")
            : t.NewPage("Report a problem", "");
        v.AddThemeConstantOverride("separation", Ui.Gap.Block);
        var copied = new Label { Modulate = Muted };
        var copy = Primary(closed != null ? "Copy the report" : "Copy diagnostics", () =>
        {
            AppClipboard.SetText(Diagnostics.Report());
            copied.Text = "Copied.";
        });
        copy.TooltipText = closed != null
            ? "Version, PC, graphics card, and the log of the run that closed, onto the clipboard"
            : "Version, PC, graphics card, the latest errors and the last commands, onto the clipboard";
        var issue = Ui.Btn("Open an issue on GitHub", () => OS.ShellOpen(Diagnostics.IssueUrl), "Opens the problem form in your browser");
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(copy);
        row.AddChild(issue);
        row.AddChild(copied);
        v.AddChild(row);
        v.AddChild(Ui.Hint($"Paste it into the issue, or send it to Bruno Firme: {Diagnostics.Contact}. Nothing is sent by the app itself."));
        var folder = Ui.Btn("Open the log folder", () => OS.ShellOpen(OS.GetUserDataDir()), "app.log and the engine's logs, for anyone who asks for the whole files");
        Ui.MakeQuiet(folder);
        var folderRow = new HBoxContainer();
        folderRow.AddChild(folder);
        v.AddChild(folderRow);
    }
}
