shader_type spatial;
render_mode cull_disabled, specular_disabled;

#include "res://shaders/ground_tiles.gdshaderinc"
#include "res://shaders/grid_lines.gdshaderinc"

uniform vec3 paper = vec3(0.93, 0.9, 0.83);
uniform bool cut = false;

void fragment() {
	vec4 t = ground_tiles(UV, cut ? vec4(0.0) : vec4(paper, 1.0));
	if (cut && t.a < 0.5) discard;
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	ALBEDO = with_grid(heights_tint(ground_lit(t), world, ground_gain), world);
	ROUGHNESS = 1.0;
}
