shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, blend_mix, shadows_disabled;

#include "res://shaders/sighting.gdshaderinc"

void fragment() {
	ALBEDO = shade.rgb;
	ALPHA = in_sight(wpos.xz) ? 0.0 : shade.a;
}
