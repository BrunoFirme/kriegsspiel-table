float road_width(int style, float class) {
	if (style == ANCIENT) return class < 0.33 ? ANCIENT_ROAD_W : class < 0.66 ? ANCIENT_LANE_W : ANCIENT_HIGHWAY_W;
	return class < 0.33 ? W2 * 3.0 : metres(class < 0.66 ? ROAD_W_M : CHAUSSEE_W_M) * 0.5;
}

float road_mid(vec2 uv) {
	vec2 n = street_normal(1, uv);
	vec2 t = vec2(-n.y, n.x) * 0.5 / cells;
	return 0.2 * (mid_at(uv - t * 2.0) + mid_at(uv - t) + mid_at(uv) + mid_at(uv + t) + mid_at(uv + t * 2.0));
}

float dash_along(vec2 p, vec2 uv, float period, float share, float fw) {
	vec2 n = street_normal(1, uv);
	float by_x = step(fract(p.x / period), share);
	float by_y = step(fract(p.y / period), share);
	return mix(mix(by_y, by_x, n.y * n.y), share, far_blend(fw, period));
}

vec3 road_over(int style, vec3 c, vec3 sheet, vec2 p, vec2 uv, float class, float mid, float band_edge, float fw, bool over_water) {
	float d = abs(mid);
	if (style == ANCIENT) {
		float w = road_width(ANCIENT, class);
		c = mix(c, RED_OCHRE, 1.0 - smoothstep(w - fw, w + fw, d));
		if (over_water) c = mix(c, ANCIENT_INK, ink(abs(d - w - W2), W2 * 0.7, fw));
		return c;
	}
	if (class < 0.33) {
		if (style == MODERN) return mix(c, MODERN_BLACK, ink(d, M1 * 0.5, fw) * dash_along(p, uv, 0.16, 0.55, fw));
		return mix(c, WEGEBRAUN, ink(d, W2 * 0.9, fw) * dash_along(p, uv, 0.14, 0.55, fw));
	}
	float inside = smoothstep(-fw, fw, band_edge);
	if (style == MODERN) {
		float casing = M1 * 0.9;
		c = mix(c, sheet, inside);
		if (class >= 0.66) c = mix(c, MODERN_RED, smoothstep(casing * 2.0 - fw, casing * 2.0 + fw, plate_edge(band_edge, PLATE_RED)));
		c = mix(c, MODERN_BLACK, ink(abs(band_edge - casing), casing, fw));
		if (over_water) c = mix(c, MODERN_BLACK, ink(abs(band_edge - casing * 3.0), M1 * 0.6, fw));
		return c;
	}
	if (class < 0.66) {
		c = mix(c, wash(palette[ROAD].rgb, sheet, SURVEY_PAPER, p, band_edge, 0.5), inside);
		c = mix(c, SURVEY_INK, hand_ink(abs(band_edge - W1), W1, p, fw));
	} else {
		c = mix(c, wash(mix(WEGEBRAUN, sheet, 0.55), sheet, SURVEY_PAPER, p, band_edge, 0.4), inside);
		c = mix(c, SURVEY_INK, hand_ink(abs(band_edge - W3), W3, p, fw));
	}
	if (over_water) c = mix(c, SURVEY_INK, ink(abs(band_edge - W3 * 3.0), W2 * 0.7, fw));
	return c;
}
