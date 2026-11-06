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

float square_block(vec2 p, vec2 uv, mat2 to_uv, float size, float jitter, float salt) {
	vec2 n = floor(p / size);
	vec2 f = p / size - n;
	float nearest = 8.0;
	float own = 0.0;
	float deepest = -1.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 cell = seeded(n + g, salt);
			vec2 o = g + 0.5 + (hash2(cell) - 0.5) * jitter;
			float depth = depth_at((n + o) * size, p, uv, to_uv);
			float d = length(o - f);
			if (d < nearest) { nearest = d; own = depth; }
			deepest = max(deepest, depth);
		}
	}
	return own >= deepest - 1e-4 ? 1.0 : 0.0;
}

float church(int style, vec2 p, vec2 at, float fw) {
	float r = metres(CHURCH_M) * 0.5;
	if (style == ANCIENT) {
		vec2 d = abs(p - at) - vec2(r, r * 0.6);
		float temple = ink(max(d.x, d.y), metres(0.6), fw) * step(max(d.x, d.y), r * 0.15);
		float dots = ink(length(vec2(abs(fract((p.x - at.x) / (r * 0.4)) - 0.5) * r * 0.4, p.y - at.y + r * 0.3)), r * 0.09, fw);
		return max(temple, dots * step(abs(p.x - at.x), r));
	}
	float cross = max(ink(segment(p, at + NORTH * r * 0.1, at + NORTH * r * 0.9), metres(0.5), fw), ink(segment(p, at + NORTH * r * 0.65 - vec2(r * 0.25, 0.0), at + NORTH * r * 0.65 + vec2(r * 0.25, 0.0)), metres(0.5), fw));
	if (style == MODERN) {
		vec2 d = abs(p - at) - r * 0.4;
		return max(1.0 - smoothstep(-fw * 0.5, fw * 0.5, max(d.x, d.y)), cross);
	}
	return max(ink(abs(length(p - at) - r * 0.4), metres(0.6), fw), cross);
}

float frontage(vec2 p, Lot b, float houses, float lane_half, float depth, float fw, out float slot_mean) {
	float slot = metres(HOUSE_M + HOUSE_GAP_M);
	float s = dot(p - b.site, b.across) / slot + hash(b.id) * 9.0;
	float index = floor(s);
	float on = step(hash(b.id + index * 0.37 + 5.1), mix(SCATTERED_HOUSES, 1.0, houses));
	float width = metres(HOUSE_M) * mix(0.8, 1.0, houses) * (0.85 + 0.3 * hash(b.id + index * 0.59));
	float along = abs(fract(s) - 0.5) * slot;
	float front = lane_half + metres(HOUSE_SETBACK_M);
	float d = max(along - width * 0.5, max(front - b.edge, b.edge - front - depth));
	slot_mean = mix(SCATTERED_HOUSES, 1.0, houses) * 0.5;
	return (1.0 - smoothstep(-fw * 0.5, fw * 0.5, d)) * on;
}

float street_dist(int mode, vec2 u, float w) {
	return mode == 0 ? road_away_at(u) : max(0.0, abs(mid_at(u)) - w);
}

vec2 street_normal(int mode, vec2 u) {
	if (mode == 0) return road_normal_at(u);
	vec2 c = floor(clamp(u, vec2(0.0), vec2(1.0)) * cells);
	vec2 g = vec2(distance_at((c + vec2(1.5, 0.5)) / cells) - distance_at((c + vec2(-0.5, 0.5)) / cells), distance_at((c + vec2(0.5, 1.5)) / cells) - distance_at((c + vec2(0.5, -0.5)) / cells));
	return length(g) > 1e-5 ? normalize(g) : vec2(1.0, 0.0);
}

vec2 beyond_band(vec2 uo, vec2 po) {
	vec2 n = street_normal(1, uo);
	return po - n * (distance_at(uo) + cell_world * 0.7);
}

float houses_along(int mode, float w, vec2 p, vec2 uv, mat2 to_uv, float salt, float keep, float fw, out float shadow) {
	float pitch = metres(HOUSE_M + HOUSE_GAP_M);
	float m = 0.0;
	shadow = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p, salt, pitch, 0.35, i, j, id);
			if (hash(id + 2.2) > keep) continue;
			vec2 uo = uv + to_uv * (o - p);
			float d = street_dist(mode, uo, w);
			if (d < metres(HOUSE_SETBACK_M) || d > metres(HOUSE_SETBACK_M + HOUSE_DEPTH_M)) continue;
			if (mode == 0 && kind_of(floor(clamp(uo, vec2(0.0), vec2(1.0)) * cells)) != TOWN) continue;
			if (mode == 1 && kind_of(floor(clamp(uv + to_uv * (beyond_band(uo, o) - p), vec2(0.0), vec2(1.0)) * cells)) != TOWN) continue;
			vec2 n = street_normal(mode, uo);
			float turn = atan(n.y, n.x);
			vec2 half_size = vec2(metres(HOUSE_DEPTH_M) * 0.4, metres(HOUSE_M) * 0.5 * (0.8 + 0.4 * hash(id + 3.3)));
			m = max(m, block(p, o, half_size, turn, fw));
			shadow = max(shadow, block(p - vec2(metres(SHADOW_M)), o, half_size, turn, fw));
		}
	}
	return m;
}

