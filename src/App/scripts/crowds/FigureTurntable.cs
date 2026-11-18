using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class FigureTurntable : SubViewportContainer
{
    const float TurnPerSecond = 0.6f, Fov = 28, HatRoom = 1.12f, Margin = 0.55f;

    readonly SubViewport _port = new() { OwnWorld3D = true, TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible };
    readonly Node3D _turn = new();
    readonly Camera3D _cam = new() { Fov = Fov };
    MeshInstance3D? _figure;

    public FigureTurntable(Vector2 size)
    {
        Stretch = true;
        CustomMinimumSize = size;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddChild(_port);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.55f,
        };
        _port.AddChild(new WorldEnvironment { Environment = env });
        _port.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-38, 28, 0), LightEnergy = 1.1f });
        _port.AddChild(_turn);
        _port.AddChild(_cam);
    }

    public void Show(CrowdLook look)
    {
        _figure?.QueueFree();
        _figure = CrowdFigures.Model(look, 1, false);
        _turn.AddChild(_figure);
        float tall = (float)look.HeightM * HatRoom;
        _cam.Position = new Vector3(0, tall * 0.5f, tall * Margin / Mathf.Tan(Mathf.DegToRad(Fov) * 0.5f));
    }

    public override void _Process(double delta) => _turn.RotateY((float)delta * TurnPerSecond);
}
