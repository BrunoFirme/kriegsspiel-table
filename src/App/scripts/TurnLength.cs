using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class TurnLength
{
    static readonly (string Name, int Seconds)[] Units =
        { ("seconds", 1), ("minutes", CalendarDef.SecondsInMinute), ("hours", CalendarDef.SecondsInHour), ("days", CalendarDef.SecondsInDay) };

    readonly SpinBox _count;
    readonly OptionButton _unit;
    readonly bool _mayBeNone;

    public HBoxContainer Row { get; }

    public Action? Changed;

    public TurnLength(int seconds, bool mayBeNone = false)
    {
        _mayBeNone = mayBeNone;
        bool none = mayBeNone && seconds <= 0;
        seconds = Math.Clamp(seconds, 1, ClockTime.MostTurnSeconds);
        int unit = none ? Units.Length : Array.FindLastIndex(Units, u => seconds % u.Seconds == 0);
        _count = new SpinBox { MinValue = 1, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _unit = new OptionButton { FitToLongestItem = true };
        foreach (var (name, _) in Units) _unit.AddItem(name);
        if (mayBeNone) _unit.AddItem("none");
        _unit.Selected = unit;
        Bound();
        _count.Value = none ? 1 : seconds / Units[unit].Seconds;
        _unit.ItemSelected += _ =>
        {
            Bound();
            Changed?.Invoke();
        };
        _count.ValueChanged += _ => Changed?.Invoke();
        Row = new HBoxContainer();
        Row.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Row));
        Row.AddChild(_count);
        Row.AddChild(_unit);
    }

    bool None => _mayBeNone && _unit.Selected == Units.Length;

    public int Seconds => None ? 0 : Math.Clamp((int)_count.Value * Units[_unit.Selected].Seconds, 1, ClockTime.MostTurnSeconds);

    void Bound()
    {
        _count.Editable = !None;
        if (!None) _count.MaxValue = ClockTime.MostTurnSeconds / Units[Math.Max(0, _unit.Selected)].Seconds;
    }
}
