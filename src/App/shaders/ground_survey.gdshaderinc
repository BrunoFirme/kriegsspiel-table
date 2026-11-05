vec3 survey_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover) {
	float S = spacing;
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	cover = 0.0;
	if (far_blend(fw, S) >= 1.0) return c;
	vec3 col = c;
	col = mix(col, SURVEY_INK, scatter(p + 7.3, 0.0, S * 0.45, W1 * 0.45, 0.9, 0.35, fw) * 0.5);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point_at(p, uv, to_uv, S, 0.7, i, j, id, salt);
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < 0.0) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			if (hash(id + 1.3) < clearing_at(o, salt) || hash(id + 2.4) > keep) continue;
			float small = coppice_at(o, salt);
			float r = min(RING_R * mix(0.85, 1.15, hash(id + 3.1)) * mix(1.0, 0.55, small) * mix(0.8, 1.0, trees), room * 0.95 + 0.03);
			vec2 q = p - o;
			float d = length(q);
			if (d > r * 1.6) continue;
			if (hash(id + 6.1) < conifer_share(o, salt) && small < 0.5) {
				float m = spruce(p, o, r, W1 * 0.8, fw);
				col = mix(col, SURVEY_INK, m * 0.9);
				cover = max(cover, m);
				continue;
			}
			float a = atan(q.y, q.x);
			float h = hash(id + 7.7) * TAU;
			float rr = r * (1.0 + 0.1 * sin(a * 5.0 + h) + 0.06 * sin(a * 9.0 + h * 2.3));
			float body = 1.0 - smoothstep(rr - fw * 0.75, rr + fw * 0.25, d);
			col = mix(col, tint * 0.9, body * 0.5);
			col = mix(col, SURVEY_INK, ink(abs(d - rr), W1 * 0.6, fw) * 0.85);
			cover = max(cover, body);
		}
	}
	return mix(col, c, far_blend(fw, S));
}

vec3 survey_blocks(vec3 c, float body, float shadow, vec3 fill) {
	c = mix(c, SURVEY_INK, shadow * (1.0 - body) * 0.85);
	return mix(c, fill, body);
}

vec3 wet_field(int style, vec3 c, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, float fw) {
	Lot w = lot_at(p, uv, to_uv, metres(WET_LOT_M), 0.4, FIELD, own + 0.5, levels);
	vec3 ink_c = style == MODERN ? MODERN_BLACK : style == ANCIENT ? ANCIENT_INK : SURVEY_INK;
	vec3 wet_c = style == MODERN ? MODERN_BLUE : style == ANCIENT ? ANCIENT_INK : SURVEY_BLUE;
	float w1 = style == MODERN ? M1 : W1;
	if (style == SURVEY) c = mix(c, c * vec3(0.93, 1.0, 0.86), 0.6);
	c = mix(c, ink_c, levees(w, w1, fw) * 0.8);
	return mix(c, wet_c, marks(p, own, 0.3, 0.1, w1 * 0.4, 0.0, 0.0, 0.5, fw) * 0.6);
}

