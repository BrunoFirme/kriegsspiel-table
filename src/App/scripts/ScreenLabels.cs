using System;
using System.Collections.Generic;
using Godot;

namespace KTable.App;

public partial class ScreenLabels : CanvasLayer
{
    sealed class Entry
    {
        public Vector3 World;
        public Vector2 Offset;
        public required Label Label;
        public bool Centered;
    }

    readonly Dictionary<string, Entry> _entries = new();
    readonly Func<Camera3D?> _cam;
    Control _root = null!;

    public ScreenLabels(Func<Camera3D?> cam) { _cam = cam; Layer = 0; }

    public override void _Ready()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
    }

    public void Set(string key, Vector3 world, string text, Color color, int fontSize = 13, Vector2 offset = default, bool centered = true)
    {
        if (!_entries.TryGetValue(key, out var e))
        {
            var l = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeConstantOverride("outline_size", 5);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            _root.AddChild(l);
            _entries[key] = e = new Entry { Label = l };
        }
        e.World = world;
        e.Offset = offset;
        e.Centered = centered;
        if (e.Label.Text != text) { e.Label.Text = text; e.Label.ResetSize(); }
        e.Label.AddThemeFontSizeOverride("font_size", fontSize);
        e.Label.AddThemeColorOverride("font_color", color);
        Place(e);
    }

    public string? TextOf(string key) => _entries.TryGetValue(key, out var e) ? e.Label.Text : null;

    public Rect2? RectOf(string key) => _entries.TryGetValue(key, out var e) && e.Label.Visible ? new Rect2(e.Label.Position, e.Label.Size) : null;

    public void Remove(string key)
    {
        if (_entries.Remove(key, out var e)) e.Label.QueueFree();
    }

    public void RemoveWhere(Func<string, bool> pred)
    {
        foreach (var k in new List<string>(_entries.Keys)) if (pred(k)) Remove(k);
    }

    public override void _Process(double delta)
    {
        foreach (var e in _entries.Values) Place(e);
    }

    void Place(Entry e)
    {
        var cam = _cam();
        if (cam == null || cam.IsPositionBehind(e.World)) { e.Label.Visible = false; return; }
        var p = cam.UnprojectPosition(e.World) + e.Offset;
        if (e.Centered) p -= e.Label.Size / 2;
        e.Label.Position = p.Round();
        e.Label.Visible = true;
    }
}
