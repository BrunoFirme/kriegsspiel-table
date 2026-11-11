using Godot;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class ReportPage(TitleScreen t, Main main, Settings cfg)
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
        bool lost = closed != null && Diagnostics.GraphicsStopped;
        const string CarryOn = "Games save themselves as you play: host it again to carry on where it stopped.";
        var v = closed == null ? t.NewPage("Report a problem", "")
            : lost ? t.NewPage("The graphics driver stopped", $"The graphics card stopped answering and its driver was reset (the run started {closed.Started:d MMM, HH:mm}). {CarryOn}")
            : t.NewPage("It closed unexpectedly", $"Kriegsspiel Table stopped without being quit (the run started {closed.Started:d MMM, HH:mm}). {CarryOn}");
        v.AddThemeConstantOverride("separation", Ui.Gap.Block);
        if (lost) AddDriverHelp(v);
        var copied = new Label { Modulate = Muted };
        void CopyReport()
        {
            AppClipboard.SetText(Diagnostics.Report());
            copied.Text = "Copied.";
        }
        var copyText = closed != null ? "Copy the report" : "Copy diagnostics";
        var copy = lost ? Ui.Btn(copyText, CopyReport) : Primary(copyText, CopyReport);
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

    void AddDriverHelp(VBoxContainer v)
    {
        v.AddChild(Ui.Hint("An updated driver for the card usually cures it. On a laptop, power from the wall and a frame limit (at the table: Menu → Settings… → Graphics) keep the card cooler."));
        if (!Graphics.CanChooseRenderer) return;
        var other = Graphics.OtherRenderer(Graphics.RendererNow);
        var row = new HBoxContainer();
        row.AddChild(Primary($"Restart through {Graphics.RendererName(other)}", () =>
        {
            cfg.Renderer = other;
            cfg.Save();
            Graphics.ApplyDisplay(cfg);
            main.RestartApp();
        }));
        v.AddChild(row);
    }
}
