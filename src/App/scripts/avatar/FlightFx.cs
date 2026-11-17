using Godot;

namespace KTable.App;

static class FlightFx
{
    public const float TrailFrom = 5f, BurstFrom = 9f, AirShare = 0.4f;
    const float MoteShare = 0.035f, SpreadShare = 0.22f;
    const int AirMotes = 14;
    static readonly Color Air = new(1, 1, 1, 0.16f);

    public static GpuParticles3D Trail(float h) => Motes(Air, h, AirMotes, false);

    public static Resource[] Warmed()
    {
        var motes = Motes(Colors.White, 1, 1, true);
        Resource[] looks = [motes.ProcessMaterial, ((QuadMesh)motes.DrawPass1).Material, Glow(Colors.White)];
        motes.Free();
        return looks;
    }

    public static GpuParticles3D Motes(Color colour, float h, int amount, bool burst)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = h * SpreadShare,
            Direction = Vector3.Up, Spread = 180, InitialVelocityMin = h * (burst ? 1.2f : 0.15f), InitialVelocityMax = h * (burst ? 2.4f : 0.45f),
            Gravity = Vector3.Up * (h * 0.25f), DampingMin = h * 0.5f, DampingMax = h, ScaleMin = 0.6f, ScaleMax = 1.2f, ColorRamp = Fade(colour),
        };
        return new GpuParticles3D
        {
            Amount = amount, Lifetime = burst ? 0.5 : 0.9, OneShot = burst, Explosiveness = burst ? 1 : 0, LocalCoords = false, ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One * (h * MoteShare), Material = Glow(Colors.White, billboard: true) }, Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(-Vector3.One * (h * 4), Vector3.One * (h * 8)),
        };
    }

    static GradientTexture1D Fade(Color colour) => new()
    {
        Gradient = new Gradient { Offsets = [0, 0.25f, 1], Colors = [colour.Lightened(0.3f), colour, colour with { A = 0 }] },
    };

    public static StandardMaterial3D Glow(Color colour, bool billboard = false) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        AlbedoColor = colour, VertexColorUseAsAlbedo = billboard, AlbedoTexture = billboard ? Ui.Dot : null,
        BillboardMode = billboard ? BaseMaterial3D.BillboardModeEnum.Particles : BaseMaterial3D.BillboardModeEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}

sealed class FlightView(TableContext c, Walker walk)
{
    const float FastHeights = 6f, FullHeights = 12f, AheadHeights = 3f, BoostEase = 5f;
    const int CameraMotes = 64;
    const string Flash = """
        shader_type canvas_item;
        uniform vec4 tint : source_color = vec4(1.0);
        uniform float strength = 0.0;
        uniform float boost = 0.0;
        void fragment() {
            vec2 p = UV - vec2(0.5);
            p.x *= SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
            float r = length(p) * 1.6;
            float edge = smoothstep(0.45, 1.05, r);
            float wave = exp(-pow((r - (1.0 - strength) * 1.1) * 7.0, 2.0));
            float a = edge * (0.3 * boost + 0.45 * strength) + wave * 0.35 * strength;
            COLOR = vec4(tint.rgb, clamp(a, 0.0, 0.8));
        }
        """;

    GpuParticles3D? _motes;
    CanvasLayer? _layer;
    ShaderMaterial? _shade;
    Color _colour;
    float _height, _boost;

    public void Tick(double delta)
    {
        bool on = walk.Walking && walk.Flying;
        if (!on && _layer == null) return;
        Made();
        _boost = Mathf.Lerp(_boost, walk.Boosting ? 1 : 0, 1 - Mathf.Exp(-(float)delta * BoostEase));
        _shade!.SetShaderParameter("strength", walk.Flash);
        _shade.SetShaderParameter("boost", on ? _boost : 0);
        _layer!.Visible = on && (walk.Flash > 0 || _boost > 0.01f);
        _motes!.Emitting = on && !walk.ThirdPerson && walk.Speed > FastHeights;
        _motes.SpeedScale = Mathf.Clamp(walk.Speed / FullHeights, 0.6f, 1.6f);
    }

    void Made()
    {
        var colour = UmpireHue.OfSlot(walk.Colour);
        if (_layer != null && GodotObject.IsInstanceValid(_layer) && colour == _colour && Mathf.IsEqualApprox(_height, walk.Height)) return;
        (_colour, _height) = (colour, walk.Height);
        _layer?.QueueFree();
        _motes?.QueueFree();
        _shade = new ShaderMaterial { Shader = new Shader { Code = Flash } };
        _shade.SetShaderParameter("tint", colour.Lightened(0.3f));
        var cover = new ColorRect { Material = _shade, MouseFilter = Control.MouseFilterEnum.Ignore };
        cover.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer = new CanvasLayer { Layer = 0, Visible = false };
        _layer.AddChild(cover);
        c.Scene.AddChild(_layer);
        float h = walk.Height;
        _motes = FlightFx.Motes(colour, h, CameraMotes, false);
        _motes.LocalCoords = true;
        _motes.Lifetime = 0.35;
        if (_motes.ProcessMaterial is ParticleProcessMaterial streak)
        {
            streak.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box;
            streak.EmissionBoxExtents = new Vector3(h * 2, h * 1.2f, h);
            streak.Direction = Vector3.Back;
            streak.Spread = 4;
            streak.Gravity = Vector3.Zero;
            streak.InitialVelocityMin = h * 8;
            streak.InitialVelocityMax = h * 12;
            streak.DampingMin = streak.DampingMax = 0;
        }
        _motes.Position = Vector3.Forward * (h * AheadHeights);
        c.Rig.Cam.AddChild(_motes);
    }
}
