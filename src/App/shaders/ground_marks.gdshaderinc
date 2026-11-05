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

int kind_at(vec2 uv, out float border, out float margin, out int beside) {
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
	beside = best;
	for (int k = 0; k < KINDS; k++) {
		if (k != best && score[k] > next) { next = score[k]; beside = k; }
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
