shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform, specular_disabled;

uniform float metre = 1.0;

const float RISE = 0.06;
const float HIGH_M = 9.0;
const vec3 SOOT = vec3(0.62, 0.61, 0.6);

varying float age;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void vertex() {
	vec3 base = MODEL_MATRIX[3].xyz;
	float seed = hash(base.xz);
	age = fract(TIME * RISE + UV2.x + seed);
	vec3 drift = vec3(age * age * 3.0 + sin(age * 6.0 + seed * 6.28) * 0.35, age * HIGH_M, cos(age * 5.0 + seed * 3.1) * 0.35) * metre;
	vec3 centre = (MODELVIEW_MATRIX * vec4(drift, 1.0)).xyz;
	float size = mix(0.4, 2.4, age) * metre;
	VERTEX = centre + vec3((UV - 0.5) * size, 0.0);
	NORMAL = vec3(0.0, 0.0, 1.0);
}

void fragment() {
	float d = length(UV - 0.5) * 2.0;
	ALBEDO = SOOT;
	ALPHA = (1.0 - smoothstep(0.35, 1.0, d)) * (1.0 - age) * smoothstep(0.0, 0.08, age) * 0.45;
	ROUGHNESS = 1.0;
}
