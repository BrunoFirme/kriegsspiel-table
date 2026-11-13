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
			vec2 o = grid_point_at(p, uv, to_uv, S, 0.95, i, j, id, salt);
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < 0.0) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			if (hash(id + 5.7) > mix(0.55, 1.0, smoothstep(0.35, 0.6, noise(o * 0.6 + salt))) ) continue;
			if (hash(id + 1.3) < clearing_at(o, salt) * (1.0 - closed) || hash(id + 2.4) > keep) continue;
			float small = coppice_at(o, salt);
			float r = min(min(RING_R * mix(0.55, 1.25, hash(id + 3.1)) * mix(1.0, 0.6, small) * mix(0.85, 1.0, trees), room * 0.9), fw * CROWN_MOST_PX);
			if (length(p - o) > r * 3.0 || felled(uv + to_uv * (o - p), r / units_per_metre, hash(id + 7.7))) continue;
			m = max(m, crown_sign(p, o - NORTH * r * 0.4, r * 0.85, step(hash(id + 6.1), conifer_share(o, salt)) * (1.0 - small), r * 0.5, W1 * 0.8, fw, id));
		}
	}
	cover = m;
	col = mix(col, SURVEY_INK, m * 0.9);
	return mix(far_c, col, (1.0 - far_blend(fw, S)) * symbols(fw));
}

