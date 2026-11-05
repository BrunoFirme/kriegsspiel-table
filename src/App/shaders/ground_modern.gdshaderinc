vec3 modern_wood(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover) {
	float S = spacing;
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	float k = S / 0.62;
	vec3 black = vec3(0.09, 0.1, 0.09);
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
	return mix(c, black, m * 0.92);
}

vec3 modern_field(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float fw, out float cover) {
	vec3 black = vec3(0.09, 0.1, 0.09);
	Lot l = lot_at(p, uv, to_uv, metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x)), 0.56, FIELD, own, levels);
	cover = 0.0;
	c *= 1.0 - step(0.72, hash(l.id + 2.1)) * 0.035;
	float furrow;
	float strip_edge = strips(p, l, along, follow, fw, furrow);
	float use = hash(l.id + 9.4);
	if (use < 0.6) c = mix(c, black, strip_edge * 0.75);
	else if (use < 0.75) { float o = orchard(p, l, 0.11, fw); c = mix(c, black, o * 0.8); cover = o; }
	else if (use < 0.85) c = mix(c, black, vine_rows(p, l, along, fw) * 0.6);
	float bounded = smoothstep(0.15, 0.6, l.levels.x);
	int line = HEDGE + int(hash(l.id + 3.3) * 5.0);
	c = mix(c, black, edge_line(MODERN, line, l, p, fw, 0.0045, 0.009, 0.012) * mix(0.5, 0.9, bounded));
	c = mix(c, black, ink(l.edge, 0.009, fw) * 0.9 * (1.0 - bounded));
	if (l.room > metres(30.0) && hash(l.id + 8.2) < l.levels.y) {
		float yard, wooden;
		float farm = farmstead(p, l, fw, yard, wooden);
		c = mix(c, black, farm * 0.95);
		cover = max(cover, farm);
	}
	return c;
}

