using System;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class ModelPartShader
{
    public static readonly Shader Part = new() { Code = PartCode(glass: false) }, Glass = new() { Code = PartCode(glass: true) };

    static string PartCode(bool glass)
    {
        int n = Finishes.All.Length + 1;
        string Row(Func<PieceFinish.Sheen, float> pick) =>
            string.Join(", ", Enumerable.Range(0, n).Select(c => pick(PieceFinish.Of(Finishes.OfCode(c))).ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)));
        string clear = PieceFinish.GlassClear.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
        int grains = Finishes.Grained.Length;
        string across = string.Join(", ", Finishes.Grained.Select(g => FinishGrain.Across(g).ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)));
        string samplers = string.Concat(Enumerable.Range(0, grains).Select(i => $"uniform sampler2D grain_{i} : filter_linear_mipmap, repeat_enable;\n"));
        string picks = string.Join(" : ", Enumerable.Range(0, grains).Select(i => i == grains - 1 ? $"grained(grain_{i}, w, p)" : $"g == {i} ? grained(grain_{i}, w, p)"));
        return $$"""
            shader_type spatial;
            render_mode cull_disabled{{(glass ? ", depth_draw_always" : "")}};
            uniform sampler2D base_tex : source_color, filter_linear_mipmap, repeat_disable;
            uniform sampler2D paint_tex : source_color, filter_linear, repeat_disable;
            uniform sampler2D finish_tex : filter_nearest, repeat_disable;
            uniform vec4 tint : source_color = vec4(1.0);
            uniform float selected = 0.0;
            uniform float cut = 0.0;
            uniform mat4 stand = mat4(1.0);
            {{samplers}}
            const float GRAIN_ACROSS[{{grains}}] = { {{across}} };
            varying vec3 world;
            varying vec3 world_normal;
            void vertex() {
                world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
            }
            vec3 grained(sampler2D s, vec3 w, vec3 p) {
                return texture(s, p.xy).rgb * w.z + texture(s, p.xz).rgb * w.y + texture(s, p.zy * vec2(-1.0, 1.0)).rgb * w.x;
            }
            const float FINISH_ROUGH[{{n}}] = { {{Row(s => s.Roughness)}} };
            const float FINISH_METAL[{{n}}] = { {{Row(s => s.Metallic)}} };
            void fragment() {
                vec4 b = texture(base_tex, UV);
                vec4 p = texture(paint_tex, UV);
                // A cut-out token: what its picture leaves see-through is not there, as on the table.
                if (cut > 0.5 && max(b.a, p.a) < 0.5) discard;
                ALBEDO = (b.rgb * (1.0 - p.a) + p.rgb) * tint.rgb;
                int f = clamp(int(round(texture(finish_tex, UV).r * 255.0)), 0, {{n - 1}});
                int g = f - {{Finishes.Code(Finishes.Grained[0])}};
                if (g >= 0 && g < {{grains}}) {
                    vec3 w = abs(normalize(mat3(stand) * world_normal));
                    w /= dot(w, vec3(1.0));
                    vec3 p = (stand * vec4(world, 1.0)).xyz / GRAIN_ACROSS[g] * vec3(1.0, -1.0, 1.0);
                    ALBEDO *= {{picks}};
                }
                ROUGHNESS = FINISH_ROUGH[f];
                METALLIC = FINISH_METAL[f];
                SPECULAR = FINISH_ROUGH[f] < 1.0 ? {{PieceFinish.Specular.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture)}} : 0.0;
                {{(glass ? $"ALPHA = f == {Finishes.Code(Finishes.Glass)} ? {clear} : 1.0;" : "")}}
                // The selected part wears a brass rim, so its own colours are not changed by being picked.
                float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 6.0);
                EMISSION = vec3(0.95, 0.72, 0.3) * rim * 0.7 * selected;
            }
            """;
    }
}
