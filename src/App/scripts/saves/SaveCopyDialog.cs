using System;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core;

namespace KTable.App;

sealed class SaveCopyDialog : HudPart
{
    static Task? _copying;

    readonly IHudShell Shell;
    SaveCopyDialog(HudContext ctx, IHudShell shell) : base(ctx) { Shell = shell; }

    public static void Open(HudContext ctx, IHudShell shell) => new SaveCopyDialog(ctx, shell).Show();

    void Show()
    {
        var dlg = new ConfirmationDialog { Title = "Save a copy as a new scenario", OkButtonText = "Save copy" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
        box.AddChild(new Label { Text = "Name of the copy" });
        var name = new LineEdit { Text = S.View.Name + " copy" };
        box.AddChild(name);
        box.AddChild(Hint("A second scenario from the table as it is now: map sheets, every piece, clock and scale. The table's own scenario saves itself as you work."));
        dlg.AddChild(box);
        dlg.Confirmed += () => Save(name.Text);
        Ui.Open(HudNode, dlg, new Vector2I(500, 230));
        Ui.FreeOnClose(dlg);
    }

    void Save(string typed)
    {
        if (_copying is { IsCompleted: false }) { Log("A copy is still being saved; wait for it to finish before making another."); return; }
        var title = typed.Trim().Length > 0 ? typed.Trim() : "Scenario";
        GameState snapshot;
        try { snapshot = S.Live.DeepClone(); }
        catch (Exception e) { Log($"[color=#ff7060]Could not save the scenario: {Fmt.Bb(e.Message)}[/color]"); return; }
        var store = S.Store;
        var what = $"{snapshot.Sheets.Count} map sheet{(snapshot.Sheets.Count == 1 ? "" : "s")}, {snapshot.Layer(Layers.Umpire).Count} pieces";
        Log($"Saving a copy as the scenario '{Fmt.Bb(title)}' ({Fmt.Bb(what)}); a big map takes a while.");
        var shell = Shell;
        _copying = Task.Run(() =>
        {
            string done;
            try { ScenarioFiles.SaveTable(store, snapshot, title, null); done = ""; }
            catch (Exception e) { done = e.Message.Length > 0 ? e.Message : "failed"; }
            if (!Assets.Quitting) BestEffort.Try(() => shell.CopySavedLater(title, what, done));
        });
    }
}
