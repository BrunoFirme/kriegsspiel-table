shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform float glow = 0.0;
uniform bool textured = false;
uniform bool plain = false;
uniform sampler2DArray albedos : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normals : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 means[26];
uniform sampler2D grain : filter_linear_mipmap, repeat_enable;

varying vec4 info;
varying vec4 extra;
varying vec2 metres;
varying vec2 shift;
varying float north;

const vec3 GLASS = vec3(0.07, 0.08, 0.09);
const vec3 LAMPLIGHT = vec3(1.0, 0.62, 0.28);
const vec3 TIMBER = vec3(0.24, 0.18, 0.13);
const vec3 MORTAR = vec3(0.62, 0.6, 0.55);
const float STOREY_M = 3.0;
const float GRAIN_CELLS = 32.0;
const float SNAP = 0.01;
const float UNBOUNDED = 5000.0;
const float STREET_HIGH_M = 9.0;
const vec3 MOSS = vec3(0.045, 0.06, 0.025);
const float PLAIN_GLAZED = 0.7, PLAIN_FRAMED = 0.6, PLAIN_WALL = 0.75, PLAIN_ROOF = 0.73, PLAIN_LIT = 0.05;
const float TILE_M[26] = float[](2.0, 1.5, 1.0, 1.0, 2.0, 1.57, 2.0, 2.4, 2.5, 3.5, 3.0, 3.0, 1.4, 2.5, 2.5, 3.0, 1.12, 2.0, 1.5, 2.5, 2.0, 1.0, 1.8, 2.0, 1.52, 4.0);
const float TINTED[26] = float[](1.0, 0.35, 0.35, 0.4, 0.9, 0.8, 0.5, 0.8, 0.65, 0.5, 0.3, 0.75, 0.8, 0.6, 0.3, 0.3, 0.4, 0.2, 0.4, 0.3, 0.5, 0.3, 0.9, 0.35, 0.35, 0.6);
const float CONTRAST[26] = float[](0.3, 1.0, 1.0, 1.0, 0.55, 1.0, 0.6, 0.4, 0.85, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.6, 1.0, 1.0, 0.85);
const int ALT[26] = int[](22, 24, 23, -1, -1, -1, -1, -1, 25, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1);
const vec3 SHUTTERS[4] = vec3[](vec3(0.25, 0.35, 0.25), vec3(0.3, 0.36, 0.42), vec3(0.36, 0.24, 0.16), vec3(0.45, 0.2, 0.16));
const int ROOF_LAYER[19] = int[](8, 10, 12, 11, -1, 4, 7, 14, 19, -1, 15, 17, 6, 13, 18, 16, 9, 4, -1);
const int WALL_LAYER[9] = int[](0, 1, 2, 0, 3, 4, 5, 6, 7);

int layer_of(int code) {
	if (code >= 200) return code - 200 < 19 ? ROOF_LAYER[code - 200] : -1;
	if (code == 100) return 1;
	if (code == 101) return 20;
	if (code == 104) return 21;
	if (code == 108) return 12;
	if (code == 109) return 4;
	if (code >= 100) return -1;
	return code % 16 < 9 ? WALL_LAYER[code % 16] : -1;
}

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

float grained(vec2 p) {
	return textured ? textureLod(grain, p / GRAIN_CELLS, 0.0).r : noise(p);
}

float seamw(float x, float width, float fw) {
	float f = max(fw, 1e-4);
	float d = abs(fract(x + 0.5) - 0.5);
	return (1.0 - smoothstep(width - f, width + f, d)) * (1.0 - smoothstep(0.25, 0.6, f));
}

float seam(float x, float width) {
	float fw = max(fwidth(x), 1e-4);
	float d = abs(fract(x + 0.5) - 0.5);
	return (1.0 - smoothstep(width - fw, width + fw, d)) * (1.0 - smoothstep(0.25, 0.6, fw));
}

float detail(float x) {
	return 1.0 - smoothstep(0.3, 0.8, fwidth(x));
}

vec3 laid(vec3 base, vec2 m, float row, float block, float mortar, float spread, vec3 gap) {
	float r = floor(m.y / row);
	float u = m.x / block + r * 0.5;
	vec3 c = base * (1.0 + (hash(vec2(floor(u), r)) - 0.5) * spread * detail(u));
	float joint = max(seam(m.y / row, mortar / row), seam(u, mortar / block));
	return mix(c, gap, joint);
}

vec3 courses(vec3 base, vec2 m, float row, float block, float mortar, float spread) {
	return laid(base, m, row, block, mortar, spread, mix(base, MORTAR, 0.6));
}

