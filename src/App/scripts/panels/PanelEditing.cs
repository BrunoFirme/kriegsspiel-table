using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using KTable.Client.Panels;

namespace KTable.App;

sealed partial class PanelView
{
    Control? _editor;
    PanelBox? _editing;
    const string Original = "#original";
    static readonly Paint Caret = new(50 / 255f, 50 / 255f, 50 / 255f, 1), Selection = new(168 / 255f, 206 / 255f, 1, 192 / 255f);

    void Edit(PanelBox owner)
    {
        if (_editing == owner && _editor != null) return;
        if (PanelValues.Bool(owner.Extra.GetValueOrDefault("readonly"))) return;
        _local[PanelFocus.Editing] = Key(owner);
        _local[Original] = Value(owner);
        _local.Remove(PanelFocus.Caret);
        OpenEditor(owner);
    }

    void OpenEditor(PanelBox owner)
    {
        if (_editor != null || !Alive(owner) || owner.Part("Text") is not { Text: { } t } textBox || !Nodes.TryGetValue(textBox, out var host)) return;
        var rules = PanelInputs.Of(owner);
        _editing = owner;
        var color = PanelView.ToColor(t.Color);
        var caret = Tint(owner, "caretcolor") ?? Caret;
        var selection = Tint(owner, "selectioncolor") ?? Selection;
        var field = rules.OneLine ? Line(owner, t, rules, host) : Lines(owner, t, rules);
        field.AddThemeFontOverride("font", PanelMeter.FontOf(t));
        field.AddThemeFontSizeOverride("font_size", Math.Max(1, t.Size));
        field.AddThemeColorOverride("font_color", color);
        field.AddThemeColorOverride("caret_color", ToColor(caret));
        field.AddThemeColorOverride("selection_color", ToColor(selection));
        foreach (var style in new[] { "normal", "focus", "read_only" }) field.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        foreach (Node child in host.GetChildren()) if (child is Label or RichTextLabel) ((Control)child).Visible = false;
        host.AddChild(field);
        _editor = field;
        if (_hooks.Port is { } port) port.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        Callable.From(() => Focus(field, owner)).CallDeferred();
    }

    Paint? Tint(PanelBox owner, string key) => owner.Extra.GetValueOrDefault(key) is { } s && _scene != null ? _scene.Context.TryColor(s) : null;

    Control Line(PanelBox owner, TextProps t, InputRules rules, Control host)
    {
        var le = new LineEdit
        {
            Text = Value(owner), MaxLength = rules.Limit, Secret = rules.Secret, SecretCharacter = rules.Mask.ToString(), Flat = true, ContextMenuEnabled = false, CaretBlink = true,
            Alignment = ((int)t.Alignment % 3) switch { 0 => HorizontalAlignment.Left, 2 => HorizontalAlignment.Right, _ => HorizontalAlignment.Center },
        };
        float line = PanelMeter.FontOf(t).GetHeight(Math.Max(1, t.Size));
        int down = (int)t.Alignment / 3;
        le.Position = new Vector2(0, down == 0 ? 0 : down == 2 ? host.Size.Y - line : (host.Size.Y - line) / 2);
        le.Size = new Vector2(host.Size.X, line);
        le.TextChanged += typed => Changed(owner, le, typed, rules);
        return le;
    }

    Control Lines(PanelBox owner, TextProps t, InputRules rules)
    {
        var te = new TextEdit { Text = Value(owner), WrapMode = TextEdit.LineWrappingMode.Boundary, ContextMenuEnabled = false, CaretBlink = true, ScrollFitContentHeight = false };
        te.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        te.TextChanged += () => Changed(owner, te, te.Text, rules);
        return te;
    }

    void Focus(Control field, PanelBox owner)
    {
        if (!GodotObject.IsInstanceValid(field) || !field.IsInsideTree()) return;
        field.GrabFocus();
        bool fresh = !_local.ContainsKey(PanelFocus.Caret);
        int caret = fresh ? int.MaxValue : PanelValues.Int(_local[PanelFocus.Caret]);
        if (field is LineEdit le)
        {
            le.CaretColumn = Math.Min(caret, le.Text.Length);
            if (fresh) le.SelectAll();
        }
        else if (field is TextEdit te)
        {
            if (fresh) te.SelectAll();
            else te.SetCaretColumn(Math.Min(caret, te.GetLine(te.GetCaretLine()).Length));
        }
    }

    void Changed(PanelBox owner, Control field, string typed, InputRules rules)
    {
        var ok = PanelTyping.Valid(typed, rules.Validation);
        if (rules.Limit > 0 && ok.Length > rules.Limit) ok = ok[..rules.Limit];
        if (ok != typed) Put(field, ok);
        int caret = field is LineEdit le ? le.CaretColumn : field is TextEdit te ? te.GetCaretColumn() : 0;
        _local[PanelFocus.Caret] = caret.ToString(CultureInfo.InvariantCulture);
        if (owner.Part("Placeholder") is { } ph && Nodes.TryGetValue(ph, out var pn) && GodotObject.IsInstanceValid(pn)) pn.Visible = ok.Length == 0;
        if (Value(owner) == ok) return;
        _local[Key(owner)] = ok;
        Fire(owner, "onValueChanged", ok);
    }

    static void Put(Control field, string text)
    {
        if (field is LineEdit le)
        {
            int at = Math.Min(le.CaretColumn, text.Length);
            le.Text = text;
            le.CaretColumn = at;
        }
        else if (field is TextEdit te)
        {
            int line = te.GetCaretLine(), column = te.GetCaretColumn();
            te.Text = text;
            te.SetCaretLine(Math.Min(line, te.GetLineCount() - 1));
            te.SetCaretColumn(column);
        }
    }

    bool Typing(InputEventKey e)
    {
        if (_editing is not { } owner || _editor is not { } field || !GodotObject.IsInstanceValid(field)) return false;
        var rules = PanelInputs.Of(owner);
        if (e.Keycode == Godot.Key.Escape)
        {
            EndEdit(true);
            return true;
        }
        if (e.Keycode is Godot.Key.Enter or Godot.Key.KpEnter && !e.Echo)
        {
            bool submits = !string.IsNullOrEmpty(owner.Element?.Get("onSubmit"));
            if (rules.EnterSubmits) EndEdit(false);
            if (submits) Fire(owner, "onSubmit", Value(owner));
            if (rules.EnterSubmits) return true;
        }
        if (_hooks.Port is not { } port || !GodotObject.IsInstanceValid(port)) return false;
        port.PushInput(e);
        return true;
    }

    public void EndEdit(bool cancel)
    {
        if (_editing is not { } owner) return;
        var field = _editor;
        _editor = null;
        _editing = null;
        _local.Remove(PanelFocus.Editing);
        _local.Remove(PanelFocus.Caret);
        var was = _local.GetValueOrDefault(Original);
        _local.Remove(Original);
        if (cancel && was != null && Value(owner) != was)
        {
            _local[Key(owner)] = was;
            Fire(owner, "onValueChanged", was);
        }
        Fire(owner, "onEndEdit", Value(owner));
        if (field != null && GodotObject.IsInstanceValid(field)) field.QueueFree();
        if (_hooks.Port is { } port && GodotObject.IsInstanceValid(port)) port.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        Redraw();
    }
}
