shader_type spatial;
render_mode specular_disabled, cull_disabled;

#include "res://shaders/ground_pages.gdshaderinc"

global uniform vec3 ground_gain;
global uniform vec3 table_shift;
global uniform float units_per_metre;
#include "res://shaders/cloud_shade.gdshaderinc"

uniform vec3 fallback : source_color = vec3(0.3, 0.4, 0.25);
uniform float level = 3.0;

const float JITTER = 0.012;
const float PATCH_M = 260.0;
const float PATCH_PLAY = 0.18;
const float LEAST_COVER = 1e-4;

varying vec3 world;

vec3 horizon_linear(vec3 c) {
	c = clamp(c, vec3(0.0), vec3(1.0));
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

float horizon_hash(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float horizon_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(horizon_hash(i), horizon_hash(i + vec2(1.0, 0.0)), u.x), mix(horizon_hash(i + vec2(0.0, 1.0)), horizon_hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz + vec3(table_shift.x, 0.0, table_shift.z);
}

void fragment() {
	float scale = max(units_per_metre, 1e-6) * PATCH_M;
	vec2 p = world.xz / scale;
	vec2 uv = clamp(UV + (vec2(horizon_noise(p), horizon_noise(p + 7.3)) - 0.5) * JITTER * 2.0, vec2(0.001), vec2(0.999));
	vec4 t = tile_at(uv, min(level, tile_levels), vec4(fallback, 1.0));
	vec3 c = horizon_linear(t.rgb / max(t.a, LEAST_COVER)) * ground_gain;
	c *= 1.0 - PATCH_PLAY + 2.0 * PATCH_PLAY * horizon_noise(p * 0.6 + 3.1);
	ALBEDO = c * (1.0 - cloud_shadow(world.xz));
	NORMAL = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
	ROUGHNESS = 1.0;
}
