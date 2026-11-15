using System.Collections.Generic;
using Godot;

namespace KTable.App;

readonly record struct WallCut(float Low, float Top, bool NearOnly, Vector2 Middle, Vector2 Eye)
{
    public bool Hides(Vector3 at) => at.Y > Top || at.Y > Low && (!NearOnly || (new Vector2(at.X, at.Z) - Middle).Dot(Eye - Middle) > 0);
}

sealed class PieceCut(PieceNode piece)
{
    const string CutShaderCode = """
        shader_type spatial;
        render_mode cull_disabled;
        uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;
        uniform bool textured;
        uniform vec4 albedo : source_color = vec4(1.0);
        uniform bool vertex_colour;
        uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic;
        uniform bool bumped;
        uniform float scissor = 0.0;
        uniform float roughness = 1.0;
        uniform float cut_y;
        uniform float top_y;
        uniform bool near_only;
        uniform vec2 middle;
        uniform vec2 eye;
        varying vec3 world;
        void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
        void fragment() {
            if (world.y > top_y || world.y > cut_y && (!near_only || dot(world.xz - middle, eye - middle) > 0.0)) discard;
            vec4 c = albedo;
            if (textured) c *= texture(albedo_texture, UV);
            if (c.a < scissor) discard;
            if (vertex_colour) c.rgb *= pow(COLOR.rgb, vec3(2.2));
            if (bumped) NORMAL_MAP = texture(normal_texture, UV).rgb;
            ALBEDO = c.rgb;
            ROUGHNESS = roughness;
        }
        """;

    static Shader? _shader;
    List<Material?>? _uncut;
    List<ShaderMaterial>? _cutMats;
    ulong _cutModel;

    public PieceNode Piece => piece;

    public void Set(WallCut? cut)
    {
        if (piece.Model is not { Mesh: { } mesh } model || !GodotObject.IsInstanceValid(model))
        {
            _uncut = null;
            _cutMats = null;
            piece.Hidden = null;
            return;
        }
        if (_uncut != null && _cutModel != model.GetInstanceId())
        {
            _uncut = null;
            _cutMats = null;
        }
        if (cut is not { } c)
        {
            Uncut(model);
            return;
        }
        if (_uncut == null) Open(model, mesh);
        for (int s = 0; s < _cutMats!.Count; s++) Wear(_cutMats[s], _uncut![s]);
        foreach (var m in _cutMats!)
        {
            m.SetShaderParameter("cut_y", c.Low);
            m.SetShaderParameter("top_y", c.Top);
            m.SetShaderParameter("near_only", c.NearOnly);
            m.SetShaderParameter("middle", c.Middle);
            m.SetShaderParameter("eye", c.Eye);
        }
        foreach (var p in piece.Parts) p.Visible = !c.Hides(p.GlobalPosition);
        piece.Hidden = c.Hides;
    }

    void Open(MeshInstance3D model, Mesh mesh)
    {
        _shader ??= new Shader { Code = CutShaderCode };
        _cutModel = model.GetInstanceId();
        _uncut = new();
        _cutMats = new();
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var was = model.GetSurfaceOverrideMaterial(s);
            _uncut.Add(was);
            var cut = new ShaderMaterial { Shader = _shader };
            _cutMats.Add(cut);
            model.SetSurfaceOverrideMaterial(s, cut);
        }
    }

    static void Wear(ShaderMaterial cut, Material? was)
    {
        if (was is not BaseMaterial3D b) return;
        cut.SetShaderParameter("albedo", b.AlbedoColor);
        cut.SetShaderParameter("textured", b.AlbedoTexture != null);
        if (b.AlbedoTexture != null) cut.SetShaderParameter("albedo_texture", b.AlbedoTexture);
        cut.SetShaderParameter("vertex_colour", b.VertexColorUseAsAlbedo);
        cut.SetShaderParameter("roughness", b.Roughness);
        cut.SetShaderParameter("scissor", b.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor ? b.AlphaScissorThreshold : 0f);
        cut.SetShaderParameter("bumped", b.NormalEnabled && b.NormalTexture != null);
        if (b.NormalTexture != null) cut.SetShaderParameter("normal_texture", b.NormalTexture);
    }

    void Uncut(MeshInstance3D model)
    {
        piece.Hidden = null;
        if (_uncut == null) return;
        for (int s = 0; s < _uncut.Count && s < model.GetSurfaceOverrideMaterialCount(); s++) model.SetSurfaceOverrideMaterial(s, _uncut[s]);
        foreach (var p in piece.Parts) p.Visible = true;
        _uncut = null;
        _cutMats = null;
    }
}
