using KTable.Core.Model;
using MoonSharp.Interpreter;

namespace KTable.Server.Scripting;

internal sealed class TableControls
{
    readonly List<(PieceScript owner, string label, DynValue fn, bool onUp)> _hotkeys = new();
    readonly List<(PieceScript owner, string label, DynValue fn)> _menu = new();
    readonly Dictionary<long, (List<string> selected, string? hovered)> _pointer = new();

    public int AddHotkey(PieceScript owner, string label, DynValue fn, bool onUp)
    {
        _hotkeys.Add((owner, label, fn, onUp));
        return _hotkeys.Count;
    }

    public int AddMenuRow(PieceScript owner, string label, DynValue fn)
    {
        _menu.Add((owner, label, fn));
        return _menu.Count;
    }

    public bool ClearHotkeys(PieceScript? owner) => _hotkeys.RemoveAll(h => owner == null || h.owner == owner) > 0;

    public bool ClearMenu(PieceScript? owner) => _menu.RemoveAll(m => owner == null || m.owner == owner) > 0;

    public (bool hotkeys, bool menu) Forget(PieceScript ps) => (_hotkeys.RemoveAll(h => h.owner == ps) > 0, _menu.RemoveAll(m => m.owner == ps) > 0);

    public bool Clear()
    {
        if (_hotkeys.Count == 0 && _menu.Count == 0) return false;
        _hotkeys.Clear();
        _menu.Clear();
        return true;
    }

    public (bool hotkeys, bool menu) Adopt(PieceScript temp, PieceScript? real)
    {
        bool hotkeys = false, menu = false;
        for (int i = _hotkeys.Count - 1; i >= 0; i--)
            if (_hotkeys[i].owner == temp)
            {
                if (real != null) _hotkeys[i] = (real, _hotkeys[i].label, _hotkeys[i].fn, _hotkeys[i].onUp);
                else { _hotkeys.RemoveAt(i); hotkeys = true; }
            }
        for (int i = _menu.Count - 1; i >= 0; i--)
            if (_menu[i].owner == temp)
            {
                if (real != null) _menu[i] = (real, _menu[i].label, _menu[i].fn);
                else { _menu.RemoveAt(i); menu = true; }
            }
        return (hotkeys, menu);
    }

    public (PieceScript owner, DynValue fn, bool onUp)? Hotkey(int index) =>
        index >= 1 && index <= _hotkeys.Count ? (_hotkeys[index - 1].owner, _hotkeys[index - 1].fn, _hotkeys[index - 1].onUp) : null;

    public (PieceScript owner, DynValue fn)? MenuRow(int index) =>
        index >= 1 && index <= _menu.Count ? (_menu[index - 1].owner, _menu[index - 1].fn) : null;

    public List<HotkeyDef> HotkeyDefs() => _hotkeys.Select(h => new HotkeyDef { Label = h.label, OnUp = h.onUp }).ToList();
    public List<MenuItemDef> MenuDefs() => _menu.Select(m => new MenuItemDef { Label = m.label }).ToList();

    public void Pointed(long peer, List<string> selected, string? hovered) => _pointer[peer] = (selected, hovered);

    public void Select(long peer, string id, bool on)
    {
        if (_pointer.TryGetValue(peer, out var pt)) { if (on && !pt.selected.Contains(id)) pt.selected.Add(id); else if (!on) pt.selected.Remove(id); }
        else if (on) _pointer[peer] = (new List<string> { id }, null);
    }

    public IEnumerable<string> SelectedBy(long peer) => _pointer.TryGetValue(peer, out var pt) ? pt.selected : Enumerable.Empty<string>();
    public string? HoveredBy(long peer) => _pointer.TryGetValue(peer, out var pt) ? pt.hovered : null;
}
