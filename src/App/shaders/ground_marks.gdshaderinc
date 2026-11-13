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
	float w = max(min(width, fw * INK_MOST_PX * sqrt(max(width, 1e-6) / W1)), fw * 0.5);
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

vec3 texel_of(vec2 cell) {
	ivec2 c = clamp(ivec2(cell), ivec2(0), ivec2(cells) - 1);
	return round(texelFetch(kinds, c, 0).rgb * 255.0);
}

int kind_of(vec2 cell) {
	return int(texel_of(cell).r);
}

vec2 seeded(vec2 id, float salt) {
	return id + vec2(salt * 13.71, salt * 7.33);
}

float salt_at(vec2 o, vec2 p, vec2 uv, mat2 to_uv) {
	vec2 u = clamp(uv + to_uv * (o - p), vec2(0.0), vec2(1.0));
	return texel_of(floor(u * cells)).g;
}

vec2 grid_point(vec2 p, float salt, float spacing, float jitter, int i, int j, out vec2 id) {
	float row = floor(p.y / spacing) + float(j);
	float shift = mod(row, 2.0) * 0.5;
	float col = floor(p.x / spacing - shift) + float(i);
	id = seeded(vec2(col, row), salt);
	return (vec2(col + shift, row) + 0.5 + (hash2(id) - 0.5) * jitter) * spacing;
}

vec2 grid_point_at(vec2 p, vec2 uv, mat2 to_uv, float spacing, float jitter, int i, int j, out vec2 id, out float salt) {
	float row = floor(p.y / spacing) + float(j);
	float shift = mod(row, 2.0) * 0.5;
	float col = floor(p.x / spacing - shift) + float(i);
	vec2 centre = (vec2(col + shift, row) + 0.5) * spacing;
	salt = salt_at(centre, p, uv, to_uv);
	id = seeded(vec2(col, row), salt);
	return centre + (hash2(id) - 0.5) * jitter * spacing;
}

float scatter(vec2 p, float salt, float spacing, float r, float jitter, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, salt, spacing, jitter, i, j, id) + spacing * 0.5;
			float size = r * (0.6 + 0.8 * hash(id + 3.7));
			m = max(m, ink(length(p - o), size, fw) * step(1.0 - keep, hash(id + 2.9)));
		}
	}
	float mean = keep * 3.1416 * r * r / (spacing * spacing);
	return mix(m, clamp(mean, 0.0, 1.0), far_blend(fw, spacing));
}

float marks(vec2 p, float salt, float spacing, float len, float width, float angle, float wobble, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, salt, spacing, 0.8, i, j, id) + spacing * 0.5;
			float a = angle + (hash(id + 3.3) - 0.5) * wobble;
			vec2 dir = vec2(cos(a), sin(a));
			float l = len * (0.6 + 0.8 * hash(id + 8.1));
			m = max(m, ink(segment(p, o - dir * l, o + dir * l), width, fw) * step(1.0 - keep, hash(id + 7.7)));
		}
	}
	float mean = keep * 4.0 * len * width / (spacing * spacing);
	return mix(m, clamp(mean, 0.0, 1.0), far_blend(fw, spacing));
}

float tufts(vec2 p, float salt, float spacing, float len, float width, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, salt, spacing, 0.85, i, j, id) + spacing * 0.5;
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

float screen(vec2 p, float salt, float spacing, float r, float fw) {
	vec2 q = vec2(p.x + p.y, p.x - p.y) * 0.7071 / spacing + vec2(salt * 0.37, salt * 0.61);
	float d = length(fract(q) - 0.5) * spacing;
	float exact = ink(d, r, fw);
	return mix(exact, clamp(3.1416 * r * r / (spacing * spacing), 0.0, 1.0), far_blend(fw, spacing));
}

float distance_at(vec2 uv) {
	return max(0.0, textureLod(edges, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).r * 255.0 / EDGE_STEPS - 0.5) * cell_world;
}