vec3 modern(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, float near_town, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover) {
	vec3 black = vec3(0.09, 0.1, 0.09);
	vec3 blue = vec3(0.12, 0.45, 0.72);
	vec3 brown = vec3(0.55, 0.36, 0.2);
	float mottle = (noise(p * 3.0) - 0.5) * 0.03;
	vec3 c = tint * (sheet / MODERN_PAPER) * (1.0 + mottle);
	cover = 0.0;
	if (units_per_metre <= 0.0) return mix(c, black, ink(edge, 0.012, fw) * 0.6);
	if (k == TOWN) {
		return town_draw(MODERN, c, sheet, p, uv, to_uv, own, levels, fw, cover);
	} else if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own, levels);
		float rough = step(0.6, hash(l.id + 1.1));
		float pair = 0.0;
		for (int j = 0; j <= 1; j++) {
			for (int i = 0; i <= 1; i++) {
				vec2 id;
				vec2 o = grid_point(p - 0.24, own, 0.48, 0.8, i, j, id) + 0.24;
				if (hash(id + 2.0) > 0.7) continue;
				for (int s = -1; s <= 1; s += 2) {
					vec2 b = o + vec2(float(s) * 0.018, 0.0);
					pair = max(pair, ink(segment(p, b, b + NORTH * 0.045), 0.008, fw));
				}
			}
		}
		pair = mix(pair, 0.01, far_blend(fw, 0.48));
		c = mix(c, black * 1.4, pair * 0.85);
		if (rough > 0.5) c = mix(c, black * 1.4, tufts(p + 0.4, own, 0.5, 0.06, 0.008, 0.45, fw) * 0.8);
		if (l.room >= 0.0) {
			float fenced = smoothstep(0.2, 0.6, l.levels.x) * step(0.65, hash(l.id + 7.2));
			c = mix(c, black, edge_line(MODERN, FENCE, l, p, fw, 0.0045, 0.009, 0.012) * fenced * 0.9);
			float what = hash(l.id + 5.5);
			float body;
			if (what < 0.08 && l.room > metres(20.0)) {
				float o = pond(p, l, fw, body);
				c = mix(c, palette[MODERN * KINDS + WATER].rgb * (sheet / MODERN_PAPER), body);
				c = mix(c, blue, o);
			} else if (what < 0.2 && l.room > metres(8.0)) {
				float o = lone_tree(p, l, fw, body);
				c = mix(c, black, o * 0.9);
				cover = max(cover, body);
			}
		}
	} else if (k == FIELD) {
		c = modern_field(c, p, uv, to_uv, own, vec2(max(levels.x, near_town * NEAR_TOWN_PLOTS), levels.y), along, follow, fw, cover);
	} else if (k == WOOD) {
		Lot s = lot_at(p, uv, to_uv, metres(STAND_M), 0.5, WOOD, own, levels);
		float open = clearing_at(p, own);
		c = mix(c, palette[MODERN * KINDS + GRASS].rgb * (sheet / MODERN_PAPER), open);
		c = modern_wood(c, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)) * 1.3, levels.x * (1.0 - near_town * NEAR_TOWN_THINS), cover);
		float dots = screen(p + 0.02, own, 0.085, 0.017, fw);
		c = mix(c, black, dots * (1.0 - smoothstep(0.015, 0.04, edge)) * 0.95);
		if (s.room >= 0.0) {
			float ride = (1.0 - smoothstep(metres(2.0), metres(2.0) + fw, s.edge)) * step(0.7, hash(s.id + 2.2)) * (1.0 - ledges);
			c = mix(c, sheet, ride * RIDE);
			if (open > 0.5 && hash(s.id + 3.9) < 0.5) c = mix(c, black, hut(p, s, fw));
		}
	} else if (k == MARSH) {
		Lot l = lot_at(p, uv, to_uv, metres(MARSH_LOT_M), 0.5, MARSH, own, levels);
		c = mix(c, blue, marks(p, own, 0.17, 0.065, 0.009, 0.0, 0.05, 0.9, fw) * 0.85);
		c = mix(c, blue * 0.6, tufts(p + 0.4, own, 0.5, 0.06, 0.008, 0.55, fw) * 0.85);
		if (l.room >= 0.0 && hash(l.id + 4.4) < 0.5) c = mix(c, blue, dashed(dot(p - l.site, l.across), l.edge, metres(6.0), 0.6, 0.006, fw) * 0.8);
	} else if (k == WATER) {
		c = mix(c, blue, hatch(p.y, 0.1, 0.006, fw) * 0.45);
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own, levels);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < 0.08) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, palette[MODERN * KINDS + GRASS].rgb * (sheet / MODERN_PAPER), body);
			c = mix(c, blue, o);
		}
		return mix(c, blue, ink(edge, 0.02, fw));
	} else if (k == ROAD) {
		return mix(c, black, ink(edge, 0.02, fw));
	} else if (k == BRIDGE) {
		float wall = (beside == WATER ? 1.0 : 0.0) * (1.0 - smoothstep(PARAPET - fw, PARAPET, edge));
		c = mix(c, MODERN_PAPER, wall);
		return mix(c, black, max(ink(edge, 0.03, fw), ink(abs(edge - PARAPET), 0.015, fw) * (beside == WATER ? 1.0 : 0.0)));
	} else if (k == EARTH) {
		Lot l = lot_at(p, uv, to_uv, metres(EARTH_LOT_M), 0.5, EARTH, own, levels);
		c = mix(c, brown, screen(p, own, 0.06, 0.012, fw) * 0.75);
		if (l.room >= 0.0) {
			if (hash(l.id + 4.1) < 0.4) c = mix(c, black, edge_line(MODERN, TRACK, l, p, fw, 0.0045, 0.009, 0.012) * 0.8);
			if (hash(l.id + 7.7) < 0.3 && l.room > metres(12.0)) c = mix(c, black, pit(p, l, fw) * 0.9);
		}
	} else if (k == ROCK) {
		c = mix(c, black, marks(p, own, 0.15, 0.045, 0.01, 1.9, 0.9, 0.8, fw) * 0.8);
		c = mix(c, black, scatter(p + 1.3, own, 0.1, 0.009, 0.9, 0.5, fw) * 0.7);
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.6, border);
		c = mix(c, brown, scatter(p, own, 0.06, 0.009, 0.95, 0.5 + 0.35 * near, fw) * 0.9);
		c = mix(c, brown, dunes(p, along, fw) * 0.5);
	} else if (k == SNOW) {
		c = mix(c, blue, marks(p, own, 0.35, 0.06, 0.007, 0.1, 0.4, 0.4, fw) * 0.7);
		c = mix(c, blue, screen(p, own, 0.07, 0.012, fw) * (1.0 - smoothstep(0.0, 0.08, edge)) * 0.8);
	}
	return c;
}
