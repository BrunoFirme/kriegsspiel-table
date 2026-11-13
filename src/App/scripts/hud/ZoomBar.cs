using System;
using System.Globalization;
using Godot;

namespace KTable.App;

public partial class ZoomBar : PanelContainer
{
    public CameraRig? Rig;

    readonly HSlider _slider;
    readonly LineEdit _typed;
    bool _dragging, _following;

    const float Resting = 0.3f, SliderWidth = 76, FieldWidth = 40, FadeASecond = 6;
    const int Margin = 4, Lettering = 11;
    const double Step = 0.1;
    const string Shown = "0.#";

    public ZoomBar()
    {
        var box = Ui.Panel();
        box.ContentMarginLeft = box.ContentMarginRight = Margin;
        box.ContentMarginTop = box.ContentMarginBottom = 0;
        AddThemeStyleboxOverride("panel", box);
        Modulate = Colors.White with { A = Resting };
        MouseFilter = MouseFilterEnum.Pass;
        TooltipText = "Zoom: how far the eye is from the point it looks at, in table units · drag, or type it for the same view every time";
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", Ui.Gap.Tight);
        _slider = new HSlider
        {
            Step = Step, ExpEdit = true, CustomMinimumSize = new Vector2(SliderWidth, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None,
            TooltipText = "Zoom · left is closer, right is farther",
        };
        _typed = new LineEdit
        {
            Flat = true, Alignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(FieldWidth, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SelectAllOnFocus = true, ContextMenuEnabled = false, TooltipText = "Zoom in table units · type it and press Enter",
        };
        _typed.AddThemeFontSizeOverride("font_size", Lettering);
        _typed.AddThemeConstantOverride("minimum_character_width", 0);
        _slider.DragStarted += () => _dragging = true;
        _slider.DragEnded += _ => _dragging = false;
        _slider.ValueChanged += Dragged;
        _typed.TextSubmitted += Take;
        _typed.FocusExited += () => Say(Rig?.ZoomGoal ?? 0);
        row.AddChild(_slider);
        row.AddChild(_typed);
        AddChild(row);
    }

    void Dragged(double distance)
    {
        if (!_following) Rig?.ZoomTo((float)distance);
    }

    void Take(string text)
    {
        if (double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var distance) && distance > 0) Rig?.ZoomTo((float)distance);
        _typed.ReleaseFocus();
    }

    void Say(double distance)
    {
        var said = distance.ToString(Shown, CultureInfo.InvariantCulture);
        if (_typed.Text != said) _typed.Text = said;
    }

    public bool PointedAt => IsVisibleInTree() && GetGlobalRect().HasPoint(Pointer.In(GetViewport()));

    public bool InUse => _dragging || _typed.HasFocus();

    public override void _Process(double delta)
    {
        if (Rig == null || !IsVisibleInTree()) return;
        float wanted = PointedAt || InUse ? 1 : Resting;
        if (!Mathf.IsEqualApprox(Modulate.A, wanted)) Modulate = Colors.White with { A = Mathf.MoveToward(Modulate.A, wanted, FadeASecond * (float)delta) };
        Follow();
    }

    public void Follow()
    {
        if (Rig == null) return;
        double now = Rig.ZoomGoal;
        _following = true;
        if (_slider.MinValue != Rig.MinDistance) _slider.MinValue = Rig.MinDistance;
        if (_slider.MaxValue != Rig.MaxDistance) _slider.MaxValue = Rig.MaxDistance;
        if (!_dragging && Math.Abs(_slider.Value - now) >= Step / 2) _slider.Value = now;
        _following = false;
        if (!_typed.HasFocus()) Say(now);
    }
}