float beams(vec2 m, float px) {
	float post = seamw(m.x / 1.3, 0.06, px / 1.3);
	float rail = max(seamw(m.y / STOREY_M, 0.04, px / STOREY_M), seamw((m.y - 0.9) / STOREY_M, 0.03, px / STOREY_M));
	float brace = seamw((m.x + m.y * 0.8) / 2.6, 0.04, px * 1.8 / 2.6) * step(0.2, fract(m.y / STOREY_M)) * step(fract(m.y / STOREY_M), 0.8) * step(fract(m.x / 5.2), 0.25);
	return max(max(post, rail), brace);
}

vec3 framed(vec3 base, vec2 m) {
	return mix(base, TIMBER, beams(m, max(fwidth(m.x), fwidth(m.y))));
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

vec4 layout_of(int glazing) {
	float apart = glazing == 1 ? 2.6 : glazing == 2 ? 3.2 : glazing == 3 ? 3.0 : glazing == 4 ? 2.0 : glazing == 5 ? 4.0 : 3.6;
	vec2 size = glazing == 1 ? vec2(0.95, 1.6) : glazing == 2 ? vec2(0.6, 0.7) : glazing == 3 ? vec2(0.6, 1.1) : glazing == 4 ? vec2(1.5, 1.2) : glazing == 5 ? vec2(2.8, 2.2) : vec2(0.16, 1.1);
	return vec4(apart, size, glazing == 2 ? 1.3 : glazing == 6 ? 1.2 : 0.95);
}

vec2 column_of(float apart, float run, float x) {
	float n = floor((run - 1.0) / apart + SNAP);
	float start = (run - (n - 1.0) * apart) * 0.5;
	return vec2(start + clamp(floor((x - start) / apart + 0.5), 0.0, max(n - 1.0, 0.0)) * apart, step(1.0, n));
}

float floors_of(float stored) {
	return floor(stored + 0.25);
}

bool shopped(float stored) {
	return fract(stored + 0.25) > 0.5;
}

float streak(int glazing, vec2 m, float run, float stored) {
	float storeys = floors_of(stored);
	if (glazing == 0 || glazing == 6 || storeys < 0.5 || m.y < 0.0) return 0.0;
	vec4 l = layout_of(glazing);
	vec2 column = column_of(l.x, run, m.x);
	float cx = column.x;
	bool any = column.y > 0.5;
	float storey = min(floor((m.y + 1.8) / STOREY_M), storeys - 1.0);
	float below = storey * STOREY_M + l.w - m.y;
	float across = abs(m.x - cx) / (l.y * 0.5);
	if (!any || below < 0.0 || below > 1.8 || across > 1.0) return 0.0;
	return (1.0 - below / 1.8) * (1.0 - smoothstep(0.5, 1.0, across)) * (0.4 + 0.6 * grained(vec2(m.x * 4.0, m.y * 0.6)));
}

vec4 pane(vec2 d, vec2 half_size, float bars_apart, float lit, float px) {
	float bars = max(seamw(d.x / bars_apart, 0.04, px / bars_apart), seamw(d.y / bars_apart, 0.04, px / bars_apart));
	return vec4(mix(mix(GLASS, LAMPLIGHT, lit), vec3(0.85, 0.83, 0.78), bars), 1.0 + lit);
}

vec4 shop(vec2 m, float run, float door, float px) {
	float apart = 3.4;
	vec2 column = column_of(apart, run, m.x);
	float cx = column.x;
	bool any = column.y > 0.5;
	vec2 d = vec2(m.x - cx, m.y - 1.4);
	if (!any || (door >= 0.0 && abs(cx - door) < 1.9)) return vec4(0.0);
	if (abs(d.x) < 1.2 && abs(d.y) < 1.05) return pane(d, vec2(1.2, 1.05), 0.6, step(hash(vec2(cx, 7.0)), 0.7) * glow, px);
	return vec4(0.0);
}

vec4 dormer(vec2 m, float run, float px) {
	vec2 d = vec2(m.x - run * 0.5, m.y - 0.75);
	if (abs(d.x) < 0.38 && abs(d.y) < 0.45) return pane(d, vec2(0.38, 0.45), 0.38, step(hash(vec2(run, m.x)), 0.5) * glow, px);
	return vec4(0.0);
}

vec4 opening(int glazing, vec2 m, float run, float stored, float door, int kind, vec2 sh, float px) {
	if (door < -1.5) return dormer(m, run, px);
	if (door >= 0.0 && abs(m.x - door) < 0.6 && m.y > -0.1 && m.y < 2.15) return vec4(TIMBER * 0.8, 1.0);
	float storeys = floors_of(stored);
	if (glazing == 0 || storeys < 0.5 || m.y < 0.0) return vec4(0.0);
	if (shopped(stored) && m.y < STOREY_M) return shop(m, run, door, px);
	float storey = floor(m.y / STOREY_M);
	if (storey >= storeys) return vec4(0.0);
	vec4 l = layout_of(glazing);
	vec2 size = l.yz;
	float sill = l.w;
	vec2 column = column_of(l.x, run, m.x);
	float cx = column.x;
	bool any = column.y > 0.5;
	if (!any) return vec4(0.0);
	vec2 d = vec2(m.x - cx, m.y - storey * STOREY_M - sill - size.y * 0.5);
	if (door >= 0.0 && storey < 0.5 && abs(cx - door) < (size.x + 1.2) * 0.5) return vec4(0.0);
	vec2 half_size = size * 0.5;
	bool inside = abs(d.x) < half_size.x && abs(d.y) < half_size.y;
	if (glazing == 3 && d.y > half_size.y - half_size.x) inside = length(vec2(d.x, d.y - (half_size.y - half_size.x))) < half_size.x;
	bool shuttered = glazing == 3 || glazing == 1 && (kind == 0 || kind == 8) && fract(sh.x * 1.31) > 0.5;
	if (!inside && shuttered && abs(d.y) < half_size.y && abs(d.x) < half_size.x * 2.0 + 0.04) {
		vec3 paint = SHUTTERS[int(fract(sh.y * 2.17) * 4.0) % 4];
		return vec4(paint * (0.85 + 0.15 * seamw(d.y / 0.12, 0.12, px / 0.12)), 1.0);
	}
	if (!inside) return vec4(0.0);
	float lit = step(hash(vec2(cx, storey)), 0.55) * glow;
	vec3 glass = mix(GLASS, LAMPLIGHT, lit);
	float bars = glazing == 4 || glazing == 5 ? max(seamw(d.x / 0.3, 0.06, px / 0.3), seamw(d.y / 0.3, 0.06, px / 0.3))
		: glazing == 1 ? max(seamw(d.x / size.x, 0.03, px / size.x), seamw(d.y / (size.y / 3.0), 0.04, px * 3.0 / size.y)) : 0.0;
	return vec4(mix(glass, vec3(0.85, 0.83, 0.78) * (glazing == 4 ? 0.7 : 1.0), bars), 1.0 + lit);
}

vec3 roof_of(int cover, vec3 base, vec2 m, out float rough) {
	rough = 0.88;
	if (cover == 0) return laid(base, m, 0.32, 0.24, 0.012, 0.3, base * 0.5) * mix(0.78, 1.05, fract(m.y / 0.32));
	if (cover == 16) return base * (0.82 + 0.25 * sin(m.x / 0.32 * 6.2832) * detail(m.x / 0.32)) * mix(0.8, 1.04, fract(m.y / 0.36));
	if (cover == 1) return laid(base, m, 0.22, 0.3, 0.008, 0.2, base * 0.55) * mix(0.86, 1.03, fract(m.y / 0.22));
	if (cover == 3) return laid(base, m, 0.16, 0.11, 0.008, 0.35, base * 0.5) * mix(0.82, 1.04, fract(m.y / 0.16));
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
	if (cover == 8) return laid(base, m, 0.5, 0.6, 0.015, 0.25, base * 0.6);
	return base * (0.9 + 0.12 * noise(m * 0.9) + 0.05 * noise(m * 7.0));
}

void vertex() {
	info = CUSTOM0;
	extra = CUSTOM1;
	metres = UV;
	shift = UV2;
	north = smoothstep(0.1, 0.7, -normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz).z);
}

