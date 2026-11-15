using System;
using Godot;
using KTable.Client;

namespace KTable.App;

public static partial class Ui
{
    public const float ScaleStep = 0.05f;
    public const int BaseFontSize = 13;

    public static float Scale { get; private set; } = 1f;

    public static int Px(float design) => (int)MathF.Round(design * Scale, MidpointRounding.AwayFromZero);
    public static float Pxf(float design) => design * Scale;
    public static Vector2 Px(float x, float y) => new(Px(x), Px(y));
    public static Vector2 Px(Vector2 design) => new(Px(design.X), Px(design.Y));
    public static Vector2I Px(Vector2I design) => new(Px(design.X), Px(design.Y));
    public static Vector2I PxI(float x, float y) => new(Px(x), Px(y));

    public static T Scaled<T>(T box) where T : StyleBox
    {
        UiRescale.Box(box, 1f, Scale);
        return box;
    }

    static Theme? _engineTheme;

    static Theme EngineCopy(Theme from)
    {
        var t = new Theme();
        foreach (var type in from.GetConstantTypeList())
            foreach (var name in from.GetConstantList(type)) t.SetConstant(name, type, from.GetConstant(name, type));
        foreach (var type in from.GetFontSizeTypeList())
            foreach (var name in from.GetFontSizeList(type))
                if (from.HasFontSize(name, type)) t.SetFontSize(name, type, from.GetFontSize(name, type));
        foreach (var type in from.GetStyleboxTypeList())
            foreach (var name in from.GetStyleboxList(type))
                if (from.HasStylebox(name, type)) t.SetStylebox(name, type, from.GetStylebox(name, type));
        foreach (var type in from.GetIconTypeList())
            foreach (var name in from.GetIconList(type))
                if (from.GetIcon(name, type) is DpiTexture icon) t.SetIcon(name, type, icon);
        return t;
    }

    static void Restyle()
    {
        _engineTheme ??= EngineCopy(ThemeDB.GetDefaultTheme());
        var e = _engineTheme;
        var t = new Theme();
        foreach (var type in e.GetConstantTypeList())
            foreach (var name in e.GetConstantList(type)) t.SetConstant(name, type, UiRescale.Constant(name, e.GetConstant(name, type), 1f, Scale));
        foreach (var type in e.GetFontSizeTypeList())
            foreach (var name in e.GetFontSizeList(type)) t.SetFontSize(name, type, Px(e.GetFontSize(name, type)));
        foreach (var type in e.GetStyleboxTypeList())
            foreach (var name in e.GetStyleboxList(type)) t.SetStylebox(name, type, Scaled((StyleBox)e.GetStylebox(name, type).Duplicate()));
        foreach (var type in e.GetIconTypeList())
            foreach (var name in e.GetIconList(type))
            {
                var icon = (DpiTexture)e.GetIcon(name, type);
                icon.BaseScale = Scale;
                t.SetIcon(name, type, icon);
            }
        if (_theme == null) _theme = MakeTheme();
        else _theme.MergeWith(MakeTheme());
        t.MergeWith(_theme);
        ThemeDB.FallbackFontSize = Px(BaseFontSize);
        ThemeDB.FallbackBaseScale = Scale;
        ThemeDB.GetDefaultTheme().MergeWith(t);
    }

    static readonly Vector2 LeastRoom = new(1100, 600);
    const float PlainDpi = 96f, WindowsScaleStep = 0.05f;
    static Settings? _scaleFrom;
    static Window? _scaled;

    public static void ApplyScale(Window root, Settings cfg)
    {
        _scaleFrom = cfg;
        if (_scaled != root)
        {
            _scaled = root;
            root.SizeChanged += Rescale;
            Watch(root.GetTree());
        }
        Rescale();
    }

    static SceneTree? _watched;

    internal static void Watch(SceneTree tree)
    {
        if (_watched == tree) return;
        _watched = tree;
        tree.NodeAdded += OnScreen.Watch;
        tree.NodeAdded += UiRescale.Arrived;
    }

    public static float WindowsScale(Window root)
    {
        float dpi = DisplayServer.ScreenGetDpi(root.CurrentScreen);
        return dpi > 0 ? Mathf.Max(Mathf.Snapped(dpi / PlainDpi, WindowsScaleStep), 1f) : 1f;
    }

    public static float Fitted(Window root) => Settings.Fitted(WindowsScale(root), root.Size.Y);

    public static float WantedScale(Window root, Settings cfg) => cfg.UiScaleFor(WindowsScale(root), root.Size.Y);

    public static float ScaleThatFits(Window root, float wanted) => ScaleThatFits((Vector2)root.Size, wanted);

    internal static float ScaleThatFits(Vector2 room, float wanted)
    {
        float fits = Mathf.Max(Mathf.Floor(Mathf.Min(room.X / LeastRoom.X, room.Y / LeastRoom.Y) / ScaleStep + 0.001f) * ScaleStep, Settings.UiScaleMin);
        return Mathf.Clamp(Mathf.Min(wanted, fits), Settings.UiScaleMin, Settings.FittedMost * Settings.UiScaleMax);
    }

    static void Rescale()
    {
        if (_scaled == null || !GodotObject.IsInstanceValid(_scaled)) return;
        if (_scaleFrom == null) return;
        var root = _scaled;
        Callable.From(() => { if (GodotObject.IsInstanceValid(root)) OnScreen.FitAll(root); }).CallDeferred();
        root.ContentScaleFactor = 1f;
        SetScale(ScaleThatFits(root, WantedScale(root, _scaleFrom)), root);
    }

    internal static void SetScale(float scale, Node? shown = null)
    {
        if (_engineTheme != null && Mathf.IsEqualApprox(Scale, scale)) return;
        float was = Scale;
        Scale = scale;
        Restyle();
        if (shown != null && !Mathf.IsEqualApprox(was, scale)) UiRescale.Walk(shown, was, scale);
    }
}
