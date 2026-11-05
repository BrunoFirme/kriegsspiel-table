vec3 ancient_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover) {
	float S = spacing * CROWN_CROWDING;
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	vec3 crown_c = tint * vec3(0.78, 0.86, 0.72);
	cover = 0.0;
	if (far_blend(fw, S) >= 1.0) return mix(c, tint * 0.9, 0.7);
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point_at(p + NORTH * S * 0.5, uv, to_uv, S, 0.22, i, j, id, salt) - NORTH * S * 0.5;
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < 0.05 || hash(id + 4.2) > 0.92) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			if (hash(id + 1.3) < clearing_at(o, salt) || hash(id + 2.4) > keep) continue;
			float scale = clamp(room / 0.22, 0.45, 1.0) * (0.9 + 0.2 * hash(id + 3.1)) * S / 0.6 * mix(1.0, 0.6, coppice_at(o, salt));
			float rc = 0.16 * scale;
			float trunk = 0.11 * scale;
			if (hash(id + 6.1) < conifer_share(o, salt)) {
				vec2 cc = o + NORTH * rc * 1.6;
				vec2 q = (p - cc) * vec2(3.2, 1.0);
				float d = length(q);
				if (d > rc * 2.4) continue;
				float body = 1.0 - smoothstep(rc * 1.5 - fw, rc * 1.5 + fw, d);
				col = mix(col, crown_c * 0.85, body);
				col = mix(col, ANCIENT_INK, ink(abs(d - rc * 1.5), W1 * 0.7, fw));
				cover = max(cover, body);
				continue;
			}
			vec2 cc = o + NORTH * (trunk + rc * 0.9);
			vec2 q = (p - cc) * vec2(1.0, 0.88);
			float d = length(q);
			if (length(p - o) > rc * 3.2) continue;
			float body = 1.0 - smoothstep(rc - fw * 0.75, rc + fw * 0.25, d);
			float stem = ink(segment(p, o, cc), W1 * 0.8 * scale, fw);
			col = mix(col, ANCIENT_INK, stem * 0.9);
			col = mix(col, crown_c, body);
			float lit = 1.0 - smoothstep(rc * 0.85, rc * 1.0, length(q + vec2(rc * CROWN_SHADE_SHIFT)));
			col = mix(col, crown_c * 0.72, body * (1.0 - lit));
			cover = max(cover, body);
			col = mix(col, ANCIENT_INK, ink(abs(d - rc), W1 * 0.9, fw));
		}
	}
	return mix(col, mix(c, tint * 0.9, 0.7), far_blend(fw, S));
}

vec3 ancient_farm(vec3 c, vec2 p, vec2 site, vec2 seed, float turn, float fw, inout float cover) {
	float house = metres(14.0) * farm_scale(seed);
	float m;
	if (farm_variant(seed) >= 4) {
		float outer = block(p, site, vec2(house, house * 0.7), turn, fw);
		float court = block(p, site, vec2(house * 0.5, house * 0.3), turn, fw);
		m = outer - court;
	} else m = block(p, site, vec2(house * 0.4, house * 0.3), turn, fw);
	c = mix(c, ANCIENT_INK, m * 0.9);
	cover = max(cover, m);
	return c;
}

vec3 ancient_field(vec3 c, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float fw, float edge, out float cover) {
	p = curled(p);
	Lot l = lot_at(p, uv, to_uv, metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x)), 0.56, FIELD, own, levels);
	cover = 0.0;
	vec2 site_uv = uv + to_uv * (l.site - p);
	if (wet_field_at(site_uv, follow) > 0.5) return wet_field(ANCIENT, c, sheet, p, uv, to_uv, own, levels, fw);
	float use = hash(l.id + 9.4);
	if (use < 0.2) { float o = orchard(p, l, 0.18, fw); c = mix(c, ANCIENT_INK, o * 0.85); cover = o; }
	else if (use < 0.35) c = mix(c, ANCIENT_INK, vine_rows(p, l, along, fw) * 0.6);
	vec2 pair = l.id + l.other;
	c = mix(c, ANCIENT_INK, ink(l.edge, W1, fw) * step(hash(pair + 4.4), 0.1) * 0.7);
	if (l.room > metres(30.0) && hash(l.id + 8.2) < farms_here(l)) {
		float turn = hash(l.id + 11.3) * 3.1416;
		c = ancient_farm(c, p, l.site, l.id, turn, fw, cover);
		if (hamlet_here(l) > 0.5) c = ancient_farm(c, p, second_site(l), l.id + 21.0, turn + 0.4, fw, cover);
	}
	return c;
}

