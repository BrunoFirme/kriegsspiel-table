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

vec3 modern_field(vec3 c, vec3 sheet, vec2 p, vec2 uv, float own, float follow, float fw) {
	float tone;
	float fallow;
	float balk;
	float bound;
	fields(p, own, vec2(0.0), 0.0, M1 * 0.4, fw, tone, fallow, balk, bound);
	c = mix(c, MODERN_GREY, max(balk * FIELD_LINES * 0.6, bound * FIELD_LINES));
	float wet = wet_meadow(uv, follow);
	if (wet > 0.001) {
		c = mix(c, sheet, wet * 0.8);
		c = mix(c, MODERN_BLACK, marks(p, own, MEADOW_ROW * 0.5, MEADOW_LEN, M1 * 0.4, 0.0, 0.0, 0.6, fw) * 0.85 * wet);
		c = mix(c, MODERN_BLUE, tufts(p + 0.9, own, MEADOW_ROW * 0.6, 0.1, M1 * 0.4, 0.5, fw) * 0.8 * wet);
	}
	return c;
}

vec3 plate_spills(vec3 c, vec3 sheet, int k, int beside, vec2 p, float edge, float fw) {
	if (beside == WOOD && k != WOOD) c = mix(c, MODERN_GREEN, WOOD_TINT * plate_spill(edge, PLATE_GREEN, fw));
	if (beside == WATER && k != WATER) c = mix(c, MODERN_BLUE, WATER_TINT * plate_spill(edge, PLATE_BLUE, fw));
	return c;
}

vec3 modern(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover) {
	vec3 c = tint * (sheet / MODERN_PAPER);
	cover = 0.0;
	if (units_per_metre <= 0.0) return mix(c, MODERN_BLACK, ink(edge, M1, fw) * 0.6);
	if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own);
		float rough = step(0.6, hash(l.id + 1.1));
		float wet = wet_meadow(uv, 0.0);
		c = mix(c, MODERN_GREY, marks(p, own, MEADOW_ROW * 0.7, MEADOW_LEN, M1 * 0.4, 0.0, 0.0, mix(0.35, 0.5, rough), fw) * 0.8);
		if (rough > 0.5) c = mix(c, MODERN_GREY, tufts(p + 0.4, own, MEADOW_ROW, 0.12, M1 * 0.4, 0.4, fw) * 0.8);
		c = mix(c, MODERN_BLUE, tufts(p + 0.9, own, MEADOW_ROW * 0.6, 0.1, M1 * 0.4, 0.5, fw) * 0.8 * wet);
		if (l.room >= 0.0) {
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
		c = modern_field(c, sheet, p, uv, own, follow, fw);
	} else if (k == WOOD) {
		float open = clearing_at(p, own) * (1.0 - smoothstep(CLOSED_FROM, CLOSED_FULL, levels.x));
		c = mix(mix(sheet, MODERN_GREEN, WOOD_TINT * mix(0.85, 1.4, levels.x)), sheet, open);
		c = modern_wood(c, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)) * MODERN_PITCH, levels.x, cover);
		c = mix(c, MODERN_BLACK, dotted_line(p, uv, to_uv, M1 * 2.0, 0.2, M1 * 0.6, fw) * 0.95);
	} else if (k == MARSH) {
		Lot l = lot_at(p, uv, to_uv, metres(MARSH_LOT_M), 0.5, MARSH, own);
		c = mix(c, MODERN_BLUE, marks(p + PLATE_BLUE * fw, own, MEADOW_ROW * 0.5, MEADOW_LEN, M1 * 0.45, 0.0, 0.05, 0.9, fw) * 0.9);
		c = mix(c, MODERN_BLUE, tufts(p + 0.4 + PLATE_BLUE * fw, own, MEADOW_ROW, 0.12, M1 * 0.4, 0.5, fw) * 0.9);
		if (l.room >= 0.0 && hash(l.id + 4.4) < 0.5) c = mix(c, MODERN_BLUE, dashed(dot(p - l.site, l.across), l.edge, metres(6.0), 0.6, M1 * 0.5, fw) * 0.8);
	} else if (k == WATER) {
		float blue = plate_fill(edge, PLATE_BLUE, fw);
		c = mix(sheet, MODERN_BLUE, WATER_TINT * blue);
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < ISLET_SHARE) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, sheet, body);
			c = mix(c, MODERN_BLUE, o);
		}
		return mix(c, MODERN_BLUE, ink(abs(plate_edge(edge, PLATE_BLUE)), M2, fw));
	} else if (k == EARTH) {
		c = mix(c, MODERN_BROWN, screen(p + PLATE_BROWN * fw, own, 0.09, M1 * 0.5, fw) * 0.75);
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
