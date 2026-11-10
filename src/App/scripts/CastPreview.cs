using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

public partial class CastPreview : MeshInstance3D
{
    const float RaySide = 0.2f, Ease = 0.1f;
    static readonly Color Glass = new(1f, 1f, 1f, 0.4f);

    public Vector3 From { get; private set; }
    public Vector3 To { get; private set; }

    public static CastPreview Show(Node3D scene, ShowCast c)
    {
        var from = At(c.From);
        var to = At(c.To);
        var p = new CastPreview { From = from, To = to, Name = "CastPreview", CastShadow = ShadowCastingSetting.Off, Layers = Table.LiveOnlyLayer };
        p.Mesh = c.Shape switch
        {
            "sphere" => new SphereMesh { Radius = (float)c.Size[0] / 2, Height = (float)c.Size[0] },
            "box" => new BoxMesh { Size = new Vector3((float)c.Size[0], (float)c.Size[1], (float)c.Size[2]) },
            _ => new BoxMesh { Size = Vector3.One * RaySide },
        };
        p.MaterialOverride = new StandardMaterial3D { AlbedoColor = Glass, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        scene.AddChild(p);
        p.GlobalPosition = from;
        if (c.Shape == "box") p.RotationDegrees = new Vector3((float)c.Turn[0], -(float)c.Turn[1], -(float)c.Turn[2]);
        else if (c.Shape == "ray" && (to - from).LengthSquared() > 0 && Mathf.Abs((to - from).Normalized().Dot(Vector3.Up)) < 1 - 1e-4f) p.LookAtFromPosition(from, to, Vector3.Up);
        float seconds = (float)c.Seconds;
        var tween = p.CreateTween();
        tween.TweenInterval(seconds * Ease);
        tween.TweenProperty(p, "global_position", to, seconds * (1 - 2 * Ease)).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenInterval(seconds * Ease);
        tween.TweenCallback(Callable.From(p.QueueFree));
        return p;
    }

    static Vector3 At(double[] v) => Coords.ToWorld(new Vec2(v[0], v[2]), (float)v[1]);
}
