const float CLOSE_M = 0.3;
const float STREET_M = 0.085;
const vec3 PETAL = vec3(0.93, 0.92, 0.86);
const vec3 LEAF_RUST = vec3(0.86, 0.62, 0.4);
const vec3 LEAF_OCHRE = vec3(1.02, 0.86, 0.5);
const vec3 LEAF_BROWN = vec3(0.7, 0.56, 0.42);
const vec3 SNOW_PRINT = vec3(0.72, 0.79, 0.92);

float near_tier(float fw) {
	return 1.0 - far_blend(fw, metres(NEAR_M));
}

float close_tier(float fw) {
	return 1.0 - far_blend(fw, metres(CLOSE_M));
}

float street_tier(float fw) {
	return 1.0 - far_blend(fw, metres(STREET_M));
}

vec2 turned(vec2 q, float a) {
	return vec2(q.x * cos(a) - q.y * sin(a), q.x * sin(a) + q.y * cos(a));
}

float grain(vec2 p) {
	return clamp(0.5 + swell_of(p) * 1.2, 0.0, 1.0);
}

float organic(vec2 q) {
	vec2 w = q + vec2(swell_of(q * 0.6 + 3.1), swell_of(q * 0.6 + 8.4)) * 0.5;
	return clamp(0.5 + swell_of(w) * 0.8 + swell_of(w * 2.3 + 5.0) * 0.36 + swell_of(w * 4.9 + 9.0) * 0.16, 0.0, 1.0);
}

float joints(vec2 q, float size, float width, out float tone) {
	vec2 g = q / size;
	vec2 cell = floor(g);
	float first = 8.0;
	float second = 8.0;
	tone = 0.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 c = cell + vec2(float(x), float(y));
			float d = length(g - c - hash2(c + 41.3));
			if (d < first) {
				second = first;
				first = d;
				tone = hash(c + 9.7) - 0.5;
			} else if (d < second) second = d;
		}
	}
	return 1.0 - smoothstep(0.0, width / size, second - first);
}

bool strewn(vec2 p, float salt, float spacing, float keep, out vec2 local, out vec2 id) {
	float best = 1e9;
	bool any = false;
	local = vec2(0.0);
	id = vec2(0.0);
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 cid;
			vec2 o = grid_point(p - spacing * 0.5, salt, spacing, 0.6, i, j, cid) + spacing * 0.5;
			if (hash(cid + 5.1) > keep || length(p - o) >= best) continue;
			best = length(p - o);
			local = (p - o) / metres(1.0);
			id = cid;
			any = true;
		}
	}
	return any;
}

float roughness(vec2 o, float thick) {
	return max(thick, smoothstep(0.4, 0.62, fbm(o / metres(ROUGH_M) + 40.0)));
}

float strokes(vec2 p, float salt, float spacing, float len, float pen, float thick, float lean, float fw, out float light) {
	float m = 0.0;
	light = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, salt, spacing, 1.0, i, j, id) + spacing * 0.5;
			if (hash(id + 5.1) > mix(0.3, 0.97, roughness(o, thick))) continue;
			float a = (grain(o / metres(WIND_M)) - 0.5) * 1.8 + (hash(id + 2.2) - 0.5) * lean;
			float stroke = ink(segment(p, o, o + vec2(sin(a), -cos(a)) * len * (0.5 + 0.5 * hash(id + 8.8))), pen, fw);
			if (hash(id + 9.9) < 0.5) light = max(light, stroke);
			else m = max(m, stroke);
		}
	}
	return m;
}

float blades(vec2 p, float salt, float thick, float fw, out float light) {
	return strokes(p, salt, metres(BLADE_APART_M), metres(BLADE_M), metres(BLADE_PEN_M), thick, 0.9, fw, light);
}

float ticks(vec2 p, float salt, float thick, float fw) {
	float spacing = metres(TICKS_APART_M);
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, salt + 4.0, spacing, 0.9, i, j, id) + spacing * 0.5;
			if (hash(id + 5.1) > TICKS_KEEP * roughness(o, thick)) continue;
			for (int k = 0; k < 3; k++) {
				float h = metres(TICK_M) * (0.35 + 0.65 * hash(id + float(k) * 1.7));
				vec2 foot = o + vec2((float(k) - 1.0) * metres(TICK_M) * 0.36, (hash(id + float(k) * 3.1) - 0.5) * metres(TICK_M) * 0.2);
				m = max(m, ink(segment(p, foot, foot + NORTH * h), metres(BLADE_PEN_M), fw));
			}
		}
	}
	return m;
}

vec3 pebble(vec3 t, vec3 c, vec2 l, float r, float e, float amount) {
	float own = fract(r * 531.7) * 6.2832;
	vec2 lying = turned(l, own) * vec2(1.0, 1.0 + 0.6 * fract(r * 211.3));
	float a = atan(lying.y, lying.x);
	float rough = r * (0.8 + 0.13 * cos(a * 3.0 + own) + 0.07 * cos(a * 5.0 - own * 2.0));
	float body = cover(length(lying) - rough, e);
	float cast = cover(length(l - SHADE_TO * r * 0.35) - r * 0.85, e + r * 0.25);
	t = mix(t, t * 0.7, cast * (1.0 - body) * amount * 0.6);
	float shaded = smoothstep(-0.5, 0.6, dot(l, SHADE_TO) / max(r, 1e-4));
	return mix(t, c * mix(0.96, 0.76, shaded) * (1.0 + (grain(l * 70.0 + own) - 0.5) * 0.1), body * amount);
}

