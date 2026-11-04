shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D kinds : filter_nearest, repeat_disable;
uniform sampler2D edges : filter_linear, repeat_disable;
uniform vec4 palette[33];
uniform bool painted_only = false;
uniform vec2 cells = vec2(64.0, 64.0);
uniform float cell_world = 0.2;
uniform float detail = 1.0;
global uniform int ground_style;
global uniform float contour_interval;

const int KINDS = 11;
const int GRASS = 1;
const int FIELD = 2;
const int WOOD = 3;
const int MARSH = 4;
const int WATER = 5;
const int ROAD = 6;
const int EARTH = 7;
const int ROCK = 8;
const int SAND = 9;
const int SNOW = 10;
const int SURVEY = 0;
const int ANCIENT = 1;
const int MODERN = 2;
const float WARP = 0.42;
const float EDGE_STEPS = 16.0;
const float BLOCK = 7.0;
const float ANCHOR = 3.0;
const vec2 NORTH = vec2(0.0, -1.0);
const vec2 SUN = vec2(-0.7071, -0.7071);
const vec3 LIGHT_FROM = vec3(-0.55, 0.62, -0.55);
const vec3 SURVEY_PAPER = vec3(0.925, 0.895, 0.81);
const vec3 ANCIENT_PAPER = vec3(0.85, 0.77, 0.6);
const vec3 MODERN_PAPER = vec3(0.945, 0.93, 0.875);
const float SHEET_GAIN = 0.93;

varying vec3 world;

vec3 to_linear(vec3 c) {
	c = clamp(c, vec3(0.0), vec3(1.0));
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

float hash(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

vec2 hash2(vec2 p) {
	return vec2(hash(p), hash(p + vec2(17.31, 41.97)));
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += a * noise(p);
		p = p * 2.03 + vec2(1.7, 9.2);
		a *= 0.5;
	}
	return v;
}

float ink(float d, float width, float fw) {
	float w = max(width, fw * 0.5);
	return (1.0 - smoothstep(w - fw * 0.5, w + fw * 0.5, d)) * clamp(width / max(fw * 0.5, 1e-5), 0.0, 1.0);
}

float far_blend(float fw, float spacing) {
	return smoothstep(0.18, 0.42, fw / max(spacing, 1e-5));
}

float hatch(float s, float spacing, float width, float fw) {
	float exact = ink(abs(fract(s / spacing) - 0.5) * spacing, width, fw);
	return mix(exact, clamp(2.0 * width / spacing, 0.0, 1.0), far_blend(fw, spacing));
}

float segment(vec2 p, vec2 a, vec2 b) {
	vec2 pa = p - a;
	vec2 ba = b - a;
	float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-8), 0.0, 1.0);
	return length(pa - ba * h);
}

vec2 grid_point(vec2 p, float spacing, float jitter, int i, int j, out vec2 id) {
	float row = floor(p.y / spacing) + float(j);
	float shift = mod(row, 2.0) * 0.5;
	float col = floor(p.x / spacing - shift) + float(i);
	id = vec2(col, row);
	return (vec2(col + shift, row) + 0.5 + (hash2(id) - 0.5) * jitter) * spacing;
}

float scatter(vec2 p, float spacing, float r, float jitter, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, spacing, jitter, i, j, id) + spacing * 0.5;
			float size = r * (0.6 + 0.8 * hash(id + 3.7));
			m = max(m, ink(length(p - o), size, fw) * step(1.0 - keep, hash(id + 2.9)));
		}
	}
	float mean = keep * 3.1416 * r * r / (spacing * spacing);
	return mix(m, clamp(mean, 0.0, 1.0), far_blend(fw, spacing));
}

float marks(vec2 p, float spacing, float len, float width, float angle, float wobble, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, spacing, 0.8, i, j, id) + spacing * 0.5;
			float a = angle + (hash(id + 3.3) - 0.5) * wobble;
			vec2 dir = vec2(cos(a), sin(a));
			float l = len * (0.6 + 0.8 * hash(id + 8.1));
			m = max(m, ink(segment(p, o - dir * l, o + dir * l), width, fw) * step(1.0 - keep, hash(id + 7.7)));
		}
	}
	float mean = keep * 4.0 * len * width / (spacing * spacing);
	return mix(m, clamp(mean, 0.0, 1.0), far_blend(fw, spacing));
}