vec3 houses_drawn(int style, vec3 c, float house, float shadow, float far) {
	vec3 ink_c = style == MODERN ? MODERN_BLACK : style == ANCIENT ? ANCIENT_INK : SURVEY_INK;
	vec3 house_c = style == MODERN ? MODERN_BLACK : style == ANCIENT ? RED_OCHRE : BRICK;
	if (style == SURVEY) c = mix(c, ink_c, shadow * (1.0 - house) * 0.85 * (1.0 - far));
	return mix(c, house_c, house);
}

float works(int style, vec2 p, vec2 uv, mat2 to_uv, float depth, float walls, float salt, float fw, out float inside) {
	float fort = style == ANCIENT ? 0.0 : smoothstep(0.33, 0.9, walls);
	float wall_in = max(metres(mix(WALL_IN_M, FORT_IN_M, fort)), cell_world * WALL_CELLS + metres(BASTION_OUT_M + DITCH_OUT_M) * fort);
	float period = metres(mix(WALL_TOWERS_M, BASTIONS_M, fort));
	float bump = 0.0;
	float towers = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p, salt, period, 0.0, i, j, id);
			float at = depth_at(o, p, uv, to_uv);
			float on = 1.0 - smoothstep(period * 0.3, period * 0.5, abs(at - wall_in));
			float d = length(p - o);
			float across = depth - at;
			float along = sqrt(max(0.0, d * d - across * across));
			bump = max(bump, on * max(0.0, 1.0 - along / (period * BASTION_SHARE)));
			vec2 off = vec2(depth - wall_in, along);
			float tower = style == ANCIENT ? 1.0 - smoothstep(-fw, fw, max(abs(off.x), off.y) - metres(TOWER_M)) : ink(length(off), metres(TOWER_M), fw);
			towers = max(towers, on * tower);
		}
	}
	float line = wall_in - bump * metres(BASTION_OUT_M) * fort;
	inside = smoothstep(line - fw, line + fw, depth);
	float wall = ink(abs(depth - line), metres(mix(1.5, 2.5, fort)), fw);
	if (style == MODERN) wall = max(wall, ink(abs(depth - line - metres(2.5)), metres(0.8), fw));
	float ditch_line = line - metres(DITCH_OUT_M);
	float ditch = ink(abs(depth - ditch_line), metres(0.8), fw) * fort;
	float glacis = hatch(depth, metres(GLACIS_LINES_M), metres(0.3), fw) * (1.0 - smoothstep(ditch_line - fw, ditch_line + fw, depth)) * fort * 0.6;
	return max(max(wall, towers * (1.0 - fort)), max(ditch, glacis));
}

vec3 village_draw(int style, vec3 c, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, float houses, float road_w, float fw, out float cover) {
	vec3 ink_c = style == MODERN ? MODERN_BLACK : style == ANCIENT ? ANCIENT_INK : SURVEY_INK;
	float jitter = style == ANCIENT ? 0.0 : 0.5;
	Lot b = lot_at(p, uv, to_uv, metres(TOWN_BLOCK_M), jitter, TOWN, own, vec2(houses, 0.0));
	cover = 0.0;
	if (style == ANCIENT) {
		Lot q = lot_at(p, uv, to_uv, metres(TOWN_QUARTER_M), jitter, TOWN, own, vec2(houses, 0.0));
		float square = square_block(p, uv, to_uv, metres(TOWN_QUARTER_M), jitter, own);
		float ring = ink(abs(length(p - q.site) - VILLAGE_RING), W2, fw) * square;
		cover = ring;
		return mix(palette[ANCIENT * KINDS + GRASS].rgb * (sheet / ANCIENT_PAPER), ink_c, ring);
	}
	if (style == SURVEY) {
		c = wash(GARTENGRUEN, sheet, SURVEY_PAPER, p, SEAMLESS, 0.3);
		c = mix(c, ink_c, hatch(p.x + p.y, metres(GARDEN_HATCH_M), W1 * 0.5, fw) * 0.3);
	} else {
		c = mix(sheet, MODERN_BLACK, TOWN_TINT);
	}
	c = mix(c, ink_c, ink(b.edge, W1 * 0.6, fw) * 0.6);
	float pitch = metres(HOUSE_M + HOUSE_GAP_M);
	float far = far_blend(fw, pitch);
	float keep = mix(0.45, 1.0, houses * 2.0);
	float near_road = 1.0 - smoothstep(metres(40.0), metres(90.0), road_away_at(uv));
	float shadow = 0.0;
	float house = far < 1.0 ? houses_along(0, 0.0, p, uv, to_uv, own, keep, fw, shadow) : 0.0;
	Lot q = lot_at(p, uv, to_uv, metres(TOWN_QUARTER_M), jitter, TOWN, own, vec2(houses, 0.0));
	float street_half = metres(STREET_M) * 0.5;
	float slot_mean;
	float lane_house = frontage(p, q, houses * 2.0, street_half, metres(HOUSE_DEPTH_M), fw, slot_mean) * (1.0 - near_road);
	float lane_shadow = frontage(p - vec2(metres(SHADOW_M)), q, houses * 2.0, street_half, metres(HOUSE_DEPTH_M), fw, slot_mean) * (1.0 - near_road);
	float street = (1.0 - smoothstep(street_half - fw, street_half + fw, q.edge)) * (1.0 - near_road);
	c = mix(c, sheet, street * 0.9);
	house = max(house, lane_house);
	shadow = max(shadow, lane_shadow);
	if (road_w >= 0.0 && far < 1.0) {
		float road_shadow;
		float road_house = houses_along(1, road_w, p, uv, to_uv, own, keep, fw, road_shadow);
		house = max(house, road_house);
		shadow = max(shadow, road_shadow);
	}
	house = mix(house, keep * 0.18, far);
	c = houses_drawn(style, c, house, shadow, far);
	cover = house;
	return c;
}

