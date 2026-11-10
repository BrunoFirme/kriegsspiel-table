using Godot;

namespace KTable.App;

static class SelectionRing
{
    const float CasingShare = 0.4f;

    static Shader? _shader;
    static Shader Shader => _shader ??= new Shader
    {
        Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, shadows_disabled;
            uniform sampler2D tex : source_color, filter_linear_mipmap;
            uniform vec4 color : source_color = vec4(1.0, 0.85, 0.1, 1.0);
            uniform vec4 casing : source_color = vec4(0.08, 0.07, 0.06, 1.0);
            uniform bool use_alpha = false;
            uniform float inset = 1.0;
            void fragment() {
                vec2 inner = (UV - 0.5) * inset + 0.5;
                float within = step(0.0, min(inner.x, inner.y)) * step(max(inner.x, inner.y), 1.0);
                float lit = use_alpha ? step(0.5, texture(tex, inner).a) * within : step(0.5, UV.x);
                ALBEDO = mix(casing.rgb, color.rgb, lit);
                ALPHA = use_alpha ? texture(tex, UV).a : 1.0;
                ALPHA_SCISSOR_THRESHOLD = 0.5;
            }
            """,
    };

    public static ShaderMaterial Material(Color c)
    {
        var mat = new ShaderMaterial { Shader = Shader };
        mat.SetShaderParameter("color", c);
        mat.SetShaderParameter("casing", SelectionAura.CasingFor(c));
        return mat;
    }

    public static Mesh Halo(ShaderMaterial mat, Vector2 footprint, float pad, Texture2D? tex)
    {
        float least = Mathf.Max(0.01f, Mathf.Min(footprint.X, footprint.Y));
        float lit = 1 + 2 * pad / least, whole = 1 + 2 * pad * (1 + CasingShare) / least;
        mat.SetShaderParameter("use_alpha", true);
        mat.SetShaderParameter("inset", whole / lit);
        if (tex != null) mat.SetShaderParameter("tex", tex);
        return new PlaneMesh { Size = footprint * whole };
    }

    public static Mesh Bands(string shape, Vector2 footprint, float y)
    {
        float rim = Mathf.Clamp(Mathf.Min(footprint.X, footprint.Y) * 0.05f, 0.025f, 0.2f);
        float casing = rim * CasingShare;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (from, to, colour) in new[] { (0f, casing, false), (casing, casing + rim, true), (casing + rim, casing * 2 + rim, false) })
        {
            var inner = PieceNode.Outline(shape, footprint.X + from * 2, footprint.Y + from * 2);
            var outer = PieceNode.Outline(shape, footprint.X + to * 2, footprint.Y + to * 2);
            st.SetUV(new Vector2(colour ? 1 : 0, 0));
            for (int i = 0; i < inner.Count; i++)
            {
                int j = (i + 1) % inner.Count;
                Vector3 a0 = new(inner[i].X, y, inner[i].Y), a1 = new(outer[i].X, y, outer[i].Y), b0 = new(inner[j].X, y, inner[j].Y), b1 = new(outer[j].X, y, outer[j].Y);
                foreach (var v in new[] { a0, a1, b1, a0, b1, b0 }) st.AddVertex(v);
            }
        }
        return st.Commit();
    }
}
