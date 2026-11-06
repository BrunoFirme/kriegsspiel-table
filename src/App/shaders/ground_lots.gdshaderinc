float metres(float m) {
	return m * units_per_metre;
}

float town_at(vec2 uv) {
	return max(0.0, textureLod(edges, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).g * 255.0 / FIELD_STEPS - 0.5) * cell_world;
}

float wet_at(vec2 uv) {
	return max(0.0, textureLod(edges, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).b * 255.0 / FIELD_STEPS - 0.5) * cell_world;
}

float town_depth_at(vec2 uv) {
	return max(0.0, textureLod(edges, clamp(uv, vec2(0.0), vec2(1.0)), 0.0).a * 255.0 / TOWN_STEPS - 0.5) * cell_world;
}

float depth_at(vec2 o, vec2 p, vec2 uv, mat2 to_uv) {
	return town_depth_at(uv + to_uv * (o - p));
}

float wet_meadow(vec2 uv, float follow) {
	return (1.0 - smoothstep(metres(WET_NEAR_M * 0.7), metres(WET_NEAR_M), wet_at(uv))) * (1.0 - follow);
}

vec2 levels_at(vec2 o, vec2 p, vec2 uv, mat2 to_uv) {
	vec2 u = clamp(uv + to_uv * (o - p), vec2(0.0), vec2(1.0));
	float b = texel_of(floor(u * cells)).b;
	return vec2(mod(b, 16.0), floor(b / 16.0)) / float(LEVEL_STEPS);
}

struct Lot {
	vec2 id;
	vec2 other;
	vec2 site;
	float edge;
	vec2 across;
	float salt;
	float room;
	vec2 levels;
};

Lot lot_at(vec2 p, vec2 uv, mat2 to_uv, float size, float jitter, int k, float salt, vec2 levels) {
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
	l.levels = levels;
	return l;
}

vec2 lot_axis(Lot l) {
	float a = (hash(floor(l.site / metres(STRIP_REGION_M)) + 0.7) + (hash(l.id + 0.7) - 0.5) * 0.25) * 3.1416;
	return vec2(cos(a), sin(a));
}

float block(vec2 p, vec2 c, vec2 half_size, float turn, float fw) {
	vec2 d = abs(mat2(vec2(cos(turn), sin(turn)), vec2(-sin(turn), cos(turn))) * (p - c)) - half_size;
	return 1.0 - smoothstep(-fw * 0.5, fw * 0.5, max(d.x, d.y));
}

float dashed(float along, float across, float period, float share, float width, float fw) {
	float on = step(fract(along / period), share);
	return ink(across, width, fw) * mix(on, share, far_blend(fw, period));
}

float edge_line(int style, int kind, Lot l, vec2 p, float fw, float w1, float w2, float w3) {
	float along = dot(p - l.site, l.across);
	if (kind == HEDGE) {
		float bumps = metres(2.5);
		float r = ink(length(vec2(l.edge, (fract(along / bumps) - 0.5) * bumps)), bumps * 0.4, fw);
		return style == SURVEY ? r : ink(l.edge, w1, fw);
	}
	if (kind == FENCE) {
		float ticks = metres(6.0);
		float tick = ink(length(vec2(max(0.0, l.edge - w1 * 0.5), (fract(along / ticks) - 0.5) * ticks)), w1, fw) * step(l.edge, metres(2.0));
		float post = ink(max(abs(l.edge), abs((fract(along / ticks) - 0.5) * ticks)), w1 * 1.6, fw);
		if (style == ANCIENT) return ink(l.edge, w1, fw);
		return style == SURVEY ? max(ink(l.edge, w2 * 0.7, fw), post) : max(ink(l.edge, w1, fw), tick * 0.9);
	}
	if (kind == WALL) {
		float sides = max(ink(abs(l.edge - w1), w1 * 0.6, fw), ink(abs(l.edge + w1), w1 * 0.6, fw));
		return style == SURVEY ? max(sides, ink(l.edge, w1, fw) * step(0.5, fract(along / metres(8.0)))) : sides;
	}
	if (kind == DITCH) return dashed(along, l.edge, metres(8.0), 0.6, w1, fw);
	return max(dashed(along, abs(l.edge - w2), metres(10.0), 0.55, w1, fw), dashed(along, abs(l.edge + w2), metres(10.0), 0.55, w1, fw));
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

float mid_at(vec2 uv) {
	vec2 u = clamp(uv, vec2(0.0), vec2(1.0));
	return band_half_at(u) - distance_at(u);
}

float rides(vec2 p, float trees, float width, float dash, float fw) {
	if (trees < RIDES_FROM) return 0.0;
	vec2 region = floor(p / metres(STRIP_REGION_M));
	float a = hash(region + 5.5) * 3.1416;
	vec2 q = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a))) * p + hash2(region + 6.6) * metres(RIDE_M);
	vec2 pitch = vec2(metres(RIDE_M), metres(RIDE_M) * 2.0);
	vec2 f = abs(fract(q / pitch) - 0.5) * pitch;
	float along = f.x < f.y ? q.y : q.x;
	float on = dash > 0.0 ? mix(step(0.5, fract(along / dash)), 0.5, far_blend(fw, dash)) : 1.0;
	return ink(min(f.x, f.y), width, fw) * on;
}

