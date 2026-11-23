using System;
using Godot;

namespace KTable.App;

public sealed partial class DropFrame : Control
{
    const int OverEverything = 127, FramesBeforeTaking = 2, EdgePx = 4, TagTopPx = 52, TagFontPx = 15;
    const float LeastSeconds = 0.9f, FadeSeconds = 0.3f, PulseSeconds = 1.4f, PulseLow = 0.45f;

    Label _tag = null!;
    Control _tagRow = null!;
    PanelContainer _box = null!;
    int _holds, _waitFrames;
    float _shown, _fading, _pulse;
    Action? _then;

    public string Text => Visible && _tagRow.Visible ? _tag.Text : "";
    public bool Working => Visible && (_holds > 0 || _then != null);

    public static DropFrame On(Node host)
    {
        var layer = new CanvasLayer { Layer = OverEverything, Name = "DropFrame" };
        var frame = new DropFrame { Visible = false };
        layer.AddChild(frame);
        host.AddChild(layer);
        return frame;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = Ui.Theme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _tagRow = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _tagRow.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _tagRow.OffsetTop = Ui.Px(TagTopPx);
        AddChild(_tagRow);
        _box = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        _tagRow.AddChild(_box);
        _tag = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _tag.AddThemeFontOverride("font", Ui.SansStrong());
        _tag.AddThemeFontSizeOverride("font_size", Ui.Px(TagFontPx));
        _box.AddChild(_tag);
    }

    public void Arrive(string label, Action take)
    {
        if (_then is { } earlier) { _then = null; earlier(); }
        Show(label);
        _then = take;
        _waitFrames = FramesBeforeTaking;
    }

    public void Taken(bool taken)
    {
        if (taken) return;
        _tagRow.Visible = false;
        _shown = LeastSeconds;
    }

    public void Keep(string label)
    {
        _holds++;
        Show(label);
    }

    public void Release() => _holds = Math.Max(0, _holds - 1);

    void Show(string label)
    {
        _box.AddThemeStyleboxOverride("panel", Ui.Box(Ui.Accent, 4, 12, 5));
        _tag.AddThemeColorOverride("font_color", Ui.OnAccent);
        _tag.Text = label;
        _tagRow.Visible = true;
        _shown = 0;
        _fading = 0;
        Modulate = Colors.White;
        Visible = true;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        var dt = (float)delta;
        _shown += dt;
        _pulse = (_pulse + dt / PulseSeconds) % 1f;
        if (_then != null && --_waitFrames <= 0)
        {
            var take = _then;
            _then = null;
            take();
        }
        if (Working || _shown < LeastSeconds) { _fading = 0; Modulate = Colors.White; }
        else
        {
            _fading += dt;
            Modulate = Colors.White with { A = Math.Max(0, 1 - _fading / FadeSeconds) };
            if (_fading >= FadeSeconds) Visible = false;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var glow = _holds > 0 ? PulseLow + (1 - PulseLow) * (0.5f + 0.5f * Mathf.Cos(_pulse * Mathf.Tau)) : 1f;
        var edge = new Color(Ui.Accent, glow);
        float w = Ui.Px(EdgePx);
        var s = Size;
        DrawRect(new Rect2(0, 0, s.X, w), edge);
        DrawRect(new Rect2(0, s.Y - w, s.X, w), edge);
        DrawRect(new Rect2(0, w, w, s.Y - 2 * w), edge);
        DrawRect(new Rect2(s.X - w, w, w, s.Y - 2 * w), edge);
    }
}
