shader_type spatial;
render_mode cull_disabled, specular_disabled;

#include "res://shaders/ground_head.gdshaderinc"

varying vec3 world;

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float bump;
	int k;
	float there;
	vec3 c = ground_colour(UV, world, normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz), bump, k, there);
	if (there <= 0.0) discard;
	ALBEDO = to_linear(c) * ground_gain;
	if (bump > 0.0) {
		vec3 dx = dFdx(VERTEX);
		vec3 dy = dFdy(VERTEX);
		vec3 r1 = cross(dy, NORMAL);
		vec3 r2 = cross(NORMAL, dx);
		float det = dot(dx, r1);
		vec3 grad = sign(det) * (dFdx(bump) * r1 + dFdy(bump) * r2);
		NORMAL = normalize(NORMAL - grad / max(abs(det), 1e-12) * 0.6);
	}
	ROUGHNESS = k == WATER ? 0.5 : 1.0;
}
