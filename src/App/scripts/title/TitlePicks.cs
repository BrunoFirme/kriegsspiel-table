using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using static KTable.App.TitleStyle;

namespace KTable.App;

sealed class TitlePicks
{
    readonly Picking<string> _pick = new();
    readonly List<(string key, PanelContainer box)> _boxes = new();
    readonly HBoxContainer _bar = new() { Visible = false };
    readonly Label _count = new() { Modulate = Muted, VerticalAlignment = VerticalAlignment.Center };
    readonly List<(Button button, Func<int, string> text)> _actions = new();

    internal static TitlePicks? Latest { get; private set; }

    internal IReadOnlyList<PanelContainer> Boxes => _boxes.Select(b => b.box).ToList();

    public TitlePicks()
    {
        Latest = this;
        _bar.AddThemeConstantOverride("separation", 8);
        _bar.AddChild(_count);
        var clear = new Button { Text = "Clear", Flat = true, FocusMode = Control.FocusModeEnum.None };
        clear.AddThemeColorOverride("font_color", Muted);
        clear.Pressed += Clear;
        _bar.AddChild(clear);
    }

    public Control Bar => _bar;

    public IReadOnlyList<string> Picked => _pick.Picked;

    public int Count => _pick.Count;

    public Action? Changed { get; set; }

    public void Act(Func<int, string> text, Action<IReadOnlyList<string>> run, bool primary = false)
    {
        var b = primary ? Primary(text(2), () => run(Picked)) : new Button { Text = text(2), FocusMode = Control.FocusModeEnum.None };
        if (!primary) b.Pressed += () => run(Picked);
        _bar.AddChild(b);
        _bar.MoveChild(b, _bar.GetChildCount() - 2);
        _actions.Add((b, text));
    }

    public void Add(string key, PanelContainer box)
    {
        _boxes.Add((key, box));
        _pick.Show(_boxes.Select(b => b.key));
    }

    public bool Click(string key, InputEventMouseButton mb)
    {
        bool toggle = mb.CtrlPressed || mb.MetaPressed, range = mb.ShiftPressed;
        _pick.Click(key, toggle, range);
        Paint();
        return toggle || range;
    }

    public bool ForMenu(string key)
    {
        _pick.PickForMenu(key);
        Paint();
        return _pick.Count > 1;
    }

    public void Clear()
    {
        _pick.Clear();
        Paint();
    }

    void Paint()
    {
        foreach (var (key, box) in _boxes)
            if (GodotObject.IsInstanceValid(box)) box.AddThemeStyleboxOverride("panel", CardStyle(_pick.Has(key)));
        int n = _pick.Count;
        _bar.Visible = n > 1;
        _count.Text = $"{n} picked";
        foreach (var (button, text) in _actions) button.Text = text(n);
        Changed?.Invoke();
    }

    public void Menu(Node parent, Vector2 at)
    {
        var pm = new PopupMenu();
        for (int i = 0; i < _actions.Count; i++) pm.AddItem(_actions[i].button.Text, i);
        pm.IdPressed += id => _actions[(int)id].button.EmitSignal(BaseButton.SignalName.Pressed);
        pm.PopupHide += () => pm.QueueFree();
        parent.AddChild(pm);
        pm.Popup(new Rect2I((Vector2I)at.Round(), Vector2I.Zero));
    }

    public static PopupMenu FolderMenu(Library lib, Action<string> into)
    {
        var pm = new PopupMenu();
        pm.AddItem("Unfiled", 0);
        for (int i = 0; i < lib.Folders.Count; i++) pm.AddItem(lib.Folders[i], i + 1);
        pm.IdPressed += id => into(id == 0 ? "" : lib.Folders[(int)id - 1]);
        pm.PopupHide += () => pm.QueueFree();
        return pm;
    }
}
