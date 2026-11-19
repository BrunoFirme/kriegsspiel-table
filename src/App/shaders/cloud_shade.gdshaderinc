global uniform float cloud_shade;
global uniform float cloud_scale;
global uniform vec2 cloud_drift;

const float CLOUD_FROM = 0.46;
const float CLOUD_FULL = 0.72;

float cloud_hash(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float cloud_value(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(cloud_hash(i), cloud_hash(i + vec2(1.0, 0.0)), u.x), mix(cloud_hash(i + vec2(0.0, 1.0)), cloud_hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float cloud_shadow(vec2 table_xz) {
	if (cloud_shade <= 0.0) return 0.0;
	vec2 p = table_xz * cloud_scale + cloud_drift * TIME;
	float cover = cloud_value(p) * 0.65 + cloud_value(p * 2.7 + 13.1) * 0.35;
	return smoothstep(CLOUD_FROM, CLOUD_FULL, cover) * cloud_shade;
}