float fallen(vec2 p, float salt, float fw, out float top) {
	float spacing = metres(LOGS_APART_M);
	float m = 0.0;
	top = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, salt, spacing, 0.8, i, j, id) + spacing * 0.5;
			if (hash(id + 3.3) > 0.4) continue;
			float a = hash(id + 1.1) * 6.2832;
			vec2 lie = vec2(cos(a), sin(a)) * metres(2.0 + 2.5 * hash(id + 2.2));
			m = max(m, ink(segment(p, o - lie, o + lie), metres(0.22), fw));
			top = max(top, ink(segment(p + SHADE_TO * metres(0.08), o - lie, o + lie), metres(0.08), fw));
		}
	}
	return m;
}

float crown_at(vec2 p, vec2 uv, mat2 to_uv, float salt, float spacing, float r, float keep, float e, out vec2 local, out vec2 id) {
	float best = 1e9;
	local = vec2(0.0);
	id = vec2(0.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 cid;
			vec2 o = grid_point(p, salt, spacing, 0.55, i, j, cid);
			if (hash(cid + 2.9) < 1.0 - keep) continue;
			vec2 d = (p - o) / metres(1.0);
			float size = r * (0.6 + 0.8 * hash(cid + 3.7)) / metres(1.0);
			float a = atan(d.y, d.x);
			float h = hash(cid + 6.6) * 6.2832;
			float sd = length(d) - size * (0.88 + 0.08 * cos(a * 5.0 + h) + 0.05 * cos(a * 11.0 + h * 2.0));
			if (sd >= best || sd > e * 2.0 || felled(uv + to_uv * (o - p), size, hash(cid + 7.7))) continue;
			best = sd;
			local = d / max(size, 0.01);
			id = cid;
		}
	}
	return cover(best, e);
}

float crowns(vec2 p, vec2 uv, mat2 to_uv, float salt, float spacing, float r, float girth, float keep, float fw) {
	float m = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p, salt, spacing, 0.55, i, j, id);
			float size = 0.6 + 0.8 * hash(id + 3.7);
			if (hash(id + 2.9) < 1.0 - keep || length(p - o) > r * size + fw * 2.0 || felled(uv + to_uv * (o - p), girth * size / metres(1.0), hash(id + 7.7))) continue;
			m = max(m, ink(length(p - o), r * size, fw));
		}
	}
	return m;
}

vec3 stumps(vec3 under, vec3 c, vec2 p, vec2 uv, float own, float e, float close) {
	vec2 near = built_at(uv);
	float room = min(near.r - HOUSE_CLEAR_M, near.g - WAY_CLEAR_M);
	float band = smoothstep(-1.0, 1.5, room) * (1.0 - smoothstep(FELLED_M, FELLED_M + 5.0, room));
	vec2 l;
	vec2 id;
	if (band <= 0.01 || !strewn(p + 5.1, own + 41.0, metres(3.4), 0.55 * band, l, id)) return under;
	float r = 0.2 + 0.2 * hash(id + 2.0);
	under = mix(under, under * 0.6, cover(length(l - SHADE_TO * r * 0.5) - r, e + 0.04) * close * 0.7);
	under = mix(under, vec3(0.36, 0.28, 0.19), cover(length(l) - r, e) * close);
	under = mix(under, vec3(0.63, 0.52, 0.36), cover(length(l) - r * 0.8, e) * close);
	return mix(under, vec3(0.5, 0.4, 0.27), cover(abs(length(l) - r * 0.42) - 0.012, e) * close * 0.7);
}

