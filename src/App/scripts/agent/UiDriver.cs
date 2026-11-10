using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace KTable.App;

sealed class UiDriver
{
    const int TextMost = 300, ItemsMost = 100, TipMost = 160;
    const string Main = "main window";
    readonly Dictionary<string, ulong> _refs = new();
    int _gen;

    public sealed record Row(string Ref, string Kind, string Window, string? Text, string? Value, string? Tip, bool Disabled, List<string>? Items);

    public List<Row> Read(Window root, bool labels)
    {
        _gen++;
        _refs.Clear();
        var rows = new List<Row>();
        Walk(root, root, Main, rows, labels);
        return rows;
    }

    public static IEnumerable<string> OpenWindows(Window root) =>
        All(root).OfType<Window>().Where(w => w != root && w.Visible && w is not PopupMenu && !AgentFence.Fenced(w)).Select(w => Title(w)).Distinct();

    static IEnumerable<Node> All(Node n) => new[] { n }.Concat(n.GetChildren(true).Where(c => c is not Node3D).SelectMany(All));

    static string Title(Window w) => w.Title.Length > 0 ? w.Title : w.Name;

    string Keep(GodotObject o, string? item = null)
    {
        var r = $"{_gen}.{_refs.Count + 1}";
        _refs[r] = o.GetInstanceId();
        return item == null ? r : r + "/" + item;
    }

    void Walk(Node n, Window root, string window, List<Row> rows, bool labels)
    {
        if (n is Node3D or SubViewport || AgentFence.Marked(n)) return;
        switch (n)
        {
            case PopupMenu pm when pm != root:
                if (!pm.Visible) return;
                Menu(pm, rows);
                return;
            case Window w when w != root:
                if (!w.Visible) return;
                window = Title(w);
                rows.Add(new Row(Keep(w), "window", window, window, null, null, false, null));
                break;
            case Control c:
                if (!c.IsVisibleInTree()) return;
                if (RowOf(c, window, labels) is { } row) rows.Add(row);
                if (c is SpinBox or OptionButton or MenuButton or LineEdit or TextEdit or ScrollBar or ItemList or Tree or TabBar) return;
                break;
        }
        foreach (var child in n.GetChildren(true)) Walk(child, root, window, rows, labels);
    }

    static bool Shown(Node n) => n switch { Window w => w.Visible, Control c => c.IsVisibleInTree(), _ => false };

    void Menu(PopupMenu pm, List<Row> rows)
    {
        var title = pm.Title.Length > 0 ? pm.Title : "menu";
        var menu = Keep(pm);
        rows.Add(new Row(menu, "menu", title, title, null, null, false, null));
        for (int i = 0; i < pm.ItemCount; i++)
        {
            if (pm.IsItemSeparator(i))
            {
                if (pm.GetItemText(i) is { Length: > 0 } heading) rows.Add(new Row(menu + "/-", "heading", title, heading, null, null, true, null));
                continue;
            }
            var sub = pm.GetItemSubmenuNode(i);
            if (sub != null && AgentFence.Fenced(sub)) continue;
            bool checkable = pm.IsItemCheckable(i) || pm.IsItemRadioCheckable(i);
            var kind = sub != null ? "submenu" : checkable ? "menu_check" : "menu_item";
            rows.Add(new Row(menu + "/" + i.ToString(CultureInfo.InvariantCulture), kind, title, pm.GetItemText(i), checkable ? OnOff(pm.IsItemChecked(i)) : null,
                Tip(pm.GetItemTooltip(i)), pm.IsItemDisabled(i), null));
        }
    }

