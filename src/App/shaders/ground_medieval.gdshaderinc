float tree_sign(vec2 p, vec2 o, float r, bool fir, float fw, out float stem, out float outline, out float side) {
	float trunk = r * 0.55;
	vec2 base = o + NORTH * trunk;
	vec2 q = p - base;
	float x = q.x;
	float y = dot(q, NORTH);
	float sd;
	if (fir) {
		float h = r * FIR_HEIGHT;
		float t = clamp(y / h, 0.0, 1.0);
		float tiers = 1.0 - 0.18 * fract(t * FIR_TIERS);
		sd = max(max(abs(x) - r * 0.8 * pow(1.0 - t, 1.1) * tiers, -y), y - h);
		side = smoothstep(-0.15 * r, 0.15 * r, x);
	} else {
		vec2 l = vec2(x, y - r * 0.95);
		float body = length(l) - r * 0.78;
		float left = length(l - vec2(-r * 0.5, -r * 0.2)) - r * 0.55;
		float right = length(l - vec2(r * 0.5, -r * 0.2)) - r * 0.55;
		float top = length(l - vec2(r * 0.06, r * 0.45)) - r * 0.5;
		sd = min(min(body, top), min(left, right));
		side = smoothstep(-0.5 * r, 0.45 * r, (l.x - l.y) * 0.707);
	}
	stem = ink(segment(p, o, base + NORTH * r * (fir ? 0.15 : 0.4)), max(r * 0.12, W1 * 0.6), fw);
	outline = ink(abs(sd), W1 * 0.7, fw);
	return cover(sd, fw * 0.6);
}

vec3 tree_colour(bool fir, float side) {
	return fir ? mix(MEDIEVAL_FIR_LIT, MEDIEVAL_FIR, side) : mix(MEDIEVAL_LEAF_LIT, MEDIEVAL_LEAF, side);
}

vec3 medieval_wood(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, float spacing, float trees, out float cover_of) {
	float closed = smoothstep(CLOSED_FROM, CLOSED_FULL, trees);
	float S = spacing * mix(0.95, CROWN_CROWDING, closed);
	float keep = mix(OPEN_WOOD_KEEP, 1.0, trees);
	cover_of = 0.0;
	float far = far_blend(fw, S);
	if (far >= 1.0) return c;
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point_at(p + NORTH * S * 0.5, uv, to_uv, S, 0.6, i, j, id, salt) - NORTH * S * 0.5;
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < -0.02) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			if (hash(id + 1.3) < clearing_at(o, salt) * (1.0 - closed) || hash(id + 2.4) > keep) continue;
			bool fir = hash(id + 6.1) < conifer_share(o, salt);
			float r = S * MEDIEVAL_CROWN * clamp((room + 0.05) / 0.22, 0.55, 1.0) * (0.85 + 0.3 * hash(id + 3.1)) * mix(1.0, 0.7, coppice_at(o, salt));
			if (length(p - o) > r * 3.4 || felled(uv + to_uv * (o - p), r / units_per_metre, hash(id + 7.7))) continue;
			float stem;
			float outline;
			float side;
			float body = tree_sign(p, o, r, fir, fw, stem, outline, side);
			col = mix(col, MEDIEVAL_INK, stem * 0.9);
			col = mix(col, tree_colour(fir, side), body);
			col = mix(col, MEDIEVAL_INK, outline * 0.85);
			cover_of = max(cover_of, body);
		}
	}
	cover_of *= symbols(fw);
	return mix(c, col, (1.0 - far) * symbols(fw));
}

