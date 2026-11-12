vec3 ancient_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover) {
	float closed = smoothstep(CLOSED_FROM, CLOSED_FULL, trees);
	float S = spacing * mix(0.95, CROWN_CROWDING, closed);
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	vec3 crown_c = tint * vec3(0.78, 0.86, 0.72) * mix(1.0, 0.88, closed);
	cover = 0.0;
	if (far_blend(fw, S) >= 1.0) return mix(c, tint * 0.9, 0.7);
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point_at(p + NORTH * S * 0.5, uv, to_uv, S, 0.45, i, j, id, salt) - NORTH * S * 0.5;
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < -0.02 || hash(id + 4.2) > 0.96) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			if (hash(id + 1.3) < clearing_at(o, salt) * (1.0 - closed) || hash(id + 2.4) > keep) continue;
			float scale = clamp((room + 0.05) / 0.22, 0.45, 1.0) * (0.9 + 0.2 * hash(id + 3.1)) * S / 0.6 * mix(1.0, 0.7, coppice_at(o, salt));
			float rc = 0.16 * scale;
			float trunk = 0.11 * scale;
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

vec3 ancient_hills(vec3 c, vec2 p, vec3 n, float hill, float convex, float fill, float fw) {
	float wobbled = hill + (noise(p * 0.9) - 0.5) * HILL_WOBBLE;
	float inside = smoothstep(0.5 - HILL_SOFT, 0.5 + HILL_SOFT, wobbled);
	float d = abs(wobbled - 0.5) / max(fwidth(wobbled), 1e-5) * fw;
	vec3 rock_c = noise(p / metres(HILL_ROCK_M) + 5.5) > 0.5 ? palette[ANCIENT * KINDS + ROCK].rgb : palette[ANCIENT * KINDS + EARTH].rgb;
	c = mix(c, rock_c * (c / ANCIENT_PAPER), inside * fill);
	c = engraved_relief(c, n, p, fw, ANCIENT_UMBER, inside * mix(0.5, 1.0, step(0.01, fill)));
	c = mix(c, ANCIENT_RIDGE, marks(p, 0.0, 0.14, 0.05, W1 * 0.7, 0.0, 0.4, 0.7, fw) * convex * inside * 0.85);
	return mix(c, ANCIENT_INK, ink(d, W2, fw) * 0.9);
}

vec3 ancient_field(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float own, float fw) {
	p = curled(p);
	Lot l = lot_at(p, uv, to_uv, metres(PLOT_OPEN_M), 0.56, FIELD, own);
	return mix(c, ANCIENT_INK, ink(l.edge, W1, fw) * step(hash(l.id + l.other + 4.4), 0.1) * 0.7);
}

vec3 ancient(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover) {
	vec3 c = tint * (sheet / ANCIENT_PAPER);
	cover = 0.0;
	float rim = 1.0;
	float weight = W2;
	if (units_per_metre <= 0.0) return mix(c, ANCIENT_INK, ink(edge, W2, fw) * 0.6);
	if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own);
		if (l.room > metres(20.0) && hash(l.id + 5.5) < 0.08) {
			float body;
			float o = pond(p, l, fw, body);
			c = mix(c, palette[ANCIENT * KINDS + WATER].rgb * (sheet / ANCIENT_PAPER), body);
			c = mix(c, ANCIENT_INK, o * 0.9);
		}
		rim = 0.0;
	} else if (k == FIELD) {
		c = ancient_field(c, p, uv, to_uv, own, fw);
		weight = W1;
	} else if (k == WOOD) {
		float open = clearing_at(p, own) * (1.0 - smoothstep(CLOSED_FROM, CLOSED_FULL, levels.x));
		c = mix(c, palette[ANCIENT * KINDS + GRASS].rgb * (sheet / ANCIENT_PAPER), open);
		c = ancient_wood(c, tint, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)) * 1.2, levels.x, cover);
	} else if (k == MARSH) {
		c = mix(c, ANCIENT_INK, marks(p, own, 0.16, 0.05, W1 * 0.6, 0.0, 0.0, 0.8, fw) * 0.85);
		rim = 0.0;
	} else if (k == WATER) {
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < ISLET_SHARE) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, palette[ANCIENT * KINDS + GRASS].rgb * (sheet / ANCIENT_PAPER), body);
			c = mix(c, ANCIENT_INK, o * 0.9);
		}
	} else if (k == EARTH) {
		c = mix(c, ANCIENT_INK, scatter(p, own, 0.09, W1 * 0.5, 0.9, 0.35, fw) * 0.6);
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
