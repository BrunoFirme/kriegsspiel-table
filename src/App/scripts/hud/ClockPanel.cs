using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class ClockPanel : HudPart
{
    public const string Key = "clock";
    static readonly double[] Rates = { 0, 1, 10, 60, 600 };
    const float FieldWidth = 340, WhenWidth = 92, RateWidth = 84;

    public readonly FloatPanel Panel;
    readonly VBoxContainer _box;
    SpinBox? _turn;
    LineEdit? _name, _time;
    Container? _flow, _timers;
    string _built = "";
    bool _syncing;
    long _shown = long.MinValue;

    string? _place, _placeName;

    GameClock C => S.View.Clock.AsOf(_place);

    public void Follow(string? place, string? name)
    {
        (_place, _placeName) = (place, name);
        Panel.SetTitle(place == null ? "Clock" : $"Clock · {name}");
        if (Panel.Visible) Show();
    }

    public ClockPanel(HudContext ctx, IHudShell shell) : base(ctx)
    {
        Panel = shell.MakePanel(Key, "Clock");
        _box = Panel.Body;
        Panel.VisibilityChanged += () => { if (Panel.Visible) Show(); };
    }

    public static bool Moves(Command c) =>
        c is SetClock or AdvanceClock or StepClock or RunClock or SetTimer or ClearTimer or SetClockSteps or SetCalendar or SetClockApart or RestoreTurn or LoadSave;

    public void Applied(Command c)
    {
        if (Panel.Visible && Moves(c)) Show();
    }

    public void Tick()
    {
        if (!Panel.Visible || !C.Runs || C.Now == _shown) return;
        Tock();
    }

    string Shape() => $"{C.TurnSeconds}|{string.Join(',', C.StepList)}|{C.Cal.Id}|{S.IsLead}|{_place}";

    void Show()
    {
        if (Shape() != _built) Build();
        else Sync();
    }

    void Build()
    {
        _built = Shape();
        foreach (var c in _box.GetChildren())
        {
            _box.RemoveChild(c);
            c.QueueFree();
        }
        var grid = Ui.Form(Ui.PanelLabels);
        grid.AddChild(new Label { Text = "Turn" });
        _turn = new SpinBox { MinValue = 0, MaxValue = 100000, TooltipText = "The turn number; a higher one ends the turn in play" };
        _turn.ValueChanged += v => { if (!_syncing) Set(turn: (int)v); };
        grid.AddChild(_turn);
        grid.AddChild(new Label { Text = "Name" });
        _name = new LineEdit { MaxLength = GameClock.MostNameLength, CustomMinimumSize = Ui.Px(FieldWidth, 0), TooltipText = "Shown beside the turn in the history" };
        _name.TextSubmitted += _ => Named();
        _name.FocusExited += Named;
        grid.AddChild(_name);
        grid.AddChild(new Label { Text = "Time" });
        _time = new LineEdit { TooltipText = "A date and time as 22 July 1812 06:00 or 22/07/1812 06:00, or a time of day", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _time.TextSubmitted += Jump;
        var timeRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        timeRow.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        timeRow.AddChild(_time);
        timeRow.AddChild(CalendarPick.Opener(() => C.Cal, () => C.Now, at => Set(seconds: at)));
        grid.AddChild(timeRow);
        grid.AddChild(new Label { Text = "Step" });
        grid.AddChild(Steps());
        grid.AddChild(new Label { Text = "Each turn" });
        var each = new TurnLength(C.TurnSeconds, mayBeNone: true);
        each.Changed = () => { if (each.Seconds != C.TurnSeconds) Set(turnSeconds: each.Seconds); };
        grid.AddChild(each.Row);
        grid.AddChild(new Label { Text = "Flow" });
        grid.AddChild(_flow = new MarginContainer());
        Ui.AlignForm(grid);
        _box.AddChild(grid);
        _box.AddChild(Header("Timers"));
        _box.AddChild(_timers = new VBoxContainer());
        _box.AddChild(NewTimer());
        Sync();
    }

    void Set(int? turn = null, long? seconds = null, int? turnSeconds = null, string? name = null) =>
        S.Do(new SetClock { Turn = turn ?? C.Turn, Seconds = seconds ?? C.Now, TurnSeconds = turnSeconds ?? C.TurnSeconds, Name = name ?? C.Name, Place = _place });

    void Named()
    {
        if (_name is { } field && field.Text.Trim() != (C.Name ?? "")) Set(name: field.Text.Trim());
    }

    void Jump(string text)
    {
        if (ClockWords.Moment(C.Cal, C.Now, text) is { } seconds) Set(seconds: seconds);
        else Say("Not a time in this calendar");
        _time?.ReleaseFocus();
        Sync();
    }

    Control Steps()
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Row));
        row.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Hair));
        foreach (int step in C.StepList)
        {
            string words = GameClock.StepText(step);
            var chip = new HBoxContainer();
            chip.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.None));
            chip.AddChild(Arrow("‹", $"Back {words}", -step));
            chip.AddChild(new Label { Text = words, VerticalAlignment = VerticalAlignment.Center });
            chip.AddChild(Arrow("›", $"On {words}", step));
            row.AddChild(chip);
        }
        var edit = Btn("…", EditSteps, "Change the steps");
        edit.Flat = true;
        row.AddChild(edit);
        return row;
    }

    Button Arrow(string sign, string tip, int seconds)
    {
        var b = Btn(sign, () => S.Do(new StepClock { Seconds = seconds, Place = _place }), tip);
        b.Flat = true;
        b.CustomMinimumSize = Ui.Px(18, 22);
        return b;
    }

    void EditSteps()
    {
        var dlg = new ConfirmationDialog { Title = "Steps", OkButtonText = "Set the steps" };
        var rows = new VBoxContainer();
        var lengths = new List<TurnLength>();
        Button? add = null;
        void Add(int seconds)
        {
            var length = new TurnLength(seconds);
            var row = new HBoxContainer();
            row.AddChild(length.Row);
            var remove = Btn("✕", () =>
            {
                lengths.Remove(length);
                rows.RemoveChild(row);
                row.QueueFree();
                if (add != null) add.Disabled = false;
            }, "Remove this step");
            remove.Flat = true;
            row.AddChild(remove);
            lengths.Add(length);
            rows.AddChild(row);
            if (add != null) add.Disabled = lengths.Count >= GameClock.MostSteps;
        }
        foreach (int step in C.StepList) Add(step);
        add = Btn("Add a step", () => Add(CalendarDef.SecondsInMinute));
        var box = new VBoxContainer();
        box.AddChild(rows);
        box.AddChild(add);
        dlg.AddChild(box);
        dlg.Confirmed += () => S.Do(new SetClockSteps { Steps = lengths.Select(l => l.Seconds).Distinct().OrderBy(s => s).ToList() });
        Ui.Open(HudNode, dlg, new Vector2I(320, 240));
        Ui.FreeOnClose(dlg);
    }

    Control Flow()
    {
        if (!S.IsLead)
        {
            var state = new HBoxContainer();
            state.AddChild(new Label { Text = ClockWords.Rate(C.Rate) });
            if (C.Runs) state.AddChild(Btn("Pause", () => S.Do(new RunClock { Rate = 0, Place = _place })));
            return state;
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        row.AddChild(Ui.Segments(Rates.Select(r => (r == 0 ? "❚❚" : ClockWords.Rate(r), C.Rate == r, r == 0 ? "Paused" : $"{r:0} game seconds a second", (Action)(() => S.Do(new RunClock { Rate = r, Place = _place })))).ToList()));
        var typed = new SpinBox { MinValue = 0, MaxValue = ClockTime.MostRate, Step = 1, Prefix = "×", Value = C.Rate, CustomMinimumSize = Ui.Px(RateWidth, 0), TooltipText = "Game seconds a second" };
        typed.ValueChanged += v => { if (v != C.Rate) S.Do(new RunClock { Rate = v, Place = _place }); };
        row.AddChild(typed);
        return row;
    }

    Control TimerRow(GameTimer timer)
    {
        long now = C.Now;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        row.AddChild(new Label { Text = timer.Name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, TooltipText = timer.Name });
        var due = new Label { Text = timer.Due <= now ? "due" : $"{ClockWords.Due(C.Cal, now, timer.Due)} · {ClockWords.Left(timer.Due - now)}", TooltipText = C.Cal.Words(timer.Due) };
        if (timer.Due <= now) due.AddThemeColorOverride("font_color", Ui.Danger);
        row.AddChild(due);
        var eye = Ui.IconBtn("eye", timer.Everyone ? "Players see this timer" : "Only umpires see this timer");
        eye.ToggleMode = true;
        eye.ButtonPressed = timer.Everyone;
        eye.Pressed += () => S.Do(new SetTimer { Name = timer.Name, Due = timer.Due, Everyone = !timer.Everyone, Place = _place });
        row.AddChild(eye);
        row.AddChild(Trash(() => S.Do(new ClearTimer { Name = timer.Name, Place = _place })));
        return row;
    }

    static Button Trash(Action clear)
    {
        var b = Ui.IconBtn("trash", "Remove this timer");
        b.Pressed += clear;
        return b;
    }

    Control NewTimer()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        var name = new LineEdit { PlaceholderText = "Name", MaxLength = GameTimer.MostNameLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var when = new LineEdit { PlaceholderText = "When", CustomMinimumSize = Ui.Px(WhenWidth, 0), TooltipText = "A time of day, a date and time, or a span from now such as 2 h or 90 min" };
        void Add()
        {
            if (name.Text.Trim().Length == 0) return;
            if (ClockWords.When(C.Cal, C.Now, when.Text) is not { } due)
            {
                Say("A timer is due at a time, as 14:00, or after a span, as 2 h");
                return;
            }
            S.Do(new SetTimer { Name = name.Text.Trim(), Due = due, Place = _place });
            name.Text = "";
            when.Text = "";
        }
        when.TextSubmitted += _ => Add();
        name.TextSubmitted += _ => when.GrabFocus();
        row.AddChild(name);
        row.AddChild(when);
        row.AddChild(Btn("Add", Add));
        return row;
    }

    static void Refill(Container holder, IEnumerable<Control> with)
    {
        foreach (var c in holder.GetChildren())
        {
            holder.RemoveChild(c);
            c.QueueFree();
        }
        foreach (var c in with) holder.AddChild(c);
    }

    void Sync()
    {
        if (_turn == null || _name == null || _time == null || _flow == null || _timers == null) return;
        _syncing = true;
        _turn.Value = C.Turn;
        _syncing = false;
        (_turn.Editable, _name.Editable) = (_place == null, _place == null);
        if (!_name.HasFocus()) _name.Text = C.Name ?? "";
        Refill(_flow, new[] { Flow() });
        Tock();
        Panel.Fit();
    }

    void Tock()
    {
        if (_time == null || _timers == null) return;
        _shown = C.Now;
        if (!_time.HasFocus()) _time.Text = C.Cal.Typed(_shown);
        var timers = (C.Timers ?? new()).OrderBy(t => t.Due).Select(TimerRow).ToList();
        Refill(_timers, timers.Count > 0 ? timers : new Control[] { Hint("No timers") });
    }
}