    Row? RowOf(Control c, string window, bool labels)
    {
        string? tip = Tip(c.TooltipText);
        Row Make(string kind, string? text, string? value = null, bool disabled = false, List<string>? items = null, string? hint = null) =>
            new(Keep(c), kind, window, Cut(text), Cut(value), hint ?? tip, disabled, items);
        switch (c)
        {
            case OptionButton ob:
                var current = ob.Selected >= 0 ? ob.GetItemText(ob.Selected) : null;
                return Make("choice", current, current, ob.Disabled, Range(ob.ItemCount).Select(ob.GetItemText).ToList());
            case MenuButton mb:
                return Make("menu_button", Named(mb.Text, tip), null, mb.Disabled);
            case CheckBox or CheckButton:
                var check = (BaseButton)c;
                return Make("check", ((Button)c).Text, OnOff(check.ButtonPressed), check.Disabled);
            case BaseButton b:
                var text = (b as Button)?.Text is { Length: > 0 } t ? t : null;
                return Make(b.ToggleMode ? "toggle" : "button", text ?? tip, b.ToggleMode ? OnOff(b.ButtonPressed) : null, b.Disabled);
            case LineEdit le:
                return Make("field", null, le.Text, !le.Editable, null, Tip(le.PlaceholderText is { Length: > 0 } p ? (tip == null ? p : p + " · " + tip) : tip));
            case TextEdit te:
                return Make("text_area", null, te.Text, !te.Editable);
            case SpinBox sb:
                return Make("number", null, Num(sb.Value), !sb.Editable, null, $"{Num(sb.MinValue)}–{Num(sb.MaxValue)}" + (tip != null ? " · " + tip : ""));
            case Slider sl:
                return Make("slider", null, Num(sl.Value), !sl.Editable, null, $"{Num(sl.MinValue)}–{Num(sl.MaxValue)}" + (tip != null ? " · " + tip : ""));
            case ProgressBar pb:
                return Make("progress", null, Num(pb.Value));
            case ItemList il:
                var picked = Range(il.ItemCount).Where(il.IsSelected).Select(il.GetItemText).ToList();
                return Make("list", null, picked.Count > 0 ? string.Join(", ", picked) : null, false, Range(Math.Min(il.ItemCount, ItemsMost)).Select(il.GetItemText).ToList());
            case Tree tree:
                return Make("tree", null, tree.GetSelected()?.GetText(0), false, TreeItems(tree).Take(ItemsMost).Select(x => x.GetText(0)).ToList());
            case TabContainer tc:
                return Make("tabs", null, tc.GetTabTitle(tc.CurrentTab), false, Range(tc.GetTabCount()).Select(tc.GetTabTitle).ToList());
            case TabBar tb when tb.GetParent() is not TabContainer:
                return Make("tabs", null, tb.GetTabTitle(tb.CurrentTab), false, Range(tb.TabCount).Select(tb.GetTabTitle).ToList());
            case RichTextLabel rt when labels && rt.GetParsedText().Trim().Length > 0:
                return Make("label", rt.GetParsedText().Trim());
            case Label l when labels && l.Text.Trim().Length > 0:
                return Make("label", l.Text.Trim());
        }
        return null;
    }

    static IEnumerable<int> Range(int n) => Enumerable.Range(0, Math.Max(0, n));

    static IEnumerable<TreeItem> TreeItems(Tree tree)
    {
        for (var it = tree.GetRoot(); it != null; it = it.GetNextVisible())
            if (it != tree.GetRoot() || !tree.HideRoot) yield return it;
    }

    static string OnOff(bool on) => on ? "on" : "off";
    static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string? Named(string text, string? tip) => text.Length > 0 ? text : tip;
    static string? Cut(string? s) => s is not { Length: > 0 } ? null : s.Length <= TextMost ? s : s[..TextMost] + "…";
    static string? Tip(string? s) => s?.Split('\n')[0].Trim() is { Length: > 0 } t ? t.Length <= TipMost ? t : t[..TipMost] + "…" : null;