vec2 road_along_at(vec2 site_uv) {
	vec2 n = road_normal_at(site_uv);
	return vec2(-n.y, n.x);
}

int farm_variant(vec2 seed) {
	return int(hash(seed + 2.6) * float(FARM_VARIANTS));
}

mat2 turned(float turn) {
	return mat2(vec2(cos(turn), sin(turn)), vec2(-sin(turn), cos(turn)));
}

float farm_blocks(vec2 p, vec2 site, vec2 seed, float turn, float scale, float fw, out float yard, out float court) {
	float h = metres(HOUSE_M) * scale;
	mat2 r = turned(turn);
	float n1 = mix(0.8, 1.4, hash(seed + 7.1));
	float n2 = mix(0.6, 1.2, hash(seed + 8.3));
	int v = farm_variant(seed);
	float m = 0.0;
	yard = 0.0;
	court = 0.0;
	if (v == 0) {
		m = block(p, site, vec2(h * 1.5 * n1, h * 0.36), turn, fw);
	} else if (v == 1) {
		m = block(p, site + r * vec2(0.0, h * 0.75), vec2(h * 1.1 * n1, h * 0.3), turn, fw);
		m = max(m, block(p, site - r * vec2(h * 0.2, h * 0.7), vec2(h * 0.9 * n2, h * 0.32), turn, fw));
	} else if (v == 2) {
		m = block(p, site + r * vec2(0.1 * h, h * 0.9), vec2(h * 1.1 * n1, h * 0.32), turn, fw);
		m = max(m, block(p, site + r * vec2(h * 0.95, -h * 0.15), vec2(h * 0.3, h * 0.8 * n2), turn, fw));
	} else if (v == 3) {
		m = block(p, site + r * vec2(0.0, h * 0.95), vec2(h * 1.0 * n1, h * 0.3), turn, fw);
		m = max(m, block(p, site + r * vec2(h * 0.9, -h * 0.1), vec2(h * 0.28, h * 0.8), turn, fw));
		m = max(m, block(p, site + r * vec2(-h * 0.9, -h * 0.2), vec2(h * 0.28, h * 0.7 * n2), turn, fw));
		yard = block(p, site, vec2(h * 1.2, h * 1.25), turn, fw) * (1.0 - m);
	} else if (v == 4) {
		float outer = block(p, site, vec2(h * 1.35 * n1, h * 1.1), turn, fw);
		court = block(p, site, vec2(h * 1.35 * n1 - h * 0.55, h * 0.55), turn, fw);
		m = outer * (1.0 - court);
	} else if (v == 5) {
		for (int i = 0; i < 4; i++) {
			vec2 o = (hash2(seed + float(i) * 3.3) - 0.5) * h * 3.2;
			float a = turn + (hash(seed + float(i) * 1.7) - 0.5) * 1.2;
			m = max(m, block(p, site + o, vec2(h * mix(0.4, 0.7, hash(seed + float(i) * 2.9)), h * 0.28), a, fw));
		}
	} else {
		m = block(p, site + r * vec2(0.0, h * 0.8), vec2(h * 1.0 * n1, h * 0.3), turn, fw);
		m = max(m, block(p, site + r * vec2(h * 0.9, -h * 0.4), vec2(h * 0.28, h * 0.7), turn, fw));
		m = max(m, 1.0 - smoothstep(h * 0.28 - fw, h * 0.28 + fw, length(p - (site + r * vec2(-h * 0.85, -h * 0.5)))));
	}
	return m;
}

