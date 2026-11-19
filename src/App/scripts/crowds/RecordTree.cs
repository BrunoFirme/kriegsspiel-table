using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Godot;

namespace KTable.App;

sealed class RecordTree
{
    const int Add = 0, Remove = 1, Clear = 2, ValueWidth = 150;

    public readonly Tree View = new()
    {
        HideRoot = true, Columns = 2, SelectMode = Tree.SelectModeEnum.Single, AllowRmbSelect = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Click a value to change it · right-click to add or remove",
    };

    public event Action? Changed;

    public HashSet<string> Hidden { get; } = new();

    readonly Dictionary<TreeItem, Action<TreeItem>> _set = new();
    readonly Dictionary<TreeItem, (Action? Add, Action? Remove, Action? Clear)> _menu = new();
    readonly HashSet<string> _open = new();
    readonly NullabilityInfoContext _nulls = new();
    readonly PopupMenu _popup = new();
    object _root = new();
    TreeItem? _at;

    public RecordTree()
    {
        View.SetColumnExpand(1, true);
        View.SetColumnCustomMinimumWidth(1, Ui.Px(ValueWidth));
        View.AddThemeConstantOverride("draw_relationship_lines", 0);
        View.ItemEdited += Edited;
        View.ItemMouseSelected += Clicked;
        View.ItemCollapsed += item => Remember(item);
        View.AddChild(_popup);
        _popup.IdPressed += Chosen;
    }

    public void Show(object root)
    {
        _root = root;
        Fill();
    }

    void Fill()
    {
        View.Clear();
        _set.Clear();
        _menu.Clear();
        var top = View.CreateItem();
        Members(top, _root, "");
    }

    void Changes()
    {
        Changed?.Invoke();
        Fill();
    }

    static string Words(string name) =>
        string.Concat(name.Select((ch, i) => i > 0 && char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : ch.ToString()));

