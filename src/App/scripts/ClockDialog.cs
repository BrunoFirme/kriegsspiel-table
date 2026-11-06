using Godot;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class ClockDialog : HudPart
{
    ClockDialog(HudContext c) : base(c) { }

    public static void Open(HudContext c) => new ClockDialog(c).Show();

    void Show()
    {
        var dlg = new ConfirmationDialog { Title = "Game clock", OkButtonText = "Set the clock" };
        var grid = Ui.Form(110);
        grid.AddChild(new Label { Text = "Turn" });
        var turn = new SpinBox { MinValue = 0, MaxValue = 100000, Value = S.View.Clock.Turn };
        grid.AddChild(turn);
        grid.AddChild(new Label { Text = "Game time" });
        var time = new LineEdit { Text = S.View.Clock.GameTime, PlaceholderText = "1812-07-22T06:00", TooltipText = "Year-month-day, T, then hours and minutes" };
        grid.AddChild(time);
        grid.AddChild(new Label { Text = "Each turn" });
        var step = new TurnLength(S.View.Clock.IncrementMinutes);
        grid.AddChild(step.Row);
        Ui.AlignForm(grid);
        dlg.AddChild(grid);
        dlg.Confirmed += () => S.Do(new SetClock { Turn = (int)turn.Value, GameTime = time.Text, IncrementMinutes = step.Minutes });
        Ui.Open(HudNode, dlg, new Vector2I(380, 160));
        Ui.FreeOnClose(dlg);
    }
}
