vec3 modern_wood(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover) {
	float closed = smoothstep(CLOSED_FROM, CLOSED_FULL, trees);
	float S = spacing * mix(1.0, 0.72, closed);
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	float m = 0.0;
	if (far_blend(fw, S) < 1.0) {
		for (int j = -1; j <= 1; j++) {
			for (int i = -1; i <= 1; i++) {
				vec2 id;
				float salt;
				vec2 o = grid_point_at(p, uv, to_uv, S, 0.3, i, j, id, salt);
				float room = room_at(o, p, uv, to_uv, WOOD, salt);
				float k = GLYPH * mix(0.85, 1.1, hash(id + 3.1));
				if (hash(id + 5.0) > 0.97) continue;
				if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
				if (room < 0.1 * k) continue;
				if (hash(id + 1.3) < clearing_at(o, salt) * (1.0 - closed) || hash(id + 2.4) > keep) continue;
				k *= mix(1.0, 0.7, coppice_at(o, salt));
				m = max(m, crown_sign(p, o - NORTH * 0.25 * k, 0.26 * k, step(hash(id + 6.1), conifer_share(o, salt)), 0.3 * k, M1 * 0.6, fw, id));
			}
		}
	}
	cover = m;
	m = mix(m, 0.09, far_blend(fw, S));
	return mix(c, MODERN_BLACK, m * 0.85);
}

vec3 modern_farm(vec3 c, vec2 p, vec2 site, vec2 seed, float turn, float fw, inout float cover) {
	float yard, court;
	float farm = farm_blocks(p, site, seed, turn, farm_scale(seed), fw, yard, court);
	c = mix(c, MODERN_BLACK, hatch(dot(p, vec2(0.7071, 0.7071)), metres(2.5), M1 * 0.3, fw) * max(yard, court) * (1.0 - farm) * 0.6);
	c = mix(c, MODERN_BLACK, farm * 0.95);
	cover = max(cover, farm);
	return c;
}

vec3 modern_field(vec3 c, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float fw, out float cover) {
	p = curled(p);
	Lot l = lot_at(p, uv, to_uv, metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x)), 0.56, FIELD, own, levels);
	cover = 0.0;
	float use = hash(l.id + 9.4);
	float small = step(l.room, metres(ORCHARD_LOT_M));
	if (small > 0.5 && use >= 0.75 && use < 0.79) c = mix(c, MODERN_BLACK, vine_strokes(p, 0.45, M1 * 0.45, fw) * 0.85);
	float wet = wet_meadow(uv, follow);
	if (wet > 0.001) {
		c = mix(c, sheet, wet * 0.8);
		c = mix(c, MODERN_BLACK, marks(p, own, MEADOW_ROW * 0.5, MEADOW_LEN, M1 * 0.4, 0.0, 0.0, 0.6, fw) * 0.85 * wet);
		c = mix(c, MODERN_BLUE, tufts(p + 0.9, own, MEADOW_ROW * 0.6, 0.1, M1 * 0.4, 0.5, fw) * 0.8 * wet);
	}
	float bounded = smoothstep(ENCLOSED_FROM, ENCLOSED_FULL, l.levels.x);
	vec2 pair = l.id + l.other;
	float marked = step(hash(pair + 4.4), bounded * 0.8);
	int line = HEDGE + int(hash(pair + 1.1) * 4.0);
	c = mix(c, MODERN_BLACK, edge_line(MODERN, line, l, p, fw, M1 * 0.5, M1, M2) * marked * mix(0.6, 0.9, hash(pair + 2.2)));
	return c;
}

vec3 plate_spills(vec3 c, vec3 sheet, int k, int beside, vec2 p, float edge, float fw) {
	if (beside == WOOD && k != WOOD) c = mix(c, MODERN_GREEN, WOOD_TINT * plate_spill(edge, PLATE_GREEN, fw));
	if (beside == WATER && k != WATER) c = mix(c, MODERN_BLUE, WATER_TINT * plate_spill(edge, PLATE_BLUE, fw));
	return c;
}