vec3 medieval_field(vec3 c, vec3 sheet, vec2 p, float own, vec2 along, float follow, float fw, float edge) {
	float tone;
	float fallow;
	float balk;
	float bound;
	float furrow = fields(p, own, along, follow, MEDIEVAL_BEND, MEDIEVAL_NARROW, W1 * 0.6, fw, tone, fallow, balk, bound);
	vec3 green = plain(MEDIEVAL, palette[MEDIEVAL * KINDS + GRASS].rgb, sheet, p, edge);
	c = mix(c * MEDIEVAL_PLOUGHED, c * MEDIEVAL_RIPE, clamp(tone + 0.5, 0.0, 1.0));
	c = mix(c, green, fallow * 0.85);
	c = mix(c, MEDIEVAL_INK, max(balk * 0.22, bound * 0.6));
	return mix(c, MEDIEVAL_INK, furrow * FURROW_INK * 0.8);
}

float crest_rise(vec2 at, vec2 p, vec2 uv, mat2 to_uv) {
	vec2 u = clamp(uv + to_uv * (at - p), vec2(0.0), vec2(1.0));
	float reach = metres(HUMP_REACH_M);
	float around = 0.0;
	for (int i = 0; i < 4; i++) {
		float a = float(i) * 1.5708 + 0.7854;
		around += smooth_height(u + to_uv * vec2(cos(a), sin(a)) * reach);
	}
	return smooth_height(u) - around * 0.25;
}

vec3 medieval_hills(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float fw) {
	float S = metres(HUMP_M);
	float rise = metres(HUMP_RISE_M);
	float far = far_blend(fw, S * 0.5);
	if (far >= 1.0 || crest_rise(p, p, uv, to_uv) < rise * 0.2) return c;
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p + NORTH * S * 0.5, 0.0, S, 0.35, i, j, id) - NORTH * S * 0.5;
			float w = S * 0.5 * (0.8 + 0.35 * hash(id + 1.1));
			float h = w * HUMP_PITCH;
			vec2 q = p - o;
			float y = dot(q, NORTH);
			if (abs(q.x) > w * 1.2 || y < -w * 0.1 || y > h * 1.2) continue;
			vec2 u = clamp(uv + to_uv * (o - p), vec2(0.0), vec2(1.0));
			int under = kind_of(floor(u * cells));
			if (under == WOOD || under == WATER || under == FIELD || hash(id + 4.7) > smoothstep(rise * 0.5, rise, crest_rise(o, p, uv, to_uv))) continue;
			float e = length(vec2(q.x / w, max(y, 0.0) / h)) - 1.0;
			float sd = max(e * min(w, h), -y);
			float body = cover(sd, fw * 0.6) * smoothstep(0.0, h * 0.25, y);
			float shade = smoothstep(-0.35 * w, 0.55 * w, q.x);
			col = mix(col, mix(c * HUMP_LIT, c * HUMP_SHADE, shade), body);
			col = mix(col, MEDIEVAL_INK, ink(abs(e) * min(w, h), W1 * 0.8, fw) * step(h * 0.12, y) * 0.9);
		}
	}
	return mix(c, col, (1.0 - far) * symbols(fw));
}

float rose_point(vec2 q, vec2 dir, float len, float base, out float side) {
	float along = dot(q, dir);
	float across = dot(q, vec2(-dir.y, dir.x));
	side = step(0.0, across);
	return max(max(abs(across) - base * (1.0 - clamp(along / len, 0.0, 1.0)), -along), along - len);
}

vec3 rose_points(vec3 c, vec2 q, int count, float turn, float len, float base, vec3 lit, bool north, float fw) {
	for (int i = 0; i < 8; i++) {
		if (i >= count) break;
		float a = turn + float(i) * 6.2832 / float(count);
		float side;
		float sd = rose_point(q, vec2(sin(a), -cos(a)), len, base, side);
		vec3 face = north && i == 0 ? MEDIEVAL_RUBRIC : lit;
		c = mix(c, mix(face, MEDIEVAL_INK, side * 0.8), cover(sd, fw * 0.6));
		c = mix(c, MEDIEVAL_INK, ink(abs(sd), W1 * 0.45, fw));
	}
	return c;
}

