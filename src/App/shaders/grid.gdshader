shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;

#include "res://shaders/grid_lines.gdshaderinc"

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float m = grid_at(wpos);
	if (m <= 0.002) discard;
	ALBEDO = grid_linear(grid_ink.rgb);
	ALPHA = m;
}
