using System;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core;

namespace KTable.App;

sealed class CacheShelf(Node asksIn, Action<string> say, Func<CacheKind, string?> busy)
{
    public HBoxContainer Row(Cache c)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        var name = Ui.RowLabel(c.Name, Ui.DialogLabels);
        name.TooltipText = c.Holds;
        name.MouseFilter = Control.MouseFilterEnum.Pass;
        row.AddChild(name);
        var size = Ui.Readout("", 72);
        size.HorizontalAlignment = HorizontalAlignment.Left;
        row.AddChild(size);
        var clear = new Button { Text = "Clear", Disabled = true, FocusMode = Control.FocusModeEnum.None, TooltipText = c.Holds };
        row.AddChild(clear);
        void Shown(long bytes)
        {
            if (!GodotObject.IsInstanceValid(size)) return;
            size.Text = bytes > 0 ? ContentLimits.Describe(bytes) : "empty";
            clear.Disabled = bytes <= 0;
        }
        void Measure() => Later(() => Caches.Size(c.Path), Shown);
        void Go()
        {
            if (busy(c.Kind) is { } why) { say(why); return; }
            clear.Disabled = true;
            Later(() => Caches.Clear(c.Path), done =>
            {
                say($"Cleared {ContentLimits.Describe(done.Freed)} of {c.InSentence}." + (done.Left > 0 ? $" {done.Left} file{(done.Left == 1 ? " is" : "s are")} in use and stay." : ""));
                Measure();
            });
        }
        clear.Pressed += () =>
        {
            if (busy(c.Kind) is { } why) { say(why); return; }
            Ui.Confirm(asksIn, $"Clear the {c.InSentence} ({size.Text})", "Clear", Go, c.Costs);
        };
        Measure();
        return row;
    }

    static void Later<T>(Func<T> work, Action<T> then) =>
        Task.Run(work).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && !Assets.Quitting) Callable.From(() => then(t.Result)).CallDeferred();
        });
}