vec3 modern(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, float near_town, float mid, float road_w, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover) {
	vec3 c = tint * (sheet / MODERN_PAPER);
	cover = 0.0;
	if (units_per_metre <= 0.0) return mix(c, MODERN_BLACK, ink(edge, M1, fw) * 0.6);
	if (k == TOWN) {
		c = mix(sheet, MODERN_BLACK, TOWN_TINT);
		return town_draw(MODERN, c, sheet, p, uv, to_uv, own, levels, road_w, fw, cover);
	} else if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own, levels);
		float rough = step(0.6, hash(l.id + 1.1));
		float wet = wet_meadow(uv, 0.0);
		c = mix(c, MODERN_GREY, marks(p, own, MEADOW_ROW * 0.7, MEADOW_LEN, M1 * 0.4, 0.0, 0.0, mix(0.35, 0.5, rough), fw) * 0.8);
		if (rough > 0.5) c = mix(c, MODERN_GREY, tufts(p + 0.4, own, MEADOW_ROW, 0.12, M1 * 0.4, 0.4, fw) * 0.8);
		c = mix(c, MODERN_BLUE, tufts(p + 0.9, own, MEADOW_ROW * 0.6, 0.1, M1 * 0.4, 0.5, fw) * 0.8 * wet);
		if (l.room >= 0.0) {
			float fenced = smoothstep(0.2, 0.6, max(l.levels.x, near_town)) * step(mix(0.65, 0.3, near_town), hash(l.id + 7.2));
			c = mix(c, MODERN_BLACK, edge_line(MODERN, FENCE, l, p, fw, M1 * 0.5, M1, M2) * fenced * 0.9);
			float what = hash(l.id + 5.5);
			float body;
			if (what < 0.08 && l.room > metres(20.0)) {
				float o = pond(p, l, fw, body);
				c = mix(c, mix(sheet, MODERN_BLUE, WATER_TINT), body);
				c = mix(c, MODERN_BLUE, o);
			} else if (what < 0.2 && l.room > metres(8.0)) {
				float o = lone_tree(p, l, fw, body);
				c = mix(c, MODERN_BLACK, o * 0.9);
				cover = max(cover, body);
			}
		}
	} else if (k == FIELD) {
		c = modern_field(c, sheet, p, uv, to_uv, own, vec2(max(levels.x, near_town * NEAR_TOWN_PLOTS), levels.y), along, follow, fw, cover);
	} else if (k == WOOD) {
		float open = clearing_at(p, own) * (1.0 - smoothstep(CLOSED_FROM, CLOSED_FULL, levels.x));
		c = mix(mix(sheet, MODERN_GREEN, WOOD_TINT * mix(0.85, 1.4, levels.x)), sheet, open);
		c = modern_wood(c, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)) * MODERN_PITCH, levels.x * (1.0 - near_town * NEAR_TOWN_THINS), cover);
		c = mix(c, MODERN_BLACK, dotted_line(p, uv, to_uv, M1 * 2.0, 0.2, M1 * 0.6, fw) * 0.95);
		c = mix(c, MODERN_BLACK, rides(p, levels.x, M1 * 0.4, metres(6.0), fw) * (1.0 - ledges) * 0.9);
		if (open > 0.5) {
			Lot s = lot_at(p, uv, to_uv, metres(STAND_M), 0.5, WOOD, own, levels);
			if (s.room >= 0.0 && hash(s.id + 3.9) < 0.5) c = mix(c, MODERN_BLACK, hut(p, s, fw));
		}
	} else if (k == MARSH) {
		Lot l = lot_at(p, uv, to_uv, metres(MARSH_LOT_M), 0.5, MARSH, own, levels);
		c = mix(c, MODERN_BLUE, marks(p + PLATE_BLUE * fw, own, MEADOW_ROW * 0.5, MEADOW_LEN, M1 * 0.45, 0.0, 0.05, 0.9, fw) * 0.9);
		c = mix(c, MODERN_BLUE, tufts(p + 0.4 + PLATE_BLUE * fw, own, MEADOW_ROW, 0.12, M1 * 0.4, 0.5, fw) * 0.9);
		if (l.room >= 0.0 && hash(l.id + 4.4) < 0.5) c = mix(c, MODERN_BLUE, dashed(dot(p - l.site, l.across), l.edge, metres(6.0), 0.6, M1 * 0.5, fw) * 0.8);
	} else if (k == WATER) {
		float blue = plate_fill(edge, PLATE_BLUE, fw);
		c = mix(sheet, MODERN_BLUE, WATER_TINT * blue);
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own, levels);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < ISLET_SHARE) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, sheet, body);
			c = mix(c, MODERN_BLUE, o);
		}
		return mix(c, MODERN_BLUE, ink(abs(plate_edge(edge, PLATE_BLUE)), M2, fw));
	} else if (k == ROAD || k == BRIDGE) {
		return sheet;
	} else if (k == EARTH) {
		Lot l = lot_at(p, uv, to_uv, metres(EARTH_LOT_M), 0.5, EARTH, own, levels);
		c = mix(c, MODERN_BROWN, screen(p + PLATE_BROWN * fw, own, 0.09, M1 * 0.5, fw) * 0.75);
		if (l.room >= 0.0) {
			if (hash(l.id + 4.1) < 0.4) c = mix(c, MODERN_BLACK, edge_line(MODERN, TRACK, l, p, fw, M1 * 0.5, M1, M2) * 0.8);
			if (hash(l.id + 7.7) < 0.3 && l.room > metres(12.0)) c = mix(c, MODERN_BLACK, pit(p, l, fw) * 0.9);
		}
	} else if (k == ROCK) {
		c = mix(c, MODERN_BLACK, marks(p, own, 0.22, 0.07, M1 * 0.6, 1.9, 0.9, 0.8, fw) * 0.8);
		c = mix(c, MODERN_BLACK, scatter(p + 1.3, own, 0.14, M1 * 0.6, 0.9, 0.5, fw) * 0.7);
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.6, border);
		c = mix(c, MODERN_BROWN, scatter(p + PLATE_BROWN * fw, own, 0.08, M1 * 0.3, 0.95, 0.5 + 0.35 * near, fw) * 0.9);
		c = mix(c, MODERN_BROWN, scatter(p + 2.2 + PLATE_BROWN * fw, own, 0.25, M1 * 0.7, 0.9, 0.5, fw) * 0.9);
		c = mix(c, MODERN_BROWN, dunes(p, along, fw) * 0.5);
	} else if (k == SNOW) {
		c = mix(c, MODERN_BLUE, marks(p, own, 0.45, 0.12, M1 * 0.4, 0.1, 0.4, 0.4, fw) * 0.7);
	}
	return plate_spills(c, sheet, k, beside, p, edge, fw);
}
