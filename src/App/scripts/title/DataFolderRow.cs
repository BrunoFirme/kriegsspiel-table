using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

sealed class DataFolderRow(Node asksIn, Action<string> say, Func<string?> busy, Settings cfg, Action restart, string defaultFolder, string folder)
{
    const int SizeWidth = 72;

    void Measure(Label size) =>
        Task.Run(() => Caches.Size(folder)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && !Assets.Quitting)
                Callable.From(() => { if (GodotObject.IsInstanceValid(size)) size.Text = t.Result > 0 ? ContentLimits.Describe(t.Result) : "empty"; }).CallDeferred();
        });
    static readonly Vector2I PickerSize = new(720, 480), WaitSize = new(420, 96);

    public HBoxContainer Row()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var name = Ui.RowLabel("Data folder", Ui.DialogLabels);
        name.TooltipText = "Where packs, scenarios, saves and their trash are kept on this PC";
        name.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(name);
        var size = Ui.Readout("", SizeWidth);
        size.HorizontalAlignment = HorizontalAlignment.Left;
        row.AddChild(size);
        Measure(size);
        var where = Ui.Readout(folder, 0);
        where.HorizontalAlignment = HorizontalAlignment.Left;
        where.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        where.ClipText = true;
        where.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        where.TooltipText = folder;
        where.MouseFilter = Control.MouseFilterEnum.Pass;
        var move = new Button { Text = "Move…", FocusMode = Control.FocusModeEnum.None, TooltipText = "Keep it in another folder or on another drive · the app restarts" };
        move.Pressed += Pick;
        row.AddChild(move);
        row.AddChild(where);
        return row;
    }

    void Pick()
    {
        if (busy() is { } why)
        {
            say(why);
            return;
        }
        var fd = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Data folder", UseNativeDialog = true };
        fd.DirSelected += picked => Looked(picked);
        asksIn.AddChild(fd);
        Ui.FreeOnClose(fd);
        fd.PopupCentered(PickerSize);
    }

    void Looked(string picked)
    {
        Task.Run(() => DataFolderMove.Look(picked, folder)).ContinueWith(t => Callable.From(() =>
        {
            if (t.IsCompletedSuccessfully) Offer(t.Result);
            else say("That folder cannot be used: " + t.Exception?.GetBaseException().Message);
        }).CallDeferred());
    }

    internal void Offer(DataMovePlan plan)
    {
        if (plan.Refusal is { } no) say(no);
        else if (plan.UseAsIs) Ui.Confirm(asksIn, $"Use the data already in {plan.To}", "Use it and restart", () => UseAsIs(plan), $"What is in {plan.From} stays there.");
        else
            Ui.Confirm(asksIn, $"Move the data to {plan.To}", "Move and restart", () => Go(plan),
                $"{ContentLimits.Describe(plan.Bytes)} in {plan.Files:#,0} files. Close any other open copy of the app first.");
    }

    void UseAsIs(DataMovePlan plan)
    {
        DataFolder.Choose(defaultFolder, plan.To);
        Settle(plan);
    }

    void Go(DataMovePlan plan)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, plan.Bytes), ShowPercentage = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var wait = new AcceptDialog { Title = "Moving the data", Exclusive = true, DialogCloseOnEscape = false };
        Ui.NoCloseButton(wait);
        wait.GetOkButton().Visible = false;
        wait.AddChild(bar);
        Ui.Open(asksIn, wait, WaitSize);
        long shown = 0, step = Math.Max(1, plan.Bytes / 100);
        void Progress(long done, long all)
        {
            if (done - shown < step && done < all) return;
            shown = done;
            Callable.From(() => { if (GodotObject.IsInstanceValid(bar)) bar.Value = done; }).CallDeferred();
        }
        Task.Run(() => DataFolderMove.Move(plan, defaultFolder, Progress)).ContinueWith(t => Callable.From(() => Moved(plan, t, wait)).CallDeferred());
    }

    void Moved(DataMovePlan plan, Task<IReadOnlyList<string>> move, AcceptDialog wait)
    {
        if (GodotObject.IsInstanceValid(wait)) wait.QueueFree();
        if (!move.IsCompletedSuccessfully)
        {
            say($"Not moved: {move.Exception?.GetBaseException().Message} Nothing was changed.");
            return;
        }
        GD.Print($"[data] moved from {plan.From} to {plan.To}; {move.Result.Count} file(s) could not be removed from the old folder");
        Settle(plan);
    }

    void Settle(DataMovePlan plan)
    {
        cfg.CaptureFolder = DataFolderMove.Rebased(cfg.CaptureFolder, plan.From, plan.To);
        cfg.Save();
        restart();
    }

    internal static void AskWhereItIs(Main main, string gone)
    {
        var ask = Ui.Confirm(main, "The data folder is not there", "Try again", main.RestartApp, gone);
        ask.CancelButtonText = "Quit";
        ask.Also("Use this PC's folder", () =>
        {
            DataFolder.Choose(DataPaths.DefaultLocal, null);
            main.RestartApp();
        });
        ask.Canceled += main.QuitApp;
    }
}