float farm_garden(vec2 p, vec2 site, vec2 seed, float turn, float scale, float fw) {
	if (hash(seed + 4.8) > 0.45) return 0.0;
	float h = metres(HOUSE_M) * scale;
	mat2 r = turned(turn);
	float side = floor(hash(seed + 5.9) * 4.0);
	vec2 at = side < 1.0 ? vec2(h * 2.3, h * 0.3) : side < 2.0 ? vec2(-h * 2.3, -h * 0.2) : side < 3.0 ? vec2(h * 0.4, -h * 2.2) : vec2(-h * 0.3, h * 2.2);
	vec2 half_size = vec2(h * mix(0.6, 1.1, hash(seed + 6.2)), h * mix(0.5, 0.9, hash(seed + 6.7)));
	return block(p, site + r * at, half_size, turn, fw);
}

float crown_sign(vec2 p, vec2 o, float r, float pointy, float stem, float width, float fw, vec2 id) {
	vec2 c = o + NORTH * (stem + r * 0.9);
	vec2 q = (p - c) * vec2(1.0, mix(1.0, 0.72, pointy));
	float a = atan(q.y, q.x);
	float rr = r * (1.0 + 0.12 * sin(3.0 * a + hash(id + 7.7) * 6.2832)) * (1.0 - pointy * 0.3 * max(0.0, -sin(a)));
	float body = 1.0 - smoothstep(rr - fw * 0.5, rr + fw * 0.5, length(q));
	return max(body, ink(segment(p, o, o + NORTH * stem), width, fw));
}

float farm_trees(vec2 p, vec2 site, vec2 seed, float turn, float scale, float width, float fw) {
	if (hash(seed + 9.1) > 0.35) return 0.0;
	float h = metres(HOUSE_M) * scale;
	mat2 r = turned(turn);
	float m = 0.0;
	float n = 1.0 + floor(hash(seed + 9.5) * 2.0);
	for (int i = 0; i < 2; i++) {
		if (float(i) >= n) break;
		vec2 at = r * vec2((float(i) - (n - 1.0) * 0.5) * h * 1.2, -h * 2.0);
		m = max(m, crown_sign(p, site + at, metres(5.0), 0.0, metres(3.0), width, fw, seed + float(i)));
	}
	return m;
}

float gewann_lines(vec2 p, vec2 uv, mat2 to_uv, float own, vec2 along, float follow, float width, float fw) {
	Lot g = lot_at(p, uv, to_uv, metres(GEWANN_M), 0.56, FIELD, own, vec2(0.0));
	vec2 dir = lot_axis(g);
	dir = normalize(mix(dir, along * (dot(dir, along) < 0.0 ? -1.0 : 1.0), step(0.8, follow) * step(0.5, length(along))));
	float pitch = metres(mix(30.0, 55.0, hash(g.id + 4.4)));
	float s = dot(p - g.site, dir) / pitch + hash(g.id) * 10.0;
	float lines = ink(min(fract(s), 1.0 - fract(s)) * pitch, width, fw);
	return max(lines, ink(g.edge, width * 1.15, fw) * 0.9);
}

float orchard_rings(vec2 p, Lot l, float width, float fw) {
	float gap = metres(ORCHARD_M);
	vec2 axis = lot_axis(l);
	vec2 q = mat2(axis, vec2(-axis.y, axis.x)) * (p - l.site);
	vec2 c = (floor(q / gap) + 0.5) * gap;
	return crown_sign(q, c + vec2(0.0, gap * 0.2), gap * 0.14, 0.0, gap * 0.12, width, fw, floor(q / gap));
}

