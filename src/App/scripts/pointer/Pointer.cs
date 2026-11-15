using Godot;

namespace KTable.App;

public static class Pointer
{
    static Vector2? _driven, _seen;
    static Window? _watched;

    public static void Drive(Vector2 at) => _driven = at;

    public static void Watch(Window root)
    {
        _watched = root;
        root.WindowInput += e => { if (e is InputEventMouse m) _seen = m.Position; };
    }

    public static Vector2 In(Viewport vp)
    {
        if (vp != vp.GetTree().Root) return vp.GetMousePosition();
        if (_driven is { } at) return at;
        return vp == _watched && _seen is { } seen ? vp.GetFinalTransform().AffineInverse() * seen : vp.GetMousePosition();
    }

    public static Vector2 Global(CanvasItem c) => c.GetCanvasTransform().AffineInverse() * In(c.GetViewport());

    public static Vector2 Local(CanvasItem c) => c.GetGlobalTransform().AffineInverse() * Global(c);
}
