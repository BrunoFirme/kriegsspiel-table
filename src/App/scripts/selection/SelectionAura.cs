using System;
using Godot;

namespace KTable.App;

public partial class SelectionAura : Node
{
    public const uint MaskLayer = 1u << 1;
    public const uint HoverLayer = 1u << 5;

    public bool Active { get; set; }
    public static readonly Color Picked = new(1f, 0.88f, 0.2f, 1f);
    public Color Color = Picked;

    public static readonly Color Casing = new(0.08f, 0.07f, 0.06f, 0.9f);
    static readonly Color LightCasing = new(0.96f, 0.94f, 0.88f, 0.9f);
    const float DarkColour = 0.3f;

    public static Color CasingFor(Color c) => c.Luminance < DarkColour ? LightCasing : Casing;

    readonly Camera3D _main;
    readonly uint _mask;
    SubViewport _vp = null!;
    Camera3D _cam = null!;
    ColorRect _rect = null!;
    ShaderMaterial _mat = null!;

    public SelectionAura(Camera3D main, uint mask = MaskLayer, Color? color = null)
    {
        _main = main;
        _mask = mask;
        if (color is { } c) Color = c;
    }

    const string Code = """
        shader_type canvas_item;
        uniform sampler2D mask : filter_nearest;
        uniform vec4 color : source_color = vec4(1.0, 0.88, 0.2, 1.0);
        uniform vec4 casing : source_color = vec4(0.08, 0.07, 0.06, 0.9);
        uniform float width = 1.5;     // outline thickness in pixels
        uniform float casing_width = 1.0;
        void fragment() {
            vec2 px = 1.0 / vec2(textureSize(mask, 0));
            float inside = texture(mask, UV).a;
            float near = 0.0;
            float cased = 0.0;
            for (int i = 0; i < 16; i++) {
                float a = 6.2831853 * float(i) / 16.0;
                vec2 d = vec2(cos(a), sin(a)) * px;
                for (float r = 1.0; r <= width + casing_width; r += 1.0) {
                    float m = texture(mask, UV + d * r).a;
                    cased = max(cased, m);
                    if (r <= width) near = max(near, m);
                }
            }
            // outside the piece, within `width` pixels of it: the line. Hard edges, no falloff.
            float outside = 1.0 - step(0.5, inside);
            float line = step(0.5, near) * outside;
            float edge = step(0.5, cased) * outside;
            COLOR = vec4(mix(casing.rgb, color.rgb, line), mix(casing.a, color.a, line) * edge);
        }
        """;

    public override void _Ready()
    {
        _vp = new SubViewport
        {
            Name = "SelectionMask", TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Msaa3D = Viewport.Msaa.Disabled, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled, PositionalShadowAtlasSize = 0,
            World3D = GetViewport().FindWorld3D(),
        };
        AddChild(_vp);
        _cam = new Camera3D { CullMask = _mask };
        _vp.AddChild(_cam);
        var layer = new CanvasLayer { Layer = -1 };
        AddChild(layer);
        _mat = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _mat.SetShaderParameter("color", Color);
        _mat.SetShaderParameter("casing", CasingFor(Color));
        _rect = new ColorRect { Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_rect);
        GetViewport().SizeChanged += Resize;
        Resize();
        ProcessPriority = 100;
    }

    void Resize()
    {
        var s = GetViewport().GetVisibleRect().Size;
        _vp.Size = new Vector2I(Math.Max(2, (int)s.X), Math.Max(2, (int)s.Y));
        _mat.SetShaderParameter("mask", _vp.GetTexture());
    }

    public override void _Process(double delta)
    {
        _rect.Visible = Active;
        _vp.RenderTargetUpdateMode = Active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (!Active || _main == null) return;
        _cam.Projection = _main.Projection;
        _cam.Fov = _main.Fov;
        _cam.Size = _main.Size;
        _cam.Near = _main.Near;
        _cam.Far = _main.Far;
        _cam.KeepAspect = _main.KeepAspect;
        _cam.GlobalTransform = _main.GlobalTransform;
    }
}
