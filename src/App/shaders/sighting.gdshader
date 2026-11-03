shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;

#include "res://shaders/sighting.gdshaderinc"

void fragment() {
	vec2 p = wpos.xz;
	if (rim_distance(p) < edge_w) {
		ALBEDO = edge.rgb;
		ALPHA = edge.a;
	} else {
		ALBEDO = shade.rgb;
		ALPHA = in_sight(p) ? 0.0 : shade.a;
	}
}
