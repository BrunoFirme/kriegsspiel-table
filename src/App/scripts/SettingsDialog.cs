using System;
using Godot;

namespace KTable.App;

static class SettingsDialog
{
    static readonly Vector2I Size = new(700, 520);
    const string TableTour = "The table's part of the first-run tour, ending in Help; the title screen shows the rest";

    public static void Open(Node parent, Settings cfg, Hud? hud = null, Table? table = null)
    {
        var dlg = new AcceptDialog { Title = "Settings", OkButtonText = "Done" };
        (Action Run, string Tip)? tour = hud != null ? (() => { Tour.Restart(cfg); Tour.AtTable(hud, cfg); }, TableTour) : null;
        var pages = SettingsPages.Build(new SettingsPages.Place(parent, cfg) { Hud = hud, Table = table, Tour = tour });
        pages.ShowFirst();
        dlg.AddChild(pages.Root);
        dlg.Confirmed += cfg.Save;
        dlg.CloseRequested += cfg.Save;
        Ui.Open(parent, dlg, Size);
        Ui.FreeOnClose(dlg);
    }
}