float tufts(vec2 p, float spacing, float len, float width, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, spacing, 0.85, i, j, id) + spacing * 0.5;
			if (hash(id + 5.1) > keep) continue;
			for (int k = -1; k <= 1; k++) {
				float a = float(k) * 0.42;
				vec2 dir = vec2(sin(a), -cos(a));
				m = max(m, ink(segment(p, o, o + dir * len * (k == 0 ? 1.0 : 0.75)), width, fw));
			}
			m = max(m, ink(segment(p, o - vec2(len * 0.55, 0.0), o + vec2(len * 0.55, 0.0)), width * 0.8, fw));
		}
	}
	float mean = keep * 4.5 * len * width / (spacing * spacing);
	return mix(m, clamp(mean, 0.0, 1.0), far_blend(fw, spacing));
}

float screen(vec2 p, float spacing, float r, float fw) {
	vec2 q = vec2(p.x + p.y, p.x - p.y) * 0.7071 / spacing;
	float d = length(fract(q) - 0.5) * spacing;
	float exact = ink(d, r, fw);
	return mix(exact, clamp(3.1416 * r * r / (spacing * spacing), 0.0, 1.0), far_blend(fw, spacing));
}

int kind_of(vec2 cell) {
	ivec2 c = clamp(ivec2(cell), ivec2(0), ivec2(cells) - 1);
	return int(round(texelFetch(kinds, c, 0).r * 255.0));
}

float distance_at(vec2 uv) {
	return max(0.0, textureLod(edges, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).r * 255.0 / EDGE_STEPS - 0.5) * cell_world;
}

int kind_at(vec2 uv, out float border, out float margin) {
	vec2 q = uv * cells;
	vec2 g = q - 0.5;
	g += (vec2(noise(q * 0.25), noise(q * 0.25 + vec2(5.2, 1.3))) - 0.5) * WARP;
	g += (vec2(noise(q * 0.9 + 9.1), noise(q * 0.9 + vec2(2.7, 7.4))) - 0.5) * WARP * 0.2;
	vec2 base = floor(g);
	float score[KINDS];
	for (int k = 0; k < KINDS; k++) score[k] = 0.0;
	for (int j = -1; j <= 2; j++) {
		for (int i = -1; i <= 2; i++) {
			vec2 c = base + vec2(float(i), float(j));
			vec2 d = g - c;
			score[clamp(kind_of(c), 0, KINDS - 1)] += exp(-dot(d, d) * 0.75);
		}
	}
	int best = 0;
	float most = -1.0;
	for (int k = 0; k < KINDS; k++) {
		if (score[k] > most) { most = score[k]; best = k; }
	}
	float next = 0.0;
	for (int k = 0; k < KINDS; k++) {
		if (k != best) next = max(next, score[k]);
	}
	margin = (most - next) / max(most + next, 1e-4);
	border = distance_at((g + 0.5) / cells);
	return best;
}

float room_at(vec2 o, vec2 p, vec2 uv, mat2 to_uv, int k) {
	vec2 u = uv + to_uv * (o - p);
	if (u.x < 0.0 || u.y < 0.0 || u.x > 1.0 || u.y > 1.0) return -1.0;
	if (kind_of(floor(u * cells)) != k) return -1.0;
	return (distance_at(u) + cell_world * 0.5) * detail;
}

vec3 parcels(vec2 p, out float strip_edge, out float block_edge, out float angle) {
	vec2 n = floor(p / BLOCK);
	vec2 f = p / BLOCK - n;
	float f1 = 8.0;
	float f2 = 8.0;
	vec2 id = vec2(0.0);
	vec2 near = vec2(0.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 o = g + 0.22 + hash2(n + g) * 0.56;
			float d = length(o - f);
			if (d < f1) { f2 = f1; f1 = d; id = n + g; near = o; }
			else if (d < f2) f2 = d;
		}
	}
	float best = 8.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 o = g + 0.22 + hash2(n + g) * 0.56;
			if (dot(o - near, o - near) < 1e-6) continue;
			best = min(best, dot(f - (near + o) * 0.5, normalize(o - near)));
		}
	}
	block_edge = abs(best) * BLOCK;
	angle = (floor(hash(id + 1.9) * 4.0) * 0.25 + (hash(id + 6.3) - 0.5) * 0.12) * 3.1416;
	vec2 dir = vec2(cos(angle), sin(angle));
	float width = 0.55 + 0.6 * hash(id + 4.4);
	float s = dot(p, dir) / width + hash(id) * 10.0;
	float strip = floor(s);
	strip_edge = min(fract(s), 1.0 - fract(s)) * width;
	return vec3(hash(id + strip * 0.37 + 2.1), hash(id + strip * 0.71 + 8.5), hash(id));
}

