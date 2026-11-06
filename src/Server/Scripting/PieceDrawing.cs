using KTable.Core.Model;

namespace KTable.Server.Scripting;

internal sealed class PieceDrawing(Func<string, PieceInstance?> find)
{
    readonly Dictionary<string, List<Widget>> _widgets = new();
    readonly Dictionary<string, List<MenuItemDef>> _menus = new();
    readonly HashSet<string> _fromLoad = new();
    readonly Dictionary<string, HashSet<PieceScript>> _drawers = new();

    public List<Widget> Widgets(string id)
    {
        if (!_widgets.TryGetValue(id, out var ws))
            _widgets[id] = ws = (find(id)?.Widgets ?? new()).Select(w => w.Clone()).ToList();
        return ws;
    }

    public List<MenuItemDef> Menus(string id)
    {
        if (!_menus.TryGetValue(id, out var ms))
            _menus[id] = ms = (find(id)?.MenuItems ?? new()).Select(m => m.Clone()).ToList();
        return ms;
    }

    public bool TryWidgets(string id, out List<Widget> widgets) => _widgets.TryGetValue(id, out widgets!);
    public bool TryMenus(string id, out List<MenuItemDef> menus) => _menus.TryGetValue(id, out menus!);

    public void Blank(string id)
    {
        _widgets[id] = new List<Widget>();
        _menus[id] = new List<MenuItemDef>();
        _drawers.Remove(id);
    }

    public void SetWidgets(string id, List<Widget> widgets) => _widgets[id] = widgets;
    public void SetMenus(string id, List<MenuItemDef> menus) => _menus[id] = menus;

    public void DrawnBy(string id, PieceScript ps)
    {
        if (!_drawers.TryGetValue(id, out var who)) _drawers[id] = who = new HashSet<PieceScript>();
        who.Add(ps);
    }

    public bool TryDrawers(string id, out HashSet<PieceScript> who) => _drawers.TryGetValue(id, out who!);

    public List<KeyValuePair<string, HashSet<PieceScript>>> AllDrawers() => _drawers.ToList();

    public void AdoptDrawer(PieceScript temp, PieceScript? real)
    {
        foreach (var who in _drawers.Values) if (who.Remove(temp) && real != null) who.Add(real);
    }

    public void FromLoad(string id) => _fromLoad.Add(id);
    public bool IsFromLoad(string id) => _fromLoad.Contains(id);
    public void ClearFromLoad() => _fromLoad.Clear();

    public void Forget(string id, bool pieceGone)
    {
        _widgets.Remove(id);
        _menus.Remove(id);
        _fromLoad.Remove(id);
        if (pieceGone) _drawers.Remove(id);
    }

    public void Clear()
    {
        _widgets.Clear();
        _menus.Clear();
        _fromLoad.Clear();
        _drawers.Clear();
    }
}
