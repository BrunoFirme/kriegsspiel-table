shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

#include "res://shaders/sighting.gdshaderinc"

void fragment() {
	if (in_sight(wpos.xz)) discard;
	ALBEDO = shade.rgb;
}
