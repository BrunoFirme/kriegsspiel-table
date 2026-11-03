using Godot;

namespace KTable.App;

public static class Pointer
{
    static Vector2? _driven;

    public static void Drive(Vector2 at) => _driven = at;

    public static Vector2 In(Viewport vp) => _driven is { } at && vp == vp.GetTree().Root ? at : vp.GetMousePosition();

    public static Vector2 Global(CanvasItem c) => c.GetCanvasTransform().AffineInverse() * In(c.GetViewport());

    public static Vector2 Local(CanvasItem c) => c.GetGlobalTransform().AffineInverse() * Global(c);
}