vec3 ancient(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, float near_town, float mid, float road_w, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover) {
	vec3 c = tint * (sheet / ANCIENT_PAPER);
	cover = 0.0;
	float rim = 1.0;
	float weight = W2;
	if (units_per_metre <= 0.0) return mix(c, ANCIENT_INK, ink(edge, W2, fw) * 0.6);
	if (k == TOWN) {
		return town_draw(ANCIENT, c, sheet, p, uv, to_uv, own, levels, road_w, fw, cover);
	} else if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own, levels);
		if (l.room > metres(20.0) && hash(l.id + 5.5) < 0.08) {
			float body;
			float o = pond(p, l, fw, body);
			c = mix(c, palette[ANCIENT * KINDS + WATER].rgb * (sheet / ANCIENT_PAPER), body);
			c = mix(c, ANCIENT_INK, o * 0.9);
		}
		rim = 0.0;
	} else if (k == FIELD) {
		c = ancient_field(c, tint, sheet, p, uv, to_uv, own, vec2(max(levels.x, near_town * NEAR_TOWN_PLOTS), levels.y), along, follow, fw, edge, cover);
		weight = W1;
	} else if (k == WOOD) {
		Lot s = lot_at(p, uv, to_uv, metres(STAND_M), 0.5, WOOD, own, levels);
		float open = clearing_at(p, own);
		c = mix(c, palette[ANCIENT * KINDS + GRASS].rgb * (sheet / ANCIENT_PAPER), open);
		c = ancient_wood(c, tint, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)) * 1.2, levels.x * (1.0 - near_town * NEAR_TOWN_THINS), cover);
		if (s.room >= 0.0) {
			float ride = (1.0 - smoothstep(metres(2.0), metres(2.0) + fw, s.edge)) * step(0.7, hash(s.id + 2.2)) * (1.0 - ledges);
			c = mix(c, sheet, ride * RIDE);
			if (open > 0.5 && hash(s.id + 3.9) < 0.5) c = mix(c, ANCIENT_INK, hut(p, s, fw));
		}
	} else if (k == MARSH) {
		c = mix(c, ANCIENT_INK, marks(p, own, 0.16, 0.05, W1 * 0.6, 0.0, 0.0, 0.8, fw) * 0.85);
		rim = 0.0;
	} else if (k == WATER) {
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own, levels);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < 0.08) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, palette[ANCIENT * KINDS + GRASS].rgb * (sheet / ANCIENT_PAPER), body);
			c = mix(c, ANCIENT_INK, o * 0.9);
		}
	} else if (k == ROAD || k == BRIDGE) {
		return sheet;
	} else if (k == EARTH) {
		Lot l = lot_at(p, uv, to_uv, metres(EARTH_LOT_M), 0.5, EARTH, own, levels);
		c = mix(c, ANCIENT_INK, scatter(p, own, 0.09, W1 * 0.5, 0.9, 0.35, fw) * 0.6);
		if (l.room >= 0.0) {
			if (hash(l.id + 4.1) < 0.4) c = mix(c, RED_OCHRE, ink(l.edge, W1, fw) * 0.8);
			if (hash(l.id + 7.7) < 0.3 && l.room > metres(12.0)) c = mix(c, ANCIENT_INK, pit(p, l, fw) * 0.8);
		}
	} else if (k == ROCK) {
		float chevrons = marks(p, own, 0.14, 0.05, W1 * 0.7, 0.0, 0.4, 0.7, fw);
		c = mix(c, ANCIENT_RIDGE, chevrons * 0.85);
		cover = chevrons;
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.5, border);
		c = mix(c, ANCIENT_INK, scatter(p, own, 0.05, W1 * 0.45, 0.95, 0.3 + 0.5 * near, fw) * 0.8);
		rim = 0.5;
	} else if (k == SNOW) {
		c = mix(c, ANCIENT_INK, max(hatch(p.x + p.y, 0.08, W1 * 0.3, fw), hatch(p.x - p.y, 0.08, W1 * 0.3, fw)) * 0.3);
	}
	return mix(c, ANCIENT_INK, ink(abs(edge - W1), weight, fw) * rim);
}
