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

void fragment() {
	vec4 colour = tint;
	if (pictured) {
		vec2 at = UV * span;
		vec2 cell = min(floor(max(at, vec2(0.0))), vec2(parts_across - 1));
		float layer = cell.y * float(parts_across.x) + cell.x;
		colour *= textureGrad(parts, vec3((at - cell) * share + inset, layer), dFdx(at) * share, dFdy(at) * share);
	}
	if (painted) {
		vec4 paint = ground_tiles(UV, vec4(0.0));
		colour = vec4(mix(colour.rgb, ground_lit(paint), paint.a), mix(colour.a, tint.a, paint.a));
	}
	ALBEDO = heights_tint(colour.rgb, (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz, vec3(1.0));
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