vec3 real_wood(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float own, float trees, float fw) {
	float apart = metres(mix(TREE_OPEN_APART_M, TREE_CLOSE_APART_M, trees));
	float seen = 1.0 - far_blend(fw, apart * 0.4);
	if (seen <= 0.001) return c;
	vec2 q = p / metres(1.0);
	float e = fw / metres(1.0);
	float r = apart * TREE_CROWN;
	float keep = mix(0.6, 0.97, trees);
	float shadow = crowns(p - SHADE_TO * r * 0.5, uv - to_uv * (SHADE_TO * r * 0.5), to_uv, own + 3.3, apart, r, r, keep, fw);
	float crown = crowns(p, uv, to_uv, own + 3.3, apart, r, r, keep, fw);
	float light = crowns(p + SHADE_TO * r * 0.32, uv + to_uv * (SHADE_TO * r * 0.32), to_uv, own + 3.3, apart, r * 0.5, r, keep, fw);
	float close = close_tier(fw);
	vec3 floor_tint = mix(c, c * vec3(1.5, 1.38, 1.12), close * 0.8);
	vec3 under = floor_tint * (1.0 + (organic(q / 2.6 + 3.0) - 0.5) * 0.18);
	under = mix(under, floor_tint * LITTER, smoothstep(0.46, 0.64, organic(q / 4.2 + 9.0)) * 0.5);
	vec2 built = built_at(uv);
	float glade = 1.0 - smoothstep(0.0, FELLED_M, min(built.r - HOUSE_CLEAR_M, built.g - WAY_CLEAR_M) + (organic(q / 3.0 + 81.0) - 0.5) * 6.0);
	under = mix(under, c * vec3(1.62, 1.52, 1.08) * (1.0 + (organic(q / 1.7 + 27.0) - 0.5) * 0.16), glade * 0.75);
	if (close > 0.001) {
		float pale;
		float fern = blades(p + 13.3, own + 6.0, smoothstep(0.5, 0.7, organic(q / 7.0 + 19.0)), fw, pale);
		under = mix(under, c * 0.7, fern * close * 0.8 * (1.0 - glade));
		under = mix(under, c * 1.2, pale * close * 0.6 * (1.0 - glade));
		float top;
		float trunk = fallen(p, own + 9.0, fw, top);
		under = mix(under, c * LITTER * 0.6, trunk * close);
		under = mix(under, c * LITTER * 0.95, top * close);
		under = stumps(under, c, p, uv, own, e, close);
	}
	float street = street_tier(fw);
	if (street > 0.001) {
		vec2 l;
		vec2 id;
		if (strewn(p + 4.6, own + 15.0, metres(0.17), 0.8, l, id)) {
			vec2 turn = turned(l, hash(id + 1.7) * 6.2832);
			float leaf = max(length(turn - vec2(0.0, 0.03)), length(turn + vec2(0.0, 0.03))) - 0.047;
			float kind = hash(id + 6.3);
			under = mix(under, c * (kind < 0.4 ? LEAF_BROWN : kind < 0.75 ? LEAF_OCHRE : LEAF_RUST), cover(leaf, e) * street * 0.85);
			under = mix(under, c * 0.5, cover(abs(turn.y) - 0.003, e) * cover(leaf, e) * street * 0.5);
		}
		if (strewn(p + 9.2, own + 17.0, metres(0.9), 0.5, l, id)) {
			vec2 turn = turned(l, hash(id + 2.9) * 6.2832);
			under = mix(under, c * 0.45, cover(segment(turn, vec2(-0.14, 0.0), vec2(0.14, 0.02)) - 0.007, e) * street);
			under = mix(under, c * 0.45, cover(segment(turn, vec2(0.02, 0.0), vec2(0.08, 0.06)) - 0.005, e) * street);
		}
		if (strewn(p + 1.4, own + 19.0, metres(2.4), 0.3, l, id))
			for (int k = 0; k < 4; k++) {
				vec2 cap = l - (hash2(id + float(k) * 3.7) - 0.5) * 0.3;
				under = mix(under, vec3(0.86, 0.8, 0.68), cover(length(cap) - 0.03, e) * street);
				under = mix(under, vec3(0.6, 0.46, 0.34), cover(length(cap) - 0.01, e) * street);
			}
	}
	vec3 t = mix(under, c * 0.62, shadow * (1.0 - crown) * 0.85);
	t = mix(t, c * 0.8, crown);
	t = mix(t, c * 0.98, light * crown);
	if (close <= 0.001) return mix(c, t, seen);
	vec2 l;
	vec2 id;
	vec2 cast;
	float shaded = crown_at(p - SHADE_TO * r * 0.5, uv - to_uv * (SHADE_TO * r * 0.5), to_uv, own + 3.3, apart, r, keep, e, cast, id);
	float leaves = crown_at(p, uv, to_uv, own + 3.3, apart, r, keep, e, l, id);
	vec3 near_wood = mix(under, floor_tint * 0.5, shaded * 0.75);
	float clump = organic(q / 0.8 + hash2(id) * 37.0);
	vec3 leafy = c * (0.78 + (clump - 0.5) * 0.5) * (1.0 - dot(l, SHADE_TO) * 0.16);
	leafy = mix(leafy, c * 0.55, scatter(p + 3.4, own + 23.0, metres(0.9), metres(0.3), 0.95, 0.45, fw) * 0.3);
	leafy = mix(leafy, c * 1.02, scatter(p + 8.2 + SHADE_TO * metres(0.1), own + 25.0, metres(0.8), metres(0.24), 0.95, 0.45, fw) * 0.3);
	leafy *= 1.0 - smoothstep(0.78, 1.0, length(l)) * 0.2;
	if (street > 0.001) leafy = mix(leafy, c * 1.1, scatter(p + 6.1, own + 21.0, metres(0.26), metres(0.06), 0.95, 0.6, fw) * street * 0.6);
	near_wood = mix(near_wood, leafy, leaves);
	return mix(c, mix(t, near_wood, close), seen);
}

