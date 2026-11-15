using System.Collections.Generic;
using Godot;

namespace KTable.App;

sealed class PieceCut(PieceNode piece)
{
    const string CutShaderCode = """
        shader_type spatial;
        render_mode cull_disabled;
        uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;
        uniform bool textured;
        uniform vec4 albedo : source_color = vec4(1.0);
        uniform bool vertex_colour;
        uniform float roughness = 1.0;
        uniform float cut_y;
        varying float world_y;
        void vertex() { world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y; }
        void fragment() {
            if (world_y > cut_y) discard;
            vec4 c = albedo;
            if (textured) c *= texture(albedo_texture, UV);
            if (vertex_colour) c *= COLOR;
            ALBEDO = c.rgb;
            ROUGHNESS = roughness;
        }
        """;

    static Shader? _shader;
    List<Material?>? _uncut;
    List<ShaderMaterial>? _cutMats;
    ulong _cutModel;

    public PieceNode Piece => piece;

    public void Set(float? atY)
    {
        if (piece.Model is not { Mesh: { } mesh } model || !GodotObject.IsInstanceValid(model))
        {
            _uncut = null;
            _cutMats = null;
            return;
        }
        if (_uncut != null && _cutModel != model.GetInstanceId())
        {
            _uncut = null;
            _cutMats = null;
        }
        if (atY is not { } y)
        {
            Uncut(model);
            return;
        }
        if (_uncut == null) Open(model, mesh);
        foreach (var m in _cutMats!) m.SetShaderParameter("cut_y", y);
        foreach (var p in piece.Parts) p.Visible = p.GlobalPosition.Y <= y;
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
            if (was is BaseMaterial3D b)
            {
                cut.SetShaderParameter("albedo", b.AlbedoColor);
                cut.SetShaderParameter("textured", b.AlbedoTexture != null);
                if (b.AlbedoTexture != null) cut.SetShaderParameter("albedo_texture", b.AlbedoTexture);
                cut.SetShaderParameter("vertex_colour", b.VertexColorUseAsAlbedo);
                cut.SetShaderParameter("roughness", b.Roughness);
            }
            _cutMats.Add(cut);
            model.SetSurfaceOverrideMaterial(s, cut);
        }
    }

    void Uncut(MeshInstance3D model)
    {
        if (_uncut == null) return;
        for (int s = 0; s < _uncut.Count && s < model.GetSurfaceOverrideMaterialCount(); s++) model.SetSurfaceOverrideMaterial(s, _uncut[s]);
        foreach (var p in piece.Parts) p.Visible = true;
        _uncut = null;
        _cutMats = null;
    }
}
