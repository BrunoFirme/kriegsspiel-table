using System;
using Godot;

namespace KTable.App;

public partial class ZoomBar : PanelContainer
{
    public CameraRig? Rig;

    readonly HSlider _slider;
    readonly SpinBox _typed;
    bool _dragging, _following;

    const float Resting = 0.38f, SliderWidth = 170, FieldWidth = 92, FadeASecond = 6;
    const double Step = 0.1;

    public ZoomBar()
    {
        AddThemeStyleboxOverride("panel", Ui.Panel());
        Modulate = Colors.White with { A = Resting };
        MouseFilter = MouseFilterEnum.Pass;
        TooltipText = "Zoom: how far the eye is from the point it looks at · drag, or type it for the same view every time";
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", Ui.Gap.Group);
        row.AddChild(new Label { Text = "Zoom", SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Pass });
        _slider = new HSlider
        {
            Step = Step, ExpEdit = true, CustomMinimumSize = new Vector2(SliderWidth, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None,
            TooltipText = "Left is closer, right is farther",
        };
        _typed = new SpinBox { Step = Step, Suffix = "u", CustomMinimumSize = new Vector2(FieldWidth, 0), TooltipText = "The distance in table units · type it and press Enter" };
        _slider.DragStarted += () => _dragging = true;
        _slider.DragEnded += _ => _dragging = false;
        _slider.ValueChanged += Asked;
        _typed.ValueChanged += Asked;
        _typed.GetLineEdit().TextSubmitted += _ => _typed.GetLineEdit().ReleaseFocus();
        row.AddChild(_slider);
        row.AddChild(_typed);
        AddChild(row);
    }

    void Asked(double distance)
    {
        if (!_following) Rig?.ZoomTo((float)distance);
    }

    public bool PointedAt => IsVisibleInTree() && GetGlobalRect().HasPoint(Pointer.In(GetViewport()));

    public bool InUse => _dragging || _typed.GetLineEdit().HasFocus();

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
        foreach (var range in new Godot.Range[] { _slider, _typed })
        {
            if (range.MinValue != Rig.MinDistance) range.MinValue = Rig.MinDistance;
            if (range.MaxValue != Rig.MaxDistance) range.MaxValue = Rig.MaxDistance;
        }
        if (!_dragging && Math.Abs(_slider.Value - now) >= Step / 2) _slider.Value = now;
        if (!_typed.GetLineEdit().HasFocus() && Math.Abs(_typed.Value - now) >= Step / 2) _typed.Value = now;
        _following = false;
    }
}