vec3 real_grass(vec3 c, vec2 p, float own, float fw, int style, bool wet, vec2 built, vec3 bare) {
	float near = near_tier(fw);
	if (near <= 0.001) return c;
	vec2 q = p / metres(1.0);
	float e = fw / metres(1.0);
	float broad = organic(q / BROAD_M + 3.7);
	float bloom = organic(q / SWARD_M + 13.0);
	float tone = clamp(0.5 + (broad - 0.5) * 1.6 + (smoothstep(0.38, 0.62, bloom) - 0.5) * 0.45, 0.0, 1.0);
	vec3 t = mix(c * LUSH, c * DRY, tone);
	t *= 1.0 - (1.0 - smoothstep(0.0, 0.03, abs(bloom - 0.5))) * BLOOM_RIM;
	t *= 1.0 + (organic(q / 1.4 + 17.0) - 0.5) * 0.14;
	float water = 0.0;
	float rim = 0.0;
	vec2 l;
	vec2 id;
	float close = close_tier(fw);
	if (wet) {
		t = mix(t, c * PEAT, smoothstep(0.4, 0.78, organic(q / 5.0 + 21.0)) * 0.4);
		t = mix(t, c * MOSS, smoothstep(0.45, 0.8, organic(q / 3.0 + 37.0)) * 0.35);
		if (close > 0.001 && strewn(p + 13.1, own + 31.0, metres(0.8), 0.7, l, id))
			t = pebble(t, c * mix(vec3(1.0, 1.1, 0.84), vec3(1.12, 1.08, 0.8), hash(id + 9.0)), l, 0.08 + 0.2 * hash(id + 2.0) * hash(id + 4.0), e, close * 0.6);
		if (strewn(p + 4.2, 17.0, metres(POOL_APART_M), 0.6, l, id)) {
			float r = POOL_M * (0.45 + 0.55 * hash(id + 2.0));
			vec2 oval = turned(l, hash(id + 1.7) * 3.1416) * vec2(1.0, 1.0 + 0.7 * hash(id + 6.0));
			float sd = length(oval) - r + (organic(l / 1.6 + hash2(id) * 37.0) - 0.5) * r * 0.8;
			water = cover(sd, max(e, 0.05));
			rim = cover(sd - 0.7, 0.35) * (1.0 - water);
			t = mix(t, c * PEAT * 0.78, cover(sd - 0.22, 0.12) * 0.75);
			vec3 pond = c * vec3(0.62, 0.74, 0.86) * (1.0 + (organic(q / 1.1 + 2.0) - 0.5) * 0.1);
			pond = mix(pond, pond * 0.8, cover(sd + r * 0.5, r * 0.3));
			pond = mix(pond, pond * 1.25, cover(abs(sd + 0.12) - 0.03, max(e, 0.03)) * close * 0.5);
			t = mix(t, pond, water * 0.92);
		}
	}
	float trod = (1.0 - smoothstep(0.6, TROD_M, built.r + (organic(q / 1.3 + 44.0) - 0.5) * 2.4)) * (wet ? 0.5 : 1.0);
	trod = max(trod, (1.0 - smoothstep(0.0, 1.2, built.g + (organic(q / 0.9 + 48.0) - 0.5) * 1.2)) * 0.6) * near;
	trod *= 0.35 + 0.65 * smoothstep(0.38, 0.62, organic(q / 2.6 + 52.0));
	t = mix(t, c * bare * (1.0 + (grain(q / 0.08) - 0.5) * 0.12 * close), trod * 0.6);
	float bald = max(water, trod);
	if (close > 0.001) {
		t *= 1.0 + (grain(q / 0.11 + 3.0) - 0.5) * 0.06 * close;
		float thick = wet ? 0.45 + 0.55 * rim : 0.0;
		float light;
		float dark = blades(p + 1.9, own, thick, fw, light);
		t = mix(t, t * BLADE_DARK, dark * close * (1.0 - bald));
		t = mix(t, t * BLADE_LIGHT, light * close * (1.0 - bald));
		vec3 pen = style == MODERN ? mix(c, MODERN_GREEN, 0.6) : style == ANCIENT ? mix(c, ANCIENT_UMBER, 0.6) : mix(c * 0.55, SURVEY_INK, 0.3);
		if (style != MODERN || wet) t = mix(t, pen, ticks(p + 6.3, own, thick, fw) * TICK_INK * close * (1.0 - bald));
		if (wet) t = mix(t, pen, ticks(p + 11.7, own + 2.0, 1.0, fw) * TICK_INK * close * rim);
	}
	float street = street_tier(fw);
	if (close > 0.001 && !wet && style != ANCIENT && strewn(p + 3.3, own + 1.0, metres(0.5), 0.55 * smoothstep(0.56, 0.72, organic(q / 7.0 + 90.0)), l, id)) {
		float size = max(0.032, e * 0.9);
		bool gold = hash(id + 4.0) < 0.3;
		float petals = length(l) - size * (0.74 + 0.26 * cos(atan(l.y, l.x) * 9.0));
		t = mix(t, gold ? FLECK_GOLD : PETAL, cover(petals, e) * close);
		t = mix(t, gold ? vec3(0.6, 0.42, 0.12) : FLECK_GOLD, cover(length(l) - size * 0.34, e) * street);
	}
	if (street <= 0.001) return mix(c, t, near);
	float pale;
	float turf = strokes(p + 7.7, own + 3.0, metres(0.1), metres(0.07), metres(0.011), 0.8, 1.6, fw, pale);
	t = mix(t, t * vec3(0.82, 0.87, 0.78), turf * street * (1.0 - bald));
	t = mix(t, t * vec3(1.15, 1.15, 1.0), pale * street * (1.0 - bald));
	if (!wet && strewn(p + 2.1, own + 11.0, metres(0.15), 0.8 * smoothstep(0.52, 0.68, organic(q / 3.0 + 70.0)), l, id)) {
		float leaves = min(min(length(l - vec2(0.0, 0.021)), length(l - vec2(0.018, -0.011))), length(l + vec2(0.018, 0.011))) - 0.016;
		t = mix(t, c * vec3(0.7, 0.92, 0.64), cover(leaves, e) * street);
		t = mix(t, c * 1.3, cover(length(l) - 0.005, e) * street * 0.7);
	}
	if (strewn(p + 5.5, own + 13.0, metres(1.3), 0.3, l, id)) t = pebble(t, c * vec3(1.05, 1.0, 0.95), l, 0.03 + 0.04 * hash(id + 2.0), e, street * (1.0 - bald));
	if (wet && strewn(p + 8.1, own + 23.0, metres(0.09), 0.7 * water, l, id)) t = mix(t, c * vec3(0.9, 1.15, 0.7), cover(length(l) - 0.016, e) * street);
	if (!wet && strewn(p + 10.4, own + 25.0, metres(0.7), 0.4, l, id)) {
		float a = atan(l.y, l.x) + hash(id + 3.0) * 6.2832;
		float rosette = length(l) - (0.05 + 0.03 * hash(id + 2.0)) * (0.45 + 0.55 * abs(cos(a * 3.0)));
		t = mix(t, c * vec3(0.66, 0.84, 0.6), cover(rosette, e) * street);
		t = mix(t, c * vec3(0.9, 1.08, 0.78), cover(length(l) - 0.012, e) * street);
	}
	if (!wet && strewn(p + 16.6, own + 27.0, metres(9.0), 0.3, l, id)) {
		float mound = length(l) - 0.2 - (organic(l * 9.0 + hash2(id) * 37.0) - 0.5) * 0.12;
		t = mix(t, c * vec3(0.52, 0.5, 0.42), cover(mound - 0.05, e) * (1.0 - cover(mound, e)) * street * 0.5);
		t = mix(t, vec3(0.42, 0.33, 0.24) * (1.0 + (grain(l * 60.0) - 0.5) * 0.3), cover(mound, e) * street);
	}
	return mix(c, t, near);
}

