using Godot;

namespace KTable.App;

sealed class AimMeasure(TableContext c, Walker walk)
{
    const float MarkShare = 0.08f, LiftShare = 0.02f;

    MeshInstance3D? _line, _mark;
    ImmediateMesh? _mesh;

    public string? Reading { get; private set; }

    public void Tick()
    {
        bool on = walk.Walking && walk.Measuring && walk.AimPoint() is { } _;
        if (!on)
        {
            Reading = null;
            if (_line != null) _line.Visible = _mark!.Visible = false;
            return;
        }
        var to = walk.AimPoint()!.Value;
        var from = walk.Feet + Vector3.Up * (walk.Height * LiftShare);
        Reading = Fmt.Distance((to - walk.Feet).Length(), c.S.View);
        Made();
        _mesh!.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        _mesh.SurfaceAddVertex(from);
        _mesh.SurfaceAddVertex(to);
        _mesh.SurfaceEnd();
        _mark!.Position = to;
        _mark.Scale = Vector3.One * (walk.Height * MarkShare);
        _line!.Visible = _mark.Visible = true;
    }

    void Made()
    {
        if (_line != null && GodotObject.IsInstanceValid(_line)) return;
        var glow = FlightFx.Glow(UmpireHue.OfSlot(walk.Colour).Lightened(0.3f));
        _mesh = new ImmediateMesh();
        _line = new MeshInstance3D { Mesh = _mesh, MaterialOverride = glow, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _mark = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.7f, OuterRadius = 1f, Rings = 24, RingSegments = 6 }, MaterialOverride = glow,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        c.Scene.AddChild(_line);
        c.Scene.AddChild(_mark);
    }
}
