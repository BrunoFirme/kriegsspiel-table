shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

varying vec3 local;

vec3 linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

float hash(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(hash(i), hash(i + vec3(1, 0, 0)), u.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), u.x), u.y);
	float b = mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), u.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), u.x), u.y);
	return mix(a, b, u.z);
}

void vertex() {
	local = VERTEX * 3.0;
}

void fragment() {
	float leaf = 0.78 + 0.34 * noise(local) + 0.12 * noise(local * 3.7);
	vec3 c = COLOR.rgb * leaf;
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
		c *= 0.5;
	}
	ALBEDO = linear(clamp(c, 0.0, 1.0));
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