vec3 survey_field(vec3 c, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 along, float follow, float fw, float edge) {
	float tone;
	float fallow;
	float balk;
	float bound;
	float furrow = fields(p, own, along, follow, W1 * 0.6, fw, tone, fallow, balk, bound);
	vec3 green = wash(palette[GRASS].rgb, sheet, SURVEY_PAPER, p, edge, 0.3);
	c *= 1.0 + tone * CROP_PLAY;
	c.g *= 1.0 + max(tone, 0.0) * CROP_GREEN * 2.0;
	c = mix(c, green, fallow * FALLOW_GREEN);
	c = mix(c, SURVEY_INK, pasture(p + 2.2, own, MEADOW_ROW, W1 * 0.5, 0.6, fw) * 0.3 * fallow);
	c = mix(c, mix(green, SURVEY_INK, 0.35), max(balk * BALK_INK * 0.6, bound * BALK_INK));
	c = mix(c, SURVEY_INK, furrow * FURROW_INK);
	float wet = wet_meadow(uv, follow);
	if (wet > 0.001) {
		c = mix(c, wash(palette[GRASS].rgb, sheet, SURVEY_PAPER, p, edge, 0.3), wet * 0.85);
		c = mix(c, SURVEY_INK, meadow_pairs(p, own, MEADOW_ROW, MEADOW_LEN, W1 * 0.55, 0.7, fw) * 0.65 * wet);
		c = mix(c, SURVEY_BLUE, marks(p + 0.3, own, 0.55, 0.16, W1 * 0.55, 0.0, 0.15, 0.7, fw) * 0.7 * wet);
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

float coast_lines(vec2 p, float border, float fw) {
	float m = 0.0;
	float at = 0.05;
	for (int i = 0; i < 7; i++) {
		float wob = (noise(p * 1.4 + float(i) * 3.1) - 0.5) * (0.02 + 0.012 * float(i));
		m = max(m, ink(abs(border - at - wob), W1 * 0.5, fw) * (1.0 - float(i) * 0.12));
		at += 0.06 + float(i) * 0.035;
	}
	return m;
}

vec3 survey(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float ledges, float fw, float edge, float border, out float bump, out float cover) {
	float wash_edge = edge + (noise(p * HAND_CYCLES + 9.0) - 0.5) * WASH_SHIFT;
	edge += (noise(p * HAND_CYCLES) - 0.5) * HAND_SHIFT;
	float pooling = k == GRASS || k == FIELD ? 0.25 : k == WATER ? 0.6 : k == WOOD || k == SNOW ? 0.3 : k == MARSH || k == ROCK ? 0.7 : 0.8;
	vec3 c = wash(tint, sheet, SURVEY_PAPER, p, wash_edge, pooling);
	bump = 0.0;
	cover = 0.0;
	float rim = 1.0;
	vec3 rim_ink = SURVEY_INK;
	if (units_per_metre <= 0.0) return mix(c, SURVEY_INK, ink(edge, W1, fw) * 0.6);
	if (k == GRASS) {
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own);
		float rough = step(0.6, hash(l.id + 1.1));
		float wet = wet_meadow(uv, 0.0);
		if (rough > 0.5) c = mix(c, SURVEY_INK, pasture(p, own, MEADOW_ROW, W1 * 0.5, 0.75, fw) * 0.45);
		else c = mix(c, SURVEY_INK, meadow_pairs(p, own, MEADOW_ROW, MEADOW_LEN, W1 * 0.55, 0.75, fw) * 0.5);
		float bush = scatter(p + 4.1, own, metres(BUSH_APART_M), metres(BUSH_M), 0.9, BUSH_KEEP, fw);
		c = mix(c, palette[WOOD].rgb * 0.92, bush * 0.85);
		cover = max(cover, bush);
		c = mix(c, SURVEY_BLUE, marks(p + 0.3, own, 0.55, 0.16, W1 * 0.55, 0.0, 0.15, 0.7, fw) * 0.7 * wet);
		if (l.room >= 0.0) {
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
		c = survey_field(c, sheet, p, uv, to_uv, own, along, follow, fw, edge);
	} else if (k == WOOD) {
		float open = clearing_at(p, own) * (1.0 - smoothstep(CLOSED_FROM, CLOSED_FULL, levels.x));
		float conifer = conifer_share(p, own);
		vec3 ground = mix(tint, CONIFER_WASH, smoothstep(0.4, 0.9, conifer)) * mix(1.0, 0.8, levels.x) * (1.0 + (fbm(p * 0.5) - 0.5) * 0.14);
		c = wash(mix(ground, palette[GRASS].rgb, open), sheet, SURVEY_PAPER, p, wash_edge, pooling);
		c = survey_wood(c, tint, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)), levels.x, cover);
	} else if (k == MARSH) {
		Lot l = lot_at(p, uv, to_uv, metres(MARSH_LOT_M), 0.5, MARSH, own);
		float pools = smoothstep(0.58, 0.64, fbm(p * 0.35 + 7.0)) * symbols(fw);
		c = mix(c, wash(palette[WATER].rgb, sheet, SURVEY_PAPER, p, wash_edge, 1.0), pools * 0.8);
		c = mix(c, SURVEY_INK, meadow_pairs(p, own, MEADOW_ROW, MEADOW_LEN, W1 * 0.55, 0.5, fw) * 0.6);
		c = mix(c, SURVEY_INK, pasture(p + 0.6, own, MEADOW_ROW, W1 * 0.6, 0.5, fw) * 0.6);
		c = mix(c, SURVEY_BLUE, marks(p + 0.3, own, 0.5, 0.17, W1 * 0.6, 0.0, 0.15, 0.85, fw) * 0.75);
		if (l.room >= 0.0 && hash(l.id + 4.4) < 0.5) c = mix(c, SURVEY_BLUE, dashed(dot(p - l.site, l.across), l.edge, metres(6.0), 0.6, W1 * 0.7, fw) * 0.7);
		rim = 0.5;
	} else if (k == WATER) {
		bool sea = levels.x > 0.0;
		float deep = smoothstep(0.0, sea ? 1.6 : 0.9, border);
		c = mix(c * SHORE_DEEP, sea ? mix(c, sheet, 0.3) : c * 1.04, deep);
		c = mix(c, SURVEY_BLUE, sea ? coast_lines(p, border, fw) * 0.85 : water_lines(p, border, fw) * SHORE_LINES);
		float reeds = sea ? 0.0 : tufts(p + 5.3, own, MEADOW_ROW * 0.45, 0.09, W1 * 0.5, 0.45, fw) * (1.0 - smoothstep(REED_BAND * 0.4, REED_BAND, border));
		c = mix(c, palette[WOOD].rgb * 0.8, reeds * REED_INK);
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own);
		if (l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < ISLET_SHARE) {
			float body;
			float o = islet(p, l, fw, body);
			c = mix(c, wash(palette[GRASS].rgb, sheet, SURVEY_PAPER, p, edge, 1.0), body);
			c = mix(c, SURVEY_INK, o * 0.8);
		}
		return mix(c, SURVEY_INK, hand_ink(edge, W2, p, fw));
	} else if (k == EARTH) {
		c = mix(c, SURVEY_INK, scatter(p, own, 0.15, W1 * 0.8, 0.9, 0.5, fw) * 0.5);
	} else if (k == ROCK) {
		float crag = marks(p, own, 0.22, 0.07, W2 * 0.8, 0.0, 0.5, 0.8, fw);
		c = mix(c, SURVEY_INK, crag * 0.55);
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
	return mix(c, rim_ink, hand_ink(edge, k == WOOD ? W1 * 1.5 : W1, p, fw) * rim);
}