vec3 town_draw(int style, vec3 c, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, float road_w, float fw, out float cover) {
	vec3 ink_c = style == MODERN ? MODERN_BLACK : style == ANCIENT ? ANCIENT_INK : SURVEY_INK;
	vec3 church_c = style == SURVEY ? CINNABAR : ink_c;
	float houses = max(levels.x, HAMLET_HOUSES);
	float walls = levels.y;
	if (houses < 0.5 && walls < 0.02) return village_draw(style, c, sheet, p, uv, to_uv, own, houses, road_w, fw, cover);
	float jitter = style == ANCIENT ? 0.0 : 0.5;
	float depth = town_depth_at(uv);
	float walled = smoothstep(0.02, 0.1, walls);
	float inside = 1.0;
	float work = walls > 0.02 ? works(style, p, uv, to_uv, depth, walls, own, fw, inside) : 0.0;
	c = mix(mix(c, sheet, OUTSIDE_WALLS * walled), c, inside);
	Lot q = lot_at(p, uv, to_uv, metres(TOWN_QUARTER_M), jitter, TOWN, own, levels);
	Lot b = lot_at(p, uv, to_uv, metres(TOWN_BLOCK_M), jitter, TOWN, own, levels);
	float street_half = metres(STREET_M) * 0.5;
	float lane_half = metres(LANE_M) * 0.5;
	float street = 1.0 - smoothstep(street_half - fw, street_half + fw, q.edge);
	float lane = 1.0 - smoothstep(lane_half - fw, lane_half + fw, b.edge);
	float ring = (1.0 - smoothstep(metres(GARDEN_RING_M) - fw, metres(GARDEN_RING_M) + fw, depth)) * (1.0 - walled);
	float open = max(street, lane * (1.0 - ring * 0.5)) * inside;
	float square = square_block(p, uv, to_uv, metres(TOWN_BLOCK_M), jitter, own) * (1.0 - ring) * inside;
	if (style == SURVEY) {
		c = mix(c, wash(GARTENGRUEN, sheet, SURVEY_PAPER, p, SEAMLESS, 0.3), ring);
		c = mix(c, ink_c, hatch(p.x + p.y, metres(GARDEN_HATCH_M), W1 * 0.5, fw) * 0.3 * ring * (1.0 - open));
	} else if (style == MODERN) {
		c = mix(c, mix(sheet, MODERN_BLACK, TOWN_TINT), ring);
	}
	float keep = (1.0 - ring) * (1.0 - square) * (1.0 - open) * inside;
	float depth_built = metres(mix(HOUSE_DEPTH_M, TOWN_BLOCK_M * 0.5, smoothstep(0.5, 1.0, houses)));
	float slot_mean;
	float house = frontage(p, b, houses, lane_half, depth_built, fw, slot_mean) * keep;
	float slot = metres(HOUSE_M + HOUSE_GAP_M);
	float far = far_blend(fw, slot);
	float shadow = frontage(p - vec2(metres(SHADOW_M)), b, houses, lane_half, depth_built, fw, slot_mean) * keep;
	if (road_w >= 0.0 && far < 1.0) {
		float road_shadow;
		float road_house = houses_along(1, road_w, p, uv, to_uv, own, 0.9, fw, road_shadow);
		house = max(house, road_house);
		shadow = max(shadow, road_shadow);
	}
	house = mix(house, slot_mean * (1.0 - ring) * inside, far);
	c = houses_drawn(style, c, house, shadow, far);
	cover = house;
	c = mix(c, sheet, open * 0.9);
	float kerbs = max(ink(abs(q.edge - street_half), metres(0.6), fw) * (1.0 - lane), ink(abs(b.edge - lane_half), metres(0.4), fw) * 0.7);
	c = mix(c, ink_c, kerbs * inside * (1.0 - ring * 0.5));
	float ch = church(style, p, b.site, fw) * square;
	c = mix(c, church_c, ch);
	c = mix(c, ink_c, work);
	cover = max(cover, max(ch, work));
	return c;
}
