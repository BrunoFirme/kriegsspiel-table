using Godot;

namespace KTable.App;

sealed class PingMarks(TableContext c)
{
    const double Seconds = 3, Pulse = 0.6, FadeLabel = 0.8;
    const float RingFrom = 0.2f, RingTo = 1.2f, LabelAbove = 1.4f, LabelPixel = 0.0008f;

    public void Show(string name, Vector3 at, Color colour, float h)
    {
        var root = new Node3D { Position = at };
        c.Scene.AddChild(root);
        var glow = FlightFx.Glow(colour);
        var ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.8f, OuterRadius = 1f, Rings = 32, RingSegments = 6 }, MaterialOverride = glow,
            Scale = Vector3.One * (h * RingFrom), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var label = new Label3D
        {
            Text = $"{name}\n\u25be", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = true, PixelSize = LabelPixel,
            FontSize = 32, OutlineSize = 10, Modulate = colour.Lightened(0.35f), Position = Vector3.Up * (h * LabelAbove), Layers = Table.LiveOnlyLayer,
        };
        root.AddChild(ring);
        root.AddChild(label);
        var tween = root.CreateTween().SetParallel();
        tween.TweenProperty(ring, Node3D.PropertyName.Scale.ToString(), Vector3.One * (h * RingTo), Pulse).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(glow, BaseMaterial3D.PropertyName.AlbedoColor.ToString(), colour with { A = 0 }, Seconds);
        tween.TweenProperty(label, "modulate:a", 0f, FadeLabel).SetDelay(Seconds - FadeLabel);
        tween.Chain().TweenCallback(Callable.From(root.QueueFree));
    }
}