vec3 real_water(vec3 c, vec2 p, float fw, bool sea, float border) {
	float near = near_tier(fw);
	if (near <= 0.001) return c;
	vec2 q = p / metres(1.0);
	float e = fw / metres(1.0);
	float swell = organic(q / (sea ? 16.0 : 6.0) + 3.0) - 0.5;
	float ripple = sin(dot(q, SWELL_FROM) / 0.16 + organic(q / 2.2 + 9.0) * 5.0) * smoothstep(0.35, 0.7, organic(q / 3.5 + 40.0)) * 0.5;
	vec2 lie = vec2(dot(q, SWELL_FROM) / 0.14, dot(q, vec2(SWELL_FROM.y, -SWELL_FROM.x)) / 0.7);
	float glint = smoothstep(0.74, 0.9, grain(lie + 7.0)) * smoothstep(0.5, 0.75, grain(q / 3.0 + 1.0));
	vec3 t = mix(c * (1.0 + swell * (sea ? 0.2 : 0.14) + ripple * 0.05), c * 1.2 + 0.02, glint * 0.14);
	float bank = border / metres(1.0);
	float close = close_tier(fw);
	float street = street_tier(fw);
	vec2 l;
	vec2 id;
	if (sea) {
		float trains = sin(dot(q, SWELL_FROM) / SWELL_M + organic(q / 11.0) * 5.0);
		t *= 1.0 + trains * 0.045;
		vec2 crest = vec2(dot(q, SWELL_FROM) / 1.1, dot(q, vec2(SWELL_FROM.y, -SWELL_FROM.x)) / 3.4);
		float cap = smoothstep(0.5, 1.0, trains) * smoothstep(0.5, 0.85, grain(crest + 3.0)) * smoothstep(0.5, 0.7, organic(q / 14.0 + 5.0));
		t = mix(t, SURF, cap * smoothstep(0.4, 0.75, grain(q / 0.1 + 2.0)) * 0.4);
		float wash = (1.0 - smoothstep(0.6, SURF_M, bank + (grain(q / 2.3 + 2.0) - 0.5) * 2.4)) * (0.55 + 0.45 * grain(q / 0.6 + 9.0));
		float line = (1.0 - smoothstep(0.0, 0.7, abs(bank - SURF_M * 2.2 - (grain(q / 3.1 + 6.0) - 0.5) * 3.0))) * smoothstep(0.35, 0.6, grain(q / 4.0 + 1.0));
		float tone;
		float lace = 1.0 - joints(q + organic(q / 1.3) * 0.6, 0.45, 0.16, tone);
		t = mix(t, SURF, clamp(wash * mix(0.85, 0.35 + 0.65 * (1.0 - lace), street) + line * 0.5, 0.0, 1.0));
		return mix(c, t, near);
	}
	float shallow = 1.0 - smoothstep(0.4, 3.0, bank + (grain(q / 1.7 + 4.0) - 0.5) * 1.6);
	t = mix(t, c * vec3(1.2, 1.2, 1.08), shallow * 0.55);
	if (close > 0.001) {
		float margin = (1.0 - smoothstep(1.5, 2.6, bank)) * smoothstep(0.5, 0.66, organic(q / 6.0 + 15.0));
		float light;
		float reeds = blades(p + 9.1, 5.0, margin, fw, light);
		t = mix(t, mix(c * 0.45, MODERN_GREEN, 0.6), (reeds + light) * margin * close * 0.85);
		float pads = smoothstep(1.2, 2.2, bank) * (1.0 - smoothstep(3.5, 5.0, bank)) * smoothstep(0.6, 0.74, organic(q / 5.0 + 33.0));
		if (strewn(p + 12.4, 7.0, metres(0.75), 0.6 * pads, l, id)) {
			vec2 turn = turned(l, hash(id + 1.7) * 6.2832);
			float pad = max(length(turn) - max(0.17, e), -(abs(turn.y) - turn.x * 0.35));
			t = mix(t, mix(c, MODERN_GREEN, 0.6) * 1.25, cover(pad, e) * close);
			if (hash(id + 3.0) < 0.25) t = mix(t, PETAL, cover(length(turn + vec2(0.05, 0.0)) - 0.05, e) * street);
		}
	}
	if (street > 0.001) {
		if (strewn(p + 6.6, 3.0, metres(0.3), 0.75 * shallow, l, id)) t = pebble(t, c * 1.1, l, 0.03 + 0.05 * hash(id + 2.0), e, street * shallow * 0.8);
		float deep = smoothstep(2.5, 4.5, bank);
		if (strewn(p + 15.3, 9.0, metres(7.0), 0.35 * deep, l, id)) {
			vec2 turn = turned(l, hash(id + 1.7) * 6.2832);
			float fish = min(length(turn * vec2(1.0, 3.6)) - 0.2, max(abs(turn.y) - (turn.x - 0.16) * 0.7, max(0.16 - turn.x, turn.x - 0.3)));
			t = mix(t, c * 0.68, cover(fish, e) * street * 0.6);
		}
		if (strewn(p + 18.8, 11.0, metres(9.0), 0.3, l, id)) t = mix(t, c * 1.2, cover(abs(length(l) - 0.5 * hash(id + 4.0) - 0.2) - 0.012, e) * street * 0.5);
	}
	return mix(c, t, near);
}