int kind_at(vec2 uv, out float border, out float margin, out int beside) {
	vec2 q = uv * cells;
	vec2 g = q - 0.5;
	if (ground_style != MODERN) g += (vec2(noise(q * 0.9 + 9.1), noise(q * 0.9 + vec2(2.7, 7.4))) - 0.5) * EDGE_HAND;
	vec2 base = floor(g);
	float score[KINDS];
	for (int k = 0; k < KINDS; k++) score[k] = 0.0;
	for (int j = -1; j <= 2; j++) {
		for (int i = -1; i <= 2; i++) {
			vec2 c = base + vec2(float(i), float(j));
			vec2 d = g - c;
			score[clamp(kind_of(c), 0, KINDS - 1)] += exp(-dot(d, d) * EDGE_FALL);
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

float room_at(vec2 o, vec2 p, vec2 uv, mat2 to_uv, int k, out float salt) {
	vec2 u = uv + to_uv * (o - p);
	salt = 0.0;
	if (u.x < 0.0 || u.y < 0.0 || u.x > 1.0 || u.y > 1.0) return -1.0;
	vec3 texel = texel_of(floor(u * cells));
	if (int(texel.r) != k) return -1.0;
	salt = texel.g;
	return (distance_at(u) + cell_world * 0.5) * detail;
}

vec3 paper(int style, vec2 p, float fw) {
	float tooth = (noise(p * 9.0) - 0.5) * 0.5;
	if (style == ANCIENT) {
		float near = 1.0 - far_blend(fw, 0.05);
		float sx = p.x / PAPYRUS_STRIP + (noise(p * 0.3) - 0.5) * 0.5;
		float sy = p.y / PAPYRUS_STRIP + (noise(p * 0.3 + 7.0) - 0.5) * 0.5;
		float strip = hash(vec2(floor(sx), 3.0)) - 0.5;
		float cross = hash(vec2(floor(sy), 5.0)) - 0.5;
		float seam = max(1.0 - smoothstep(0.0, 0.03, abs(fract(sx) - 0.5) * PAPYRUS_STRIP), 1.0 - smoothstep(0.0, 0.03, abs(fract(sy) - 0.5) * PAPYRUS_STRIP)) * (1.0 - far_blend(fw, PAPYRUS_STRIP));
		float fibre = (noise(p * vec2(2.0, 70.0)) - 0.5 + noise(p * vec2(70.0, 2.0) + 5.0) - 0.5) * near;
		return ANCIENT_PAPER * (1.0 + strip * 0.07 + cross * 0.04 + fibre * 0.1 - seam * 0.05);
	}
	if (style == MODERN) return MODERN_PAPER * (1.0 + tooth * 0.02 + (fbm(p * 0.07) - 0.5) * 0.03);
	float wove = (noise(p * 14.0) - 0.5) * 0.5 * (1.0 - far_blend(fw, 0.07));
	float laid = (1.0 - smoothstep(0.015, 0.035, abs(fract(p.y / LAID_LINES) - 0.5) * LAID_LINES)) * (1.0 - far_blend(fw, LAID_LINES));
	float chain = (1.0 - smoothstep(0.012, 0.03, abs(fract(p.x / CHAIN_LINES) - 0.5) * CHAIN_LINES)) * (1.0 - far_blend(fw, 0.1));
	float fox = smoothstep(0.86, 0.9, noise(p * 6.0 + 21.0)) * smoothstep(0.6, 0.75, noise(p * 0.3 + 3.0));
	vec3 c = SURVEY_PAPER * (1.0 + (fbm(p * 0.05) - 0.5) * 0.08 + wove * 0.04 + tooth * 0.025 - laid * 0.014 - chain * 0.03);
	return c * (1.0 - fox * vec3(0.08, 0.13, 0.2));
}

vec3 wash(vec3 tint, vec3 sheet, vec3 ref, vec2 p, float edge, float strength) {
	float bloom = fbm(p * 0.18 + 4.0) - 0.5;
	float grain = noise(p * 7.5) - 0.5;
	float pool = 1.0 - smoothstep(0.0, 0.16, edge);
	float streak = noise(p * vec2(0.9, 5.5) + 11.0) - 0.5;
	vec3 c = tint * (sheet / ref);
	c *= 1.0 + (bloom * 0.17 + grain * 0.05 + streak * 0.05) * strength;
	return c * (1.0 - pool * 0.32 * strength);
}

vec3 plain(int style, vec3 tint, vec3 sheet, vec2 p, float edge) {
	if (style == MODERN) return tint * (sheet / MODERN_PAPER);
	if (style == ANCIENT) return tint * (sheet / ANCIENT_PAPER);
	return wash(tint, sheet, SURVEY_PAPER, p, edge, 1.0);
}

float hand_ink(float d, float width, vec2 p, float fw) {
	float w = width * (1.0 + HAND_WOBBLE * (noise(p * HAND_GRAIN) - 0.5) * 2.0);
	return max(ink(d, w, fw), (1.0 - smoothstep(w, w * BLEED_REACH, abs(d))) * BLEED);
}

float plate_edge(float edge, vec2 off) {
	return edge - dot(off, vec2(dFdx(edge), dFdy(edge)));
}

float plate_fill(float edge, vec2 off, float fw) {
	return smoothstep(-fw * 0.5, fw * 0.5, plate_edge(edge, off));
}

float plate_spill(float edge, vec2 off, float fw) {
	return 1.0 - smoothstep(-fw * 0.5, fw * 0.5, plate_edge(edge, off));
}

float meadow_pairs(vec2 p, float salt, float spacing, float len, float width, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, salt, spacing, 0.3, i, j, id) + spacing * 0.5;
			if (hash(id + 7.7) > keep) continue;
			float l = len * (0.8 + 0.4 * hash(id + 8.1));
			for (int s = -1; s <= 1; s += 2) {
				for (int t = -1; t <= 1; t += 2) {
					vec2 b = o + vec2(float(t) * (l + width * 3.0), float(s) * width * 1.8);
					m = max(m, ink(segment(p, b - vec2(l * 0.8, 0.0), b + vec2(l * 0.8, 0.0)), width, fw));
				}
			}
		}
	}
	float mean = keep * 4.0 * 1.6 * len * 2.0 * width / (spacing * spacing);
	return mix(m, clamp(mean, 0.0, 1.0), far_blend(fw, spacing));
}

float pasture(vec2 p, float salt, float spacing, float width, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, salt, spacing, 0.3, i, j, id) + spacing * 0.5;
			if (hash(id + 5.1) > keep) continue;
			for (int k = -3; k <= 3; k++) {
				float h = (0.15 - 0.022 * abs(float(k))) * spacing * 0.5;
				vec2 b = o + vec2(float(k) * width * 2.8, 0.0);
				m = max(m, ink(segment(p, b, b + NORTH * h), width, fw));
			}
			m = max(m, ink(segment(p, o - vec2(width * 9.5, -width * 1.2), o + vec2(width * 9.5, width * 1.2)), width * 0.6, fw) * 0.6);
		}
	}
	float mean = keep * 7.0 * 0.1 * spacing * 2.0 * width / (spacing * spacing);
	return mix(m, clamp(mean, 0.0, 1.0), far_blend(fw, spacing));
}
