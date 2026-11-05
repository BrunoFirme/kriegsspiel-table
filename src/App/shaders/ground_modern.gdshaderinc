vec3 modern_wood(vec3 c, vec2 p, vec2 uv, mat2 to_uv, float fw) {
	const float S = 0.62;
	vec3 black = vec3(0.09, 0.1, 0.09);
	float m = 0.0;
	if (far_blend(fw, S) < 1.0) {
		for (int j = -1; j <= 1; j++) {
			for (int i = -1; i <= 1; i++) {
				vec2 id;
				vec2 o = grid_point(p, S, 0.7, i, j, id);
				if (hash(id + 5.0) > 0.82) continue;
				float room = room_at(o, p, uv, to_uv, WOOD);
				if (room < 0.1) continue;
				if (hash(id + 9.3) < 0.28) {
					vec2 top = o + NORTH * 0.085;
					m = max(m, ink(segment(p, top, o + vec2(-0.05, 0.015)), 0.011, fw));
					m = max(m, ink(segment(p, top, o + vec2(0.05, 0.015)), 0.011, fw));
					m = max(m, ink(segment(p, top, o - NORTH * 0.06), 0.011, fw));
				} else {
					vec2 ring = o + NORTH * 0.035;
					m = max(m, ink(abs(length(p - ring) - 0.052), 0.011, fw));
					m = max(m, ink(segment(p, ring - NORTH * 0.052, o - NORTH * 0.065), 0.011, fw));
				}
			}
		}
	}
	m = mix(m, 0.06, far_blend(fw, S));
	return mix(c, black, m * 0.92);
}

vec3 modern(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float fw, float edge, float border) {
	vec3 black = vec3(0.09, 0.1, 0.09);
	vec3 blue = vec3(0.12, 0.45, 0.72);
	vec3 brown = vec3(0.55, 0.36, 0.2);
	float mottle = (noise(p * 3.0) - 0.5) * 0.03;
	vec3 c = tint * (sheet / MODERN_PAPER) * (1.0 + mottle);
	if (k == GRASS) {
		float pair = 0.0;
		for (int j = 0; j <= 1; j++) {
			for (int i = 0; i <= 1; i++) {
				vec2 id;
				vec2 o = grid_point(p - 0.24, 0.48, 0.8, i, j, id) + 0.24;
				if (hash(id + 2.0) > 0.7) continue;
				for (int s = -1; s <= 1; s += 2) {
					vec2 b = o + vec2(float(s) * 0.018, 0.0);
					pair = max(pair, ink(segment(p, b, b + NORTH * 0.045), 0.008, fw));
				}
			}
		}
		pair = mix(pair, 0.01, far_blend(fw, 0.48));
		c = mix(c, black * 1.4, pair * 0.85);
	} else if (k == FIELD) {
		float strip_edge, block_edge, angle;
		vec3 v = parcels(p, strip_edge, block_edge, angle);
		c *= 1.0 - step(0.72, v.x) * 0.035;
		c = mix(c, black, ink(strip_edge, 0.0045, fw) * step(0.4, v.y) * 0.75);
		c = mix(c, black, ink(block_edge, 0.009, fw) * 0.9);
	} else if (k == WOOD) {
		c = modern_wood(c, p, uv, to_uv, fw);
		float dots = screen(p + 0.02, 0.085, 0.017, fw);
		c = mix(c, black, dots * (1.0 - smoothstep(0.015, 0.04, edge)) * 0.95);
	} else if (k == MARSH) {
		c = mix(c, blue, marks(p, 0.17, 0.065, 0.009, 0.0, 0.05, 0.9, fw) * 0.85);
		c = mix(c, blue * 0.6, tufts(p + 0.4, 0.5, 0.06, 0.008, 0.55, fw) * 0.85);
	} else if (k == WATER) {
		c = mix(c, blue, hatch(p.y, 0.1, 0.006, fw) * 0.45);
		return mix(c, blue, ink(edge, 0.02, fw));
	} else if (k == ROAD) {
		return mix(c, black, ink(edge, 0.02, fw));
	} else if (k == BRIDGE) {
		float wall = (beside == WATER ? 1.0 : 0.0) * (1.0 - smoothstep(PARAPET - fw, PARAPET, edge));
		c = mix(c, MODERN_PAPER, wall);
		return mix(c, black, max(ink(edge, 0.03, fw), ink(abs(edge - PARAPET), 0.015, fw) * (beside == WATER ? 1.0 : 0.0)));
	} else if (k == EARTH) {
		c = mix(c, brown, screen(p, 0.06, 0.012, fw) * 0.75);
	} else if (k == ROCK) {
		c = mix(c, black, marks(p, 0.15, 0.045, 0.01, 1.9, 0.9, 0.8, fw) * 0.8);
		c = mix(c, black, scatter(p + 1.3, 0.1, 0.009, 0.9, 0.5, fw) * 0.7);
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.6, border);
		c = mix(c, brown, scatter(p, 0.06, 0.009, 0.95, 0.5 + 0.35 * near, fw) * 0.9);
	} else if (k == SNOW) {
		c = mix(c, blue, marks(p, 0.35, 0.06, 0.007, 0.1, 0.4, 0.4, fw) * 0.7);
		c = mix(c, blue, screen(p, 0.07, 0.012, fw) * (1.0 - smoothstep(0.0, 0.08, edge)) * 0.8);
	}
	return c;
}