float vine_strokes(vec2 p, float slant, float width, float fw) {
	float rows = metres(VINE_ROW_M);
	float stakes = metres(VINE_STAKE_M);
	float row = floor(p.y / rows);
	float x = p.x / stakes + 0.5 * mod(row, 2.0);
	vec2 q = vec2((fract(x) - 0.5) * stakes, (fract(p.y / rows) - 0.5) * rows);
	vec2 half_stroke = vec2(slant, 1.0) * rows * 0.22;
	float stroke = ink(segment(q, -half_stroke, half_stroke), width, fw);
	return mix(stroke, clamp(0.88 * width / stakes, 0.0, 1.0), far_blend(fw, stakes));
}

float farm_scale(vec2 seed) {
	return mix(0.75, 1.35, hash(seed + 5.1));
}

float clumps_at(Lot l) {
	return noise(l.site / metres(FARM_CLUSTER_M) + 3.3);
}

float farms_here(Lot l) {
	return l.levels.y * mix(0.15, 1.8, smoothstep(0.3, 0.75, clumps_at(l)));
}

float hamlet_here(Lot l) {
	return step(0.68, clumps_at(l)) * step(0.5, hash(l.id + 12.4)) * step(metres(60.0), l.room);
}

vec2 second_site(Lot l) {
	return l.site + (hash2(l.id + 13.7) - 0.5) * metres(70.0);
}

float lane_field(vec2 q, float seed, vec2 drift) {
	return noise(q + seed) + LANE_DRIFT * dot(q, drift);
}

float lane_iso(vec2 p, float cycle_m, float seed, out float along) {
	vec2 q = p / metres(cycle_m);
	vec2 drift = vec2(cos(seed), sin(seed));
	float e = 0.02;
	float v = lane_field(q, seed, drift);
	vec2 g = vec2(lane_field(q + vec2(e, 0.0), seed, drift) - v, lane_field(q + vec2(0.0, e), seed, drift) - v) / e;
	float gl = max(length(g), 0.05);
	along = dot(p, vec2(-g.y, g.x) / gl);
	float f = fract(v / LANE_SPACING);
	return min(f, 1.0 - f) * LANE_SPACING / gl * metres(cycle_m);
}

float lane_cut(vec2 p, float seed, float keep) {
	return step(1.0 - keep, noise(p / metres(LANE_CUT_M) + seed));
}

vec2 lane_toward(vec2 p, float cycle_m, float seed, out float d) {
	vec2 q = p / metres(cycle_m);
	vec2 drift = vec2(cos(seed), sin(seed));
	float e = 0.02;
	float v = lane_field(q, seed, drift);
	vec2 g = vec2(lane_field(q + vec2(e, 0.0), seed, drift) - v, lane_field(q + vec2(0.0, e), seed, drift) - v) / e;
	float gl = max(length(g), 0.05);
	float f = fract(v / LANE_SPACING);
	d = min(f, 1.0 - f) * LANE_SPACING / gl * metres(cycle_m);
	return g / gl * (f < 0.5 ? -1.0 : 1.0);
}

float lane_stroke(vec2 p, vec2 a, vec2 b, vec2 seed, float width, float dash, float fw) {
	vec2 ab = b - a;
	vec2 c = (a + b) * 0.5 + vec2(-ab.y, ab.x) * (hash(seed + 3.1) - 0.5) * LANE_BEND;
	float d1 = segment(p, a, c);
	float d2 = segment(p, c, b);
	float first = length(c - a);
	float t = d1 < d2 ? clamp(dot(p - a, c - a) / max(dot(c - a, c - a), 1e-6), 0.0, 1.0) * first : first + clamp(dot(p - c, b - c) / max(dot(b - c, b - c), 1e-6), 0.0, 1.0) * length(b - c);
	float on = dash > 0.0 ? mix(step(0.45, fract(t / dash)), 0.55, far_blend(fw, dash)) : 1.0;
	return ink(min(d1, d2), width, fw) * on;
}

float lane_to_road(vec2 p, vec2 site, vec2 uv, mat2 to_uv, vec2 seed, float near_m, float settle, float width, float dash, float fw) {
	vec2 site_uv = uv + to_uv * (site - p);
	float away = road_away_at(site_uv);
	if (away <= metres(near_m)) return 0.0;
	if (away < metres(LANE_REACH_M)) return lane_stroke(p, site, site - road_normal_at(site_uv) * away, seed, width, dash, fw);
	float d;
	vec2 dir = lane_toward(site, LANE_CYCLE_M, 3.7, d);
	vec2 target = site + dir * d;
	if (d > metres(LANE_REACH_M) || lane_cut(target, 11.0, mix(0.45, 0.75, settle)) < 0.5) return 0.0;
	return lane_stroke(p, site, target, seed, width, dash, fw);
}