vec3 paper(int style, vec2 p) {
	float fibre = (noise(p * vec2(13.0, 2.6)) - 0.5) * 0.5;
	float tooth = (noise(p * 9.0) - 0.5) * 0.5;
	if (style == ANCIENT) {
		float stain = fbm(p * 0.025) - 0.5;
		float fox = smoothstep(0.8, 0.9, noise(p * 5.0 + 21.0)) * smoothstep(0.55, 0.7, noise(p * 0.4 + 3.0));
		return ANCIENT_PAPER * (1.0 + stain * 0.12 + fibre * 0.05 + tooth * 0.04) * (1.0 - fox * 0.05);
	}
	if (style == MODERN) return MODERN_PAPER * (1.0 + tooth * 0.014 + (fbm(p * 0.06) - 0.5) * 0.025);
	float fox = smoothstep(0.7, 0.84, fbm(p * 0.07 + 11.0));
	return SURVEY_PAPER * (1.0 + (fbm(p * 0.05) - 0.5) * 0.07 + fibre * 0.03 + tooth * 0.025) * (1.0 - fox * 0.05);
}

vec3 wash(vec3 tint, vec3 sheet, vec3 ref, vec2 p, float edge, float strength) {
	float bloom = fbm(p * 0.18 + 4.0) - 0.5;
	float grain = noise(p * 7.5) - 0.5;
	float pool = 1.0 - smoothstep(0.0, 0.16, edge);
	vec3 c = tint * (sheet / ref);
	c *= 1.0 + (bloom * 0.1 + grain * 0.035) * strength;
	return c * (1.0 - pool * 0.2 * strength);
}

vec3 survey_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw, out float bump) {
	const float S = 0.42;
	vec3 sepia = vec3(0.2, 0.17, 0.1);
	vec3 lit = tint * vec3(1.22, 1.2, 0.96);
	vec3 mid = tint * 0.88;
	vec3 dark = tint * vec3(0.42, 0.5, 0.56);
	bump = 0.0;
	if (far_blend(fw, S) >= 1.0) return mix(c, tint, 0.85);
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p, S, 0.62, i, j, id);
			float room = room_at(o, p, uv, to_uv, WOOD);
			if (room < 0.0) continue;
			float r = min(S * 0.58 * (0.78 + 0.44 * hash(id + 3.1)), room * 0.95 + 0.03);
			vec2 q = p - o;
			float d = length(q);
			if (d > r * 1.75) continue;
			float a = atan(q.y, q.x);
			float h = hash(id + 7.7) * TAU;
			float rr = r * (1.0 + 0.12 * sin(a * 5.0 + h) + 0.07 * sin(a * 9.0 + h * 2.3) + 0.04 * sin(a * 15.0 + h * 4.1));
			float body = 1.0 - smoothstep(rr - fw * 0.75, rr + fw * 0.25, d);
			float cast = (1.0 - smoothstep(r * 0.95 - fw, r * 0.95 + fw, length(q - vec2(0.3, 0.34) * r))) * (1.0 - body);
			col = mix(col, col * vec3(0.55, 0.62, 0.62), cast * 0.6);
			float side = dot(q / max(rr, 1e-4), -SUN);
			float speck = noise(q * 38.0 + h) - 0.5;
			vec3 crown = mix(lit, mid, smoothstep(-0.75, 0.05, side + speck * 0.5));
			crown = mix(crown, dark, smoothstep(0.1, 0.9, side + speck * 0.35));
			col = mix(col, crown, body);
			col = mix(col, sepia, ink(abs(d - rr + 0.006), 0.009, fw) * smoothstep(-0.1, 0.6, side) * 0.75);
			bump = max(bump, body * sqrt(max(0.0, 1.0 - d * d / (rr * rr))) * rr);
		}
	}
	return mix(col, mix(c, tint, 0.85), far_blend(fw, S));
}