vec3 sampled(int layer, vec2 m, vec3 tint, vec2 sh) {
	vec2 uv = vec2(m.x + sh.x, -(m.y + sh.y)) / TILE_M[layer];
	vec3 tex = mix(means[layer], texture(albedos, vec3(uv, float(layer))).rgb, CONTRAST[layer]);
	return tex * mix(vec3(1.0), linear(tint) / max(means[layer], vec3(0.02)), TINTED[layer]);
}

vec3 weathered_wall(vec3 lin, vec2 m, int glazing, vec4 inf, vec4 ex, vec2 sh) {
	float age = ex.z;
	float damp = (1.0 - smoothstep(0.1, 0.7 + 0.6 * grained(vec2(m.x * 0.8 + sh.x, 1.7)), m.y)) * age;
	lin = mix(lin, lin * vec3(0.62, 0.62, 0.55), damp);
	lin *= 1.0 - 0.3 * age * smoothstep(0.55, 0.9, grained(vec2(m.x * 0.5, m.y * 0.18) + sh));
	lin *= 1.0 - 0.45 * age * streak(glazing, m, inf.y, inf.w);
	lin *= mix(0.5, 1.0, smoothstep(-0.3, 1.6, m.y));
	if (ex.x > 2.5 && ex.x < UNBOUNDED) lin *= 1.0 - 0.4 * smoothstep(ex.x - 1.0, ex.x, m.y);
	if (ex.y < UNBOUNDED) lin *= 1.0 - 0.5 * clamp(atan(max(STREET_HIGH_M - m.y, 0.0) / max(ex.y, 0.5)) / 1.5708, 0.0, 1.0);
	return lin;
}

