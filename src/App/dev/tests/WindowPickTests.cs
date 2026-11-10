using System.Threading.Tasks;
using Godot;
using KTable.Client.Prefs;

namespace KTable.App;

static class WindowPickTests
{
    [NodeTest]
    static async Task Every_open_window_picker_shows_the_mode_chosen_elsewhere(TestRunner r)
    {
        var cfg = new Settings();
        var title = AppSettings.WindowPick(cfg);
        var table = AppSettings.WindowPick(cfg);
        r.AddChild(title);
        r.AddChild(table);
        await r.Frame();
        cfg.WindowMode = Settings.Borderless;
        Graphics.ApplyWindow(cfg);
        Check.Equal(Settings.Borderless, title.Selected, "title page picker");
        Check.Equal(Settings.Borderless, table.Selected, "table dialog picker");
        r.RemoveChild(table);
        cfg.WindowMode = Settings.Fullscreen;
        Graphics.ApplyWindow(cfg);
        Check.Equal(Settings.Fullscreen, title.Selected, "still open");
        Check.Equal(Settings.Borderless, table.Selected, "closed pickers stop listening");
        r.AddChild(table);
        Check.Equal(Settings.Fullscreen, table.Selected, "reopened picker catches up");
        title.QueueFree();
        table.QueueFree();
        await r.Frame();
    }
}
