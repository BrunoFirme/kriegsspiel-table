float road_width(int style, float class) {
	if (style == ANCIENT) return class < 0.33 ? ANCIENT_ROAD_W : class < 0.66 ? ANCIENT_LANE_W : ANCIENT_HIGHWAY_W;
	return class < 0.33 ? W2 * 3.0 : metres(class < 0.66 ? ROAD_W_M : CHAUSSEE_W_M) * 0.5;
}

struct RoadHit {
	float d;
	float cls;
	bool bridge;
	bool found;
};

RoadHit road_at(vec2 uv, float texel_p) {
	RoadHit h;
	h.d = 1e9;
	h.cls = 0.0;
	h.bridge = false;
	h.found = false;
	if (textureSize(road_axes, 0) != ivec2(cells)) return h;
	vec2 q = uv * cells;
	vec2 base = floor(q - 0.5);
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 cell = base + vec2(float(i), float(j));
			if (cell.x < 0.0 || cell.y < 0.0 || cell.x >= cells.x || cell.y >= cells.y) continue;
			vec4 t = round(texelFetch(road_axes, ivec2(cell), 0) * 255.0);
			if (t.a < 0.5) continue;
			float a = t.r / 255.0 * PI;
			vec2 dir = vec2(cos(a), sin(a));
			float s = abs(dot(q - cell - 0.5, vec2(-dir.y, dir.x)) - (t.g - 128.0) / ROAD_OFFSET_SCALE);
			if (s < h.d) {
				h.d = s;
				h.cls = t.b / 255.0;
				h.bridge = t.a > 1.5;
				h.found = true;
			}
		}
	}
	h.d *= texel_p;
	return h;
}

float road_mid(vec2 uv) {
	vec2 n = street_normal(1, uv);
	vec2 t = vec2(-n.y, n.x) * 0.5 / cells;
	return 0.2 * (mid_at(uv - t * 2.0) + mid_at(uv - t) + mid_at(uv) + mid_at(uv + t) + mid_at(uv + t * 2.0));
}

float road_edge_smooth(vec2 uv) {
	vec2 n = street_normal(1, uv);
	vec2 t = vec2(-n.y, n.x) * 0.5 / cells;
	return 0.2 * (distance_at(uv - t * 2.0) + distance_at(uv - t) + distance_at(uv) + distance_at(uv + t) + distance_at(uv + t * 2.0));
}

float dash_along(vec2 p, vec2 uv, float period, float share, float fw) {
	vec2 n = street_normal(1, uv);
	float by_x = step(fract(p.x / period), share);
	float by_y = step(fract(p.y / period), share);
	return mix(mix(by_y, by_x, n.y * n.y), share, far_blend(fw, period));
}

vec3 road_over(int style, vec3 c, vec3 sheet, vec2 p, vec2 uv, float class, float d, float band_half, float fw, bool over_water) {
	float w = min(road_width(style, class), band_half);
	if (style == ANCIENT) {
		c = mix(c, RED_OCHRE, 1.0 - smoothstep(w - fw, w + fw, d));
		if (over_water) c = mix(c, ANCIENT_INK, ink(abs(d - w - W2), W2 * 0.7, fw));
		return c;
	}
	if (class < 0.33) {
		if (style == MODERN) return mix(c, MODERN_BLACK, ink(d, M1 * 0.5, fw) * dash_along(p, uv, 0.16, 0.55, fw));
		return mix(c, WEGEBRAUN, ink(d, W2 * 0.9, fw) * dash_along(p, uv, 0.14, 0.55, fw));
	}
	float inside = 1.0 - smoothstep(w - fw, w + fw, d);
	if (style == MODERN) {
		float casing = M1 * 0.9;
		c = mix(c, sheet, inside);
		if (class >= 0.66) c = mix(c, MODERN_RED, 1.0 - smoothstep(w - casing * 2.0 - fw, w - casing * 2.0 + fw, plate_edge(d, PLATE_RED)));
		c = mix(c, MODERN_BLACK, ink(abs(d - w + casing), casing, fw));
		if (over_water) c = mix(c, MODERN_BLACK, ink(abs(d - w - M1), M1 * 0.6, fw));
		return c;
	}
	if (class < 0.66) {
		c = mix(c, wash(palette[ROAD].rgb, sheet, SURVEY_PAPER, p, w - d, 0.5), inside);
		c = mix(c, SURVEY_INK, hand_ink(abs(d - w + W1), W1, p, fw));
	} else {
		c = mix(c, wash(mix(WEGEBRAUN, sheet, 0.55), sheet, SURVEY_PAPER, p, w - d, 0.4), inside);
		c = mix(c, SURVEY_INK, hand_ink(abs(d - w + W3), W3, p, fw));
	}
	if (over_water) c = mix(c, SURVEY_INK, ink(abs(d - w - W2), W2 * 0.7, fw));
	return c;
}
