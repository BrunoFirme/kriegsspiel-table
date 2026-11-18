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
            uniform vec4 color : source_color = vec4(1.0, 0.85, 0.1, 1.0);
            uniform vec4 casing : source_color = vec4(0.08, 0.07, 0.06, 1.0);
            void fragment() {
                ALBEDO = mix(casing.rgb, color.rgb, step(0.5, UV.x));
            }
            """,
    };

    static Shader? _haloShader;
    static Shader HaloShader => _haloShader ??= new Shader
    {
        Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, shadows_disabled;
            uniform sampler2D tex : source_color, filter_linear_mipmap;
            uniform sampler2D holes : filter_nearest;
            uniform bool holed = false;
            uniform vec4 color : source_color = vec4(1.0, 0.85, 0.1, 1.0);
            uniform vec4 casing : source_color = vec4(0.08, 0.07, 0.06, 1.0);
            uniform vec2 grown = vec2(1.0);
            uniform vec2 reach = vec2(0.1);
            uniform float lit_share = 0.7;
            const int STEPS = 7;
            const int TURNS = 16;
            bool solid(vec2 p, float lod) {
                if (p.x < 0.0 || p.y < 0.0 || p.x > 1.0 || p.y > 1.0) return false;
                return textureLod(tex, p, lod).a >= 0.5 || holed && textureLod(holes, p, 0.0).r >= 0.5;
            }
            void fragment() {
                vec2 p = (UV - 0.5) * grown + 0.5;
                float lod = textureQueryLod(tex, p).x;
                if (solid(p, lod)) discard;
                float found = 0.0;
                for (int r = 1; r <= STEPS && found == 0.0; r++) {
                    float k = float(r) / float(STEPS);
                    for (int i = 0; i < TURNS; i++) {
                        float a = 6.2831853 * float(i) / float(TURNS);
                        if (solid(p + vec2(cos(a), sin(a)) * reach * k, lod)) { found = k; break; }
                    }
                }
                if (found == 0.0) discard;
                ALBEDO = found <= lit_share + 0.001 ? color.rgb : casing.rgb;
            }
            """,
    };

    static Shader? _coverShader;
    static Shader CoverShader => _coverShader ??= new Shader
    {
        Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, shadows_disabled;
            uniform sampler2D holes : filter_nearest;
            void fragment() {
                if ((CAMERA_VISIBLE_LAYERS & 1u) != 0u || texture(holes, UV).r < 0.5) discard;
                ALBEDO = vec3(1.0);
            }
            """,
    };

    public static ShaderMaterial Material(Color c) => Dyed(new ShaderMaterial { Shader = Shader }, c);

    static ShaderMaterial Dyed(ShaderMaterial mat, Color c)
    {
        mat.SetShaderParameter("color", c);
        mat.SetShaderParameter("casing", SelectionAura.CasingFor(c));
        return mat;
    }

    public static Mesh Halo(ShaderMaterial mat, Color c, Vector2 footprint, float pad, Texture2D? tex, Texture2D? holes)
    {
        float reach = pad * (1 + CasingShare);
        var size = footprint + Vector2.One * (2 * reach);
        var least = new Vector2(Mathf.Max(0.01f, footprint.X), Mathf.Max(0.01f, footprint.Y));
        mat.Shader = HaloShader;
        Dyed(mat, c);
        mat.SetShaderParameter("grown", size / least);
        mat.SetShaderParameter("reach", Vector2.One * reach / least);
        mat.SetShaderParameter("lit_share", 1 / (1 + CasingShare));
        Picture(mat, tex, holes);
        return new PlaneMesh { Size = size };
    }

    public static void Picture(ShaderMaterial mat, Texture2D? tex, Texture2D? holes)
    {
        if (tex != null) mat.SetShaderParameter("tex", tex);
        mat.SetShaderParameter("holed", holes != null);
        if (holes != null) mat.SetShaderParameter("holes", holes);
    }

    public static ShaderMaterial HoleCover(Texture2D holes)
    {
        var mat = new ShaderMaterial { Shader = CoverShader };
        mat.SetShaderParameter("holes", holes);
        return mat;
    }

    public static Mesh Bands(string shape, Vector2 footprint, float y)
    {
        float rim = Mathf.Clamp(Mathf.Min(footprint.X, footprint.Y) * 0.05f, 0.025f, 0.2f);
        float casing = rim * CasingShare;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (from, to, colour) in new[] { (0f, casing, false), (casing, casing + rim, true), (casing + rim, casing * 2 + rim, false) })
        {
            var inner = PieceArt.Outline(shape, footprint.X + from * 2, footprint.Y + from * 2);
            var outer = PieceArt.Outline(shape, footprint.X + to * 2, footprint.Y + to * 2);
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
