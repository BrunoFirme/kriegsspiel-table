vec3 survey_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover) {
	float closed = smoothstep(CLOSED_FROM, CLOSED_FULL, trees);
	float S = spacing * mix(0.95, CROWN_CROWDING, closed);
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	cover = 0.0;
	vec3 far_c = mix(c, SURVEY_INK, mix(0.04, 0.09, trees));
	if (far_blend(fw, S) >= 1.0) return far_c;
	vec3 col = mix(c, SURVEY_INK, scatter(p + 7.3, 0.0, S * 0.45, W1 * 0.45, 0.9, 0.35, fw) * 0.5);
	float m = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point_at(p, uv, to_uv, S, 0.75, i, j, id, salt);
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < 0.0) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			if (hash(id + 1.3) < clearing_at(o, salt) * (1.0 - closed) || hash(id + 2.4) > keep) continue;
			float small = coppice_at(o, salt);
			float r = min(RING_R * mix(0.7, 1.2, hash(id + 3.1)) * mix(1.0, 0.6, small) * mix(0.85, 1.0, trees), room * 0.9);
			if (length(p - o) > r * 3.0) continue;
			m = max(m, crown_sign(p, o - NORTH * r * 0.4, r * 0.85, step(hash(id + 6.1), conifer_share(o, salt)) * (1.0 - small), r * 0.5, W1 * 0.8, fw, id));
		}
	}
	cover = m;
	col = mix(col, SURVEY_INK, m * 0.9);
	return mix(col, far_c, far_blend(fw, S));
}

vec3 survey_blocks(vec3 c, float body, float shadow, vec3 fill) {
	c = mix(c, SURVEY_INK, shadow * (1.0 - body) * 0.85);
	return mix(c, fill, body);
}

vec3 survey_farm(vec3 c, vec3 sheet, vec2 p, vec2 site, vec2 seed, float turn, float edge, float fw, inout float cover) {
	float scale = farm_scale(seed);
	float yard, court, shade_yard, shade_court;
	float farm = farm_blocks(p, site, seed, turn, scale, fw, yard, court);
	float shadow = farm_blocks(p - vec2(metres(SHADOW_M)), site, seed, turn, scale, fw, shade_yard, shade_court);
	float garden = farm_garden(p, site, seed, turn, scale, fw) * (1.0 - farm);
	c = mix(c, wash(GARTENGRUEN, sheet, SURVEY_PAPER, p, edge, 0.4), garden * 0.8);
	c = mix(c, SURVEY_INK, hatch(p.x + p.y, metres(GARDEN_HATCH_M), W1 * 0.5, fw) * garden * 0.3);
	c = mix(c, wash(palette[EARTH].rgb, sheet, SURVEY_PAPER, p, edge, 0.6), max(yard, court) * 0.6);
	c = survey_blocks(c, farm, shadow, BRICK);
	c = mix(c, SURVEY_INK, hatch(p.x - p.y, metres(2.5), W1 * 0.45, fw) * farm * step(0.7, hash(seed + 3.4)) * 0.6);
	c = mix(c, SURVEY_INK, farm_trees(p, site, seed, turn, scale, W1 * 0.8, fw) * 0.9);
	cover = max(cover, max(farm, max(yard, court) * 0.5));
	return c;
}

vec3 survey_field(vec3 c, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float fw, float edge, out float cover) {
	p = curled(p);
	Lot l = lot_at(p, uv, to_uv, metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x)), 0.56, FIELD, own, levels);
	cover = 0.0;
	float use = hash(l.id + 9.4);
	float small = step(l.room, metres(ORCHARD_LOT_M));
	if (use < 0.55 || small < 0.5) {
		c = mix(c, SURVEY_INK, gewann_lines(p, uv, to_uv, own, along, follow, W1 * 0.7, fw) * 0.45);
	} else if (use < 0.7) {
		c = mix(c, wash(GARTENGRUEN, sheet, SURVEY_PAPER, p, l.edge, 0.4), 0.7);
		c = mix(c, SURVEY_INK, hatch(p.x + p.y, metres(GARDEN_HATCH_M), W1 * 0.5, fw) * 0.25);
		float o = orchard_rings(p, l, W1 * 0.8, fw);
		c = mix(c, SURVEY_INK, o * 0.85);
		cover = o;
	} else if (use < 0.77) {
		c = wash(GAMBOGE, sheet, SURVEY_PAPER, p, l.edge, 0.45);
		c = mix(c, SURVEY_INK, vine_strokes(p, 0.0, W1 * 0.8, fw) * 0.6);
	} else if (use < 0.9) c = mix(c, SURVEY_INK, scatter(p, own, metres(12.0), metres(0.5), 0.9, 0.3, fw) * 0.4);
	float wet = wet_meadow(uv, follow);
	if (wet > 0.001) {
		c = mix(c, wash(palette[GRASS].rgb, sheet, SURVEY_PAPER, p, edge, 0.3), wet * 0.85);
		c = mix(c, SURVEY_INK, meadow_pairs(p, own, MEADOW_ROW, MEADOW_LEN, W1 * 0.55, 0.7, fw) * 0.65 * wet);
		c = mix(c, SURVEY_BLUE, marks(p + 0.3, own, 0.55, 0.16, W1 * 0.55, 0.0, 0.15, 0.7, fw) * 0.7 * wet);
	}
	float bounded = smoothstep(ENCLOSED_FROM, ENCLOSED_FULL, l.levels.x);
	vec2 pair = l.id + l.other;
	float marked = step(hash(pair + 4.4), bounded * 0.85);
	int line = HEDGE + int(hash(pair + 1.1) * 4.0);
	c = mix(c, SURVEY_INK, edge_line(SURVEY, line, l, p, fw, W1, W2, W3) * marked * mix(0.5, 1.0, hash(pair + 2.2)));
	if (l.room > metres(15.0) && hash(l.id + 4.9) < l.levels.y * 0.15) {
		float m = windmill(p, l, fw);
		c = mix(c, SURVEY_INK, m);
		cover = max(cover, m);
	}
	return c;
}

