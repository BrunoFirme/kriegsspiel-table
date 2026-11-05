vec3 ancient_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover) {
	float S = spacing;
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	vec3 sepia = vec3(0.2, 0.12, 0.05);
	vec3 crown_c = tint * vec3(0.9, 0.95, 0.88);
	cover = 0.0;
	if (far_blend(fw, S) >= 1.0) return mix(c, tint * 0.9, 0.7);
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point_at(p + NORTH * S * 0.5, uv, to_uv, S, 0.55, i, j, id, salt) - NORTH * S * 0.5;
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < 0.05 || hash(id + 4.2) > 0.9) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			if (hash(id + 1.3) < clearing_at(o, salt) || hash(id + 2.4) > keep) continue;
			float scale = clamp(room / 0.22, 0.45, 1.0) * (0.85 + 0.3 * hash(id + 3.1)) * S / 0.6 * mix(1.0, 0.6, coppice_at(o, salt));
			float rc = 0.16 * scale;
			float trunk = 0.11 * scale;
			if (hash(id + 6.1) < conifer_share(o, salt)) {
				vec2 cc = o + NORTH * rc * 1.6;
				vec2 q = (p - cc) * vec2(3.2, 1.0);
				float d = length(q);
				if (d > rc * 2.4) continue;
				float body = 1.0 - smoothstep(rc * 1.5 - fw, rc * 1.5 + fw, d);
				col = mix(col, crown_c * 0.85, body);
				col = mix(col, sepia, ink(abs(d - rc * 1.5), 0.008, fw));
				cover = max(cover, body);
				continue;
			}
			vec2 cc = o + NORTH * (trunk + rc * 0.9);
			vec2 q = (p - cc) * vec2(1.0, 0.88);
			float d = length(q);
			if (length(p - o) > rc * 3.2) continue;
			float a = atan(q.y, q.x);
			float h = hash(id + 7.7) * TAU;
			float rr = rc * (1.0 + 0.07 * sin(a * 7.0 + h) + 0.04 * sin(a * 13.0 + h * 3.0));
			float body = 1.0 - smoothstep(rr - fw * 0.75, rr + fw * 0.25, d);
			float ground = ink(segment(p, o + vec2(-rc * 0.25, 0.0), o + vec2(rc * 1.25, 0.0)), 0.008, fw);
			float stem = ink(segment(p, o, cc), 0.011 * scale, fw);
			col = mix(col, sepia, max(ground, stem) * 0.9);
			float shade = smoothstep(-0.35, 0.55, dot(q / rc, vec2(0.8, 0.6)));
			float lines = hatch(p.x + p.y, 0.032, 0.0065 * shade, fw);
			col = mix(col, crown_c, body);
			cover = max(cover, body);
			col = mix(col, sepia, lines * body * 0.85);
			col = mix(col, sepia, ink(abs(d - rr), 0.008, fw));
		}
	}
	return mix(col, mix(c, tint * 0.9, 0.7), far_blend(fw, S));
}

vec3 ancient_field(vec3 c, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float fw, float edge, out float cover) {
	vec3 sepia = vec3(0.22, 0.13, 0.06);
	Lot l = lot_at(p, uv, to_uv, metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x)), 0.56, FIELD, own, levels);
	cover = 0.0;
	c *= 0.95 + 0.08 * hash(l.id + 2.1);
	float furrow;
	float strip_edge = strips(p, l, along, follow, fw, furrow);
	float use = hash(l.id + 9.4);
	if (use < 0.6) c = mix(c, sepia, max(furrow * 0.35, strip_edge * 0.3));
	else if (use < 0.75) { float o = orchard(p, l, 0.18, fw); c = mix(c, sepia, o * 0.8); cover = o; }
	else if (use < 0.85) c = mix(c, sepia, vine_rows(p, l, along, fw) * 0.6);
	c = mix(c, sepia, ink(l.edge, 0.012, fw) * 0.8);
	c = mix(c, sepia, scatter(p, own, 0.06, 0.012, 0.2, 1.0, fw) * (1.0 - smoothstep(0.0, 0.03, l.edge)) * 0.85);
	if (l.room > metres(30.0) && hash(l.id + 8.2) < l.levels.y) {
		float turn = hash(l.id + 11.3) * 3.1416;
		float house = metres(14.0);
		float outer = block(p, l.site, vec2(house, house * 0.7), turn, fw);
		float court = block(p, l.site, vec2(house * 0.5, house * 0.3), turn, fw);
		float villa = outer - court;
		c = mix(c, sepia, villa * 0.85);
		cover = max(cover, villa);
	}
	return c;
}