vec3 sea_rose(vec3 c, vec2 q, float fw) {
	float R = metres(ROSE_DISC_M);
	if (length(q) > R * 1.1) return c;
	c = mix(c, MEDIEVAL_INK, ink(abs(length(q) - R * 0.6), W1 * 0.6, fw));
	c = rose_points(c, q, 8, 0.3927, R * 0.42, R * 0.07, MEDIEVAL_RUBRIC, false, fw);
	c = rose_points(c, q, 4, 0.7854, R * 0.68, R * 0.11, MEDIEVAL_VERDIGRIS, false, fw);
	c = rose_points(c, q, 4, 0.0, R, R * 0.15, MEDIEVAL_CONTOUR, true, fw);
	return mix(c, MEDIEVAL_INK, cover(length(q) - R * 0.04, fw * 0.6));
}

vec3 rhumbs(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float own, float border, float fw) {
	Lot l = lot_at(p, uv, to_uv, metres(ROSE_M), 0.5, WATER, own);
	if (l.room < cell_world * detail * ROSE_ROOM_CELLS || hash(l.id + 3.3) > ROSE_SHARE) return c;
	vec2 q = p - l.site;
	float shown = far_blend(fw, metres(ROSE_FADE_M));
	float fade = shown * smoothstep(0.1, 0.4, border);
	for (int k = 0; k < 16; k++) {
		float a = float(k) * 0.19635;
		float d = abs(dot(q, vec2(cos(a), sin(a))));
		vec3 tone = k % 4 == 0 ? MEDIEVAL_INK : k % 2 == 0 ? MEDIEVAL_VERDIGRIS : MEDIEVAL_RUBRIC;
		c = mix(c, tone, ink(d, W1 * (k % 4 == 0 ? 0.7 : 0.5), fw) * fade * 0.85);
	}
	return mix(c, sea_rose(c, q, fw), shown);
}