vec3 ancient_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw) {
	const float S = 0.6;
	vec3 sepia = vec3(0.2, 0.12, 0.05);
	vec3 crown_c = tint * vec3(0.9, 0.95, 0.88);
	if (far_blend(fw, S) >= 1.0) return mix(c, tint * 0.9, 0.7);
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p + NORTH * S * 0.5, S, 0.55, i, j, id) - NORTH * S * 0.5;
			float room = room_at(o, p, uv, to_uv, WOOD);
			if (room < 0.05 || hash(id + 4.2) > 0.9) continue;
			float scale = clamp(room / 0.22, 0.45, 1.0) * (0.85 + 0.3 * hash(id + 3.1));
			float rc = 0.16 * scale;
			float trunk = 0.11 * scale;
			vec2 cc = o + NORTH * (trunk + rc * 0.9);
			vec2 q = (p - cc) * vec2(1.0, 0.88);
			float d = length(q);
			if (length(p - o) > rc * 3.2) continue;
			float a = atan(q.y, q.x);
			float h = hash(id + 7.7) * TAU;
			float rr = rc * (1.0 + 0.07 * sin(a * 7.0 + h) + 0.04 * sin(a * 13.0 + h * 3.0));
			float body = 1.0 - smoothstep(rr - fw * 0.75, rr + fw * 0.25, d);
			float ground = ink(segment(p, o + vec2(-rc * 0.25, 0.0), o + vec2(rc * 1.25, 0.0)), 0.008, fw);
			float stem = ink(segment(p, o, cc), 0.011 * scale, fw);
			col = mix(col, sepia, max(ground, stem) * 0.9);
			float shade = smoothstep(-0.35, 0.55, dot(q / rc, vec2(0.8, 0.6)));
			float lines = hatch(p.x + p.y, 0.032, 0.0065 * shade, fw);
			col = mix(col, crown_c, body);
			col = mix(col, sepia, lines * body * 0.85);
			col = mix(col, sepia, ink(abs(d - rr), 0.008, fw));
		}
	}
	return mix(col, mix(c, tint * 0.9, 0.7), far_blend(fw, S));
}

vec3 modern_wood(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float fw) {
	const float S = 0.62;
	vec3 black = vec3(0.09, 0.1, 0.09);
	float m = 0.0;
	if (far_blend(fw, S) < 1.0) {
		for (int j = -1; j <= 1; j++) {
			for (int i = -1; i <= 1; i++) {
				vec2 id;
				vec2 o = grid_point(p, S, 0.7, i, j, id);
				if (hash(id + 5.0) > 0.82) continue;
				float room = room_at(o, p, uv, to_uv, WOOD);
				if (room < 0.1) continue;
				if (hash(id + 9.3) < 0.28) {
					vec2 top = o + NORTH * 0.085;
					m = max(m, ink(segment(p, top, o + vec2(-0.05, 0.015)), 0.011, fw));
					m = max(m, ink(segment(p, top, o + vec2(0.05, 0.015)), 0.011, fw));
					m = max(m, ink(segment(p, top, o - NORTH * 0.06), 0.011, fw));
				} else {
					vec2 ring = o + NORTH * 0.035;
					m = max(m, ink(abs(length(p - ring) - 0.052), 0.011, fw));
					m = max(m, ink(segment(p, ring - NORTH * 0.052, o - NORTH * 0.065), 0.011, fw));
				}
			}
		}
	}
	m = mix(m, 0.06, far_blend(fw, S));
	return mix(c, black, m * 0.92);
}

float shaded(vec3 n, out float steep) {
	steep = clamp(length(n.xz) / max(n.y, 0.05), 0.0, 3.0);
	vec3 light = normalize(LIGHT_FROM);
	return clamp((1.0 - dot(n, light) / light.y) * 1.35, 0.0, 1.0);
}

float swiss_hachures(vec2 p, float h, vec2 down, float steep, float dark, float fw) {
	const float SX = 0.22;
	const float DH = 0.25;
	const float LONGEST = 1.2;
	vec2 across = vec2(-down.y, down.x);
	float band_h = max(DH, steep * LONGEST / detail);
	float level = h / band_h + (noise(p * 1.7) - 0.5) * 0.3;
	float row = floor(level);
	float within = fract(level);
	float fl = fwidth(level);
	float seam = 0.05 + fl;
	float band = smoothstep(0.0, seam, within) * (1.0 - smoothstep(1.0 - seam, 1.0, within));
	band = mix(band, 0.9, smoothstep(0.3, 0.6, fl));
	float width = SX * mix(0.05, 0.24, dark);
	float keep = mix(0.55, 1.0, dark);
	vec2 a = floor(p / ANCHOR);
	vec2 f = fract(p / ANCHOR);
	f = f * f * (3.0 - 2.0 * f);
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 cell = a + vec2(float(i), float(j));
			float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
			if (w < 0.01) continue;
			float s = dot(p - cell * ANCHOR, across) / SX + hash(vec2(row, cell.x * 13.0 + cell.y)) * 7.0;
			float col = floor(s);
			vec2 id = vec2(col, row) + cell * 5.3;
			if (hash(id) > w * keep) continue;
			float wave = sin(within * 7.0 + hash(id + 2.2) * 6.3) * 0.1;
			float heft = width * (0.85 + 0.3 * hash(id + 4.4)) * (1.15 - 0.3 * within);
			m = max(m, ink(abs(fract(s) - 0.5 + wave) * SX, heft, fw));
		}
	}
	float mean = keep * 2.0 * width / SX * 0.9;
	return mix(m * band, mean, far_blend(fw, SX));
}

