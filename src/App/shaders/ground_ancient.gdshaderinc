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
	cover *= symbols(fw);
	return mix(mix(c, tint * 0.9, 0.7), col, (1.0 - far_blend(fw, S)) * symbols(fw));
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

vec3 ancient_field(vec3 c, vec2 p, float own, float fw) {
	float tone;
	float fallow;
	float balk;
	float bound;
	float furrow = fields(p, own, vec2(0.0), 0.0, W1 * 0.6, fw, tone, fallow, balk, bound);
	c *= 1.0 + tone * CROP_PLAY * 0.7 - fallow * 0.06;
	c = mix(c, ANCIENT_UMBER, max(balk * BALK_INK * 0.4, bound * BALK_INK * 0.7));
	return mix(c, ANCIENT_INK, furrow * FURROW_INK);
}

float waves(vec2 p, float salt, float fw) {
	float m = 0.0;
	float r = SEA_WAVE * 0.16;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - SEA_WAVE * 0.5, salt, SEA_WAVE, 0.35, i, j, id) + SEA_WAVE * 0.5;
			if (hash(id + 4.2) > 0.6) continue;
			for (int k = -1; k <= 1; k += 2) {
				vec2 d = p - o - vec2(float(k) * r, 0.0);
				if (dot(d, NORTH) <= 0.0) m = max(m, ink(abs(length(d) - r), W1, fw));
			}
		}
	}
	return m * symbols(fw);
}

const float BEAST_GIRTH = 0.05;
const float SEA_ART_FADE_M = 0.8;

vec4 over(vec4 on, vec3 colour, float amount) {
	return vec4(mix(on.rgb, colour, amount), max(on.a, amount));
}

vec4 sea_ship(vec2 u, float e, vec3 cream) {
	vec4 art = vec4(0.0);
	float oar = 1e3;
	for (int i = 0; i < 6; i++) {
		float x = -0.3 + float(i) * 0.12;
		oar = min(oar, segment(u, vec2(x, -0.02), vec2(x - 0.075, -0.25)));
	}
	art = over(art, cream, cover(oar - 0.007, e));
	float ax = abs(u.x);
	float deck = 0.02 + 0.3 * smoothstep(0.34, 0.56, ax);
	float keel = -0.17 + 0.36 * u.x * u.x;
	float hull = max(max(keel - u.y, u.y - deck), ax - 0.56);
	art = over(art, ANCIENT_RIDGE, cover(hull, e));
	art = over(art, ANCIENT_INK, cover(abs(hull + 0.045) - 0.008, e) * cover(hull, e));
	art = over(art, cream, cover(abs(u.y - deck) - 0.011, e) * step(ax, 0.56));
	art = over(art, cream, cover(abs(length(u - vec2(0.55, 0.36)) - 0.045) - 0.012, e));
	art = over(art, ANCIENT_INK, cover(length(u - vec2(0.43, -0.02)) - 0.022, e));
	art = over(art, cream, cover(max(abs(u.x) - 0.011, max(-u.y, u.y - 0.68)), e));
	art = over(art, cream, cover(segment(u, vec2(-0.3, 0.63), vec2(0.3, 0.63)) - 0.011, e));
	float belly = 0.17 + 0.06 * cos(u.x * 5.5);
	float sail = max(abs(u.x) - 0.27 + 0.03 * sin((u.y - 0.17) * 7.0), max(belly - u.y, u.y - 0.61));
	art = over(art, mix(cream, ANCIENT_RIDGE, step(0.0, sin(u.x * 24.0 + 1.5))), cover(sail, e));
	art = over(art, ANCIENT_INK, cover(abs(sail) - 0.006, e));
	return art;
}