vec3 survey_field(vec3 c, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float fw, float edge, out float cover) {
	Lot l = lot_at(p, uv, to_uv, metres(mix(PLOT_OPEN_M, PLOT_SMALL_M, levels.x)), 0.56, FIELD, own, levels);
	cover = 0.0;
	vec2 site_uv = uv + to_uv * (l.site - p);
	float wet = wet_field_at(site_uv, follow);
	if (wet > 0.5) return wet_field(SURVEY, c, sheet, p, uv, to_uv, own, levels, fw);
	float fronts = fronts_road(site_uv, follow);
	vec2 road_along = road_along_at(site_uv);
	float furrow;
	float strip_edge = strips(p, l, mix(along, road_along, fronts), max(follow, fronts), fw, furrow);
	float use = hash(l.id + 9.4);
	if (use < 0.55) {
		c = mix(c, SURVEY_INK, furrow * 0.22);
		c = mix(c, SURVEY_INK, strip_edge * 0.5);
	} else if (use < 0.7) {
		float o = orchard(p, l, 0.16, fw);
		c = mix(c, SURVEY_INK, hatch(p.x + p.y, metres(GARDEN_HATCH_M), W1 * 0.5, fw) * (1.0 - o) * 0.25);
		c = mix(c, palette[WOOD].rgb * 0.85, o * 0.9);
		cover = o;
	} else if (use < 0.8) {
		c = wash(GAMBOGE, sheet, SURVEY_PAPER, p, l.edge, 0.6);
		c = mix(c, SURVEY_INK, vine_rows(p, l, along, fw) * 0.55);
	} else if (use < 0.9) c = mix(c, SURVEY_INK, scatter(p, own, metres(12.0), metres(0.5), 0.9, 0.3, fw) * 0.4);
	float bounded = smoothstep(0.15, 0.6, l.levels.x);
	int line = HEDGE + int(hash(l.id + 3.3) * 5.0);
	float stands = line == WALL ? 1.0 : 0.0;
	c = mix(c, mix(SURVEY_INK, CINNABAR, stands * 0.6), edge_line(SURVEY, line, l, p, fw, W1, W2, W3) * mix(0.3, 0.9, bounded));
	c = mix(c, SURVEY_INK, ink(l.edge, W1, fw) * 0.75 * (1.0 - bounded));
	float near_road = 1.0 - smoothstep(metres(60.0), metres(160.0), road_away_at(site_uv));
	if (l.room > metres(30.0) && hash(l.id + 8.2) < l.levels.y * (1.0 + near_road)) {
		float turn = mix(hash(l.id + 11.3) * 3.1416, atan(road_along.y, road_along.x), step(0.5, near_road));
		float yard, wooden;
		float farm = farmstead(p, l, turn, fw, yard, wooden);
		float shade;
		float shadow = farmstead(p - vec2(metres(SHADOW_M)), l, turn, fw, shade, wooden);
		float garden = block(p, l.site + vec2(metres(22.0), metres(6.0)), vec2(metres(10.0), metres(8.0)), turn, fw) * (1.0 - yard);
		c = mix(c, wash(GARTENGRUEN, sheet, SURVEY_PAPER, p, edge, 0.4), garden * 0.8);
		c = mix(c, SURVEY_INK, hatch(p.x + p.y, metres(GARDEN_HATCH_M), W1 * 0.5, fw) * garden * 0.3);
		c = mix(c, wash(palette[EARTH].rgb, sheet, SURVEY_PAPER, p, edge, 0.6), yard * 0.7);
		c = survey_blocks(c, farm, shadow, CARMINE);
		c = mix(c, SURVEY_INK, haystacks(p, l, fw) * 0.7);
		cover = max(cover, max(farm, yard * 0.5));
	}
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
	float pooling = k == GRASS ? 0.25 : k == WATER ? 0.6 : k == WOOD || k == MARSH || k == ROCK || k == TOWN ? 0.7 : k == SNOW ? 0.3 : 0.8;
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
		float wet = 1.0 - smoothstep(metres(WET_NEAR_M * 0.7), metres(WET_NEAR_M), wet_at(uv));
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
		Lot s = lot_at(p, uv, to_uv, metres(STAND_M), 0.5, WOOD, own, levels);
		float open = clearing_at(p, own);
		float conifer = conifer_share(p, own);
		vec3 ground = mix(tint, CONIFER_WASH, smoothstep(0.4, 0.9, conifer));
		c = wash(mix(ground, palette[GRASS].rgb, open), sheet, SURVEY_PAPER, p, wash_edge, pooling);
		c = survey_wood(c, tint, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)), levels.x * (1.0 - near_town * NEAR_TOWN_THINS), cover);
		if (s.room >= 0.0) {
			float ride = (1.0 - smoothstep(metres(3.0), metres(3.0) + fw, s.edge)) * step(0.7, hash(s.id + 2.2)) * (1.0 - ledges);
			c = mix(c, sheet, ride * RIDE);
			c = mix(c, SURVEY_INK, dashed(dot(p - s.site, s.across), abs(s.edge - metres(3.0)), 0.25, 0.5, W1 * 0.5, fw) * ride * 0.7);
			if (open > 0.5 && hash(s.id + 3.9) < 0.5) c = mix(c, SURVEY_INK, hut(p, s, fw));
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
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < 0.08) {
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
