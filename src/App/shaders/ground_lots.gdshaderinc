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

float tilth(vec2 p, vec2 axis, vec2 strip, float fw) {
	float ridge = metres(PLOUGH_M);
	float near = 1.0 - far_blend(fw, ridge);
	if (near <= 0.001) return 0.0;
	vec2 beside = vec2(-axis.y, axis.x);
	float crop = hash(strip + 6.1);
	float along = dot(p, beside) / metres(PLANT_M);
	float r = dot(p, axis) / ridge + crop * 7.0 + (noise(vec2(along * 0.06, crop * 31.0)) - 0.5) * 0.6;
	float row = floor(r);
	if (crop < PLOUGHED_SHARE) {
		float furrow = pow(0.5 + 0.5 * cos(6.2832 * fract(r)), 3.0);
		float clods = noise(vec2(along * 0.6, row * 3.1) + strip);
		return furrow * (0.3 + 0.7 * clods) * PLOUGHED_INK * near;
	}
	if (crop < PLOUGHED_SHARE + ROWS_SHARE) {
		float cell = floor(along);
		vec2 id = vec2(cell, row) + strip;
		vec2 o = vec2(cell + 0.5 + (hash(id) - 0.5) * 0.45, row + 0.5 + (hash(id + 3.1) - 0.5) * 0.3);
		float d = length(vec2((along - o.x) * metres(PLANT_M), (r - o.y) * ridge));
		return ink(d, metres(PLANT_M) * (0.17 + 0.14 * hash(id + 1.7)), fw) * step(hash(id + 9.2), 0.82) * ROWS_INK * near;
	}
	float ears = smoothstep(0.55, 0.8, noise(p / metres(0.3) + strip)) * (0.6 + 0.4 * noise(p / metres(2.5) + strip));
	return ears * STANDING_INK * near;
}

float fields(vec2 p, float own, vec2 down, float follow, float width, float fw, out float tone, out float fallow, out float balk, out float bound) {
	float size = metres(BLOCK_M);
	vec2 n = floor(p / size);
	vec2 f = p / size - n;
	float f1 = 8.0;
	vec2 near = vec2(0.0);
	vec2 id = vec2(0.0);
	vec2 sites[9];
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 cell = seeded(n + g, own);
			vec2 o = g + 0.5 + (hash2(cell) - 0.5) * BLOCK_JITTER;
			sites[(j + 1) * 3 + i + 1] = o;
			float d = length(o - f);
			if (d < f1) { f1 = d; id = cell; near = o; }
		}
	}
	float best = 8.0;
	for (int k = 0; k < 9; k++) {
		vec2 o = sites[k];
		if (dot(o - near, o - near) < 1e-6) continue;
		best = min(best, abs(dot(f - (near + o) * 0.5, normalize(o - near))));
	}
	float edge = best * size;
	vec2 site = (n + near) * size;
	float a = noise(site / metres(STRIP_REGION_M) + own * 0.37) * 3.1416 + step(hash(id + 1.7), FURLONG_CROSS_SHARE) * 1.5708 + (hash(id + 2.9) - 0.5) * FURROW_SWAY;
	vec2 across = vec2(cos(a), sin(a));
	float wide = metres(mix(STRIP_NARROW_M, STRIP_WIDE_M, hash(id + 4.4)));
	float s = dot(p - site, across) / wide + 50.0;
	float i = floor(s);
	float lo = i + (hash(id + vec2(i, 3.3)) - 0.5) * STRIP_PLAY;
	float hi = i + 1.0 + (hash(id + vec2(i + 1.0, 3.3)) - 0.5) * STRIP_PLAY;
	if (s < lo) {
		i -= 1.0;
		hi = lo;
		lo = i + (hash(id + vec2(i, 3.3)) - 0.5) * STRIP_PLAY;
	} else if (s > hi) {
		i += 1.0;
		lo = hi;
		hi = i + 1.0 + (hash(id + vec2(i + 1.0, 3.3)) - 0.5) * STRIP_PLAY;
	}
	float side = min(s - lo, hi - s) * wide;
	vec2 strip = id + vec2(i * 7.3, i * 1.9);
	float seen = 1.0 - far_blend(fw, wide * 0.5);
	tone = (hash(strip + 12.7) - 0.5) * seen;
	fallow = step(hash(strip + 9.4), FALLOW_SHARE) * seen;
	balk = (1.0 - smoothstep(metres(BALK_M) * 0.3, metres(BALK_M) * 0.3 + fw, side)) * seen;
	bound = (1.0 - smoothstep(metres(BALK_M) * 0.6, metres(BALK_M) * 0.6 + fw, edge)) * (1.0 - far_blend(fw, size * 0.08));
	if (fallow > 0.5) return 0.0;
	vec2 axis = follow > 0.8 && length(down) > 0.5 ? down : across;
	float pitch = metres(mix(FURROW_NEAR_M, FURROW_WIDE_M, hash(strip + 4.4)));
	float t = dot(p, axis) / pitch + hash(strip) * 10.0;
	float run = dot(p, vec2(-axis.y, axis.x)) / metres(FURROW_RUN_M);
	float line = ink(abs(fract(t) - 0.5) * pitch, width, fw);
	float drawn = smoothstep(FURROW_GAPS, FURROW_GAPS + 0.15, noise(vec2(run, floor(t) * 7.3) + strip));
	float headland = smoothstep(0.0, metres(HEADLAND_M), edge) * smoothstep(0.0, metres(BALK_M), side);
	return max(line * drawn * headland * (1.0 - far_blend(fw, pitch)) * symbols(fw), tilth(p, axis, strip, fw) * smoothstep(0.0, metres(BALK_M), side));
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