vec4 sea_beast(vec2 at, float fine, vec3 cream) {
	vec2 u = at * 1.3;
	float e = fine * 1.3;
	vec4 art = vec4(0.0);
	float body = 1e3;
	float fins = 1e3;
	float scales = 0.0;
	for (int k = 0; k < 3; k++) {
		float r = 0.17 - 0.03 * float(k);
		vec2 d = u - vec2(-0.2 + float(k) * 0.34 - float(k * k) * 0.025, -0.05);
		float ring = abs(length(d) - r);
		float a = atan(d.y, d.x);
		body = min(body, max(ring - BEAST_GIRTH, -d.y));
		fins = min(fins, max(max(length(d) - r - BEAST_GIRTH - 0.06 * abs(sin(a * 7.0)), r - length(d)), 0.08 - d.y));
		if (d.y > 0.0) scales = max(scales, cover(ring - 0.014, e) * step(0.2, sin(a * 18.0)));
	}
	float neck = min(segment(u, vec2(-0.56, -0.05), vec2(-0.6, 0.2)), segment(u, vec2(-0.6, 0.2), vec2(-0.7, 0.33))) - BEAST_GIRTH;
	float crest = segment(u, vec2(-0.5, 0.02), vec2(-0.6, 0.3)) - 0.03 - 0.035 * abs(sin(u.y * 38.0));
	float head = max(length((u - vec2(-0.78, 0.35)) * vec2(1.0, 1.5)) - 0.1, (-0.8 - u.x) * 0.5 - abs(u.y - 0.335));
	float tail = min(segment(u, vec2(0.62, -0.05), vec2(0.72, 0.16)) - 0.035,
		min(segment(u, vec2(0.72, 0.16), vec2(0.84, 0.2)), segment(u, vec2(0.72, 0.16), vec2(0.7, 0.3))) - 0.028);
	float whole = min(min(body, neck), min(head, tail));
	art = over(art, ANCIENT_RIDGE, cover(min(fins, crest), e));
	art = over(art, cream, cover(whole, e));
	art = over(art, ANCIENT_RIDGE, scales * cover(body, e));
	art = over(art, ANCIENT_INK, cover(abs(whole) - 0.012, e));
	art = over(art, ANCIENT_INK, cover(length(u - vec2(-0.76, 0.39)) - 0.02, e));
	for (int k = 0; k < 6; k++)
		art = over(art, cream, cover(abs(length(u - vec2(-0.58 + float(k) * 0.24, -0.06)) - 0.07) - 0.012, e) * step(u.y, -0.07));
	return art;
}

vec4 sea_marks(vec2 p, Lot l, float fw, vec3 cream) {
	vec2 d = (p - l.site) / SEA_MARK;
	vec2 u = vec2(hash(l.id + 6.1) < 0.5 ? d.x : -d.x, dot(d, NORTH));
	float e = fw / SEA_MARK;
	vec4 art = hash(l.id + 3.9) < 0.5 ? sea_ship(u, e, cream) : sea_beast(u, e, cream);
	return vec4(art.rgb, art.a * far_blend(fw, metres(SEA_ART_FADE_M)));
}

vec3 ancient(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 levels, vec2 along, float follow, float ledges, float fw, float edge, float border, out float cover) {
	vec3 c = tint * (sheet / ANCIENT_PAPER) * (1.0 + (fbm(p * 0.18 + 4.0) - 0.5) * MINERAL_PLAY + (noise(p * 6.0) - 0.5) * 0.03);
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
		c = ancient_field(c, p, own, fw);
		weight = W1;
	} else if (k == WOOD) {
		float open = clearing_at(p, own) * (1.0 - smoothstep(CLOSED_FROM, CLOSED_FULL, levels.x));
		c = mix(c, palette[ANCIENT * KINDS + GRASS].rgb * (sheet / ANCIENT_PAPER), open);
		c = ancient_wood(c, tint, p, uv, to_uv, fw, ledges, metres(mix(TREES_OPEN_M, TREES_CLOSED_M, levels.x)) * 1.2, levels.x, cover);
	} else if (k == MARSH) {
		c = mix(c, ANCIENT_INK, marks(p, own, 0.16, 0.05, W1 * 0.6, 0.0, 0.0, 0.8, fw) * 0.85);
		rim = 0.0;
	} else if (k == WATER) {
		if (levels.x > 0.0) {
			c *= SEA_DEEP;
			c = mix(c, sheet * 1.12, waves(p, own, fw) * 0.4 * smoothstep(0.3, 0.9, border));
			Lot far = lot_at(p, uv, to_uv, metres(SEA_MARK_M), 0.5, WATER, own);
			if (far.room > metres(60.0) && hash(far.id + 8.3) < SEA_MARK_SHARE) {
				vec4 art = sea_marks(p, far, fw, sheet * 1.12);
				c = mix(c, art.rgb, art.a);
			}
		}
		Lot l = lot_at(p, uv, to_uv, metres(WATER_LOT_M), 0.5, WATER, own);
		if (levels.x <= 0.0 && l.room > metres(ISLET_SHORE_M) && hash(l.id + 6.1) < ISLET_SHARE) {
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