float crest_of(vec2 q, float size, out float lee) {
	float along = dot(q, WIND) / size + organic(q / (size * 2.6) + 7.0) * 1.5;
	float s = fract(along);
	lee = smoothstep(DUNE_LEE, DUNE_LEE + 0.025, s) * (1.0 - smoothstep(DUNE_LEE + 0.03, 1.0, s));
	return s < DUNE_LEE ? s / DUNE_LEE : (1.0 - s) / (1.0 - DUNE_LEE);
}

vec3 real_sand(vec3 c, vec2 p, float own, float fw, float shore, int style) {
	float near = near_tier(fw);
	if (near <= 0.001) return c;
	vec2 q = p / metres(1.0);
	float e = fw / metres(1.0);
	float lee;
	float rise = crest_of(q, DUNE_M, lee);
	float field = smoothstep(0.42, 0.6, organic(q / 46.0 + 12.0));
	vec3 t = c * (1.0 + (organic(q / 7.0) - 0.5) * 0.1);
	t *= 1.0 + (rise - 0.45) * 0.09 * field;
	t = mix(t, c * vec3(0.87, 0.85, 0.83), lee * 0.8 * field);
	float wet = 1.0 - smoothstep(metres(1.0), metres(5.0), shore + (grain(q / 2.0 + 8.0) - 0.5) * metres(2.0));
	float close = close_tier(fw);
	if (close > 0.001) {
		float rippled = smoothstep(0.52, 0.68, organic(q / 6.0 + 30.0)) * (1.0 - lee);
		float ripple = sin(dot(q, WIND) / RIPPLE_M * 6.2832 + organic(q / 3.4 + 3.0) * 5.0);
		t *= 1.0 + ripple * 0.03 * close * rippled;
	}
	t = mix(t, t * vec3(0.78, 0.77, 0.76), wet);
	float street = street_tier(fw);
	if (street <= 0.001) return mix(c, t, near);
	t *= 1.0 + (grain(q / 0.018 + 5.0) - 0.5) * 0.09 * street;
	vec2 l;
	vec2 id;
	if (strewn(p + 2.2, own + 3.0, metres(1.1), 0.5, l, id)) {
		vec2 turn = turned(l, hash(id + 1.7) * 6.2832);
		float fan = max(length(turn) - 0.045, -turn.y - 0.006);
		t = mix(t, vec3(0.9, 0.86, 0.78), cover(fan, e) * street);
		t = mix(t, c * 0.6, cover(abs(fract(atan(turn.y, turn.x) * 2.2) - 0.5) - 0.42, 0.06) * cover(fan, e) * street * 0.5);
	}
	if (strewn(p + 7.1, own + 5.0, metres(0.8), 0.45, l, id)) t = pebble(t, c * 0.95, l, 0.015 + 0.03 * hash(id + 2.0), e, street);
	if (strewn(p + 11.9, own + 7.0, metres(5.0), 0.3, l, id)) {
		vec2 turn = turned(l, hash(id + 1.7) * 6.2832);
		for (int k = -2; k <= 2; k++) {
			vec2 foot = turn - vec2(float(k) * 0.34, mod(float(k), 2.0) * 0.16 - 0.08);
			t = mix(t, t * 0.84, cover(length(foot * vec2(1.0, 2.1)) - 0.12, e) * street);
		}
	}
	if (wet > 0.05 && strewn(p + 14.2, own + 9.0, metres(1.6), 0.5 * wet, l, id))
		t = mix(t, c * vec3(0.42, 0.46, 0.3), cover(organic(l * 9.0 + hash2(id) * 37.0) - 0.42 + length(l) * 1.6, 0.05) * street * 0.85);
	return mix(c, t, near);
}

