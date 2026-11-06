using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class TurnLength
{
    static readonly (string Name, int Minutes)[] Units = { ("minutes", 1), ("hours", GameClock.MinutesInHour), ("days", GameClock.MinutesInDay) };

    readonly SpinBox _count;
    readonly OptionButton _unit;

    public HBoxContainer Row { get; }

    public TurnLength(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, GameClock.MostMinutes);
        int unit = Array.FindLastIndex(Units, u => minutes % u.Minutes == 0);
        _count = new SpinBox { MinValue = 1, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _unit = new OptionButton { FitToLongestItem = true };
        foreach (var (name, _) in Units) _unit.AddItem(name);
        _unit.Selected = unit;
        Bound();
        _count.Value = minutes / Units[unit].Minutes;
        _unit.ItemSelected += _ => Bound();
        Row = new HBoxContainer();
        Row.AddThemeConstantOverride("separation", Ui.Gap.Row);
        Row.AddChild(_count);
        Row.AddChild(_unit);
    }

    public int Minutes => Math.Clamp((int)_count.Value * Units[_unit.Selected].Minutes, 1, GameClock.MostMinutes);

    void Bound() => _count.MaxValue = GameClock.MostMinutes / Units[Math.Max(0, _unit.Selected)].Minutes;
}
