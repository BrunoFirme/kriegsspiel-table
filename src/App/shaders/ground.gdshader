shader_type spatial;
render_mode cull_disabled, specular_disabled;

#include "res://shaders/ground_head.gdshaderinc"
#include "res://shaders/grid_lines.gdshaderinc"
#include "res://shaders/pending.gdshaderinc"
#include "res://shaders/sheet_cover.gdshaderinc"

varying vec3 world;

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz + table_shift;
}

void fragment() {
	if (cover_count > 0 && under_higher_sheet(world)) discard;
	float bump;
	int k;
	float there;
	vec3 c = ground_colour(UV, world, normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz), bump, k, there);
	if (there <= 0.0) discard;
	ALBEDO = with_grid(with_pending(to_linear(c) * ground_gain, UV, ground_gain), world);
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
