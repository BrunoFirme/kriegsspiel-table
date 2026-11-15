using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Server.Scripting;

namespace KTable.App;

public sealed class ScriptKitBar
{
    readonly CodeEdit _edit;
    readonly Func<KitKind?> _kind;
    readonly Action<string> _said;

    public MenuButton Insert { get; }
    public Button Helper { get; }
    public ScriptHelperForm Form { get; }

    public ScriptKitBar(CodeEdit edit, Func<KitKind?> kind, Action<string> said, int labels)
    {
        _edit = edit;
        _kind = kind;
        _said = said;
        Insert = new MenuButton { Text = "Insert ▾", FocusMode = Control.FocusModeEnum.None, TooltipText = "Examples fitted to this kind; each goes in working, beside what the script has" };
        Insert.AboutToPopup += Fill;
        Form = new ScriptHelperForm(kind, () => _edit.Text, Put, labels) { Visible = false };
        Helper = new Button { Text = "Helper", ToggleMode = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Builds a piece of script from When and Do" };
        Helper.Toggled += on =>
        {
            Form.Visible = on;
            if (on) Form.Refresh();
        };
    }

    public void AddTo(Container tools)
    {
        tools.AddChild(Insert);
        tools.AddChild(Helper);
    }

    public bool Shown
    {
        set
        {
            Insert.Visible = value;
            Helper.Visible = value;
            if (!value) Helper.ButtonPressed = false;
        }
    }

    void Fill()
    {
        var pop = Insert.GetPopup();
        foreach (var old in pop.GetChildren().OfType<PopupMenu>()) old.QueueFree();
        pop.Clear();
        if (_kind() is not { } k) return;
        var examples = ScriptKit.Examples;
        foreach (var group in Enumerable.Range(0, examples.Count).GroupBy(i => examples[i].Group))
        {
            var sub = new PopupMenu();
            foreach (var i in group)
            {
                sub.AddItem(examples[i].Name, i);
                if (examples[i].Missing(k) is not { } why) continue;
                sub.SetItemDisabled(sub.ItemCount - 1, true);
                sub.SetItemTooltip(sub.ItemCount - 1, why);
            }
            sub.IdPressed += id => Put(ScriptKit.Fit(examples[(int)id], k, _edit.Text));
            pop.AddSubmenuNodeItem(group.Key, sub);
        }
    }

    public void Put(KitPart part)
    {
        if (!_edit.Editable) return;
        var before = _edit.Text.Replace("\r", "");
        var (text, refused) = ScriptKit.Add(before, part);
        if (refused != null) { _said(refused + ": add this one by hand"); return; }
        _said("");
        int first = FirstChange(before, text);
        _edit.BeginComplexOperation();
        _edit.SelectAll();
        _edit.InsertTextAtCaret(text);
        _edit.EndComplexOperation();
        _edit.SetCaretLine(first);
        _edit.SetCaretColumn(0);
        _edit.CenterViewportToCaret();
        _edit.GrabFocus();
    }

    static int FirstChange(string a, string b)
    {
        var x = a.Split('\n');
        var y = b.Split('\n');
        int i = 0;
        while (i < x.Length && i < y.Length && x[i] == y[i]) i++;
        return Math.Min(i, y.Length - 1);
    }
}

public sealed partial class ScriptHelperForm : VBoxContainer
{
    const int PreviewHeight = 120, NameLetters = 40;
    static readonly string[] Places = ["Top", "Middle", "Bottom"];

    readonly Func<KitKind?> _kind;
    readonly Func<string> _script;
    readonly Action<KitPart> _add;
    readonly OptionButton _when = new(), _place = new(), _do = new(), _counter = new(), _state = new(), _line = new();
    readonly LineEdit _label = new() { PlaceholderText = "Rally", MaxLength = NameLetters };
    readonly LineEdit _text = new() { PlaceholderText = "{name} holds", TooltipText = "{name} stands for the piece's name" };
    readonly LineEdit _choices = new() { PlaceholderText = "Advance, Hold, Retire", TooltipText = "Separated by commas" };
    readonly SpinBox _amount = new() { MinValue = -999, MaxValue = 999, Value = 1 };
    readonly SpinBox _dice = new() { MinValue = 1, MaxValue = 20, Value = 1 };
    readonly SpinBox _sides = new() { MinValue = 2, MaxValue = 100, Value = 6 };
    readonly ColorPickerButton _colour = new() { Color = new Color("E0B040"), EditAlpha = false, CustomMinimumSize = Ui.Px(48, 24) };
    readonly CodeEdit _preview = new() { Editable = false, GuttersDrawLineNumbers = false, CustomMinimumSize = Ui.Px(0, PreviewHeight), WrapMode = TextEdit.LineWrappingMode.None };
    readonly Dictionary<string, Control[]> _rows = new();
    KitKind? _shown;

