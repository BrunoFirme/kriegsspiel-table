shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform float glow = 0.0;
uniform vec3 warm = vec3(1.0, 0.62, 0.25);

varying vec2 local;

void vertex() {
	local = VERTEX.xz * 2.0;
}

void fragment() {
	float d = clamp(length(local), 0.0, 1.0);
	float f = (1.0 - d) * (1.0 - d);
	ALBEDO = warm * f * glow * 0.14;
}