float farm_here(vec2 id, vec2 site, float room, vec2 levels, float near_road) {
	Lot l;
	l.id = id;
	l.site = site;
	l.levels = levels;
	return step(metres(30.0), room) * step(hash(id + 8.2), farms_here(l) * (1.0 + near_road));
}

float near_road_at(vec2 site_uv) {
	return 1.0 - smoothstep(metres(60.0), metres(160.0), road_away_at(site_uv));
}

float access_lanes(vec2 p, vec2 uv, mat2 to_uv, vec2 levels, float settle, float width, float dash, float fw) {
	float m = 0.0;
	if (levels.y > 0.05) {
		float size = metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x));
		vec2 n = floor(p / size);
		for (int j = -2; j <= 2; j++) {
			for (int i = -2; i <= 2; i++) {
				vec2 cell = n + vec2(float(i), float(j));
				vec2 centre = (cell + 0.5) * size;
				float salt = salt_at(centre, p, uv, to_uv);
				vec2 id = seeded(cell, salt);
				vec2 site = centre + (hash2(id) - 0.5) * 0.56 * size;
				float room = room_at(site, p, uv, to_uv, FIELD, salt);
				if (room < metres(30.0)) continue;
				vec2 site_uv = uv + to_uv * (site - p);
				if (farm_here(id, site, room, levels_at(site, p, uv, to_uv), near_road_at(site_uv)) < 0.5) continue;
				m = max(m, lane_to_road(p, site, uv, to_uv, id, LANE_NEAR_M, settle, width, dash, fw));
			}
		}
	}
	if (town_at(uv) < metres(LANE_REACH_M)) {
		float size = metres(TOWN_QUARTER_M);
		vec2 n = floor(p / size);
		for (int j = -2; j <= 2; j++) {
			for (int i = -2; i <= 2; i++) {
				vec2 cell = n + vec2(float(i), float(j));
				vec2 centre = (cell + 0.5) * size;
				float salt = salt_at(centre, p, uv, to_uv);
				vec2 id = seeded(cell, salt);
				vec2 site = centre + (hash2(id) - 0.5) * 0.5 * size;
				if (room_at(site, p, uv, to_uv, TOWN, salt) < 0.0) continue;
				m = max(m, lane_to_road(p, site, uv, to_uv, id, TOWN_LANE_NEAR_M, settle, width, dash, fw));
			}
		}
	}
	return m;
}


vec2 curled(vec2 p) {
	return p + (vec2(noise(p * LOT_CURL_CYCLES + 3.0), noise(p * LOT_CURL_CYCLES + 8.0)) - 0.5) * metres(LOT_CURL_M);
}

float windmill(vec2 p, Lot l, float fw) {
	float r = metres(6.0);
	float a = hash(l.id + 3.3) * 1.57;
	vec2 d1 = vec2(cos(a), sin(a));
	vec2 d2 = vec2(-d1.y, d1.x);
	float sails = max(ink(segment(p, l.site - d1 * r, l.site + d1 * r), r * 0.12, fw), ink(segment(p, l.site - d2 * r, l.site + d2 * r), r * 0.12, fw));
	return max(sails, ink(length(p - l.site), r * 0.25, fw));
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

float hut(vec2 p, Lot l, float fw) {
	float s = metres(3.0);
	vec2 d = abs(p - l.site) - s;
	return ink(max(d.x, d.y), metres(0.4), fw) * step(max(d.x, d.y), s * 0.2);
}

float pit(vec2 p, Lot l, float fw) {
	float r = metres(10.0);
	vec2 q = p - l.site;
	float d = length(q);
	float rim = ink(abs(d - r), metres(0.5), fw) * step(0.0, q.y);
	float a = atan(q.y, q.x);
	float ticks = ink(abs(d - r * 0.8), metres(0.3), fw) * step(0.6, fract(a * 4.0)) * step(0.0, q.y);
	return max(rim, ticks);
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