    public ScriptHelperForm(Func<KitKind?> kind, Func<string> script, Action<KitPart> add, int labels)
    {
        _kind = kind;
        _script = script;
        _add = add;
        AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Tight));
        foreach (var t in ScriptHelper.Triggers) _when.AddItem(t.Name);
        foreach (var a in ScriptHelper.Actions) _do.AddItem(a.Name);
        foreach (var p in Places) _place.AddItem(p);
        _place.Selected = (int)HelperPlace.Bottom;
        _preview.SyntaxHighlighter = ScriptAssist.Lua();
        _preview.AddThemeFontSizeOverride("font_size", Ui.Px(11));
        var form = Ui.Form(labels);
        Row(form, "When", _when);
        Row(form, "Label", _label);
        Row(form, "Place", _place);
        Row(form, "Do", _do);
        Row(form, "Counter", _counter);
        Row(form, "Amount", _amount);
        Row(form, "State", _state);
        Row(form, "Text line", _line);
        Row(form, "Text", _text);
        Row(form, "Choices", _choices);
        Row(form, "Colour", _colour);
        Row(form, "Dice", _dice);
        Row(form, "Sides", _sides);
        Ui.AlignForm(form);
        AddChild(form);
        AddChild(_preview);
        AddChild(Ui.UnderControls(Ui.Primary("Add to the script", Add, "Goes in beside what the script has; Ctrl+Z takes it out"), labels));
        foreach (var o in new[] { _when, _place, _do, _counter, _state, _line }) o.ItemSelected += _ => Refresh();
        foreach (var e in new[] { _label, _text, _choices }) e.TextChanged += _ => Preview();
        foreach (var s in new[] { _amount, _dice, _sides }) s.ValueChanged += _ => Preview();
        _colour.ColorChanged += _ => Preview();
    }

    void Row(GridContainer form, string name, Control c)
    {
        var l = new Label { Text = name };
        form.AddChild(l);
        form.AddChild(c);
        _rows[name] = [l, c];
    }

    void Rows(string name, bool on)
    {
        foreach (var c in _rows[name]) c.Visible = on;
    }

    public HelperChoice Choice() => new()
    {
        Trigger = _when.Selected, Action = _do.Selected, Label = _label.Text, Place = (HelperPlace)Math.Max(0, _place.Selected),
        Counter = Math.Max(0, _counter.Selected), Amount = (int)_amount.Value, State = Math.Max(0, _state.Selected), TextLine = Math.Max(0, _line.Selected),
        Text = _text.Text, Choices = _choices.Text, Colour = "#" + _colour.Color.ToHtml(false).ToUpperInvariant(), Dice = (int)_dice.Value, Sides = (int)_sides.Value,
    };

    public void Refresh()
    {
        if (_kind() is not { } k) return;
        if (_shown != k)
        {
            Names(_counter, k.Counters);
            Names(_state, k.States);
            Names(_line, k.TextLines);
            _shown = k;
        }
        var trigger = ScriptHelper.Triggers[Math.Max(0, _when.Selected)];
        for (int i = 0; i < ScriptHelper.Actions.Length; i++)
        {
            var why = ScriptHelper.Missing(ScriptHelper.Actions[i], trigger, k);
            _do.SetItemDisabled(i, why != null);
            _do.SetItemTooltip(i, why ?? "");
        }
        if (_do.IsItemDisabled(Math.Max(0, _do.Selected)))
            _do.Selected = Enumerable.Range(0, _do.ItemCount).FirstOrDefault(i => !_do.IsItemDisabled(i));
        var fields = ScriptHelper.Actions[Math.Max(0, _do.Selected)].Fields;
        Rows("Label", trigger.Button || trigger.Row);
        Rows("Place", trigger.Button);
        Rows("Counter", fields.Contains(HelperField.Counter));
        Rows("Amount", fields.Contains(HelperField.Amount));
        Rows("State", fields.Contains(HelperField.State));
        Rows("Text line", fields.Contains(HelperField.TextLine));
        Rows("Text", fields.Contains(HelperField.Text));
        Rows("Choices", fields.Contains(HelperField.Choices));
        Rows("Colour", fields.Contains(HelperField.Colour));
        Rows("Dice", fields.Contains(HelperField.Dice));
        Rows("Sides", fields.Contains(HelperField.Dice));
        Preview();
    }

    static void Names(OptionButton o, IReadOnlyList<string> names)
    {
        int had = o.Selected;
        o.Clear();
        foreach (var n in names) o.AddItem(n);
        if (names.Count > 0) o.Selected = Math.Clamp(had, 0, names.Count - 1);
    }

    void Preview()
    {
        if (_kind() is not { } k) return;
        var c = Choice();
        bool ok = ScriptHelper.Missing(ScriptHelper.Actions[c.Action], ScriptHelper.Triggers[c.Trigger], k) == null;
        _preview.Text = ok ? ScriptKit.Alone(ScriptHelper.Make(c, k, "")) : "";
    }

    void Add()
    {
        if (_kind() is not { } k) return;
        var c = Choice();
        if (ScriptHelper.Missing(ScriptHelper.Actions[c.Action], ScriptHelper.Triggers[c.Trigger], k) != null) return;
        _add(ScriptHelper.Make(c, k, _script()));
    }
}
