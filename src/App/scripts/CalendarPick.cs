using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CalendarPick
{
    const int DayWide = 34, DayHigh = 28, ArrowWide = 28, YearWide = 104, ClockWide = 76, WeekWhenNone = 7, ShortDay = 2, PickedPad = 2;
    const int CellColumns = 3, CellsShown = 12, CellsBack = 5, Tenfold = 10, CellWide = DayWide * WeekWhenNone / CellColumns;
    const long YearMost = 9_999_999;
    internal const string Tip = "Pick the date on a calendar";

    internal enum Zoom { Days, Months, Years, Decades, Centuries, Millennia }

    readonly CalendarDef _cal;
    readonly Func<long> _now;
    readonly Action<long> _picked;
    readonly Button _title = new() { Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
    readonly SpinBox _year = new() { MinValue = -YearMost, MaxValue = YearMost, Step = 1, Rounded = true, TooltipText = "Year" };
    readonly LineEdit _time = new() { Alignment = HorizontalAlignment.Center, TooltipText = "A time of day, as 06:00" };
    readonly GridContainer _cells = new();
    readonly Button _back, _on;
    long _shownYear, _base;
    int _shownMonth;
    bool _quiet;

    public Control Content { get; }
    public Action? Done { get; set; }
    public Action? Relaid { get; set; }
    internal Zoom View { get; private set; }

    internal CalendarPick(CalendarDef cal, Func<long> now, Action<long> picked)
    {
        _cal = cal;
        _now = now;
        _picked = picked;
        _back = Arrow("‹", -1);
        _on = Arrow("›", 1);
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
            pick.Relaid = () => Callable.From(() => { if (GodotObject.IsInstanceValid(pop)) pop.ResetSize(); }).CallDeferred();
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
        head.AddChild(_back);
        _title.Pressed += ZoomOut;
        head.AddChild(_title);
        _year.CustomMinimumSize = Ui.Px(YearWide, 0);
        _year.ValueChanged += v => { if (!_quiet) Typed(Year((long)v)); };
        head.AddChild(_year);
        head.AddChild(_on);
        box.AddChild(head);
        _cells.AddThemeConstantOverride("h_separation", Ui.Px(Ui.Gap.Hair));
        _cells.AddThemeConstantOverride("v_separation", Ui.Px(Ui.Gap.Hair));
        box.AddChild(_cells);
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

    Button Arrow(string sign, int step)
    {
        var b = Ui.Btn(sign, () => Step(step));
        b.Flat = true;
        b.CustomMinimumSize = Ui.Px(ArrowWide, DayHigh);
        return b;
    }

    bool Real(long year) => _cal.DaysIn(year, 1) > 0;

    long Year(long wanted) => Real(wanted) ? wanted : wanted >= _shownYear ? wanted + 1 : wanted - 1;

    long YearsOn(long from, long by)
    {
        long year = from;
        int way = Math.Sign(by);
        for (long i = 0; i < Math.Abs(by); i++) year = Real(year + way) ? year + way : year + 2 * way;
        return year;
    }

    void Step(int step)
    {
        switch (View)
        {
            case Zoom.Days:
                Turn(step);
                break;
            case Zoom.Months:
                ShowMonths(YearsOn(_shownYear, step));
                break;
            default:
                ShowSpan(View, _base + step * CellsShown);
                break;
        }
    }

    void Typed(long year)
    {
        switch (View)
        {
            case Zoom.Days:
                Show(year, _shownMonth);
                break;
            case Zoom.Months:
                ShowMonths(year);
                break;
            default:
                _shownYear = year;
                ShowSpan(View, KeyOf(View, year) - CellsBack);
                break;
        }
    }

    internal void ZoomOut()
    {
        if (View == Zoom.Days) ShowMonths(_shownYear);
        else if (View == Zoom.Months) ShowSpan(Zoom.Years, KeyOf(Zoom.Years, _shownYear) - CellsBack);
        else if (View != Zoom.Millennia) ShowSpan(View + 1, KeyOf(View + 1, CellYears(View, _base + CellsShown / 2).From) - CellsBack);
    }

    static long SpanOf(Zoom view) => view switch { Zoom.Decades => Tenfold, Zoom.Centuries => Tenfold * Tenfold, Zoom.Millennia => Tenfold * Tenfold * Tenfold, _ => 1 };

    long Idx(long year) => _cal.Before != null && year < 0 ? year + 1 : year;

    long FromIdx(long index) => _cal.Before != null && index <= 0 ? index - 1 : index;

    long KeyOf(Zoom view, long year)
    {
        long span = SpanOf(view);
        if (span == 1) return Idx(year);
        return _cal.Before != null && year < 0 ? -(long)Math.Floor(-year / (double)span) - 1 : (long)Math.Floor(year / (double)span);
    }

    (long From, long To) CellYears(Zoom view, long key)
    {
        long span = SpanOf(view);
        if (span == 1) return (FromIdx(key), FromIdx(key));
        if (_cal.Before == null) return (key * span, key * span + span - 1);
        return key >= 0 ? (Math.Max(key * span, 1), key * span + span - 1) : (-(-key * span - 1), -Math.Max((-key - 1) * span, 1));
    }

    string Range(long from, long to) =>
        _cal.Before != null && from < 0 && to < 0 ? $"{-from}–{-to} {_cal.Before}" : $"{YearWords(from)}–{YearWords(to)}";

    internal void Turn(int months)
    {
        long year = _shownYear;
        int month = _shownMonth + months;
        while (month < 1) { month += _cal.Months.Count; year = Year(year - 1); }
        while (month > _cal.Months.Count) { month -= _cal.Months.Count; year = Year(year + 1); }
        Show(year, month);
    }

    internal void StepOn(int step) => Step(step);

    internal string Shown => $"{_title.Text} {_year.Value}";

    void Head(Zoom view, long year, string title, string tip, string backTip, string onTip)
    {
        View = view;
        _shownYear = year;
        _quiet = true;
        _year.Value = year;
        _quiet = false;
        _title.Text = title;
        _title.TooltipText = tip;
        _title.Disabled = view == Zoom.Millennia;
        _back.TooltipText = backTip;
        _on.TooltipText = onTip;
        foreach (var c in _cells.GetChildren()) { _cells.RemoveChild(c); c.QueueFree(); }
    }

    void Cell(string text, bool now, Action pick, int wide)
    {
        var b = Ui.Btn(text, pick);
        b.CustomMinimumSize = Ui.Px(wide, DayHigh);
        if (now) Ui.MakePrimary(b, PickedPad, PickedPad);
        else b.Flat = true;
        _cells.AddChild(b);
    }

    internal void Show(long year, int month)
    {
        _shownMonth = month;
        Head(Zoom.Days, year, _cal.Months[month - 1].Name, "The months of the year", "Month before", "Month after");
        _cells.Columns = Week;
        foreach (var name in _cal.WeekDays)
            _cells.AddChild(new Label { Text = name.Length <= ShortDay ? name : name[..ShortDay], Modulate = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center });
        var first = _cal.Seconds(new DateParts(year, month, 1, 0, 0, 0));
        int lead = first is { } at ? _cal.WeekDayIndex(at) ?? 0 : 0;
        for (int i = 0; i < lead; i++) _cells.AddChild(new Control());
        var now = _cal.Parts(_now());
        int days = _cal.DaysIn(year, month);
        for (int day = 1; day <= days; day++)
        {
            int d = day;
            Cell(day.ToString(System.Globalization.CultureInfo.InvariantCulture), now.Year == year && now.Month == month && now.Day == day, () => PickDay(d), DayWide);
        }
        Relaid?.Invoke();
    }

    void ShowMonths(long year)
    {
        Head(Zoom.Months, year, YearWords(year), "The years around it", "Year before", "Year after");
        _cells.Columns = CellColumns;
        var now = _cal.Parts(_now());
        for (int month = 1; month <= _cal.Months.Count; month++)
        {
            int m = month;
            Cell(_cal.Months[month - 1].Name, now.Year == year && now.Month == month, () => Show(year, m), CellWide);
        }
        Relaid?.Invoke();
    }

    void ShowSpan(Zoom view, long first)
    {
        _base = first;
        long last = first + CellsShown - 1;
        string words = view == Zoom.Years ? "Years" : view.ToString();
        Head(view, _shownYear, Range(CellYears(view, first).From, CellYears(view, last).To), view == Zoom.Millennia ? "" : (view + 1).ToString(), words + " before", words + " after");
        _cells.Columns = CellColumns;
        long now = KeyOf(view, _cal.Parts(_now()).Year);
        for (long key = first; key <= last; key++)
        {
            var (from, to) = CellYears(view, key);
            Cell(view == Zoom.Years ? YearWords(from) : Range(from, to), key == now, () => { if (view == Zoom.Years) ShowMonths(from); else ShowSpan(view - 1, KeyOf(view - 1, from)); }, CellWide);
        }
        Relaid?.Invoke();
    }

    string YearWords(long year) => _cal.Before != null && year < 0 ? $"{-year} {_cal.Before}" : year.ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal string[] Cells => _cells.GetChildren().OfType<Button>().Select(b => b.Text).ToArray();

    internal int[] Days => View == Zoom.Days ? Cells.Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray() : [];

    internal int Lead => _cells.GetChildren().TakeWhile(c => c is not Button).Count(c => c is not Label);

    internal void PickCell(string text) => _cells.GetChildren().OfType<Button>().First(b => b.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

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