vec3 hachures(vec3 c, vec3 n, vec2 p, float h, float fw, vec3 tone) {
	float steep;
	float dark = shaded(n, steep);
	float amount = smoothstep(0.04, 0.35, steep);
	if (amount <= 0.001) return c;
	float stroke = swiss_hachures(p, h, normalize(n.xz), steep, dark * amount, fw);
	c *= 1.0 - dark * amount * 0.1;
	return mix(c, tone, stroke * amount * (0.5 + 0.5 * dark));
}

vec3 engraved_relief(vec3 c, vec3 n, vec2 p, float fw, vec3 tone) {
	float steep;
	float dark = shaded(n, steep);
	float amount = smoothstep(0.04, 0.35, steep);
	if (amount <= 0.001) return c;
	float wobble = noise(p * 1.2) * 0.05;
	float hand = 0.7 + 0.6 * noise(p * 4.0 + 3.0);
	float broken = smoothstep(0.3, 0.45, noise(vec2(p.x + p.y, p.x - p.y) * vec2(1.6, 9.0)));
	float shade = smoothstep(0.08, 0.6, dark) * amount;
	float one = hatch(p.x - p.y + wobble, 0.2, (0.006 + 0.028 * dark) * hand, fw) * shade * broken;
	float two = hatch(p.x - p.y + wobble + 0.1, 0.2, (0.004 + 0.02 * dark) * hand, fw) * smoothstep(0.55, 0.9, dark) * amount * broken;
	c = mix(c, c * vec3(0.84, 0.78, 0.68), dark * amount * 0.3);
	return mix(c, tone, max(one, two) * 0.85);
}

vec3 hillshade(vec3 c, vec3 n) {
	float steep;
	float dark = shaded(n, steep);
	vec3 light = normalize(LIGHT_FROM);
	float lit = clamp((dot(n, light) / light.y - 1.0) * 1.2, 0.0, 1.0);
	c = mix(c, c * vec3(0.5, 0.52, 0.6), dark * 0.75);
	return mix(c, c * vec3(1.06, 1.04, 0.98), lit * 0.3);
}

vec3 slopes(vec3 c, vec3 n, vec2 p, float h, float fw, int style) {
	if (style == MODERN) return hillshade(c, n);
	if (style == ANCIENT) return engraved_relief(c, n, p, fw, vec3(0.25, 0.15, 0.08));
	return hachures(c, n, p, h, fw, vec3(0.3, 0.2, 0.12));
}

