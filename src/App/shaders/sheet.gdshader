shader_type spatial;
#ifdef SEEN_THROUGH
render_mode specular_disabled, depth_draw_always;
#else
render_mode specular_disabled;
#endif

uniform sampler2DArray parts : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform ivec2 parts_across = ivec2(1);
uniform vec2 span = vec2(1.0);
uniform vec2 share = vec2(1.0);
uniform vec2 inset = vec2(0.0);
uniform vec4 tint : source_color = vec4(1.0);
uniform bool pictured = false;
uniform bool painted = false;

#include "res://shaders/ground_tiles.gdshaderinc"
#include "res://shaders/grid_lines.gdshaderinc"
#include "res://shaders/pending.gdshaderinc"

void fragment() {
	vec4 colour = tint;
	if (pictured) {
		vec2 at = UV * span;
		vec2 cell = min(floor(max(at, vec2(0.0))), vec2(parts_across - 1));
		float layer = cell.y * float(parts_across.x) + cell.x;
		colour *= textureGrad(parts, vec3((at - cell) * share + inset, layer), dFdx(at) * share, dFdy(at) * share);
	}
	if (painted) {
		float level;
		vec4 paint = ground_tiles(UV, vec4(0.0), level);
		colour = vec4(mix(colour.rgb, ground_lit(paint), paint.a), mix(colour.a, tint.a, paint.a));
		if (ground_relief > 0.0 && paint.a > 0.5) {
			vec3 along_v;
			vec3 along_u = along_uv(VERTEX, NORMAL, UV, along_v);
			NORMAL = ground_relief_normal(UV, level, NORMAL, along_u, along_v);
		}
	}
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz + table_shift;
	ALBEDO = with_grid(with_pending(heights_tint(colour.rgb, world, vec3(1.0)), UV, vec3(1.0)), world) * (1.0 - cloud_shadow(world.xz));
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
#ifdef CUT_OUT
	ALPHA = colour.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
#endif
#ifdef SEEN_THROUGH
	ALPHA = colour.a;
#endif
}