vec3 ancient(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover) {
	vec3 sepia = vec3(0.22, 0.13, 0.06);
	vec3 c = wash(tint, sheet, ANCIENT_PAPER, p, edge, 0.4);
	cover = 0.0;
	float rim = 0.85;
	if (units_per_metre <= 0.0) return mix(c, sepia, ink(edge, 0.016, fw) * 0.6);
	if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own, levels);
		c = mix(c, sepia, tufts(p, own, 0.6, 0.06, 0.008, 0.45, fw) * 0.6);
		if (l.room > metres(20.0) && hash(l.id + 5.5) < 0.08) {
			float body;
			float o = pond(p, l, fw, body);
			c = mix(c, wash(palette[ANCIENT * KINDS + WATER].rgb, sheet, ANCIENT_PAPER, p, edge, 0.4), body * 0.9);
			c = mix(c, sepia, o * 0.8);
		}
		rim = 0.0;
	} else if (k == FIELD) {
		c = ancient_field(c, tint, sheet, p, uv, to_uv, own, levels, along, follow, fw, edge, cover);
	} else if (k == WOOD) {
		Lot s = lot_at(p, uv, to_uv, metres(STAND_M), 0.5, WOOD, own, levels);
		float open = clearing_at(p, own);
		c = mix(c, wash(palette[ANCIENT * KINDS + GRASS].rgb, sheet, ANCIENT_PAPER, p, edge, 0.4), open);
		c = ancient_wood(c, tint, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)) * 1.4, levels.x, cover);
		if (s.room >= 0.0) {
			float ride = (1.0 - smoothstep(metres(2.0), metres(2.0) + fw, s.edge)) * step(0.7, hash(s.id + 2.2)) * (1.0 - ledges);
			c = mix(c, sheet, ride * RIDE);
			if (open > 0.5 && hash(s.id + 3.9) < 0.5) c = mix(c, sepia, hut(p, s, fw));
		}
		rim = 0.0;
	} else if (k == MARSH) {
		Lot l = lot_at(p, uv, to_uv, metres(MARSH_LOT_M), 0.5, MARSH, own, levels);
		c = mix(c, sepia, marks(p, own, 0.16, 0.08, 0.007, 0.0, 0.1, 0.8, fw) * 0.6);
		c = mix(c, sepia, tufts(p, own, 0.38, 0.08, 0.008, 0.65, fw) * 0.8);
		if (l.room >= 0.0 && hash(l.id + 4.4) < 0.5) c = mix(c, sepia, dashed(dot(p - l.site, l.across), l.edge, metres(6.0), 0.6, 0.008, fw) * 0.7);
		rim = 0.4;
	} else if (k == WATER) {
		float lining = 0.0;
		for (int i = 0; i < 7; i++) {
			float at = 0.045 + float(i) * (0.05 + float(i) * 0.012);
			float wob = (noise(p * 2.2 + float(i) * 3.1) - 0.5) * 0.02;
			lining = max(lining, ink(abs(border - at - wob), 0.0065, fw) * (1.0 - float(i) * 0.11));
		}
		float open = smoothstep(0.35, 0.6, border);
		float ripple = marks(p, own, 0.2, 0.06, 0.005, 0.0, 0.25, 0.55, fw) * open;
		c = mix(c, sepia, max(lining, ripple) * 0.65);
		c = mix(c, sepia, scatter(p, own, 0.05, 0.006, 0.9, 0.35, fw) * open * 0.45);
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own, levels);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < 0.08) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, wash(palette[ANCIENT * KINDS + GRASS].rgb, sheet, ANCIENT_PAPER, p, edge, 0.4), body);
			c = mix(c, sepia, o * 0.8);
		}
		return mix(c, sepia, ink(edge, 0.022, fw));
	} else if (k == ROAD) {
		c = mix(c, sepia, ink(abs(edge - 0.05), 0.006, fw) * 0.35);
		return mix(c, sepia, ink(edge, 0.016, fw));
	} else if (k == BRIDGE) {
		float wall = (beside == WATER ? 1.0 : 0.0) * (1.0 - smoothstep(PARAPET - fw, PARAPET, edge));
		c = mix(c, sepia, hatch(dot(p, vec2(0.7071, 0.7071)), 0.03, 0.006, fw) * wall * 0.7);
		c = mix(c, sepia, ink(abs(edge - PARAPET), 0.01, fw) * (beside == WATER ? 1.0 : 0.0));
		return mix(c, sepia, ink(edge, 0.02, fw));
	} else if (k == EARTH) {
		Lot l = lot_at(p, uv, to_uv, metres(EARTH_LOT_M), 0.5, EARTH, own, levels);
		c = mix(c, sepia, marks(p, own, 0.13, 0.025, 0.007, 0.6, 1.2, 0.6, fw) * 0.55);
		if (l.room >= 0.0) {
			if (hash(l.id + 4.1) < 0.4) c = mix(c, sepia, ink(l.edge, 0.008, fw) * 0.7);
			if (hash(l.id + 7.7) < 0.3 && l.room > metres(12.0)) c = mix(c, sepia, pit(p, l, fw) * 0.8);
		}
	} else if (k == ROCK) {
		float crag = marks(p, own, 0.12, 0.05, 0.011, 2.1, 0.5, 0.9, fw);
		float cross = marks(p + 1.7, own, 0.12, 0.04, 0.008, 0.6, 0.5, 0.6, fw);
		c = mix(c, sepia, max(crag, cross * smoothstep(0.5, 0.7, fbm(p * 1.1))) * 0.85);
		cover = crag;
	} else if (k == SAND) {
		c = mix(c, sepia, scatter(p, own, 0.045, 0.0075, 0.95, 0.7, fw) * 0.75);
		c = mix(c, sepia, dunes(p, along, fw) * 0.3);
		rim = 0.5;
	} else if (k == SNOW) {
		c = mix(c, vec3(0.35, 0.42, 0.5), hatch(p.y, 0.09, 0.004, fw) * 0.35);
		rim = 0.6;
	}
	return mix(c, sepia, ink(edge, 0.016, fw) * rim);
}
