using Godot;

namespace KTable.App;

sealed partial class SightLens : ColorRect
{
    const float GroundHeight = 0.022f, RayLength = 1f;

    readonly Camera3D _camera;

    public ShaderMaterial Glass { get; }

    public SightLens(Camera3D camera, ShaderMaterial glass)
    {
        _camera = camera;
        Glass = glass;
        Material = glass;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetAnchorsPreset(LayoutPreset.FullRect);
        glass.SetShaderParameter("ground", GroundHeight);
    }

    public static ShaderMaterial NewGlass() => new() { Shader = new Shader { Code = FileAccess.GetFileAsString("res://shaders/sighting_blur.gdshader") } };

    public override void _EnterTree() => RenderingServer.FramePreDraw += Aim;

    public override void _ExitTree() => RenderingServer.FramePreDraw -= Aim;

    public void Aim()
    {
        if (!Visible || !IsInstanceValid(_camera) || !_camera.IsInsideTree()) return;
        var size = _camera.GetViewport().GetVisibleRect().Size;
        Vector2 corner = Vector2.Zero, across = new(size.X, 0), down = new(0, size.Y);
        float near = _camera.Near, far = near + RayLength;
        Vector3 nearCorner = _camera.ProjectPosition(corner, near), farCorner = _camera.ProjectPosition(corner, far);
        Glass.SetShaderParameter("near_corner", nearCorner);
        Glass.SetShaderParameter("near_across", _camera.ProjectPosition(across, near) - nearCorner);
        Glass.SetShaderParameter("near_down", _camera.ProjectPosition(down, near) - nearCorner);
        Glass.SetShaderParameter("far_corner", farCorner);
        Glass.SetShaderParameter("far_across", _camera.ProjectPosition(across, far) - farCorner);
        Glass.SetShaderParameter("far_down", _camera.ProjectPosition(down, far) - farCorner);
    }
}