float water_lines(vec2 p, float border, float fw) {
	float m = 0.0;
	for (int i = 0; i < 3; i++) {
		float at = 0.06 + float(i) * (0.06 + float(i) * 0.02);
		float wob = (noise(p * 1.8 + float(i) * 3.1) - 0.5) * 0.03;
		m = max(m, ink(abs(border - at - wob), W1 * 0.5, fw) * (1.0 - float(i) * 0.3));
	}
	return m;
}

vec3 survey(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, float near_town, float mid, float road_w, vec2 along, float follow, float ledges, float fw, float edge, float border, out float bump, out float cover) {
	float wash_edge = edge + (noise(p * HAND_CYCLES + 9.0) - 0.5) * WASH_SHIFT;
	edge += (noise(p * HAND_CYCLES) - 0.5) * HAND_SHIFT;
	float pooling = k == GRASS ? 0.25 : k == WATER ? 0.6 : k == WOOD || k == SNOW ? 0.3 : k == MARSH || k == ROCK || k == TOWN ? 0.7 : 0.8;
	vec3 c = k == FIELD ? tint * (sheet / SURVEY_PAPER) : wash(tint, sheet, SURVEY_PAPER, p, wash_edge, pooling);
	bump = 0.0;
	cover = 0.0;
	float rim = 1.0;
	vec3 rim_ink = SURVEY_INK;
	if (units_per_metre <= 0.0) return mix(c, SURVEY_INK, ink(edge, W1, fw) * 0.6);
	if (k == TOWN) {
		return town_draw(SURVEY, c, sheet, p, uv, to_uv, own, levels, road_w, fw, cover);
	} else if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own, levels);
		float rough = step(0.6, hash(l.id + 1.1));
		float wet = wet_meadow(uv, 0.0);
		if (rough > 0.5) c = mix(c, SURVEY_INK, pasture(p, own, MEADOW_ROW, W1 * 0.5, 0.75, fw) * 0.6);
		else c = mix(c, SURVEY_INK, meadow_pairs(p, own, MEADOW_ROW, MEADOW_LEN, W1 * 0.55, 0.75, fw) * 0.7);
		c = mix(c, SURVEY_BLUE, marks(p + 0.3, own, 0.55, 0.16, W1 * 0.55, 0.0, 0.15, 0.7, fw) * 0.7 * wet);
		if (l.room >= 0.0) {
			float fenced = smoothstep(0.2, 0.6, max(l.levels.x, near_town)) * step(mix(0.65, 0.3, near_town), hash(l.id + 7.2));
			c = mix(c, SURVEY_INK, edge_line(SURVEY, HEDGE + int(hash(l.id + 3.3) * 2.0), l, p, fw, W1, W2, W3) * fenced * 0.8);
			float what = hash(l.id + 5.5);
			float body;
			if (what < 0.08 && l.room > metres(20.0)) {
				float o = pond(p, l, fw, body);
				c = mix(c, wash(POND, sheet, SURVEY_PAPER, p, edge, 1.0), body * 0.9);
				c = mix(c, SURVEY_INK, o * 0.8);
			} else if (what < 0.2 && l.room > metres(8.0)) {
				float o = lone_tree(p, l, fw, body);
				c = mix(c, palette[WOOD].rgb, body * 0.8);
				c = mix(c, SURVEY_INK, o * 0.85);
				cover = max(cover, body);
			}
		}
	} else if (k == FIELD) {
		c = survey_field(c, tint, sheet, p, uv, to_uv, own, vec2(max(levels.x, near_town * NEAR_TOWN_PLOTS), levels.y), along, follow, fw, edge, cover);
	} else if (k == WOOD) {
		float open = clearing_at(p, own) * (1.0 - smoothstep(CLOSED_FROM, CLOSED_FULL, levels.x));
		float conifer = conifer_share(p, own);
		vec3 ground = mix(tint, CONIFER_WASH, smoothstep(0.4, 0.9, conifer)) * mix(1.0, 0.8, levels.x) * (1.0 + (fbm(p * 0.5) - 0.5) * 0.14);
		c = wash(mix(ground, palette[GRASS].rgb, open), sheet, SURVEY_PAPER, p, wash_edge, pooling);
		c = survey_wood(c, tint, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)), levels.x * (1.0 - near_town * NEAR_TOWN_THINS), cover);
		c = mix(c, SURVEY_INK, rides(p, levels.x, W1 * 0.6, 0.0, fw) * (1.0 - ledges) * 0.7);
		if (open > 0.5) {
			Lot s = lot_at(p, uv, to_uv, metres(STAND_M), 0.5, WOOD, own, levels);
			if (s.room >= 0.0 && hash(s.id + 3.9) < 0.5) c = mix(c, SURVEY_INK, hut(p, s, fw));
		}
	} else if (k == MARSH) {
		Lot l = lot_at(p, uv, to_uv, metres(MARSH_LOT_M), 0.5, MARSH, own, levels);
		float pools = smoothstep(0.58, 0.64, fbm(p * 0.35 + 7.0));
		c = mix(c, wash(palette[WATER].rgb, sheet, SURVEY_PAPER, p, wash_edge, 1.0), pools * 0.8);
		c = mix(c, SURVEY_INK, meadow_pairs(p, own, MEADOW_ROW, MEADOW_LEN, W1 * 0.55, 0.5, fw) * 0.6);
		c = mix(c, SURVEY_INK, pasture(p + 0.6, own, MEADOW_ROW, W1 * 0.6, 0.5, fw) * 0.6);
		c = mix(c, SURVEY_BLUE, marks(p + 0.3, own, 0.5, 0.17, W1 * 0.6, 0.0, 0.15, 0.85, fw) * 0.75);
		if (l.room >= 0.0 && hash(l.id + 4.4) < 0.5) c = mix(c, SURVEY_BLUE, dashed(dot(p - l.site, l.across), l.edge, metres(6.0), 0.6, W1 * 0.7, fw) * 0.7);
		rim = 0.5;
	} else if (k == WATER) {
		float deep = smoothstep(0.0, 0.9, border);
		c = mix(c * 0.92, c * 1.02, deep);
		c = mix(c, SURVEY_BLUE, water_lines(p, border, fw) * 0.35);
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own, levels);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < ISLET_SHARE) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, wash(palette[GRASS].rgb, sheet, SURVEY_PAPER, p, edge, 1.0), body);
			c = mix(c, SURVEY_INK, o * 0.8);
		}
		return mix(c, SURVEY_INK, hand_ink(edge, W2, p, fw));
	} else if (k == ROAD || k == BRIDGE) {
		return sheet;
	} else if (k == EARTH) {
		Lot l = lot_at(p, uv, to_uv, metres(EARTH_LOT_M), 0.5, EARTH, own, levels);
		c = mix(c, SURVEY_INK, scatter(p, own, 0.15, W1 * 0.8, 0.9, 0.5, fw) * 0.5);
		if (l.room >= 0.0) {
			if (hash(l.id + 4.1) < 0.4) c = mix(c, WEGEBRAUN, edge_line(SURVEY, TRACK, l, p, fw, W1, W2, W3) * 0.8);
			if (hash(l.id + 7.7) < 0.3 && l.room > metres(12.0)) c = mix(c, SURVEY_INK, pit(p, l, fw) * 0.8);
		}
	} else if (k == ROCK) {
		float crag = marks(p, own, 0.22, 0.07, W2 * 0.8, 0.0, 0.5, 0.8, fw);
		c = mix(c, CINNABAR, crag * 0.8);
		c = mix(c, SURVEY_INK, scatter(p + 3.1, own, 0.14, W1 * 0.7, 0.9, 0.4, fw) * 0.5);
		cover = crag;
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.5, border);
		c = mix(c, WEGEBRAUN, scatter(p, own, 0.15, W1 * 0.7, 0.95, 0.45 + 0.4 * near, fw) * 0.8);
		c = mix(c, SURVEY_INK, dunes(p, along, fw) * 0.35);
	} else if (k == SNOW) {
		c = mix(c, SURVEY_BLUE, marks(p, own, 0.45, 0.14, W1 * 0.5, 0.15, 0.2, 0.5, fw) * 0.3);
		rim_ink = SURVEY_BLUE;
	}
	return mix(c, rim_ink, hand_ink(edge, W1, p, fw) * rim);
}
