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

vec3 texel_of(vec2 cell) {
	ivec2 c = clamp(ivec2(cell), ivec2(0), ivec2(cells) - 1);
	return round(texelFetch(kinds, c, 0).rgb * 255.0);
}

int kind_of(vec2 cell) {
	return int(texel_of(cell).r);
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
