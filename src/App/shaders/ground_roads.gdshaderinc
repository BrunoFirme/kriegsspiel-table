float road_width(int style, float class) {
	if (style == ANCIENT) return class < 0.33 ? ANCIENT_ROAD_W : class < 0.66 ? ANCIENT_LANE_W : ANCIENT_HIGHWAY_W;
	return class < 0.33 ? W2 * 3.0 : metres(class < 0.66 ? ROAD_W_M : CHAUSSEE_W_M) * 0.5;
}

float road_mid(vec2 uv, mat2 to_uv) {
	vec2 du = to_uv * vec2(cell_world * 0.5, 0.0);
	vec2 dv = to_uv * vec2(0.0, cell_world * 0.5);
	return 0.25 * (mid_at(uv + du + dv) + mid_at(uv + du - dv) + mid_at(uv - du + dv) + mid_at(uv - du - dv));
}

vec3 road_over(int style, vec3 c, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float class, float mid, float fw, bool over_water) {
	float d = abs(mid);
	if (style == ANCIENT) {
		float w = road_width(ANCIENT, class);
		c = mix(c, RED_OCHRE, 1.0 - smoothstep(w - fw, w + fw, d));
		if (over_water) c = mix(c, ANCIENT_INK, ink(abs(d - w - W2), W2 * 0.7, fw));
		return c;
	}
	if (class < 0.33) {
		if (style == MODERN) return mix(c, MODERN_BLACK, max(dotted_road(p, uv, to_uv, M2 * 1.5, 0.12, M1 * 0.7, fw), dotted_road(p + 0.06, uv, to_uv, M2 * 1.5, 0.12, M1 * 0.7, fw)));
		return mix(c, WEGEBRAUN, dotted_road(p, uv, to_uv, 0.0, 0.1, W2, fw));
	}
	float w = road_width(style, class);
	float line = 1.0 - smoothstep(w - fw, w + fw, d);
	if (style == MODERN) {
		float casing = M1 * 1.2;
		c = mix(c, sheet, line);
		if (class >= 0.66) c = mix(c, MODERN_RED, 1.0 - smoothstep(w - casing * 2.0 - fw, w - casing * 2.0 + fw, plate_edge(d, PLATE_RED)));
		c = mix(c, MODERN_BLACK, ink(abs(d - w + casing), casing, fw));
		if (over_water) c = mix(c, MODERN_BLACK, ink(abs(d - w - M2), M1, fw));
		return c;
	}
	if (class < 0.66) {
		c = mix(c, wash(palette[ROAD].rgb, sheet, SURVEY_PAPER, p, w - d, 0.5), line);
		c = mix(c, SURVEY_INK, hand_ink(abs(d - w + W1), W1, p, fw));
	} else {
		c = mix(c, wash(mix(CARMINE, sheet, 0.45), sheet, SURVEY_PAPER, p, w - d, 0.3), line);
		c = mix(c, SURVEY_INK, hand_ink(abs(d - w + W3), W3, p, fw));
		c = mix(c, SURVEY_INK, dotted_road(p, uv, to_uv, w + W3 * 3.0, 0.42, W3 * 0.8, fw));
	}
	if (over_water) c = mix(c, SURVEY_INK, ink(abs(d - w - W3 * 2.0), W2, fw));
	return c;
}