vec3 weathered_roof(vec3 lin, vec2 m, int cover, vec4 ex, vec2 sh, float facing) {
	float age = ex.z;
	float organic = cover == 4 || cover == 9 || cover == 11 || cover == 15 ? 0.0 : 1.0;
	float moss = age * organic * (0.3 + 0.7 * facing) * smoothstep(0.5, 0.8, grained(m * 0.6 + sh));
	lin = mix(lin, MOSS, moss * 0.7);
	lin *= 1.0 - 0.25 * age * smoothstep(0.4, 0.9, grained(vec2(m.x * 2.5 + sh.y, m.y * 0.15)));
	float mended = step(0.93, hash(floor(vec2(m.x / 1.2, m.y / 0.9) + sh * 3.0)));
	lin = mix(lin, lin * 1.3, mended * age * organic);
	return lin * mix(0.72, 1.0, smoothstep(-0.4, 0.8, m.y));
}

void fragment() {
	if (plain) {
		int code = int(info.x + 0.5);
		vec3 lin = linear(COLOR.rgb);
		int layer = textured ? layer_of(code) : -1;
		if (layer >= 0) lin = mix(means[layer], lin, TINTED[layer]);
		int glazing = code / 16;
		lin *= code >= 200 ? PLAIN_ROOF : code >= 100 ? 1.0 : code % 16 == 3 ? PLAIN_FRAMED : glazing > 0 ? PLAIN_GLAZED : PLAIN_WALL;
		if (!FRONT_FACING) {
			NORMAL = -NORMAL;
			lin *= 0.3;
		}
		ALBEDO = clamp(lin, 0.0, 1.0);
		ROUGHNESS = 0.9;
		SPECULAR = 0.3;
		EMISSION = code < 100 && glazing > 0 ? linear(LAMPLIGHT) * glow * PLAIN_LIT : vec3(0.0);
	} else {
		float px = max(fwidth(metres.x), fwidth(metres.y));
		int code = int(info.x + 0.5);
		vec3 base = COLOR.rgb;
		vec2 m = metres;
		float rough = 0.92;
		float lit = 0.0;
		int layer = textured ? layer_of(code) : -1;
		if (layer >= 0 && ALT[layer] >= 0 && fract(shift.x * 0.37 + shift.y * 0.71) > 0.5) layer = ALT[layer];
		vec3 lin;
		if (layer >= 0) {
			lin = sampled(layer, m, base, shift);
			rough = 0.88;
			if (FRONT_FACING) NORMAL_MAP = texture(normals, vec3(vec2(m.x + shift.x, -(m.y + shift.y)) / TILE_M[layer], float(layer))).rgb;
		} else {
			vec3 c = code >= 200 ? roof_of(code - 200, base, m, rough) : code >= 100 ? base * (0.85 + 0.25 * noise(m * 2.0)) : wall_of(code % 16, base, m);
			lin = linear(clamp(c, 0.0, 1.0));
		}
		if (code < 100) {
			int glazing = code / 16;
			if (layer >= 0 && code % 16 == 3) lin = mix(lin, linear(TIMBER), beams(m, px));
			if (m.y < 0.0) lin = layer >= 0 ? sampled(1, m, vec3(0.46, 0.44, 0.41), shift) * 0.85 : linear(courses(vec3(0.46, 0.44, 0.41), m, 0.4, 0.7, 0.02, 0.25));
			lin = weathered_wall(lin, m, glazing, info, extra, shift);
			vec4 o = opening(glazing, m, info.y, info.w, info.z, code % 16, shift, px);
			if (o.a > 0.0) {
				lin = linear(o.rgb);
				lit = max(o.a - 1.0, 0.0);
				rough = 0.25;
				NORMAL_MAP = vec3(0.5, 0.5, 1.0);
			}
		}
		if (code >= 200) lin = weathered_roof(lin, m, code - 200, extra, shift, north);
		if (!FRONT_FACING) {
			NORMAL = -NORMAL;
			lin *= 0.3;
		}
		ALBEDO = clamp(lin, 0.0, 1.0);
		ROUGHNESS = rough;
		SPECULAR = 0.3;
		EMISSION = linear(LAMPLIGHT) * lit * 1.6;
	}
}