    public (GodotObject? Target, int? Item, string? Why) Resolve(string reference)
    {
        var slash = reference.IndexOf('/');
        var key = slash < 0 ? reference : reference[..slash];
        int? item = slash >= 0 && int.TryParse(reference[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        if (!key.StartsWith(_gen.ToString(CultureInfo.InvariantCulture) + ".", StringComparison.Ordinal)) return (null, null, "That ref is from an older read: call ui_read again.");
        if (!_refs.TryGetValue(key, out var id) || GodotObject.InstanceFromId(id) is not { } o || !GodotObject.IsInstanceValid(o)) return (null, null, "That control is gone: call ui_read again.");
        if (o is Node node && AgentFence.Fenced(node)) return (null, null, "That control is out of the agent's reach.");
        if (o is Node shown && !Shown(shown)) return (null, null, "That control is hidden now: call ui_read again.");
        return (o, item, null);
    }

    public static string? Act(GodotObject target, int? item, string action, string? value) => target switch
    {
        PopupMenu pm when item is { } i => MenuItem(pm, i),
        Window w when action == "close" => Close(w),
        Control c => ActOn(c, action, value),
        _ => "That takes only close.",
    };

    static string? MenuItem(PopupMenu pm, int i)
    {
        if (i < 0 || i >= pm.ItemCount || pm.IsItemSeparator(i)) return "No such row in that menu.";
        if (pm.IsItemDisabled(i)) return "That row is greyed out.";
        if (pm.GetItemSubmenuNode(i) is { } sub)
        {
            sub.Position = pm.Position + new Vector2I(pm.Size.X, 0);
            sub.Popup();
            return null;
        }
        bool checkable = pm.IsItemCheckable(i) || pm.IsItemRadioCheckable(i);
        pm.EmitSignal(PopupMenu.SignalName.IdPressed, (long)pm.GetItemId(i));
        pm.EmitSignal(PopupMenu.SignalName.IndexPressed, (long)i);
        if (checkable ? pm.HideOnCheckableItemSelection : pm.HideOnItemSelection) pm.Hide();
        return null;
    }

    static string? Close(Window w)
    {
        w.EmitSignal(Window.SignalName.CloseRequested);
        if (w is AcceptDialog dlg && dlg.Visible)
        {
            dlg.Hide();
            dlg.EmitSignal(AcceptDialog.SignalName.Canceled);
        }
        return null;
    }

    static string? ActOn(Control c, string action, string? value)
    {
        switch (action)
        {
            case "right_click":
                Mouse(c, MouseButton.Right, false);
                return null;
            case "double_click":
                if (c is ItemList dl) return Pick(dl, value, activate: true);
                if (c is Tree dt) return PickTree(dt, value, activate: true);
                Mouse(c, MouseButton.Left, true);
                return null;
            case "set":
                return Set(c, value);
            case "submit":
                if (c is not LineEdit sl) return "submit is for a text field.";
                if (value != null) Set(c, value);
                sl.EmitSignal(LineEdit.SignalName.TextSubmitted, sl.Text);
                return null;
            case "open":
                if (c is MenuButton om) { om.ShowPopup(); return null; }
                if (c is OptionButton oo) { oo.ShowPopup(); return null; }
                return "open is for a menu button or a choice.";
            case "click":
                return Click(c, value);
            default:
                return "action is click, right_click, double_click, set, submit, open or close.";
        }
    }

    static string? Click(Control c, string? value)
    {
        switch (c)
        {
            case BaseButton { Disabled: true }:
                return "That button is greyed out.";
            case MenuButton mb:
                mb.ShowPopup();
                return null;
            case OptionButton ob:
                if (value != null) return Set(ob, value);
                ob.ShowPopup();
                return null;
            case BaseButton b:
                b.EmitSignal(BaseButton.SignalName.ButtonDown);
                if (b.ToggleMode) b.ButtonPressed = !b.ButtonPressed;
                b.EmitSignal(BaseButton.SignalName.ButtonUp);
                b.EmitSignal(BaseButton.SignalName.Pressed);
                return null;
            case ItemList il:
                return Pick(il, value, activate: false);
            case Tree tree:
                return PickTree(tree, value, activate: false);
            case TabContainer or TabBar:
                return Set(c, value);
            case LineEdit or TextEdit:
                c.GrabFocus();
                return null;
            default:
                Mouse(c, MouseButton.Left, false);
                return null;
        }
    }

    static string? Set(Control c, string? value)
    {
        if (value == null) return "value is needed.";
        switch (c)
        {
            case OptionButton ob:
                if (Index(Range(ob.ItemCount).Select(ob.GetItemText).ToList(), value) is not { } oi) return "No such choice: " + Choices(Range(ob.ItemCount).Select(ob.GetItemText));
                ob.Select(oi);
                ob.EmitSignal(OptionButton.SignalName.ItemSelected, (long)oi);
                return null;
            case LineEdit le:
                if (!le.Editable) return "That field cannot be typed into.";
                le.GrabFocus();
                le.Text = value;
                le.CaretColumn = value.Length;
                le.EmitSignal(LineEdit.SignalName.TextChanged, value);
                le.ReleaseFocus();
                return null;
            case TextEdit te:
                if (!te.Editable) return "That text cannot be typed into.";
                te.GrabFocus();
                te.Text = value;
                te.EmitSignal(TextEdit.SignalName.TextChanged);
                te.ReleaseFocus();
                return null;
            case Godot.Range r:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return "value is a number.";
                r.Value = v;
                return null;
            case CheckBox or CheckButton:
                if (!bool.TryParse(value, out var on)) return "value is true or false.";
                var check = (BaseButton)c;
                if (check.ButtonPressed != on) { check.ButtonPressed = on; check.EmitSignal(BaseButton.SignalName.Pressed); }
                return null;
            case BaseButton { ToggleMode: true } toggle:
                if (!bool.TryParse(value, out var down)) return "value is true or false.";
                if (toggle.ButtonPressed != down) { toggle.ButtonPressed = down; toggle.EmitSignal(BaseButton.SignalName.Pressed); }
                return null;
            case TabContainer tc:
                if (Index(Range(tc.GetTabCount()).Select(tc.GetTabTitle).ToList(), value) is not { } ti) return "No such tab: " + Choices(Range(tc.GetTabCount()).Select(tc.GetTabTitle));
                tc.CurrentTab = ti;
                return null;
            case TabBar tb:
                if (Index(Range(tb.TabCount).Select(tb.GetTabTitle).ToList(), value) is not { } bi) return "No such tab: " + Choices(Range(tb.TabCount).Select(tb.GetTabTitle));
                tb.CurrentTab = bi;
                return null;
            case ItemList il:
                return Pick(il, value, activate: false);
            case Tree tree:
                return PickTree(tree, value, activate: false);
        }
        return "That control takes click only.";
    }

    static string? Pick(ItemList il, string? value, bool activate)
    {
        var items = Range(il.ItemCount).Select(il.GetItemText).ToList();
        if (value == null || Index(items, value) is not { } i) return "value names an item: " + Choices(items);
        il.Select(i, true);
        il.EmitSignal(ItemList.SignalName.ItemSelected, (long)i);
        if (activate) il.EmitSignal(ItemList.SignalName.ItemActivated, (long)i);
        return null;
    }

    static string? PickTree(Tree tree, string? value, bool activate)
    {
        var items = TreeItems(tree).ToList();
        if (value == null || Index(items.Select(x => x.GetText(0)).ToList(), value) is not { } i) return "value names an item: " + Choices(items.Select(x => x.GetText(0)));
        items[i].Select(0);
        if (activate) tree.EmitSignal(Tree.SignalName.ItemActivated);
        return null;
    }

    static int? Index(List<string> items, string value)
    {
        var v = value.Trim();
        int exact = items.FindIndex(x => string.Equals(x.Trim(), v, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0) return exact;
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0 && n < items.Count) return n;
        int part = items.FindIndex(x => x.Contains(v, StringComparison.OrdinalIgnoreCase));
        return part >= 0 ? part : null;
    }

    static string Choices(IEnumerable<string> items) => string.Join(", ", items.Take(ItemsMost)) + ".";

    static void Mouse(Control c, MouseButton button, bool twice)
    {
        var at = c.Size / 2;
        var global = c.GetGlobalTransformWithCanvas() * at;
        foreach (var pressed in new[] { true, false })
            c.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
            {
                ButtonIndex = button, Pressed = pressed, DoubleClick = twice && pressed, Position = at, GlobalPosition = global,
                ButtonMask = pressed ? (button == MouseButton.Right ? MouseButtonMask.Right : MouseButtonMask.Left) : 0,
            });
    }
}
