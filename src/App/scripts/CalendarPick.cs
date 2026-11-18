using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CalendarPick
{
    const int DayWide = 34, DayHigh = 28, ArrowWide = 28, YearWide = 104, ClockWide = 76, WeekWhenNone = 7, ShortDay = 2, PickedPad = 2;
    const long YearMost = 9_999_999;
    internal const string Tip = "Pick the date on a calendar";

    readonly CalendarDef _cal;
    readonly Func<long> _now;
    readonly Action<long> _picked;
    readonly Label _month = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    readonly SpinBox _year = new() { MinValue = -YearMost, MaxValue = YearMost, Step = 1, Rounded = true, TooltipText = "Year" };
    readonly LineEdit _time = new() { Alignment = HorizontalAlignment.Center, TooltipText = "A time of day, as 06:00" };
    readonly GridContainer _days = new();
    long _shownYear;
    int _shownMonth;
    bool _quiet;

    public Control Content { get; }
    public Action? Done { get; set; }

    internal CalendarPick(CalendarDef cal, Func<long> now, Action<long> picked)
    {
        _cal = cal;
        _now = now;
        _picked = picked;
        Content = Build();
        var p = cal.Parts(now());
        Show(p.Year, p.Month);
        _time.Text = cal.TimeOfDay(now());
    }

    public static Button Opener(Func<CalendarDef> cal, Func<long> now, Action<long> picked)
    {
        var b = Ui.IconBtn("calendar", Tip);
        b.Pressed += () =>
        {
            var pick = new CalendarPick(cal(), now, picked);
            var pop = new PopupPanel();
            pop.AddChild(pick.Content);
            pick.Done = pop.Hide;
            pop.PopupHide += pop.QueueFree;
            b.AddChild(pop);
            pop.Position = (Vector2I)(b.GetScreenPosition() + new Vector2(0, b.Size.Y) * b.GetGlobalTransform().Scale);
            pop.Popup();
        };
        return b;
    }

    Control Build()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        head.AddChild(Arrow("‹", "Month before", -1));
        head.AddChild(_month);
        _year.CustomMinimumSize = Ui.Px(YearWide, 0);
        _year.ValueChanged += v => { if (!_quiet) Show(Year((long)v), _shownMonth); };
        head.AddChild(_year);
        head.AddChild(Arrow("›", "Month after", 1));
        box.AddChild(head);
        _days.Columns = Week;
        _days.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Hair));
        _days.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Hair));
        box.AddChild(_days);
        var time = new HBoxContainer();
        time.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        time.AddChild(new Label { Text = "Time", Modulate = Ui.Muted, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _time.CustomMinimumSize = Ui.Px(ClockWide, 0);
        _time.TextSubmitted += _ => Retimed();
        time.AddChild(_time);
        box.AddChild(time);
        return box;
    }

    int Week => _cal.WeekDays.Count > 0 ? _cal.WeekDays.Count : WeekWhenNone;

    Button Arrow(string sign, string tip, int months)
    {
        var b = Ui.Btn(sign, () => Turn(months), tip);
        b.Flat = true;
        b.CustomMinimumSize = Ui.Px(ArrowWide, DayHigh);
        return b;
    }

    bool Real(long year) => _cal.DaysIn(year, 1) > 0;

    long Year(long wanted) => Real(wanted) ? wanted : wanted >= _shownYear ? wanted + 1 : wanted - 1;

    internal void Turn(int months)
    {
        long year = _shownYear;
        int month = _shownMonth + months;
        while (month < 1) { month += _cal.Months.Count; year = Year(year - 1); }
        while (month > _cal.Months.Count) { month -= _cal.Months.Count; year = Year(year + 1); }
        Show(year, month);
    }

    internal string Shown => $"{_month.Text} {_year.Value}";

    internal void Show(long year, int month)
    {
        _shownYear = year;
        _shownMonth = month;
        _quiet = true;
        _year.Value = year;
        _quiet = false;
        _month.Text = _cal.Months[month - 1].Name;
        foreach (var c in _days.GetChildren()) { _days.RemoveChild(c); c.QueueFree(); }
        foreach (var name in _cal.WeekDays)
            _days.AddChild(new Label { Text = name.Length <= ShortDay ? name : name[..ShortDay], Modulate = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center });
        var first = _cal.Seconds(new DateParts(year, month, 1, 0, 0, 0));
        int lead = first is { } at ? _cal.WeekDayIndex(at) ?? 0 : 0;
        for (int i = 0; i < lead; i++) _days.AddChild(new Control());
        var now = _cal.Parts(_now());
        int days = _cal.DaysIn(year, month);
        for (int day = 1; day <= days; day++)
        {
            int d = day;
            var b = Ui.Btn(day.ToString(System.Globalization.CultureInfo.InvariantCulture), () => PickDay(d));
            b.CustomMinimumSize = Ui.Px(DayWide, DayHigh);
            if (now.Year == year && now.Month == month && now.Day == day) Ui.MakePrimary(b, PickedPad, PickedPad);
            else b.Flat = true;
            _days.AddChild(b);
        }
    }

    internal int[] Days => _days.GetChildren().OfType<Button>().Select(b => int.Parse(b.Text, System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    internal int Lead => _days.GetChildren().TakeWhile(c => c is not Button).Count(c => c is not Label);

    DateParts TimeTyped => _cal.Parts(ClockWords.Moment(_cal, _now(), _time.Text) ?? _now());

    internal void PickDay(int day)
    {
        var t = TimeTyped;
        if (_cal.Seconds(new DateParts(_shownYear, _shownMonth, day, t.Hour, t.Minute, t.Second)) is not { } at) return;
        _picked(at);
        Done?.Invoke();
    }

    internal void Retime(string text)
    {
        _time.Text = text;
        Retimed();
    }

    void Retimed()
    {
        var at = ClockWords.Moment(_cal, _now(), _time.Text);
        if (at != null) _picked(at.Value);
        _time.Text = _cal.TimeOfDay(at ?? _now());
    }
}
