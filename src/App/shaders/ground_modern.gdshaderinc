vec3 modern_wood(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover) {
	float S = spacing;
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	float k = S / 0.62;
	float m = 0.0;
	if (far_blend(fw, S) < 1.0) {
		for (int j = -1; j <= 1; j++) {
			for (int i = -1; i <= 1; i++) {
				vec2 id;
				float salt;
				vec2 o = grid_point_at(p, uv, to_uv, S, 0.7, i, j, id, salt);
				float room = room_at(o, p, uv, to_uv, WOOD, salt);
				if (hash(id + 5.0) > 0.82) continue;
				if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
				if (room < 0.1 * k) continue;
				if (hash(id + 1.3) < clearing_at(o, salt) || hash(id + 2.4) > keep) continue;
				if (coppice_at(o, salt) > 0.5) {
					m = max(m, ink(abs(length(p - o) - 0.02 * k), 0.009 * k, fw));
					m = max(m, ink(segment(p, o + vec2(0.02, 0.0) * k, o + vec2(0.035, -0.012) * k), 0.008 * k, fw));
					continue;
				}
				if (hash(id + 6.1) < conifer_share(o, salt)) {
					vec2 top = o + NORTH * 0.085 * k;
					m = max(m, ink(segment(p, top, o + vec2(-0.05, 0.015) * k), 0.011 * k, fw));
					m = max(m, ink(segment(p, top, o + vec2(0.05, 0.015) * k), 0.011 * k, fw));
					m = max(m, ink(segment(p, top, o - NORTH * 0.06 * k), 0.011 * k, fw));
				} else {
					vec2 ring = o + NORTH * 0.035 * k;
					m = max(m, ink(abs(length(p - ring) - 0.052 * k), 0.011 * k, fw));
					m = max(m, ink(segment(p, ring - NORTH * 0.052 * k, o - NORTH * 0.065 * k), 0.011 * k, fw));
				}
			}
		}
	}
	cover = m;
	m = mix(m, 0.06, far_blend(fw, S));
	return mix(c, MODERN_BLACK, m * 0.92);
}

vec3 modern_field(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float fw, out float cover) {
	Lot l = lot_at(p, uv, to_uv, metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x)), 0.56, FIELD, own, levels);
	cover = 0.0;
	float furrow;
	float strip_edge = strips(p, l, along, follow, fw, furrow);
	float use = hash(l.id + 9.4);
	if (use < 0.6) c = mix(c, MODERN_BLACK, strip_edge * 0.75);
	else if (use < 0.75) { float o = orchard(p, l, 0.11, fw); c = mix(c, MODERN_BLACK, o * 0.8); cover = o; }
	else if (use < 0.85) c = mix(c, MODERN_BLACK, vine_rows(p, l, along, fw) * 0.6);
	float bounded = smoothstep(0.15, 0.6, l.levels.x);
	int line = HEDGE + int(hash(l.id + 3.3) * 5.0);
	c = mix(c, MODERN_BLACK, edge_line(MODERN, line, l, p, fw, W1 * 0.5, W1, W2) * mix(0.5, 0.9, bounded));
	c = mix(c, MODERN_BLACK, ink(l.edge, W1, fw) * 0.9 * (1.0 - bounded));
	if (l.room > metres(30.0) && hash(l.id + 8.2) < l.levels.y) {
		float yard, wooden;
		float farm = farmstead(p, l, fw, yard, wooden);
		c = mix(c, MODERN_BLACK, hatch(dot(p, vec2(0.7071, 0.7071)), metres(2.5), W1 * 0.4, fw) * yard * (1.0 - farm) * 0.6);
		c = mix(c, MODERN_BLACK, farm * 0.95);
		cover = max(cover, farm);
	}
	return c;
}

vec3 modern_road(vec3 c, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float class, float edge, float fw) {
	if (class < 0.33) return mix(c, MODERN_BLACK, dotted_line(p, uv, to_uv, W1 * 2.0, 0.08, W1 * 0.7, fw));
	if (class < 0.66) return mix(c, MODERN_BLACK, ink(edge, W2, fw));
	c = mix(c, MODERN_RED, smoothstep(W3 - fw, W3 + fw, edge));
	return mix(c, MODERN_BLACK, ink(edge, W3, fw));
}