vec3 real_rock(vec3 c, vec2 p, float own, float fw) {
	float near = near_tier(fw);
	if (near <= 0.001) return c;
	vec2 q = p / metres(1.0);
	float e = fw / metres(1.0);
	vec3 t = c * (1.0 + (organic(q / 6.0) - 0.5) * 0.22 + (grain(q / 0.3 + 31.0) - 0.5) * 0.07);
	float tone;
	vec2 bedded = turned(q, 0.5) * vec2(1.0, 2.3) + (vec2(organic(q / 9.0), organic(q / 9.0 + 4.0)) - 0.5) * 9.0;
	float joint = joints(bedded, JOINT_M, 0.16, tone);
	float broken = smoothstep(0.4, 0.56, organic(q / 17.0 + 3.0));
	t *= 1.0 + tone * 0.14 * broken;
	t = mix(t, c * 0.58, joint * 0.55 * broken * (1.0 - far_blend(fw, metres(0.5))));
	float seen = 1.0 - far_blend(fw, metres(0.9));
	float shade = scatter(p + 8.8 - SHADE_TO * metres(0.3), own + 2.0, metres(6.5), metres(0.8), 0.95, 0.3, fw);
	float boulder = scatter(p + 8.8, own + 2.0, metres(6.5), metres(0.8), 0.95, 0.3, fw);
	float top = scatter(p + 8.8 + SHADE_TO * metres(0.22), own + 2.0, metres(6.5), metres(0.42), 0.95, 0.3, fw);
	t = mix(t, c * 0.5, shade * (1.0 - boulder) * 0.8 * seen);
	t = mix(t, c * 0.9, boulder * seen);
	t = mix(t, c * 1.12, top * boulder * seen);
	float scree = smoothstep(0.62, 0.72, organic(q / 8.0 + 60.0)) * (1.0 - broken);
	t = mix(t, c * LICHEN, smoothstep(0.6, 0.72, organic(q / 2.4 + 50.0)) * 0.45);
	t = mix(t, c * MOSS * 0.85, smoothstep(0.64, 0.74, organic(q / 3.3 + 77.0)) * 0.4);
	float street = street_tier(fw);
	vec2 l;
	vec2 id;
	if (close_tier(fw) > 0.001 && strewn(p + 2.9, own + 8.0, metres(0.8), 0.6 * scree, l, id))
		t = pebble(t, c * (0.85 + 0.25 * hash(id + 7.0)), l, 0.025 + 0.16 * hash(id + 2.0) * hash(id + 4.0), e, close_tier(fw));
	if (street <= 0.001) return mix(c, t, near);
	t *= 1.0 + (organic(q / 0.35 + 14.0) - 0.5) * 0.12 * street + (grain(q / 0.02 + 9.0) - 0.5) * 0.08 * street;
	float vein = abs(organic(q / 1.9 + 71.0) - 0.5);
	t = mix(t, c * 1.14, (1.0 - smoothstep(0.004, 0.012, vein)) * smoothstep(0.5, 0.7, organic(q / 5.0 + 19.0)) * street * 0.5);
	if (strewn(p + 6.4, own + 12.0, metres(2.4), 0.4, l, id)) {
		float blotch = length(l) - 0.07 - 0.1 * hash(id + 2.0) + (organic(l * 9.0 + hash2(id) * 37.0) - 0.5) * 0.16;
		t = mix(t, c * (hash(id + 5.0) < 0.6 ? vec3(1.12, 1.1, 0.96) : vec3(1.1, 1.02, 0.84)), cover(blotch, 0.02 + e) * street * 0.5);
	}
	return mix(c, t, near);
}