vec3 survey(int k, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float fw, float edge, float border, out float bump) {
	vec3 sepia = vec3(0.3, 0.2, 0.12);
	vec3 prussian = vec3(0.15, 0.3, 0.45);
	vec3 c = wash(tint, sheet, SURVEY_PAPER, p, edge, 1.0);
	bump = 0.0;
	float rim = 0.0;
	vec3 rim_ink = sepia;
	if (k == GRASS) {
		c = mix(c, tint * vec3(0.5, 0.66, 0.42), marks(p, 0.22, 0.05, 0.011, 0.0, 0.35, 0.75, fw) * 0.8);
	} else if (k == FIELD) {
		float strip_edge, block_edge, angle;
		vec3 v = parcels(p, strip_edge, block_edge, angle);
		vec3 crop = v.x < 0.35 ? vec3(0.92, 0.84, 0.64) : v.x < 0.7 ? vec3(0.93, 0.89, 0.74) : vec3(0.86, 0.86, 0.66);
		c = wash(tint * mix(vec3(1.0), crop / vec3(0.9, 0.86, 0.68), 0.5), sheet, SURVEY_PAPER, p, min(edge, strip_edge * 2.5), 0.5);
		float furrow = hatch(dot(p, vec2(-sin(angle), cos(angle))), 0.07, 0.006, fw) * step(0.6, v.y);
		c = mix(c, sepia, furrow * 0.2);
		c = mix(c, sepia, ink(strip_edge, 0.005, fw) * 0.45);
		c = mix(c, sepia, ink(block_edge, 0.012, fw) * 0.75);
		rim = 0.8;
	} else if (k == WOOD) {
		c = wash(tint * vec3(1.3, 1.25, 1.05), sheet, SURVEY_PAPER, p, edge, 1.0);
		c = survey_wood(c, tint, p, uv, to_uv, fw, bump);
		rim = 0.55;
		rim_ink = tint * 0.4;
	} else if (k == MARSH) {
		float pools = smoothstep(0.58, 0.64, fbm(p * 0.35 + 7.0));
		c = mix(c, wash(palette[WATER].rgb, sheet, SURVEY_PAPER, p, edge, 1.0), pools * 0.8);
		c = mix(c, prussian, marks(p, 0.2, 0.07, 0.01, 0.0, 0.12, 0.85, fw) * 0.65);
		c = mix(c, tint * vec3(0.45, 0.6, 0.45), tufts(p, 0.42, 0.07, 0.009, 0.6, fw) * 0.8);
		rim = 0.35;
		rim_ink = prussian;
	} else if (k == WATER) {
		float deep = smoothstep(0.0, 0.9, border);
		c = mix(c * 0.86, c * 1.04, deep);
		c = mix(c, prussian, ink(abs(border - 0.16), 0.008, fw) * 0.4);
		rim = 1.0;
		rim_ink = prussian;
		return mix(c, rim_ink, ink(edge, 0.028, fw) * rim);
	} else if (k == ROAD) {
		c = mix(c, c * 0.9, ink(abs(edge - 0.08), 0.03, fw) * 0.35);
		rim = 1.0;
		return mix(c, sepia, ink(edge, 0.014, fw));
	} else if (k == EARTH) {
		c = mix(c, sepia, scatter(p, 0.11, 0.012, 0.9, 0.55, fw) * 0.55);
		rim = 0.6;
	} else if (k == ROCK) {
		float crag = marks(p, 0.16, 0.05, 0.014, 1.9, 1.1, 0.8, fw);
		c = mix(c, c * 0.82, smoothstep(0.5, 0.8, fbm(p * 1.4)) * 0.4);
		c = mix(c, sepia, crag * 0.85);
		c = mix(c, sepia, scatter(p + 3.1, 0.09, 0.009, 0.9, 0.5, fw) * 0.6);
		bump = crag * 0.02;
		rim = 0.7;
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.5, border);
		c = mix(c, sepia, scatter(p, 0.07, 0.008, 0.95, 0.45 + 0.4 * near, fw) * 0.6);
		rim = 0.35;
	} else if (k == SNOW) {
		c = mix(c, prussian, marks(p, 0.3, 0.12, 0.006, 0.15, 0.2, 0.5, fw) * 0.25);
		rim = 0.6;
		rim_ink = prussian * 1.4;
	}
	return mix(c, rim_ink, ink(edge, 0.012, fw) * rim);
}

