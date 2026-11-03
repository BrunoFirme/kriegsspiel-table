using System.Linq;
using Godot;

namespace KTable.App;

sealed class MainMenu : HudPart
{
    readonly IHudShell Shell;
    public MainMenu(HudContext ctx, IHudShell shell) : base(ctx) { Shell = shell; }

    public MenuButton Build()
    {
        var menu = new MenuButton { Flat = false, FocusMode = Control.FocusModeEnum.None, TooltipText = "Menu\nInvite, scenario properties, export, settings, the guides, leaving the table. Esc with empty hands opens it too." };
        menu.AddChild(new ToolIcon { Kind = "menu" });
        var pm = menu.GetPopup();
        if (S.IsUmpire)
        {
            pm.AddSeparator("This game");
            pm.AddItem("Invite…", 10);
            pm.AddItem("Scenario properties…", 20);
            if (S.IsHost && N.Saves.RunInPlay != null)
            {
                pm.AddItem($"Saves…   ({Cfg.Keys.Label("saves")})", 3);
                pm.SetItemTooltip(pm.GetItemIndex(3), "This scenario's saves in folders: every ended turn, your own saves, a folder for each play of it");
            }
            if (S.IsHost) pm.AddItem("Save a copy as a new scenario…", 2);
            var tts = new PopupMenu { Name = "tts" };
            Shell.FillTtsMenu(tts);
            pm.AddChild(tts);
            pm.AddSubmenuNodeItem("Export to Tabletop Simulator", tts);
        }
        pm.AddSeparator();
        pm.AddItem("Settings…", 30);
        var scripting = BuildScripting();
        pm.AddChild(scripting);
        pm.AddSubmenuNodeItem("Scripting", scripting);
        pm.AddSeparator("Help");
        pm.AddItem("Keys and mouse", 31);
        pm.SetItemAccelerator(pm.GetItemIndex(31), Key.F1);
        pm.AddItem("Help…", 32);
        pm.AddItem("Copy diagnostics", 33);
        pm.SetItemTooltip(pm.GetItemIndex(33), "Version, PC, graphics card, the latest errors, the last commands and the packs, onto the clipboard to paste where you ask for help. Nothing is sent.");
        pm.AddItem("Report a problem…", 34);
        pm.SetItemTooltip(pm.GetItemIndex(34), "Copies the diagnostics and opens the problem form on GitHub in your browser");
        pm.AddSeparator();
        pm.AddItem("Leave the table…", 40);
        pm.IdPressed += id =>
        {
            switch (id)
            {
                case 2: Shell.ShowSaveCopy(); break;
                case 3: SavesWindow.Open(Ctx); break;
                case 10: InviteDialog.Open(Ctx); break;
                case 20: ScenarioDialog.Open(Ctx); break;
                case 30: Shell.ShowSettings(); break;
                case 31: GuideReader.Show(Ctx.Root, KTable.Client.Docs.KeysPage.File, "at-the-table"); break;
                case 32: GuideReader.Show(Ctx.Root); break;
                case 33: Log(Diagnostics.Copy()); break;
                case 34: Log(Diagnostics.ReportOnGitHub()); break;
                case 40: LeaveDialog.Open(Ctx); break;
            }
        };
        return menu;
    }

    PopupMenu BuildScripting()
    {
        var scripting = new PopupMenu { Name = "scripting" };
        if (S.IsUmpire)
        {
            scripting.AddItem($"Scripts…   ({Cfg.Keys.Label("scriptWindow")})", 1);
            scripting.AddItem($"Script console   ({Cfg.Keys.Label("scriptConsole")})", 2);
        }
        scripting.AddItem("Scripting reference…", 3);
        if (S.IsHost && S.ScenarioDir != null) scripting.AddItem("Check this scenario's scripts…", 4);
        scripting.AddSeparator();
        scripting.AddCheckItem("Show piece ids on hover", 5);
        scripting.SetItemTooltip(scripting.GetItemIndex(5), "Its id and TTS GUID, kind and place under the pointer, and Copy id / Copy GUID on its right-click card");
        scripting.AboutToPopup += () => scripting.SetItemChecked(scripting.GetItemIndex(5), Cfg.MakerTools);
        scripting.IdPressed += id =>
        {
            switch (id)
            {
                case 1: Ctx.ShowScriptWindow(null); break;
                case 2: Shell.TogglePanel("console"); break;
                case 3: GuideReader.ShowEntry(Ctx.Root, null); break;
                case 4: CheckScenarioScripts(Ctx); break;
                case 5: Cfg.MakerTools = !Cfg.MakerTools; Cfg.Save(); break;
            }
        };
        return scripting;
    }

    public static void CheckScenarioScripts(HudContext c)
    {
        var S = c.S;
        if (S.ScenarioDir is not { } dir) return;
        var file = System.IO.Path.Combine(S.Store.Root, "scenarios", dir, "scenario.json");
        ScriptCheckWindow.Run(c.Root, "Check this scenario's scripts", new[] { "check-scripts", S.Store.Root, file },
            "Every script of the scenario as it was last written to its folder, run on a copy away from this table: on_load, every button, three seconds of timers, every hotkey and menu row, on_save.");
    }
}

sealed class LeaveDialog : HudPart
{
    LeaveDialog(HudContext c) : base(c) { }

    public static void Open(HudContext c) => new LeaveDialog(c).Show();

    void Show()
    {
        void Go() { Cfg.Save(); N.Shutdown(); HudNode.GetTree().ReloadCurrentScene(); }
        Ui.Confirm(HudNode, "Leave the table", "Leave", () =>
        {
            N.SaveNow(closing: true);
            if (N.LastSaveProblem is { } problem)
                Ui.Confirm(HudNode, "Leave the table", "Leave anyway", Go, $"The game could not be saved: {problem.TrimEnd('.', ' ')}. Leave anyway?", 420).CancelButtonText = "Stay";
            else Go();
        });
    }
}
