using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class BoundShimmer(TableContext c)
{
    const int Panes = 3;
    const ulong HoldMs = 150;
    const float WideHeights = 1f, TallHeights = 1f, LiftHeights = 0.75f, AheadHeights = 0.3f, Rise = 4f, Fade = 1.6f, SameSpotHeights = 0.8f;

    const string Look = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color = vec4(0.72, 0.86, 1.0, 1.0);
uniform float strength = 0.0;
void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float r = length(p);
    float fall = smoothstep(1.0, 0.0, r);
    fall *= fall;
    float rings = 0.5 + 0.5 * sin(r * 22.0 - TIME * 6.0);
    float grain = 0.5 + 0.5 * sin(UV.x * 90.0 + TIME * 2.0) * sin(UV.y * 90.0 - TIME * 1.5);
    ALBEDO = tint.rgb;
    ALPHA = clamp(fall * (0.12 + 0.3 * rings * grain) * strength, 0.0, 0.5);
}";

    sealed class Pane
    {
        public required MeshInstance3D Node;
        public required ShaderMaterial Shade;
        public float Strength;
        public ulong Touched;
    }

    readonly List<Pane> _panes = new();
    Shader? _shader;

    public void Touch(Vector3 at, Vector3 away, float height)
    {
        var pane = Free(at, height);
        bool flat = Mathf.Abs(away.Y) > 0.7f;
        var facing = flat ? Vector3.Up : new Vector3(away.X, 0, away.Z).Normalized();
        var middle = flat ? at : at + facing * (height * AheadHeights) + Vector3.Up * (height * LiftHeights);
        var basis = flat ? new Basis(Vector3.Right, Mathf.Pi / 2) : Basis.LookingAt(facing, Vector3.Up);
        pane.Node.Transform = new Transform3D(basis * Basis.FromScale(new Vector3(height * WideHeights, height * TallHeights, 1)), middle);
        pane.Node.Visible = true;
        pane.Touched = Time.GetTicksMsec();
    }

    public void Tick(double delta)
    {
        foreach (var pane in _panes)
        {
            if (!pane.Node.Visible) continue;
            float want = Time.GetTicksMsec() - pane.Touched < HoldMs ? 1 : 0;
            pane.Strength = Mathf.MoveToward(pane.Strength, want, (float)delta * (want > pane.Strength ? Rise : Fade));
            pane.Shade.SetShaderParameter("strength", pane.Strength);
            if (pane.Strength <= 0) pane.Node.Visible = false;
        }
    }

    Pane Free(Vector3 at, float height)
    {
        foreach (var pane in _panes)
            if (pane.Node.Visible && pane.Node.Position.DistanceTo(at) < height * SameSpotHeights) return pane;
        foreach (var pane in _panes)
            if (!pane.Node.Visible) return pane;
        if (_panes.Count < Panes) return Made();
        Pane faintest = _panes[0];
        foreach (var pane in _panes)
            if (pane.Strength < faintest.Strength) faintest = pane;
        return faintest;
    }

    Pane Made()
    {
        _shader ??= new Shader { Code = Look };
        var shade = new ShaderMaterial { Shader = _shader };
        var node = new MeshInstance3D { Mesh = new QuadMesh(), MaterialOverride = shade, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        c.Scene.AddChild(node);
        var pane = new Pane { Node = node, Shade = shade };
        _panes.Add(pane);
        return pane;
    }
}