vec3 ancient(int k, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float fw, float edge, float border) {
	vec3 sepia = vec3(0.22, 0.13, 0.06);
	vec3 c = wash(tint, sheet, ANCIENT_PAPER, p, edge, 0.4);
	float rim = 0.85;
	if (k == GRASS) {
		c = mix(c, sepia, tufts(p, 0.6, 0.06, 0.008, 0.45, fw) * 0.6);
		rim = 0.0;
	} else if (k == FIELD) {
		float strip_edge, block_edge, angle;
		vec3 v = parcels(p, strip_edge, block_edge, angle);
		c *= 0.95 + 0.08 * v.x;
		float ruled = hatch(dot(p, vec2(-sin(angle), cos(angle))), 0.055, 0.004, fw) * step(0.4, v.y);
		c = mix(c, sepia, ruled * 0.35);
		c = mix(c, sepia, scatter(p, 0.06, 0.012, 0.2, 1.0, fw) * (1.0 - smoothstep(0.0, 0.03, block_edge)) * 0.85);
	} else if (k == WOOD) {
		c = ancient_wood(c, tint, p, uv, to_uv, fw);
		rim = 0.0;
	} else if (k == MARSH) {
		c = mix(c, sepia, marks(p, 0.16, 0.08, 0.007, 0.0, 0.1, 0.8, fw) * 0.6);
		c = mix(c, sepia, tufts(p, 0.38, 0.08, 0.008, 0.65, fw) * 0.8);
		rim = 0.4;
	} else if (k == WATER) {
		float lining = 0.0;
		for (int i = 0; i < 7; i++) {
			float at = 0.045 + float(i) * (0.05 + float(i) * 0.012);
			float wob = (noise(p * 2.2 + float(i) * 3.1) - 0.5) * 0.02;
			lining = max(lining, ink(abs(border - at - wob), 0.0065, fw) * (1.0 - float(i) * 0.11));
		}
		float open = smoothstep(0.35, 0.6, border);
		float ripple = marks(p, 0.2, 0.06, 0.005, 0.0, 0.25, 0.55, fw) * open;
		c = mix(c, sepia, max(lining, ripple) * 0.65);
		c = mix(c, sepia, scatter(p, 0.05, 0.006, 0.9, 0.35, fw) * open * 0.45);
		return mix(c, sepia, ink(edge, 0.022, fw));
	} else if (k == ROAD) {
		c = mix(c, sepia, ink(abs(edge - 0.05), 0.006, fw) * 0.35);
		return mix(c, sepia, ink(edge, 0.016, fw));
	} else if (k == EARTH) {
		c = mix(c, sepia, marks(p, 0.13, 0.025, 0.007, 0.6, 1.2, 0.6, fw) * 0.55);
	} else if (k == ROCK) {
		float crag = marks(p, 0.12, 0.05, 0.011, 2.1, 0.5, 0.9, fw);
		float cross = marks(p + 1.7, 0.12, 0.04, 0.008, 0.6, 0.5, 0.6, fw);
		c = mix(c, sepia, max(crag, cross * smoothstep(0.5, 0.7, fbm(p * 1.1))) * 0.85);
	} else if (k == SAND) {
		c = mix(c, sepia, scatter(p, 0.045, 0.0075, 0.95, 0.7, fw) * 0.75);
		rim = 0.5;
	} else if (k == SNOW) {
		c = mix(c, vec3(0.35, 0.42, 0.5), hatch(p.y, 0.09, 0.004, fw) * 0.35);
		rim = 0.6;
	}
	return mix(c, sepia, ink(edge, 0.016, fw) * rim);
}

vec3 modern(int k, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float fw, float edge, float border) {
	vec3 black = vec3(0.09, 0.1, 0.09);
	vec3 blue = vec3(0.12, 0.45, 0.72);
	vec3 brown = vec3(0.55, 0.36, 0.2);
	float mottle = (noise(p * 3.0) - 0.5) * 0.03;
	vec3 c = tint * (sheet / MODERN_PAPER) * (1.0 + mottle);
	if (k == GRASS) {
		float pair = 0.0;
		for (int j = 0; j <= 1; j++) {
			for (int i = 0; i <= 1; i++) {
				vec2 id;
				vec2 o = grid_point(p - 0.24, 0.48, 0.8, i, j, id) + 0.24;
				if (hash(id + 2.0) > 0.7) continue;
				for (int s = -1; s <= 1; s += 2) {
					vec2 b = o + vec2(float(s) * 0.018, 0.0);
					pair = max(pair, ink(segment(p, b, b + NORTH * 0.045), 0.008, fw));
				}
			}
		}
		pair = mix(pair, 0.01, far_blend(fw, 0.48));
		c = mix(c, black * 1.4, pair * 0.85);
	} else if (k == FIELD) {
		float strip_edge, block_edge, angle;
		vec3 v = parcels(p, strip_edge, block_edge, angle);
		c *= 1.0 - step(0.72, v.x) * 0.035;
		c = mix(c, black, ink(strip_edge, 0.0045, fw) * step(0.4, v.y) * 0.75);
		c = mix(c, black, ink(block_edge, 0.009, fw) * 0.9);
	} else if (k == WOOD) {
		c = modern_wood(c, p, uv, to_uv, fw);
		float dots = screen(p + 0.02, 0.085, 0.017, fw);
		c = mix(c, black, dots * (1.0 - smoothstep(0.015, 0.04, edge)) * 0.95);
	} else if (k == MARSH) {
		c = mix(c, blue, marks(p, 0.17, 0.065, 0.009, 0.0, 0.05, 0.9, fw) * 0.85);
		c = mix(c, blue * 0.6, tufts(p + 0.4, 0.5, 0.06, 0.008, 0.55, fw) * 0.85);
	} else if (k == WATER) {
		c = mix(c, blue, hatch(p.y, 0.1, 0.006, fw) * 0.45);
		return mix(c, blue, ink(edge, 0.02, fw));
	} else if (k == ROAD) {
		return mix(c, black, ink(edge, 0.02, fw));
	} else if (k == EARTH) {
		c = mix(c, brown, screen(p, 0.06, 0.012, fw) * 0.75);
	} else if (k == ROCK) {
		c = mix(c, black, marks(p, 0.15, 0.045, 0.01, 1.9, 0.9, 0.8, fw) * 0.8);
		c = mix(c, black, scatter(p + 1.3, 0.1, 0.009, 0.9, 0.5, fw) * 0.7);
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.6, border);
		c = mix(c, brown, scatter(p, 0.06, 0.009, 0.95, 0.5 + 0.35 * near, fw) * 0.9);
	} else if (k == SNOW) {
		c = mix(c, blue, marks(p, 0.35, 0.06, 0.007, 0.1, 0.4, 0.4, fw) * 0.7);
		c = mix(c, blue, screen(p, 0.07, 0.012, fw) * (1.0 - smoothstep(0.0, 0.08, edge)) * 0.8);
	}
	return c;
}

