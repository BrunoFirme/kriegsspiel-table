using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class ShelfFind
{
    readonly ShelfView _view;
    readonly ShelfShown _s;
    readonly ShelfSort _sort;
    readonly Control _grid;
    readonly Func<string, string, Control> _make;
    readonly Dictionary<string, Control> _cards = new();
    readonly List<Control> _home = new();
    readonly List<(Control head, List<(Control card, string?[] fields)> cards)> _also = new();

    public readonly LineEdit Box;
    public readonly Label None = new() { Text = "Nothing matches", Modulate = Muted, Visible = false };
    public Action? Changed { get; set; }

    public ShelfFind(ShelfView view, ShelfShown s, ShelfSort sort, Control grid, Func<string, string, Control> make)
    {
        _view = view;
        _s = s;
        _sort = sort;
        _grid = grid;
        _make = make;
        int n = s.Present.Count(id => !Shelf.IsFixed(id));
        Box = new LineEdit
        {
            Text = view.Finding(s.Pack), PlaceholderText = n > 0 ? $"Find among {n}" : "Find", ClearButtonEnabled = true, TooltipText = "Every folder is searched (Ctrl+F)",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = Ui.Px(0, ShelfView.ActionHeight),
        };
        Box.AddThemeFontSizeOverride("font_size", Ui.Px(ShelfView.ActionFont));
        Box.AddChild(new PickKeys { Pressed = FocusKey });
    }

    bool FocusKey(InputEventKey k)
    {
        if (k.Keycode != Key.F || !k.CtrlPressed || !Box.IsVisibleInTree()) return false;
        Box.GrabFocus();
        Box.SelectAll();
        return true;
    }

    public void Card(string id, Control card) => _cards[id] = card;

    public void Also(Control head, Control card, params string?[] fields)
    {
        var at = _also.FindIndex(a => a.head == head);
        if (at < 0)
        {
            _also.Add((head, new()));
            at = _also.Count - 1;
        }
        _also[at].cards.Add((card, fields));
    }

    public void Ready()
    {
        _home.AddRange(_grid.GetChildren().OfType<Control>());
        Box.TextChanged += t =>
        {
            Show(t);
            Changed?.Invoke();
        };
        if (_view.Finding(_s.Pack).Length == 0) return;
        Show(Box.Text);
        Box.CaretColumn = Box.Text.Length;
        Callable.From(() => { if (GodotObject.IsInstanceValid(Box) && Box.IsInsideTree()) Box.GrabFocus(); }).CallDeferred();
    }

    void Show(string text)
    {
        _view.Find(_s.Pack, text);
        var q = _view.Finding(_s.Pack);
        if (q.Length == 0)
        {
            Home();
            return;
        }
        var lib = _view.Lib;
        string FolderOf(string id) => lib.FolderOf(_s.Pack, id);
        var found = lib.Ordered(_s.Pack, _s.Present, _s.Present.Where(id => !Shelf.IsFixed(id) && TextSearch.Matches(q, _s.Name(id), id, FolderOf(id))), _sort);
        foreach (var c in _grid.GetChildren().OfType<Control>()) c.Visible = false;
        for (int i = 0; i < found.Count; i++)
        {
            if (!_cards.TryGetValue(found[i], out var card))
            {
                var folder = FolderOf(found[i]);
                card = _make(found[i], folder.Length == 0 ? ShelfView.Noun(_s.Pack) : ShelfView.Where(folder));
                _grid.AddChild(card);
                _cards[found[i]] = card;
            }
            card.Visible = true;
            _grid.MoveChild(card, i);
        }
        int also = 0;
        foreach (var (head, cards) in _also)
        {
            int shown = 0;
            foreach (var (card, fields) in cards)
            {
                card.Visible = TextSearch.Matches(q, fields);
                if (card.Visible) shown++;
            }
            head.Visible = shown > 0;
            also += shown;
        }
        None.Visible = found.Count + also == 0;
    }

    void Home()
    {
        foreach (var c in _grid.GetChildren().OfType<Control>()) c.Visible = false;
        for (int i = 0; i < _home.Count; i++)
        {
            _home[i].Visible = true;
            _grid.MoveChild(_home[i], i);
        }
        foreach (var (head, cards) in _also)
        {
            head.Visible = true;
            foreach (var (card, _) in cards) card.Visible = true;
        }
        None.Visible = false;
    }
}
