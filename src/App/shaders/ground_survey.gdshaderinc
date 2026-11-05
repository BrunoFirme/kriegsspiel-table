vec3 survey_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float bump, out float cover) {
	float S = spacing;
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	vec3 sepia = vec3(0.2, 0.17, 0.1);
	vec3 lit = tint * vec3(1.22, 1.2, 0.96);
	vec3 mid = tint * 0.88;
	vec3 dark = tint * vec3(0.42, 0.5, 0.56);
	bump = 0.0;
	cover = 0.0;
	if (far_blend(fw, S) >= 1.0) return mix(c, tint, 0.85);
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point_at(p, uv, to_uv, S, 0.62, i, j, id, salt);
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < 0.0) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			if (hash(id + 1.3) < clearing_at(o, salt) || hash(id + 2.4) > keep) continue;
			float small = coppice_at(o, salt);
			float r = min(S * mix(0.45, 0.58, trees) * mix(1.0, 0.55, small) * (0.78 + 0.44 * hash(id + 3.1)), room * 0.95 + 0.03);
			vec2 q = p - o;
			float d = length(q);
			if (d > r * 1.75) continue;
			if (hash(id + 6.1) < conifer_share(o, salt) && small < 0.5) {
				float m = spruce(p, o, r, S * 0.03, fw);
				col = mix(col, sepia, m * 0.9);
				cover = max(cover, m);
				continue;
			}
			float a = atan(q.y, q.x);
			float h = hash(id + 7.7) * TAU;
			float rr = r * (1.0 + 0.12 * sin(a * 5.0 + h) + 0.07 * sin(a * 9.0 + h * 2.3) + 0.04 * sin(a * 15.0 + h * 4.1));
			float body = 1.0 - smoothstep(rr - fw * 0.75, rr + fw * 0.25, d);
			float cast = (1.0 - smoothstep(r * 0.95 - fw, r * 0.95 + fw, length(q - vec2(0.3, 0.34) * r))) * (1.0 - body);
			col = mix(col, col * vec3(0.55, 0.62, 0.62), cast * 0.6);
			float side = dot(q / max(rr, 1e-4), -SUN);
			float speck = noise(q * 38.0 + h) - 0.5;
			vec3 crown = mix(lit, mid, smoothstep(-0.75, 0.05, side + speck * 0.5));
			crown = mix(crown, dark, smoothstep(0.1, 0.9, side + speck * 0.35));
			col = mix(col, crown, body);
			col = mix(col, sepia, ink(abs(d - rr + 0.006), 0.009, fw) * smoothstep(-0.1, 0.6, side) * 0.75);
			bump = max(bump, body * sqrt(max(0.0, 1.0 - d * d / (rr * rr))) * rr);
			cover = max(cover, body);
		}
	}
	return mix(col, mix(c, tint, 0.85), far_blend(fw, S));
}

vec3 survey_field(vec3 c, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float fw, float edge, out float cover) {
	vec3 sepia = vec3(0.3, 0.2, 0.12);
	Lot l = lot_at(p, uv, to_uv, metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x)), 0.56, FIELD, own, levels);
	float variant = hash(l.id + 2.1);
	vec3 crop = variant < 0.35 ? vec3(0.92, 0.84, 0.64) : variant < 0.7 ? vec3(0.93, 0.89, 0.74) : vec3(0.86, 0.86, 0.66);
	c = wash(tint * mix(vec3(1.0), crop / vec3(0.9, 0.86, 0.68), 0.5), sheet, SURVEY_PAPER, p, edge, 0.5);
	cover = 0.0;
	float furrow;
	float strip_edge = strips(p, l, along, follow, fw, furrow);
	float use = hash(l.id + 9.4);
	if (use < 0.55) {
		c = mix(c, sepia, furrow * 0.2);
		c = mix(c, sepia, strip_edge * 0.45);
	} else if (use < 0.7) {
		float o = orchard(p, l, 0.22, fw);
		c = mix(c, tint * vec3(0.6, 0.72, 0.5), o * 0.85);
		cover = o;
	} else if (use < 0.8) {
		c = wash(tint * vec3(1.0, 0.95, 0.75), sheet, SURVEY_PAPER, p, edge, 0.5);
		c = mix(c, sepia, vine_rows(p, l, along, fw) * 0.75);
	} else if (use < 0.9) c = mix(c, sepia, scatter(p, own, metres(6.0), metres(0.4), 0.9, 0.35, fw) * 0.45);
	float bounded = smoothstep(0.15, 0.6, l.levels.x);
	int line = HEDGE + int(hash(l.id + 3.3) * 5.0);
	c = mix(c, sepia, edge_line(SURVEY, line, l, p, fw, 0.008, 0.012, 0.02) * mix(0.3, 0.9, bounded));
	c = mix(c, sepia, ink(l.edge, 0.012, fw) * 0.75 * (1.0 - bounded));
	if (l.room > metres(30.0) && hash(l.id + 8.2) < l.levels.y) {
		float yard, wooden;
		float farm = farmstead(p, l, fw, yard, wooden);
		c = mix(c, vec3(0.9, 0.78, 0.55), yard * 0.6);
		c = mix(c, vec3(0.6, 0.25, 0.2), farm * (1.0 - wooden * 0.6));
		c = mix(c, sepia, haystacks(p, l, fw) * 0.7);
		cover = max(cover, max(farm, yard * 0.5));
	}
	if (l.room > metres(15.0) && hash(l.id + 4.9) < l.levels.y * 0.15) {
		float m = windmill(p, l, fw);
		c = mix(c, sepia, m);
		cover = max(cover, m);
	}
	return c;
}