vec3 medieval(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover_of) {
	float wash_edge = edge + (noise(p * HAND_CYCLES + 9.0) - 0.5) * WASH_SHIFT;
	edge += (noise(p * HAND_CYCLES) - 0.5) * HAND_SHIFT;
	vec3 c = wash(tint, sheet, MEDIEVAL_PAPER, p, wash_edge, k == WATER ? 0.5 : 0.35);
	cover_of = 0.0;
	float rim = 1.0;
	float weight = W1;
	vec3 rim_ink = MEDIEVAL_INK;
	if (units_per_metre <= 0.0) return mix(c, MEDIEVAL_INK, ink(edge, W1, fw) * 0.6);
	if (k == GRASS) {
		c = mix(c, MEDIEVAL_INK, pasture(p, own, MEADOW_ROW, W1 * 0.5, 0.3, fw) * 0.35);
		float bush = strewn_dots(p + 4.1, own, metres(BUSH_APART_M), metres(BUSH_M), 0.9, BUSH_KEEP, fw, false);
		c = mix(c, MEDIEVAL_LEAF, bush * 0.85);
		cover_of = max(cover_of, bush);
		Lot l = lot_at(p, uv, to_uv, metres(PADDOCK_M), 0.5, GRASS, own);
		if (l.room >= 0.0) {
			float what = hash(l.id + 5.5);
			float body;
			if (what < 0.08 && l.room > metres(20.0)) {
				float o = pond(p, l, fw, body);
				c = mix(c, plain(MEDIEVAL, palette[MEDIEVAL * KINDS + WATER].rgb, sheet, p, edge), body * 0.9);
				c = mix(c, MEDIEVAL_INK, o * 0.8);
			} else if (what < 0.22 && l.room > metres(10.0)) {
				float stem;
				float outline;
				float side;
				bool fir = hash(l.id + 6.1) < 0.3;
				body = tree_sign(p, l.site, metres(LONE_TREE_M), fir, fw, stem, outline, side) * symbols(fw);
				c = mix(c, MEDIEVAL_INK, stem * 0.9 * symbols(fw));
				c = mix(c, tree_colour(fir, side), body);
				c = mix(c, MEDIEVAL_INK, outline * 0.85 * symbols(fw));
				cover_of = max(cover_of, body);
			}
		}
		rim = 0.0;
	} else if (k == FIELD) {
		c = medieval_field(c, sheet, p, own, along, follow, fw, edge);
	} else if (k == WOOD) {
		float closed = smoothstep(CLOSED_FROM, CLOSED_FULL, levels.x);
		float open = clearing_at(p, own) * (1.0 - closed);
		vec3 mass = mix(tint, MEDIEVAL_FIR * 0.9, smoothstep(0.4, 0.9, conifer_share(p, own)) * 0.6) * mix(1.0, 0.82, levels.x) * (1.0 + (fbm(p * 0.4) - 0.5) * 0.3);
		c = wash(mix(mass, palette[MEDIEVAL * KINDS + GRASS].rgb, open), sheet, MEDIEVAL_PAPER, p, wash_edge, 0.3);
		c = medieval_wood(c, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)), levels.x, cover_of);
		weight = W1 * 1.3;
	} else if (k == MARSH) {
		float pools = smoothstep(0.56, 0.62, fbm(p * 0.35 + 7.0)) * symbols(fw);
		c = mix(c, plain(MEDIEVAL, palette[MEDIEVAL * KINDS + WATER].rgb, sheet, p, wash_edge) * 0.85, pools * 0.75);
		c = mix(c, MEDIEVAL_INK, tufts(p + 5.3, own, MEADOW_ROW * 0.5, 0.1, W1 * 0.55, 0.55, fw) * 0.75 * (1.0 - pools));
		rim = 0.4;
	} else if (k == WATER) {
		bool sea = levels.x > 0.0;
		float deep = smoothstep(0.0, sea ? 1.6 : 0.9, border);
		c = mix(c * SHALLOWS, c * (sea ? SEA_DEPTH : 0.92), deep);
		float waves = 0.0;
		for (int i = 0; i < 3; i++) {
			if (!sea && i > 1) break;
			float at = 0.07 + float(i) * 0.09;
			float wob = (noise(p * 1.6 + float(i) * 3.1) - 0.5) * 0.035;
			waves = max(waves, ink(abs(border - at - wob), W1 * 0.6, fw) * (1.0 - float(i) * 0.3));
		}
		c = mix(c, MEDIEVAL_WAVE, waves * 0.75);
		if (sea) c = rhumbs(c, p, uv, to_uv, own, border, fw);
		float reeds = sea ? 0.0 : tufts(p + 5.3, own, MEADOW_ROW * 0.45, 0.09, W1 * 0.5, 0.45, fw) * (1.0 - smoothstep(REED_BAND * 0.4, REED_BAND, border));
		c = mix(c, MEDIEVAL_LEAF, reeds * REED_INK);
		return mix(c, MEDIEVAL_INK, hand_ink(edge, W2, p, fw));
	} else if (k == EARTH) {
		c = mix(c, MEDIEVAL_INK, scatter(p, own, 0.15, W1 * 0.8, 0.9, 0.5, fw) * 0.5);
	} else if (k == ROCK) {
		float crag = marks(p, own, 0.22, 0.07, W2 * 0.8, 0.0, 0.5, 0.8, fw);
		c = mix(c, MEDIEVAL_INK, crag * 0.6);
		c = mix(c, MEDIEVAL_INK, scatter(p + 3.1, own, 0.14, W1 * 0.7, 0.9, 0.4, fw) * 0.5);
		cover_of = crag;
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.5, border);
		c = mix(c, MEDIEVAL_ROAD, scatter(p, own, 0.15, W1 * 0.7, 0.95, 0.45 + 0.4 * near, fw) * 0.8);
		c = mix(c, MEDIEVAL_INK, dunes(p, along, fw) * 0.35);
	} else if (k == SNOW) {
		c = mix(c, MEDIEVAL_BLUE, marks(p, own, 0.45, 0.14, W1 * 0.5, 0.15, 0.2, 0.5, fw) * 0.3);
		rim_ink = MEDIEVAL_BLUE;
	}
	return mix(c, rim_ink, hand_ink(edge, weight, p, fw) * rim);
}
