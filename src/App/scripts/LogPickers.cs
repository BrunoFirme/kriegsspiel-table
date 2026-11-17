using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class LogPickers
{
    public const string Anyone = "Anyone", PickedPiece = "Picked piece", All = "All", ThisTurn = "This turn";
    const int AnyTurn = -1, TurnNow = -2;
    const float PickWide = 118;

    enum WhoBy
    {
        Anyone,
        Person,
        Picked,
    }

    public readonly OptionButton Who = Pick("Who");
    public readonly OptionButton When = Pick("When");
    public event Action? Changed;

    readonly List<(WhoBy By, string Name)> _whoKeys = new();
    readonly List<int> _whenKeys = new();
    readonly SortedSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    readonly SortedSet<int> _turns = new();
    (WhoBy By, string Name) _who = (WhoBy.Anyone, "");
    int _when = AnyTurn;

    public LogPickers()
    {
        Who.ItemSelected += at =>
        {
            _who = _whoKeys[(int)at];
            Changed?.Invoke();
        };
        When.ItemSelected += at =>
        {
            _when = _whenKeys[(int)at];
            Changed?.Invoke();
        };
    }

    public bool ByPickedPiece => _who.By == WhoBy.Picked;

    public bool ByThisTurn => _when == TurnNow;

    static OptionButton Pick(string tip) => new()
    {
        TooltipText = tip, FitToLongestItem = false, ClipText = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = Ui.Px(PickWide, 0),
    };

    public void Forget()
    {
        _names.Clear();
        _turns.Clear();
    }

    public bool Note(LogLine line)
    {
        bool fresh = _turns.Add(line.Turn);
        if (line.Who is { Length: > 0 } who) fresh |= _names.Add(who);
        return fresh;
    }

    public void Offer(Func<int, string?> turnName)
    {
        _whoKeys.Clear();
        Who.Clear();
        void Person(WhoBy by, string name, string text)
        {
            _whoKeys.Add((by, name));
            Who.AddItem(text);
        }
        Person(WhoBy.Anyone, "", Anyone);
        foreach (var name in _names) Person(WhoBy.Person, name, name);
        Person(WhoBy.Picked, "", PickedPiece);
        int who = _whoKeys.FindIndex(k => k.By == _who.By && string.Equals(k.Name, _who.Name, StringComparison.OrdinalIgnoreCase));
        if (who < 0) (_who, who) = (_whoKeys[0], 0);
        Who.Selected = who;
        _whenKeys.Clear();
        When.Clear();
        void Turn(int key, string text)
        {
            _whenKeys.Add(key);
            When.AddItem(text);
        }
        Turn(AnyTurn, All);
        Turn(TurnNow, ThisTurn);
        foreach (var turn in _turns.Reverse()) Turn(turn, turnName(turn) is { Length: > 0 } name ? $"Turn {turn} · {name}" : $"Turn {turn}");
        int when = _whenKeys.IndexOf(_when);
        if (when < 0) (_when, when) = (AnyTurn, 0);
        When.Selected = when;
    }

    public bool Passes(LogLine line, IReadOnlySet<string>? picked, int turnNow)
    {
        bool who = _who.By switch
        {
            WhoBy.Person => string.Equals(line.Who, _who.Name, StringComparison.OrdinalIgnoreCase),
            WhoBy.Picked => line.Piece is { } piece && picked != null && picked.Contains(piece),
            _ => true,
        };
        return who && _when switch
        {
            AnyTurn => true,
            TurnNow => line.Turn == turnNow,
            _ => line.Turn == _when,
        };
    }
}