vec3 contour_ink(int style) {
	if (style == MODERN) return vec3(0.66, 0.38, 0.16);
	if (style == ANCIENT) return vec3(0.38, 0.23, 0.12);
	return vec3(0.46, 0.29, 0.18);
}

float contour(float h, vec2 p, int style) {
	if (contour_interval <= 0.0 || h < contour_interval * 0.5) return 0.0;
	float fh = max(fwidth(h), 1e-6);
	if (style != MODERN) h += (noise(p * 0.9) - 0.5) * contour_interval * (style == ANCIENT ? 0.16 : 0.08);
	float level = h / contour_interval;
	float d = abs(fract(level + 0.5) - 0.5) * contour_interval / fh;
	bool index = abs(mod(round(level), 5.0)) < 0.5;
	float px = index ? 1.1 : 0.6;
	float strength = index ? 0.92 : 0.75;
	if (style == SURVEY && !index) strength *= smoothstep(0.32, 0.42, noise(p * 2.6));
	if (style == ANCIENT) px *= 0.55 + 0.9 * noise(p * 1.3 + 7.0);
	float crowded = clamp(fh / contour_interval * 4.0, 0.0, 1.0);
	return (1.0 - smoothstep(px - 0.5, px + 0.5, d)) * strength * (1.0 - crowded * 0.85);
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float border;
	float margin;
	int k = kind_at(UV, border, margin);
	vec2 p = world.xz * detail;
	float fw = max(max(fwidth(p.x), fwidth(p.y)), 1e-5);
	float edge = margin / max(fwidth(margin), 1e-5) * fw;
	float shore = min(edge, border + cell_world * 0.5);
	mat2 dp = mat2(dFdx(p), dFdy(p));
	mat2 du = mat2(dFdx(UV), dFdy(UV));
	mat2 to_uv = abs(determinant(dp)) > 1e-14 ? du * inverse(dp) : mat2(0.0);
	int style = clamp(ground_style, SURVEY, MODERN);
	float lines = contour(world.y, p, style);
	vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	float bump = 0.0;
	vec3 sheet = paper(style, p);
	vec3 c;
	if (k <= 0 || k > 10) {
		if (painted_only && lines < 0.02) discard;
		c = painted_only ? contour_ink(style) : slopes(sheet, n, p, world.y, fw, style);
	} else {
		vec3 tint = palette[style * KINDS + k].rgb;
		if (style == ANCIENT) c = ancient(k, tint, sheet, p, UV, to_uv, fw, edge, shore);
		else if (style == MODERN) c = modern(k, tint, sheet, p, UV, to_uv, fw, edge, shore);
		else c = survey(k, tint, sheet, p, UV, to_uv, fw, edge, shore, bump);
		if (k != WATER && k != ROAD) c = slopes(c, n, p, world.y, fw, style);
	}
	c = mix(c, contour_ink(style), lines * (k == WATER ? 0.6 : 1.0));
	ALBEDO = to_linear(c) * SHEET_GAIN;
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