    static IEnumerable<PropertyInfo> Fields(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0
            && p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always });

    void Members(TreeItem parent, object owner, string path)
    {
        foreach (var p in Fields(owner.GetType()).Where(p => !Hidden.Contains(p.Name)))
        {
            var prop = p;
            bool optional = _nulls.Create(prop).WriteState == NullabilityState.Nullable;
            Value(parent, Words(p.Name), p.PropertyType, prop.GetValue(owner), v => prop.SetValue(owner, optional && v is "" ? null : v), path + "/" + p.Name);
        }
    }

    static bool Plain(Type t) => (Nullable.GetUnderlyingType(t) ?? t) is var u && (u == typeof(string) || u.IsPrimitive || u == typeof(decimal));

    static Type? ListOf(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) ? t.GetGenericArguments()[0] : null;

    static Type? MapOf(Type t) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>) && t.GetGenericArguments()[0] == typeof(string) ? t.GetGenericArguments()[1] : null;

    static object? Fresh(Type t) =>
        t == typeof(string) ? "" : (Nullable.GetUnderlyingType(t) ?? t).IsValueType ? Activator.CreateInstance(Nullable.GetUnderlyingType(t) ?? t) : Activator.CreateInstance(t);

    void Grow(string path, Action change)
    {
        change();
        _open.Add(path);
        Changes();
    }

    void Value(TreeItem parent, string name, Type type, object? value, Action<object?> put, string path)
    {
        var item = View.CreateItem(parent);
        item.SetText(0, name);
        bool nullable = !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        if (Plain(type))
        {
            Cell(item, type, value, put);
            if (nullable && value != null && type != typeof(string)) _menu[item] = (null, null, () => { put(null); Changes(); });
            return;
        }
        item.Collapsed = !_open.Contains(path);
        item.SetMetadata(0, path);
        if (value == null)
        {
            item.SetText(1, "—");
            item.SetCustomColor(1, Ui.Muted);
            _menu[item] = (() => Grow(path, () => put(Fresh(type))), null, null);
            return;
        }
        if (ListOf(type) is { } of) Listed(item, (IList)value, of, path);
        else if (MapOf(type) is { } to) Mapped(item, (IDictionary)value, to, path);
        else Members(item, value, path);
        if (nullable) _menu[item] = (_menu.GetValueOrDefault(item).Add, null, () => { put(null); Changes(); });
    }

    void Listed(TreeItem item, IList list, Type of, string path)
    {
        item.SetText(1, list.Count.ToString(CultureInfo.InvariantCulture));
        item.SetCustomColor(1, Ui.Muted);
        _menu[item] = (() => Grow(path, () => list.Add(Fresh(of))), null, null);
        for (int i = 0; i < list.Count; i++)
        {
            int at = i;
            Value(item, (i + 1).ToString(CultureInfo.InvariantCulture), of, list[i], v => list[at] = v, path + "/" + i);
            var child = item.GetChild(i);
            _menu[child] = (_menu.GetValueOrDefault(child).Add, () => { list.RemoveAt(at); Changes(); }, null);
        }
    }

    void Mapped(TreeItem item, IDictionary map, Type to, string path)
    {
        item.SetText(1, map.Count.ToString(CultureInfo.InvariantCulture));
        item.SetCustomColor(1, Ui.Muted);
        _menu[item] = (() => Grow(path, () => map[Unused(map)] = Fresh(to)), null, null);
        int i = 0;
        foreach (var key in map.Keys.Cast<string>().ToList())
        {
            string k = key;
            Value(item, k, to, map[k], v => map[k] = v, path + "/" + k);
            var child = item.GetChild(i++);
            child.SetEditable(0, true);
            _set[child] = Renamer(child, map, k, to, path);
            _menu[child] = (_menu.GetValueOrDefault(child).Add, () => { map.Remove(k); Changes(); }, null);
        }
    }

    Action<TreeItem> Renamer(TreeItem child, IDictionary map, string key, Type to, string path)
    {
        var value = _set.GetValueOrDefault(child);
        return edited =>
        {
            if (View.GetEditedColumn() == 1)
            {
                value?.Invoke(edited);
                return;
            }
            string now = edited.GetText(0).Trim();
            if (now.Length == 0 || now == key || map.Contains(now)) { edited.SetText(0, key); return; }
            var held = map[key];
            map.Remove(key);
            map[now] = held;
            Changes();
        };
    }

    static string Unused(IDictionary map)
    {
        for (int i = 1; ; i++)
            if (!map.Contains(i == 1 ? "new" : $"new {i}")) return i == 1 ? "new" : $"new {i}";
    }

    void Cell(TreeItem item, Type type, object? value, Action<object?> put)
    {
        var u = Nullable.GetUnderlyingType(type) ?? type;
        if (u == typeof(bool))
        {
            item.SetCellMode(1, TreeItem.TreeCellMode.Check);
            item.SetChecked(1, value is true);
            item.SetEditable(1, true);
            _set[item] = edited => { put(edited.IsChecked(1)); Changed?.Invoke(); };
            return;
        }
        item.SetText(1, value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "");
        item.SetEditable(1, true);
        _set[item] = edited => Typed(edited, u, type, put);
    }

    void Typed(TreeItem edited, Type u, Type type, Action<object?> put)
    {
        string text = edited.GetText(1).Trim();
        if (u == typeof(string)) put(text.Length == 0 && type != typeof(string) ? null : text);
        else if (text.Length == 0 && Nullable.GetUnderlyingType(type) != null) put(null);
        else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            put(Convert.ChangeType(u == typeof(double) ? n : Math.Round(n), u, CultureInfo.InvariantCulture));
        else
        {
            Fill();
            return;
        }
        Changed?.Invoke();
    }

    void Edited()
    {
        if (View.GetEdited() is { } item && _set.TryGetValue(item, out var set)) set(item);
    }

    void Remember(TreeItem item)
    {
        if (item.GetMetadata(0).VariantType != Variant.Type.String) return;
        string path = item.GetMetadata(0).AsString();
        if (item.Collapsed) _open.Remove(path);
        else _open.Add(path);
    }

    void Clicked(Vector2 at, long button)
    {
        if (button != (long)MouseButton.Right || View.GetSelected() is not { } item || !_menu.TryGetValue(item, out var acts)) return;
        _at = item;
        _popup.Clear();
        if (acts.Add != null) _popup.AddItem("Add", Add);
        if (acts.Remove != null) _popup.AddItem("Remove", Remove);
        if (acts.Clear != null) _popup.AddItem("Clear", Clear);
        if (_popup.ItemCount == 0) return;
        _popup.Position = (Vector2I)(View.GetScreenPosition() + at);
        _popup.Popup();
    }

    void Chosen(long id)
    {
        if (_at == null || !_menu.TryGetValue(_at, out var acts)) return;
        (id == Add ? acts.Add : id == Remove ? acts.Remove : acts.Clear)?.Invoke();
    }
}
