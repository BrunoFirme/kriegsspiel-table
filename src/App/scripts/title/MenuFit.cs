using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class MenuFit
{
    internal readonly record struct Give(Control What, int Roomy, int Tight);

    readonly ScrollContainer _room;
    readonly Control _column;
    readonly IReadOnlyList<Give> _gives;

    public bool Tight { get; private set; }

    MenuFit(ScrollContainer room, Control column, IReadOnlyList<Give> gives)
    {
        _room = room;
        _column = column;
        _gives = gives;
    }

    public static MenuFit Keep(ScrollContainer room, Control column, IReadOnlyList<Give> gives)
    {
        var fit = new MenuFit(room, column, gives);
        room.Resized += fit.Fit;
        column.MinimumSizeChanged += fit.Fit;
        return fit;
    }

    int Saved => Ui.Px(_gives.Sum(g => g.Roomy - g.Tight));

    void Fit()
    {
        if (_room.Size.Y <= 0) return;
        bool tight = _column.GetCombinedMinimumSize().Y + (Tight ? Saved : 0) > _room.Size.Y;
        if (tight == Tight) return;
        Tight = tight;
        foreach (var g in _gives) g.What.CustomMinimumSize = new Vector2(g.What.CustomMinimumSize.X, Ui.Px(tight ? g.Tight : g.Roomy));
    }
}