vec3 survey(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, float near_town, vec2 along, float follow, float ledges, float fw, float edge, float border, out float bump, out float cover) {
	vec3 sepia = vec3(0.3, 0.2, 0.12);
	vec3 prussian = vec3(0.15, 0.3, 0.45);
	vec3 c = wash(tint, sheet, SURVEY_PAPER, p, edge, 1.0);
	bump = 0.0;
	cover = 0.0;
	float rim = 0.0;
	vec3 rim_ink = sepia;
	if (units_per_metre <= 0.0) return mix(c, sepia, ink(edge, 0.012, fw) * 0.6);
	if (k == TOWN) {
		return town_draw(SURVEY, c, sheet, p, uv, to_uv, own, levels, fw, cover);
	} else if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own, levels);
		float rough = step(0.6, hash(l.id + 1.1));
		c = mix(c, tint * vec3(0.5, 0.66, 0.42), marks(p, own, 0.22, 0.05, 0.011, 0.0, 0.35, mix(0.75, 1.0, rough), fw) * 0.8);
		if (rough > 0.5) c = mix(c, tint * vec3(0.45, 0.6, 0.45), tufts(p, own, 0.5, 0.06, 0.009, 0.5, fw) * 0.7);
		if (l.room >= 0.0) {
			float fenced = smoothstep(0.2, 0.6, l.levels.x) * step(0.65, hash(l.id + 7.2));
			c = mix(c, sepia, edge_line(SURVEY, HEDGE + int(hash(l.id + 3.3) * 2.0), l, p, fw, 0.008, 0.012, 0.02) * fenced * 0.8);
			float what = hash(l.id + 5.5);
			float body;
			if (what < 0.08 && l.room > metres(20.0)) {
				float o = pond(p, l, fw, body);
				c = mix(c, wash(palette[WATER].rgb, sheet, SURVEY_PAPER, p, edge, 1.0), body * 0.9);
				c = mix(c, prussian, o * 0.8);
			} else if (what < 0.2 && l.room > metres(8.0)) {
				float o = lone_tree(p, l, fw, body);
				c = mix(c, tint * 0.75, body * 0.8);
				c = mix(c, sepia, o * 0.85);
				cover = max(cover, body);
			}
		}
	} else if (k == FIELD) {
		c = survey_field(c, tint, sheet, p, uv, to_uv, own, vec2(max(levels.x, near_town * NEAR_TOWN_PLOTS), levels.y), along, follow, fw, edge, cover);
		rim = 0.8;
	} else if (k == WOOD) {
		Lot s = lot_at(p, uv, to_uv, metres(STAND_M), 0.5, WOOD, own, levels);
		float open = clearing_at(p, own);
		c = wash(mix(tint * vec3(1.3, 1.25, 1.05), palette[GRASS].rgb, open), sheet, SURVEY_PAPER, p, edge, 1.0);
		c = survey_wood(c, tint, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)), levels.x * (1.0 - near_town * NEAR_TOWN_THINS), bump, cover);
		if (s.room >= 0.0) {
			float ride = (1.0 - smoothstep(metres(2.0), metres(2.0) + fw, s.edge)) * step(0.7, hash(s.id + 2.2)) * (1.0 - ledges);
			c = mix(c, sheet, ride * RIDE);
			if (open > 0.5 && hash(s.id + 3.9) < 0.5) c = mix(c, sepia, hut(p, s, fw));
		}
		rim = 0.55;
		rim_ink = tint * 0.4;
	} else if (k == MARSH) {
		Lot l = lot_at(p, uv, to_uv, metres(MARSH_LOT_M), 0.5, MARSH, own, levels);
		float pools = smoothstep(0.58, 0.64, fbm(p * 0.35 + 7.0));
		float reeds = step(0.5, hash(l.id + 2.8));
		c = mix(c, wash(palette[WATER].rgb, sheet, SURVEY_PAPER, p, edge, 1.0), pools * 0.8);
		c = mix(c, prussian, marks(p, own, 0.2, 0.07, 0.01, 0.0, 0.12, 0.85, fw) * 0.65);
		c = mix(c, tint * vec3(0.45, 0.6, 0.45), tufts(p, own, mix(0.42, 0.28, reeds), 0.07, 0.009, 0.6, fw) * 0.8);
		if (l.room >= 0.0 && hash(l.id + 4.4) < 0.5) c = mix(c, prussian, dashed(dot(p - l.site, l.across), l.edge, metres(6.0), 0.6, 0.008, fw) * 0.7);
		rim = 0.35;
		rim_ink = prussian;
	} else if (k == WATER) {
		float deep = smoothstep(0.0, 0.9, border);
		c = mix(c * 0.86, c * 1.04, deep);
		c = mix(c, prussian, ink(abs(border - 0.16), 0.008, fw) * 0.4);
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own, levels);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < 0.08) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, wash(palette[GRASS].rgb, sheet, SURVEY_PAPER, p, edge, 1.0), body);
			c = mix(c, prussian, o * 0.8);
		}
		rim = 1.0;
		rim_ink = prussian;
		return mix(c, rim_ink, ink(edge, 0.028, fw) * rim);
	} else if (k == ROAD) {
		c = mix(c, c * 0.9, ink(abs(edge - 0.08), 0.03, fw) * 0.35);
		return mix(c, sepia, ink(edge, 0.014, fw));
	} else if (k == BRIDGE) {
		float wall = (beside == WATER ? 1.0 : 0.0) * (1.0 - smoothstep(PARAPET - fw, PARAPET, edge));
		c = mix(c, mix(c, sepia, 0.5), wall);
		c = mix(c, sepia, ink(abs(edge - PARAPET), 0.012, fw) * (beside == WATER ? 1.0 : 0.0));
		return mix(c, sepia, ink(edge, 0.02, fw));
	} else if (k == EARTH) {
		Lot l = lot_at(p, uv, to_uv, metres(EARTH_LOT_M), 0.5, EARTH, own, levels);
		c = mix(c, sepia, scatter(p, own, 0.11, 0.012, 0.9, 0.55, fw) * 0.55);
		if (l.room >= 0.0) {
			if (hash(l.id + 4.1) < 0.4) c = mix(c, sepia, edge_line(SURVEY, TRACK, l, p, fw, 0.008, 0.012, 0.02) * 0.7);
			if (hash(l.id + 7.7) < 0.3 && l.room > metres(12.0)) c = mix(c, sepia, pit(p, l, fw) * 0.8);
		}
		rim = 0.6;
	} else if (k == ROCK) {
		float crag = marks(p, own, 0.16, 0.05, 0.014, 1.9, 1.1, 0.8, fw);
		c = mix(c, c * 0.82, smoothstep(0.5, 0.8, fbm(p * 1.4)) * 0.4);
		c = mix(c, sepia, crag * 0.85);
		c = mix(c, sepia, scatter(p + 3.1, own, 0.09, 0.009, 0.9, 0.5, fw) * 0.6);
		bump = crag * 0.02;
		cover = crag;
		rim = 0.7;
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.5, border);
		c = mix(c, sepia, scatter(p, own, 0.07, 0.008, 0.95, 0.45 + 0.4 * near, fw) * 0.6);
		c = mix(c, sepia, dunes(p, along, fw) * 0.35);
		rim = 0.35;
	} else if (k == SNOW) {
		c = mix(c, prussian, marks(p, own, 0.3, 0.12, 0.006, 0.15, 0.2, 0.5, fw) * 0.25);
		rim = 0.6;
		rim_ink = prussian * 1.4;
	}
	return mix(c, rim_ink, ink(edge, 0.012, fw) * rim);
}