vec3 real_earth(vec3 c, vec2 p, float own, float fw, int style) {
	float near = near_tier(fw);
	if (near <= 0.001) return c;
	vec2 q = p / metres(1.0);
	float e = fw / metres(1.0);
	vec3 t = c * (1.0 + (organic(q / 4.5) - 0.5) * 0.2 + (grain(q / 0.2 + 11.0) - 0.5) * 0.08);
	float dry = smoothstep(0.5, 0.64, organic(q / 7.0 + 23.0));
	float close = close_tier(fw);
	float clod;
	t = mix(t, c * 1.06, dry * 0.5);
	t = mix(t, c * 0.62, joints(q + (vec2(organic(q / 2.0), organic(q / 2.0 + 3.0)) - 0.5) * 1.2, CRACK_M, 0.045, clod) * dry * 0.6 * close);
	t *= 1.0 + clod * 0.06 * dry * close;
	float damp = smoothstep(0.62, 0.76, organic(q / 5.0 + 61.0)) * (1.0 - dry);
	t = mix(t, c * vec3(0.7, 0.71, 0.75), damp * 0.7);
	t = mix(t, c * 1.2, damp * smoothstep(0.7, 0.85, grain(q / 0.5 + 3.0)) * 0.4);
	if (style != MODERN && close > 0.001) t = mix(t, mix(c * 0.5, MODERN_GREEN, 0.4), ticks(p + 2.7, own, 0.25, fw) * 0.6 * close);
	float street = street_tier(fw);
	if (street <= 0.001) return mix(c, t, near);
	t *= 1.0 + (grain(q / 0.02 + 5.0) - 0.5) * 0.1 * street;
	vec2 l;
	vec2 id;
	if (strewn(p + 1.3, own + 4.0, metres(0.32), 0.6, l, id)) t = pebble(t, c * (0.9 + 0.3 * hash(id + 7.0)), l, 0.012 + 0.035 * hash(id + 2.0), e, street);
	float pale;
	float straw = strokes(p + 5.9, own + 6.0, metres(0.4), metres(0.09), metres(0.008), 0.3, 6.3, fw, pale);
	t = mix(t, c * vec3(1.3, 1.24, 0.95), (straw + pale) * street * 0.7);
	if (strewn(p + 9.4, own + 8.0, metres(2.6), 0.4, l, id)) {
		vec2 turn = turned(l, hash(id + 1.7) * 6.2832);
		float shoe = max(abs(length(turn) - 0.075) - 0.018, -turn.x - 0.02);
		t = mix(t, t * 0.74, cover(shoe, e) * street);
		t = mix(t, t * 0.74, cover(max(abs(length(turn - vec2(0.42, 0.14)) - 0.075) - 0.018, -(turn.x - 0.42) - 0.02), e) * street);
	}
	return mix(c, t, near);
}

vec3 real_snow(vec3 c, vec2 p, float own, float fw) {
	float near = near_tier(fw);
	if (near <= 0.001) return c;
	vec2 q = p / metres(1.0);
	float e = fw / metres(1.0);
	float hollow = organic(q / 12.0 + 31.0);
	vec3 t = mix(c, c * SNOW_SHADE, smoothstep(0.42, 0.72, hollow) * 0.6);
	t = mix(t, c * SNOW_SHADE, smoothstep(0.5, 0.75, organic(q / 3.2 + 4.0)) * 0.3);
	float thin = smoothstep(0.66, 0.8, organic(q / 7.0 + 44.0));
	float close = close_tier(fw);
	vec2 l;
	vec2 id;
	if (close > 0.001) {
		t = mix(t, c * vec3(0.82, 0.8, 0.75), thin * 0.3);
		if (strewn(p + 2.6, own + 13.0, metres(0.9), 0.55 * thin, l, id))
			t = pebble(t, vec3(0.5, 0.48, 0.44) * (0.8 + 0.5 * hash(id + 7.0)), l, 0.03 + 0.13 * hash(id + 2.0) * hash(id + 4.0), e, close);
		t = mix(t, vec3(0.74, 0.66, 0.42), ticks(p + 3.9, own + 7.0, thin, fw) * close * thin);
		if (strewn(p + 6.2, own + 9.0, metres(6.0), 0.4, l, id)) {
			vec2 turn = turned(l, hash(id + 1.7) * 6.2832);
			for (int k = -3; k <= 3; k++) {
				vec2 foot = turn - vec2(float(k) * 0.42, mod(float(k), 2.0) * 0.18 - 0.09);
				t = mix(t, c * SNOW_PRINT, cover(length(foot * vec2(1.0, 1.9)) - max(0.11, e), e) * close);
			}
		}
	}
	float street = street_tier(fw);
	if (street <= 0.001) return mix(c, t, near);
	t = mix(t, vec3(1.0), smoothstep(0.84, 0.95, grain(q / 0.03 + 2.0)) * 0.6 * street);
	t *= 1.0 + (organic(q / 0.25 + 8.0) - 0.5) * 0.05 * street;
	if (strewn(p + 12.7, own + 11.0, metres(2.2), 0.35, l, id)) {
		vec2 turn = turned(l, hash(id + 2.9) * 6.2832);
		t = mix(t, vec3(0.34, 0.28, 0.22), cover(segment(turn, vec2(-0.16, 0.0), vec2(0.16, 0.03)) - 0.008, e) * street);
		t = mix(t, vec3(0.34, 0.28, 0.22), cover(segment(turn, vec2(0.0, 0.0), vec2(0.09, -0.07)) - 0.006, e) * street);
	}
	return mix(c, t, near);
}