vec3 modern(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, float near_town, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover) {
	vec3 c = tint * (sheet / MODERN_PAPER);
	cover = 0.0;
	if (units_per_metre <= 0.0) return mix(c, MODERN_BLACK, ink(edge, W1, fw) * 0.6);
	if (k == TOWN) {
		c = mix(sheet, MODERN_BLACK, dot_screen(p, 0.0, SCREEN_PITCH, 0.1, fw));
		return town_draw(MODERN, c, sheet, p, uv, to_uv, own, levels, fw, cover);
	} else if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own, levels);
		float rough = step(0.6, hash(l.id + 1.1));
		c = mix(c, MODERN_BLACK, marks(p, own, 0.32, 0.045, W1 * 0.5, 0.0, 0.0, mix(0.4, 0.6, rough), fw) * 0.8);
		if (rough > 0.5) c = mix(c, MODERN_BLACK, tufts(p + 0.4, own, 0.5, 0.06, W1 * 0.55, 0.45, fw) * 0.8);
		if (l.room >= 0.0) {
			float fenced = smoothstep(0.2, 0.6, max(l.levels.x, near_town)) * step(mix(0.65, 0.3, near_town), hash(l.id + 7.2));
			c = mix(c, MODERN_BLACK, edge_line(MODERN, FENCE, l, p, fw, W1 * 0.5, W1, W2) * fenced * 0.9);
			float what = hash(l.id + 5.5);
			float body;
			if (what < 0.08 && l.room > metres(20.0)) {
				float o = pond(p, l, fw, body);
				c = mix(c, mix(sheet, MODERN_BLUE, dot_screen(p, 0.7854, SCREEN_PITCH, 0.25, fw)), body);
				c = mix(c, MODERN_BLUE, o);
			} else if (what < 0.2 && l.room > metres(8.0)) {
				float o = lone_tree(p, l, fw, body);
				c = mix(c, MODERN_BLACK, o * 0.9);
				cover = max(cover, body);
			}
		}
	} else if (k == FIELD) {
		c = modern_field(c, p, uv, to_uv, own, vec2(max(levels.x, near_town * NEAR_TOWN_PLOTS), levels.y), along, follow, fw, cover);
	} else if (k == WOOD) {
		Lot s = lot_at(p, uv, to_uv, metres(STAND_M), 0.5, WOOD, own, levels);
		float open = clearing_at(p, own);
		c = mix(mix(sheet, MODERN_GREEN, dot_screen(p, 0.2618, SCREEN_PITCH, 0.25, fw)), sheet, open);
		c = modern_wood(c, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)) * 1.3, levels.x * (1.0 - near_town * NEAR_TOWN_THINS), cover);
		c = mix(c, MODERN_BLACK, dotted_line(p, uv, to_uv, W1 * 2.0, 0.06, W1 * 0.6, fw) * 0.95);
		if (s.room >= 0.0) {
			float ride = (1.0 - smoothstep(metres(2.0), metres(2.0) + fw, s.edge)) * step(0.7, hash(s.id + 2.2)) * (1.0 - ledges);
			c = mix(c, sheet, ride * RIDE);
			if (open > 0.5 && hash(s.id + 3.9) < 0.5) c = mix(c, MODERN_BLACK, hut(p, s, fw));
		}
	} else if (k == MARSH) {
		Lot l = lot_at(p, uv, to_uv, metres(MARSH_LOT_M), 0.5, MARSH, own, levels);
		c = mix(c, MODERN_BLUE, marks(p, own, 0.17, 0.065, W1 * 0.6, 0.0, 0.05, 0.9, fw) * 0.9);
		c = mix(c, MODERN_BLUE, tufts(p + 0.4, own, 0.5, 0.06, W1 * 0.55, 0.55, fw) * 0.9);
		if (l.room >= 0.0 && hash(l.id + 4.4) < 0.5) c = mix(c, MODERN_BLUE, dashed(dot(p - l.site, l.across), l.edge, metres(6.0), 0.6, W1 * 0.5, fw) * 0.8);
	} else if (k == WATER) {
		c = mix(sheet, MODERN_BLUE, dot_screen(p, 0.7854, SCREEN_PITCH, 0.25, fw));
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own, levels);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < 0.08) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, sheet, body);
			c = mix(c, MODERN_BLUE, o);
		}
		return mix(c, MODERN_BLUE, ink(edge, W2, fw));
	} else if (k == ROAD) {
		return modern_road(c, sheet, p, uv, to_uv, levels.x, edge, fw);
	} else if (k == BRIDGE) {
		float wall = (beside == WATER ? 1.0 : 0.0) * (1.0 - smoothstep(PARAPET - fw, PARAPET, edge));
		c = mix(c, sheet, wall);
		return mix(c, MODERN_BLACK, max(ink(edge, levels.x < 0.66 ? W2 : W3, fw), ink(abs(edge - PARAPET), W1, fw) * (beside == WATER ? 1.0 : 0.0)));
	} else if (k == EARTH) {
		Lot l = lot_at(p, uv, to_uv, metres(EARTH_LOT_M), 0.5, EARTH, own, levels);
		c = mix(c, MODERN_BROWN, screen(p, own, 0.06, W1 * 0.6, fw) * 0.75);
		if (l.room >= 0.0) {
			if (hash(l.id + 4.1) < 0.4) c = mix(c, MODERN_BLACK, edge_line(MODERN, TRACK, l, p, fw, W1 * 0.5, W1, W2) * 0.8);
			if (hash(l.id + 7.7) < 0.3 && l.room > metres(12.0)) c = mix(c, MODERN_BLACK, pit(p, l, fw) * 0.9);
		}
	} else if (k == ROCK) {
		c = mix(c, MODERN_BLACK, marks(p, own, 0.15, 0.045, W1 * 0.8, 1.9, 0.9, 0.8, fw) * 0.8);
		c = mix(c, MODERN_BLACK, scatter(p + 1.3, own, 0.1, W1 * 0.7, 0.9, 0.5, fw) * 0.7);
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.6, border);
		c = mix(c, MODERN_BROWN, scatter(p, own, 0.05, W1 * 0.45, 0.95, 0.5 + 0.35 * near, fw) * 0.9);
		c = mix(c, MODERN_BROWN, scatter(p + 2.2, own, 0.17, W1 * 0.9, 0.9, 0.5, fw) * 0.9);
		c = mix(c, MODERN_BROWN, dunes(p, along, fw) * 0.5);
	} else if (k == SNOW) {
		c = mix(c, MODERN_BLUE, marks(p, own, 0.35, 0.06, W1 * 0.5, 0.1, 0.4, 0.4, fw) * 0.7);
	}
	return c;
}
