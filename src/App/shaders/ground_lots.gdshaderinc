float metres(float m) {
	return m * units_per_metre;
}

float wet_at(vec2 uv) {
	return max(0.0, textureLod(edges, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).g * 255.0 / FIELD_STEPS - 0.5) * cell_world;
}

float wet_meadow(vec2 uv, float follow) {
	return (1.0 - smoothstep(metres(WET_NEAR_M * 0.7), metres(WET_NEAR_M), wet_at(uv))) * (1.0 - follow);
}

struct Lot {
	vec2 id;
	vec2 other;
	vec2 site;
	float edge;
	vec2 across;
	float salt;
	float room;
};

Lot lot_at(vec2 p, vec2 uv, mat2 to_uv, float size, float jitter, int k, float salt) {
	Lot l;
	vec2 n = floor(p / size);
	vec2 f = p / size - n;
	float f1 = 8.0;
	vec2 near = vec2(0.0);
	vec2 sites[9];
	vec2 ids[9];
	l.id = vec2(0.0);
	l.other = vec2(0.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 cell = seeded(n + g, salt);
			vec2 o = g + 0.5 + (hash2(cell) - 0.5) * jitter;
			sites[(j + 1) * 3 + i + 1] = o;
			ids[(j + 1) * 3 + i + 1] = cell;
			float d = length(o - f);
			if (d < f1) { f1 = d; l.id = cell; near = o; }
		}
	}
	float best = 8.0;
	vec2 toward = vec2(1.0, 0.0);
	for (int s = 0; s < 9; s++) {
		vec2 o = sites[s];
		if (dot(o - near, o - near) < 1e-6) continue;
		float d = abs(dot(f - (near + o) * 0.5, normalize(o - near)));
		if (d < best) { best = d; toward = normalize(o - near); l.other = ids[s]; }
	}
	l.site = (n + near) * size;
	l.edge = best * size;
	l.across = vec2(-toward.y, toward.x);
	l.room = room_at(l.site, p, uv, to_uv, k, l.salt);
	return l;
}

vec2 lot_axis(Lot l) {
	float a = (hash(floor(l.site / metres(STRIP_REGION_M)) + 0.7) + (hash(l.id + 0.7) - 0.5) * 0.25) * 3.1416;
	return vec2(cos(a), sin(a));
}

float dashed(float along, float across, float period, float share, float width, float fw) {
	float on = step(fract(along / period), share);
	return ink(across, width, fw) * mix(on, share, far_blend(fw, period));
}

float dotted_line(vec2 p, vec2 uv, mat2 to_uv, float at, float spacing, float width, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, 0.0, spacing, 0.0, i, j, id) + spacing * 0.5;
			float there = distance_at(clamp(uv + to_uv * (o - p), vec2(0.0), vec2(1.0)));
			m = max(m, ink(length(p - o), width, fw) * step(abs(there - at), spacing * 0.5));
		}
	}
	return m;
}

float crown_sign(vec2 p, vec2 o, float r, float pointy, float stem, float width, float fw, vec2 id) {
	vec2 c = o + NORTH * (stem + r * 0.9);
	vec2 q = (p - c) * vec2(1.0, mix(1.0, 0.72, pointy));
	float a = atan(q.y, q.x);
	float rr = r * (1.0 + 0.12 * sin(3.0 * a + hash(id + 7.7) * 6.2832)) * (1.0 - pointy * 0.3 * max(0.0, -sin(a)));
	float body = 1.0 - smoothstep(rr - fw * 0.5, rr + fw * 0.5, length(q));
	return max(body, ink(segment(p, o, o + NORTH * stem), width, fw));
}

float gewann_lines(vec2 p, vec2 uv, mat2 to_uv, float own, vec2 along, float follow, float width, float fw) {
	Lot g = lot_at(p, uv, to_uv, metres(GEWANN_M), 0.56, FIELD, own);
	vec2 dir = lot_axis(g);
	dir = normalize(mix(dir, along * (dot(dir, along) < 0.0 ? -1.0 : 1.0), step(0.8, follow) * step(0.5, length(along))));
	float pitch = metres(mix(30.0, 55.0, hash(g.id + 4.4)));
	float s = dot(p - g.site, dir) / pitch + hash(g.id) * 10.0;
	float lines = ink(min(fract(s), 1.0 - fract(s)) * pitch, width, fw);
	return max(lines, ink(g.edge, width * 1.15, fw) * 0.9);
}

vec2 curled(vec2 p) {
	return p + (vec2(noise(p * LOT_CURL_CYCLES + 3.0), noise(p * LOT_CURL_CYCLES + 8.0)) - 0.5) * metres(LOT_CURL_M);
}

float islet(vec2 p, Lot l, float fw, out float body) {
	float r = metres(mix(12.0, 30.0, hash(l.id + 6.6)));
	float d = length(p - l.site);
	body = 1.0 - smoothstep(r - fw, r + fw, d);
	return ink(abs(d - r), metres(0.6), fw);
}

float pond(vec2 p, Lot l, float fw, out float body) {
	float r = metres(mix(8.0, 18.0, hash(l.id + 5.5)));
	float d = length((p - l.site) * vec2(1.0, 1.3));
	body = 1.0 - smoothstep(r - fw, r + fw, d);
	return ink(abs(d - r), metres(0.6), fw);
}

float lone_tree(vec2 p, Lot l, float fw, out float body) {
	float r = metres(6.0);
	float d = length(p - l.site);
	body = 1.0 - smoothstep(r - fw, r + fw, d);
	return max(ink(abs(d - r), metres(0.5), fw), ink(segment(p, l.site, l.site - NORTH * r * 1.3), metres(0.5), fw));
}

float conifer_share(vec2 o, float salt) {
	return smoothstep(0.42, 0.58, noise(o / max(metres(STAND_M), 1e-3) + salt * 0.37));
}

float clearing_at(vec2 o, float salt) {
	return smoothstep(0.78, 0.84, noise(o / max(metres(STAND_M * 0.8), 1e-3) + 4.1 + salt * 0.11));
}

float coppice_at(vec2 o, float salt) {
	return smoothstep(0.72, 0.8, noise(o / max(metres(STAND_M), 1e-3) + 9.3 + salt * 0.23));
}

float dunes(vec2 p, vec2 along, float fw) {
	if (length(along) < 0.5) return 0.0;
	return hatch(dot(p, vec2(-along.y, along.x)) + noise(p * 2.0) * metres(3.0), metres(12.0), metres(0.5), fw);
}
