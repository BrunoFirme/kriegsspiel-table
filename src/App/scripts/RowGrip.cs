using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class RowGrip
{
    const int LineHigh = 2, LineInset = Ui.RowIconSide;
    const float LeftBehind = 0.35f;
    enum Step { Up, Down, Top, Bottom }

    readonly string _tag;
    readonly List<string> _ids;
    readonly int _pinned;
    readonly Action<string, int> _move;
    readonly Control _list;
    readonly string _tip;
    readonly Node _menus;
    readonly List<Control> _rows = new();
    ColorRect? _line;
    Control? _lifted;
    int? _over;
    string? _carried;

    public RowGrip(Control list, Node menus, string tag, IEnumerable<string> topDown, int pinned, string tip, Action<string, int> move)
    {
        (_list, _menus, _tag, _ids, _pinned, _tip, _move) = (list, menus, tag, topDown.ToList(), pinned, tip, move);
    }

    public Control Handle(int place, string name)
    {
        if (place < _pinned) return new Control { CustomMinimumSize = Ui.Px(Ui.RowIconSide, Ui.RowIconSide), MouseFilter = Control.MouseFilterEnum.Ignore };
        var grip = Ui.RowIcon("grip", _tip);
        grip.MouseDefaultCursorShape = Control.CursorShape.Drag;
        grip.SetMeta(LiftMeta, Variant.From(name));
        grip.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }) Menu(grip, place);
        };
        return grip;
    }

    const string LiftMeta = "grip_name";

    public void Takes(Control row, int place)
    {
        while (_rows.Count <= place) _rows.Add(row);
        _rows[place] = row;
        foreach (var c in row.GetChildren().OfType<Control>().Prepend(row))
        {
            var target = c;
            c.SetDragForwarding(Callable.From((Vector2 _) => target.HasMeta(LiftMeta) ? Lift(row, place, target.GetMeta(LiftMeta).AsString()) : default),
                Callable.From((Vector2 _, Variant data) => Offered(place, Upper(row), data)), Callable.From((Vector2 _, Variant data) => Dropped(place, Upper(row), data)));
        }
    }

    static bool Upper(Control row) => row.GetLocalMousePosition().Y < row.Size.Y / 2;

    Variant Lift(Control row, int place, string name)
    {
        var chip = new PanelContainer();
        chip.AddThemeStyleboxOverride("panel", Ui.Box(Ui.LineStrong));
        var inside = new HBoxContainer();
        inside.AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        inside.AddChild(new ToolIcon { Kind = "grip", Color = Ui.Ink, CustomMinimumSize = Ui.Px(Ui.RowIconSide, Ui.RowIconSide) });
        inside.AddChild(new Label { Text = name, VerticalAlignment = VerticalAlignment.Center });
        chip.AddChild(inside);
        row.SetDragPreview(chip);
        row.Modulate = new Color(Colors.White, LeftBehind);
        _lifted = row;
        return Variant.From(_tag + _ids[place]);
    }

    string? Dragged(Variant data) => data.VariantType == Variant.Type.String && data.AsString() is var s && s.StartsWith(_tag, StringComparison.Ordinal) ? s[_tag.Length..] : null;

    internal int? Target(string id, int place, bool upper)
    {
        int from = _ids.IndexOf(id);
        if (from < _pinned) return null;
        int gap = Math.Max(_pinned, upper ? place : place + 1), to = gap > from ? gap - 1 : gap;
        return to == from ? null : to;
    }

    internal bool Offered(int place, bool upper, Variant data)
    {
        if (Dragged(data) is not { } id || _ids.IndexOf(id) < 0) return false;
        (_over, _carried) = (place, id);
        Mark(upper);
        return true;
    }

    void Mark(bool upper)
    {
        if (_over is not { } place || _carried is not { } id) return;
        ShowLine(Target(id, place, upper) == null ? null : Math.Max(_pinned, upper ? place : place + 1));
    }

    internal void Dropped(int place, bool upper, Variant data)
    {
        (_over, _carried) = (null, null);
        ShowLine(null);
        if (Dragged(data) is { } id && Target(id, place, upper) is { } to) _move(id, to);
    }

    void ShowLine(int? gap)
    {
        if (gap is not { } g || _rows.Count == 0 || !GodotObject.IsInstanceValid(_list))
        {
            if (_line != null && GodotObject.IsInstanceValid(_line)) _line.Visible = false;
            return;
        }
        if (_line == null || !GodotObject.IsInstanceValid(_line))
        {
            _line = new ColorRect { Color = Ui.Accent, TopLevel = true, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _list.AddChild(_line);
        }
        var edge = g < _rows.Count ? _rows[g].GetGlobalRect().Position.Y : _rows[^1].GetGlobalRect().End.Y;
        var rows = _list.GetGlobalRect();
        float high = Ui.Px(LineHigh), inset = Ui.Px(LineInset);
        _line.GlobalPosition = new Vector2(rows.Position.X + inset, edge - high / 2);
        _line.Size = new Vector2(Math.Max(0, rows.Size.X - inset), high);
        _line.Visible = true;
    }

    public void Sync()
    {
        if (_list.GetViewport()?.GuiIsDragging() == true)
        {
            if (_over is { } place && place < _rows.Count && GodotObject.IsInstanceValid(_rows[place]) && _rows[place].GetGlobalRect().HasPoint(_rows[place].GetGlobalMousePosition()))
                Mark(Upper(_rows[place]));
            return;
        }
        (_over, _carried) = (null, null);
        if (_line != null && GodotObject.IsInstanceValid(_line)) _line.Visible = false;
        if (_lifted != null && GodotObject.IsInstanceValid(_lifted)) _lifted.Modulate = Colors.White;
        _lifted = null;
    }

    internal int? Stepped(int place, int step) => (Step)step switch
    {
        Step.Up when place > _pinned => place - 1,
        Step.Down when place < _ids.Count - 1 => place + 1,
        Step.Top when place > _pinned => _pinned,
        Step.Bottom when place < _ids.Count - 1 => _ids.Count - 1,
        _ => null,
    };

    void Menu(Control under, int place)
    {
        var menu = new PopupMenu();
        foreach (var (step, words) in new[] { (Step.Up, "Move up"), (Step.Down, "Move down"), (Step.Top, "To top"), (Step.Bottom, "To bottom") })
        {
            menu.AddItem(words, (int)step);
            menu.SetItemDisabled(menu.ItemCount - 1, Stepped(place, (int)step) == null);
        }
        menu.IdPressed += id => { if (Stepped(place, (int)id) is { } to) _move(_ids[place], to); };
        Ui.PopupBelow(_menus, menu, under);
    }
}
