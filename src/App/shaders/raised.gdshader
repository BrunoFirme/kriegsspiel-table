shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform float glow = 0.0;

varying vec4 info;
varying vec2 metres;

const vec3 GLASS = vec3(0.07, 0.08, 0.09);
const vec3 LAMPLIGHT = vec3(1.0, 0.62, 0.28);
const vec3 TIMBER = vec3(0.24, 0.18, 0.13);
const vec3 MORTAR = vec3(0.62, 0.6, 0.55);
const float STOREY_M = 3.0;

vec3 linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

float hash(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float seam(float x, float width) {
	float fw = max(fwidth(x), 1e-4);
	float d = abs(fract(x + 0.5) - 0.5);
	return (1.0 - smoothstep(width - fw, width + fw, d)) * (1.0 - smoothstep(0.25, 0.6, fw));
}

float detail(float x) {
	return 1.0 - smoothstep(0.3, 0.8, fwidth(x));
}

vec3 courses(vec3 base, vec2 m, float row, float block, float mortar, float spread) {
	float r = floor(m.y / row);
	float u = m.x / block + r * 0.5;
	vec3 c = base * (1.0 + (hash(vec2(floor(u), r)) - 0.5) * spread * detail(u));
	float joint = max(seam(m.y / row, mortar / row), seam(u, mortar / block));
	return mix(c, mix(base, MORTAR, 0.6), joint);
}

vec3 framed(vec3 base, vec2 m) {
	float post = seam(m.x / 1.3, 0.06);
	float rail = max(seam(m.y / STOREY_M, 0.04), seam((m.y - 0.9) / STOREY_M, 0.03));
	float brace = seam((m.x + m.y * 0.8) / 2.6, 0.04) * step(0.2, fract(m.y / STOREY_M)) * step(fract(m.y / STOREY_M), 0.8) * step(fract(m.x / 5.2), 0.25);
	return mix(base, TIMBER, max(max(post, rail), brace));
}

vec3 wall_of(int kind, vec3 base, vec2 m) {
	if (kind == 1) return courses(base, m, 0.42, 0.85, 0.025, 0.22);
	if (kind == 2) return courses(base, m, 0.075, 0.24, 0.008, 0.28);
	if (kind == 3) return framed(base * (0.95 + 0.1 * noise(m * 2.0)), m);
	if (kind == 4) {
		float f = fract(m.y / 0.3);
		return base * (0.62 + 0.5 * sin(f * 3.1416) * detail(m.y / 0.3)) * (0.9 + 0.2 * noise(vec2(m.x * 0.3, floor(m.y / 0.3))));
	}
	if (kind == 5) return base * (0.88 + 0.18 * noise(vec2(m.x * 1.5, m.y * 0.25)) + 0.06 * noise(m * 9.0));
	if (kind == 6) {
		float f = fract(m.y / 0.2);
		return base * mix(0.72, 1.04, smoothstep(0.0, 0.25, f) * detail(m.y / 0.2) + (1.0 - detail(m.y / 0.2)) * 0.8);
	}
	if (kind == 7) return mix(base * (0.92 + 0.12 * noise(m * 0.8)), base * 0.8, max(seam(m.x / 3.0, 0.01), seam(m.y / 1.5, 0.008)));
	return base * (0.93 + 0.1 * noise(m * 1.3) + 0.04 * noise(m * 8.0));
}

vec4 opening(int glazing, vec2 m, float run, float storeys, float door) {
	if (door >= 0.0 && abs(m.x - door) < 0.6 && m.y > -0.1 && m.y < 2.15) return vec4(TIMBER * 0.8, 1.0);
	if (glazing == 0 || storeys < 0.5 || m.y < 0.0) return vec4(0.0);
	float storey = floor(m.y / STOREY_M);
	if (storey >= storeys) return vec4(0.0);
	float apart = glazing == 1 ? 2.6 : glazing == 2 ? 3.2 : glazing == 3 ? 3.0 : glazing == 4 ? 2.0 : glazing == 5 ? 4.0 : 3.6;
	vec2 size = glazing == 1 ? vec2(0.95, 1.6) : glazing == 2 ? vec2(0.6, 0.7) : glazing == 3 ? vec2(0.6, 1.1) : glazing == 4 ? vec2(1.5, 1.2) : glazing == 5 ? vec2(2.8, 2.2) : vec2(0.16, 1.1);
	float sill = glazing == 2 ? 1.3 : glazing == 6 ? 1.2 : 0.95;
	float n = floor((run - 1.0) / apart);
	if (n < 1.0) return vec4(0.0);
	float start = (run - (n - 1.0) * apart) * 0.5;
	float i = clamp(floor((m.x - start) / apart + 0.5), 0.0, n - 1.0);
	float cx = start + i * apart;
	vec2 d = vec2(m.x - cx, m.y - storey * STOREY_M - sill - size.y * 0.5);
	if (door >= 0.0 && storey < 0.5 && abs(cx - door) < (size.x + 1.2) * 0.5) return vec4(0.0);
	vec2 half_size = size * 0.5;
	bool inside = abs(d.x) < half_size.x && abs(d.y) < half_size.y;
	if (glazing == 3 && d.y > half_size.y - half_size.x) inside = length(vec2(d.x, d.y - (half_size.y - half_size.x))) < half_size.x;
	if (!inside) return vec4(0.0);
	float lit = step(hash(vec2(cx, storey)), 0.55) * glow;
	vec3 pane = mix(GLASS, LAMPLIGHT, lit);
	float bars = glazing == 4 || glazing == 5 ? max(seam(d.x / 0.3, 0.06), seam(d.y / 0.3, 0.06)) : glazing == 1 ? max(seam(d.x / size.x, 0.03), seam(d.y / (size.y / 3.0), 0.04)) : 0.0;
	return vec4(mix(pane, vec3(0.85, 0.83, 0.78) * (glazing == 4 ? 0.7 : 1.0), bars), 1.0 + lit);
}

vec3 roof_of(int cover, vec3 base, vec2 m, out float rough) {
	rough = 0.88;
	if (cover == 0) return courses(base, m, 0.32, 0.24, 0.012, 0.3) * mix(0.78, 1.05, fract(m.y / 0.32));
	if (cover == 16) return base * (0.82 + 0.25 * sin(m.x / 0.32 * 6.2832) * detail(m.x / 0.32)) * mix(0.8, 1.04, fract(m.y / 0.36));
	if (cover == 1) return courses(base, m, 0.22, 0.3, 0.01, 0.2);
	if (cover == 3) return courses(base, m, 0.16, 0.11, 0.008, 0.35);
	if (cover == 10) return base * (0.75 + 0.35 * abs(sin(m.x / 0.28 * 3.1416)) * detail(m.x / 0.28)) * mix(0.85, 1.03, fract(m.y / 0.3));
	if (cover == 2 || cover == 13) {
		float straw = noise(vec2(m.x * 14.0, m.y * 0.9)) * detail(m.x * 14.0);
		return base * (0.8 + 0.35 * straw) * (0.9 + 0.1 * noise(m * 0.5));
	}
	if (cover == 14) return mix(base, base * 0.7, seam(m.x / 0.26, 0.03)) * (0.92 + 0.12 * noise(vec2(m.x * 0.5, m.y * 3.0)));
	if (cover == 4 || cover == 9 || cover == 11 || cover == 15) {
		rough = 0.55;
		vec3 c = mix(base, base * 1.18, seam(m.x / 0.55, 0.03));
		if (cover == 11) c = mix(c, vec3(0.45, 0.26, 0.16), smoothstep(0.55, 0.85, noise(m * 0.7)) * 0.7);
		if (cover == 9) c = mix(c, vec3(0.3, 0.5, 0.42), noise(m * 0.4) * 0.4);
		return c;
	}
	if (cover == 7) return mix(base, vec3(0.36, 0.3, 0.2), smoothstep(0.5, 0.9, noise(m * 1.7))) * (0.85 + 0.25 * noise(m * 11.0));
	if (cover == 8) return courses(base, m, 0.5, 0.6, 0.02, 0.25);
	return base * (0.9 + 0.12 * noise(m * 0.9) + 0.05 * noise(m * 7.0));
}

void vertex() {
	info = CUSTOM0;
	metres = UV;
}

void fragment() {
	int code = int(info.x + 0.5);
	vec3 base = COLOR.rgb;
	vec3 c;
	float rough = 0.92;
	float lit = 0.0;
	if (code >= 200) {
		c = roof_of(code - 200, base, metres, rough);
	} else if (code >= 100) {
		c = base * (0.85 + 0.25 * noise(metres * 2.0));
	} else {
		int kind = code % 16;
		int glazing = code / 16;
		c = metres.y < 0.0 ? courses(vec3(0.46, 0.44, 0.41), metres, 0.4, 0.7, 0.02, 0.25) : wall_of(kind, base, metres);
		c *= mix(0.78, 1.0, smoothstep(-0.2, 1.2, metres.y));
		vec4 o = opening(glazing, metres, info.y, info.w, info.z);
		if (o.a > 0.0) {
			c = o.rgb;
			lit = max(o.a - 1.0, 0.0);
			rough = 0.25;
		}
	}
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
		c *= 0.55;
	}
	ALBEDO = linear(clamp(c, 0.0, 1.0));
	ROUGHNESS = rough;
	SPECULAR = 0.3;
	EMISSION = linear(LAMPLIGHT) * lit * 1.6;
}
