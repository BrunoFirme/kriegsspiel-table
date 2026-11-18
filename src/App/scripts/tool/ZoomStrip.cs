using System;
using System.Globalization;
using System.Linq;
using Godot;

namespace KTable.App;

public partial class ZoomStrip : VBoxContainer
{
    const float DialStep = 0.001f, TrackLight = 0.3f, FilledLight = 0.9f, DialButtons = 2.6f, LeastDialButtons = 1f, StepButton = 0.7f;
    const int Lettering = 11, UnitLettering = 9, FieldMargin = 1, TrackHalf = 2, TrackRound = 2, NotchesPerStep = 3;
    const double Thousand = 1e3, Million = 1e6, Hundred = 100, Ten = 10;
    const string Tip = "How much of the map the screen takes in, side to side · click to type a width";

    readonly Func<CameraRig?> _rig;
    readonly Func<double, string>? _measure;
    readonly float _button;
    readonly VSlider _dial;
    readonly LineEdit _field;
    readonly Label _unit;
    bool _held;

    internal VSlider Dial => _dial;
    internal LineEdit Field => _field;
    internal Label Unit => _unit;

    public ZoomStrip(Func<CameraRig?> rig, Func<double, string>? measure, float button)
    {
        _rig = rig;
        _measure = measure;
        _button = button;
        AddThemeConstantOverride("separation", Ui.Px(Ui.Gap.Hair));
        AddChild(Step("zoom-in", "Zoom in", CameraRig.WheelNotch));
        _dial = new VSlider
        {
            MinValue = 0, MaxValue = 1, Step = DialStep, SizeFlagsHorizontal = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None, TooltipText = "Zoom · up is closer",
        };
        _dial.AddThemeStyleboxOverride("slider", Track(new Color(1, 1, 1, TrackLight)));
        _dial.AddThemeStyleboxOverride("grabber_area", Track(new Color(Ui.Ink, FilledLight)));
        _dial.AddThemeStyleboxOverride("grabber_area_highlight", Track(Ui.Ink));
        _dial.ValueChanged += v => { if (_rig() is { } r) r.ZoomTo(DistanceAt(r, (float)v)); };
        _dial.DragStarted += () => _held = true;
        _dial.DragEnded += _ => _held = false;
        AddChild(_dial);
        FitDial(0);
        AddChild(Step("zoom-out", "Zoom out", 1 / CameraRig.WheelNotch));
        _field = new LineEdit
        {
            Alignment = HorizontalAlignment.Center, SelectAllOnFocus = true, ContextMenuEnabled = false, FocusMode = FocusModeEnum.Click,
            CustomMinimumSize = Ui.Px(button, 0), TooltipText = Tip,
        };
        var quiet = Ui.Box(new Color(0, 0, 0, 0), 3, FieldMargin, 1);
        _field.AddThemeStyleboxOverride("normal", quiet);
        _field.AddThemeStyleboxOverride("focus", Ui.Box(Ui.Field, 3, FieldMargin, 1));
        _field.AddThemeFontSizeOverride("font_size", Ui.Px(Lettering));
        _field.AddThemeConstantOverride("minimum_character_width", 0);
        _field.TextSubmitted += Take;
        _field.FocusExited += Say;
        AddChild(_field);
        _unit = new Label { HorizontalAlignment = HorizontalAlignment.Center, TooltipText = Tip, MouseFilter = MouseFilterEnum.Pass, ClipText = true };
        _unit.AddThemeFontSizeOverride("font_size", Ui.Px(UnitLettering));
        _unit.AddThemeColorOverride("font_color", Ui.Muted);
        AddChild(_unit);
    }

    Button Step(string kind, string tip, float factor)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = Ui.Px(_button, _button * StepButton) };
        b.AddChild(new ToolIcon { Kind = kind });
        b.Pressed += () => _rig()?.Zoom(Mathf.Pow(factor, NotchesPerStep));
        return Ui.Bare(b);
    }

    static StyleBoxFlat Track(Color fill) =>
        Ui.Scaled(new StyleBoxFlat
        {
            BgColor = fill, CornerRadiusTopLeft = TrackRound, CornerRadiusTopRight = TrackRound, CornerRadiusBottomLeft = TrackRound, CornerRadiusBottomRight = TrackRound,
            ContentMarginLeft = TrackHalf, ContentMarginRight = TrackHalf,
        });

    float DialHigh => Ui.Px(_button * DialButtons);

    float DialLeast => Ui.Px(_button * LeastDialButtons);

    public void FitDial(float given) => _dial.CustomMinimumSize = new Vector2(Ui.Px(_button), Mathf.Max(DialLeast, DialHigh - given));

    public float GiveUp(float over)
    {
        float give = Mathf.Clamp(over, 0, DialHigh - DialLeast);
        FitDial(give);
        return give;
    }

    static float DistanceAt(CameraRig rig, float dialled) => rig.MaxDistance * Mathf.Pow(rig.MinDistance / rig.MaxDistance, dialled);

    static float DialAt(CameraRig rig, float distance) => Mathf.Clamp(Mathf.Log(distance / rig.MaxDistance) / Mathf.Log(rig.MinDistance / rig.MaxDistance), 0, 1);

    public override void _Process(double delta)
    {
        if (_rig() is not { } rig || !IsVisibleInTree()) return;
        if (!_held) _dial.SetValueNoSignal(DialAt(rig, rig.ZoomGoal));
        if (!_field.HasFocus()) Say();
    }

    public float Across => _rig() is not { } rig ? 0 : GetViewportRect().Size.X / Mathf.Max(0.0001f, rig.PixelsPerUnit()) * rig.ZoomGoal / Mathf.Max(0.0001f, rig.Distance);

    (double Value, string Unit) Measured()
    {
        var said = _measure?.Invoke(Across) ?? Across.ToString(CultureInfo.InvariantCulture) + " u";
        int cut = said.LastIndexOf(' ');
        return (Number(cut > 0 ? said[..cut] : said) ?? Across, cut > 0 ? said[(cut + 1)..] : "");
    }

    internal static string Compact(double v) => v switch
    {
        >= Million => (v / Million).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= Ten * Thousand => (v / Thousand).ToString("0", CultureInfo.InvariantCulture) + "k",
        >= Thousand => (v / Thousand).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        >= Hundred => v.ToString("0", CultureInfo.InvariantCulture),
        >= Ten => v.ToString("0.#", CultureInfo.InvariantCulture),
        _ => v.ToString("0.##", CultureInfo.InvariantCulture),
    };

    internal static double? Number(string text, double current = 0) => KTable.Core.Calc.Evaluate(text, current, negatives: false, shorthand: true) is double n && n > 0 ? n : null;

    void Take(string text)
    {
        var (shown, _) = Measured();
        if (_rig() is { } rig && Number(text, shown) is { } wanted && Across > 0 && shown > 0) rig.ZoomTo((float)(rig.ZoomGoal * wanted / shown));
        _field.ReleaseFocus();
        Say();
    }

    void Say()
    {
        if (_rig() == null) return;
        var (value, unit) = Measured();
        var said = Compact(value);
        if (_field.Text != said) _field.Text = said;
        if (_unit.Text != unit) _unit.Text = unit;
    }
}
