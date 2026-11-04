using System;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class TableStage
{
    readonly TableContext _c;
    DirectionalLight3D? _sun;
    WorldEnvironment? _env;
    MeshInstance3D _surface = null!;
    MeshInstance3D _gridPlane = null!;
    ShaderMaterial _gridMat = null!;

    public TableStage(TableContext c) { _c = c; }

    public event Action? ShadeChanged;
    public event Action? WindChanged;

    public void BuildLightAndSurface()
    {
        var ceilingLightNearlyStraightDown = new Vector3(-88, 35, 0);
        _sun = new DirectionalLight3D
        {
            RotationDegrees = ceilingLightNearlyStraightDown,
            ShadowEnabled = true, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits, DirectionalShadowMaxDistance = ShadowReachNear,
            DirectionalShadowFadeStart = 0.9f, DirectionalShadowBlendSplits = true,
            ShadowBias = 0.02f, ShadowNormalBias = 0.5f, LightAngularDistance = 0f, ShadowBlur = 1.0f,
        };
        _c.Scene.AddChild(_sun);
        var env = _env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.16f, 0.17f, 0.18f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.72f,
            },
        };
        _c.Scene.AddChild(env);
        ApplyLighting(new Lighting());
        _c.Scene.AddChild(_surface = new MeshInstance3D
        {
            Name = "TableSurface", Mesh = new PlaneMesh { Size = new Vector2(20000, 20000) }, Position = new Vector3(0, -0.04f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.19f, 0.2f, 0.19f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public void BuildGrid()
    {
        _gridMat = new ShaderMaterial { Shader = new Shader { Code = FileAccess.GetFileAsString("res://shaders/grid.gdshader") } };
        _gridPlane = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(20000, 20000) }, MaterialOverride = _gridMat, Position = new Vector3(0, 0.03f, 0), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _c.Scene.AddChild(_gridPlane);
    }

    const float ShadowReachNear = 260, ShadowReachPerDistance = 3, ShadowReachStep = 0.05f;
    bool _shadowsReachFar;

    public void ApplyGraphics(Settings cfg)
    {
        Graphics.ApplyShadows(cfg);
        _shadowsReachFar = Graphics.ShadowsReachFar(cfg);
        if (_env?.Environment is { } e) e.SsaoEnabled = cfg.ContactShade;
    }

    public void FollowView(float distance)
    {
        if (_sun == null) return;
        float reach = _shadowsReachFar ? Mathf.Max(ShadowReachNear, distance * ShadowReachPerDistance) : ShadowReachNear;
        if (Mathf.Abs(reach - _sun.DirectionalShadowMaxDistance) > reach * ShadowReachStep) _sun.DirectionalShadowMaxDistance = reach;
    }

    public Lighting LightNow() => _c.S.View.Lighting.FromClock ? DayLight.At(_c.S.View.Clock, _c.S.View.Lighting) : _c.S.View.Lighting;

    public void ApplyLighting(Lighting l)
    {
        if (_sun == null) return;
        var tint = LightTint((float)l.Warmth);
        _sun.LightColor = tint;
        _sun.LightEnergy = (float)Math.Clamp(l.SunEnergy, 0, 3);
        _sun.ShadowEnabled = l.Shadows && l.SunEnergy > 0.01;
        _sun.Visible = l.SunEnergy > 0.01;
        if (_env?.Environment is { } e) { e.AmbientLightEnergy = (float)Math.Clamp(l.Ambient, 0, 3); e.AmbientLightColor = tint; }
        if (EffectFx.SetShade(l, tint)) ShadeChanged?.Invoke();
        float shade = Mathf.Clamp((float)l.TableShade, 0, 1);
        if (_standIn.on && !Mathf.IsEqualApprox(_standIn.shade, shade)) SetStandIn(true, shade);
    }

    public static Color LightTint(float warmth)
    {
        warmth = Mathf.Clamp(warmth, -1, 1);
        return warmth >= 0 ? Colors.White.Lerp(new Color(1f, 0.86f, 0.70f), warmth) : Colors.White.Lerp(new Color(0.76f, 0.87f, 1f), -warmth);
    }

    public void ApplyGrid()
    {
        var g = _c.S.View.Grid ?? new GridDef();
        int kind = g.Kind switch { "square" => 1, "hex" => 2, _ => 0 };
        _gridPlane.Visible = kind != 0;
        if (_standIn.on && _paperLined != (kind == 0)) SetStandIn(true, _standIn.shade);
        if (kind == 0) return;
        _gridMat.SetShaderParameter("kind", kind);
        _gridMat.SetShaderParameter("pitch", (float)Math.Max(0.05, g.Pitch));
        _gridMat.SetShaderParameter("subdivisions", Math.Clamp(g.Subdivisions, 1, 20));
        _gridMat.SetShaderParameter("line_color", Coords.ParseColor(g.Color, new Color(0.1f, 0.1f, 0.12f, 0.5f)));
        _gridMat.SetShaderParameter("width", (float)Math.Clamp(g.Pitch * 0.006, 0.01, 0.4));
    }

    public void ApplyWind(Wind w)
    {
        if (!EffectFx.SetWind(w)) return;
        WindChanged?.Invoke();
    }

    public void ApplyScale(Calibration c)
    {
        if (!EffectFx.SetScale(c)) return;
        WindChanged?.Invoke();
    }

    (bool on, float shade) _standIn;
    bool _paperLined = true;

    public void SetStandIn(bool on, float shade = 0)
    {
        shade = Mathf.Clamp(shade, 0, 1);
        _standIn = (on, shade);
        bool lines = (_c.S?.View?.Grid?.Kind ?? "none") == "none";
        _paperLined = lines;
        Color Shaded(Color c)
        {
            float l = c.Luminance;
            var grey = new Color(l, l, l) * 0.55f;
            return shade <= 0.5f ? c.Lerp(grey, shade * 2) : grey.Lerp(Colors.Black, (shade - 0.5f) * 2);
        }
        _env!.Environment.BackgroundColor = on ? Shaded(new Color(0.74f, 0.70f, 0.60f)) : new Color(0.16f, 0.17f, 0.18f);
        if (!on)
        {
            _surface.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.19f, 0.2f, 0.19f), Roughness = 1f, Metallic = 0f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled };
            return;
        }
        var paper = Shaded(new Color(0.80f, 0.76f, 0.66f));
        float lineK = 1 - Mathf.Clamp((shade - 0.5f) * 2, 0, 1);
        var faint = paper.Lerp(Shaded(new Color(0.70f, 0.66f, 0.56f)), lineK); var strong = paper.Lerp(Shaded(new Color(0.56f, 0.52f, 0.43f)), lineK);
        _surface.MaterialOverride = PaperMaterial(paper, faint, strong, lines);
    }

    static StandardMaterial3D PaperMaterial(Color paper, Color faint, Color strong, bool lines)
    {
        const int px = 1024, units = 20;
        var img = Image.CreateEmpty(px, px, true, Image.Format.Rgba8);
        img.Fill(paper);
        float cell = px / (float)units;
        for (int i = 0; i < (lines ? units : 0); i++)
        {
            int at = (int)Math.Round(i * cell);
            bool heavy = i % 10 == 0;
            for (int k = 0; k < px; k++)
            {
                img.SetPixel(at, k, heavy ? strong : faint); img.SetPixel(k, at, heavy ? strong : faint);
                if (heavy) { img.SetPixel(Math.Min(px - 1, at + 1), k, strong); img.SetPixel(k, Math.Min(px - 1, at + 1), strong); }
            }
        }
        img.GenerateMipmaps();
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(img), Roughness = 1f, Metallic = 0f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            TextureRepeat = true, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Uv1Scale = new Vector3(20000f / units, 20000f / units, 1),
        };
    }
}
