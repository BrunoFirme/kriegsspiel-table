vec3 ancient_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw) {
	const float S = 0.6;
	vec3 sepia = vec3(0.2, 0.12, 0.05);
	vec3 crown_c = tint * vec3(0.9, 0.95, 0.88);
	if (far_blend(fw, S) >= 1.0) return mix(c, tint * 0.9, 0.7);
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point(p + NORTH * S * 0.5, uv, to_uv, S, 0.55, i, j, id, salt) - NORTH * S * 0.5;
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < 0.05 || hash(id + 4.2) > 0.9) continue;
			float scale = clamp(room / 0.22, 0.45, 1.0) * (0.85 + 0.3 * hash(id + 3.1));
			float rc = 0.16 * scale;
			float trunk = 0.11 * scale;
			vec2 cc = o + NORTH * (trunk + rc * 0.9);
			vec2 q = (p - cc) * vec2(1.0, 0.88);
			float d = length(q);
			if (length(p - o) > rc * 3.2) continue;
			float a = atan(q.y, q.x);
			float h = hash(id + 7.7) * TAU;
			float rr = rc * (1.0 + 0.07 * sin(a * 7.0 + h) + 0.04 * sin(a * 13.0 + h * 3.0));
			float body = 1.0 - smoothstep(rr - fw * 0.75, rr + fw * 0.25, d);
			float ground = ink(segment(p, o + vec2(-rc * 0.25, 0.0), o + vec2(rc * 1.25, 0.0)), 0.008, fw);
			float stem = ink(segment(p, o, cc), 0.011 * scale, fw);
			col = mix(col, sepia, max(ground, stem) * 0.9);
			float shade = smoothstep(-0.35, 0.55, dot(q / rc, vec2(0.8, 0.6)));
			float lines = hatch(p.x + p.y, 0.032, 0.0065 * shade, fw);
			col = mix(col, crown_c, body);
			col = mix(col, sepia, lines * body * 0.85);
			col = mix(col, sepia, ink(abs(d - rr), 0.008, fw));
		}
	}
	return mix(col, mix(c, tint * 0.9, 0.7), far_blend(fw, S));
}

vec3 ancient(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, float fw, float edge, float border) {
	vec3 sepia = vec3(0.22, 0.13, 0.06);
	vec3 c = wash(tint, sheet, ANCIENT_PAPER, p, edge, 0.4);
	float rim = 0.85;
	if (k == GRASS) {
		c = mix(c, sepia, tufts(p, uv, to_uv, 0.6, 0.06, 0.008, 0.45, fw) * 0.6);
		rim = 0.0;
	} else if (k == FIELD) {
		float strip_edge, block_edge, angle;
		vec3 v = parcels(p, uv, to_uv, strip_edge, block_edge, angle);
		c *= 0.95 + 0.08 * v.x;
		float ruled = hatch(dot(p, vec2(-sin(angle), cos(angle))), 0.055, 0.004, fw) * step(0.4, v.y);
		c = mix(c, sepia, ruled * 0.35);
		c = mix(c, sepia, scatter(p, uv, to_uv, 0.06, 0.012, 0.2, 1.0, fw) * (1.0 - smoothstep(0.0, 0.03, block_edge)) * 0.85);
	} else if (k == WOOD) {
		c = ancient_wood(c, tint, p, uv, to_uv, fw);
		rim = 0.0;
	} else if (k == MARSH) {
		c = mix(c, sepia, marks(p, uv, to_uv, 0.16, 0.08, 0.007, 0.0, 0.1, 0.8, fw) * 0.6);
		c = mix(c, sepia, tufts(p, uv, to_uv, 0.38, 0.08, 0.008, 0.65, fw) * 0.8);
		rim = 0.4;
	} else if (k == WATER) {
		float lining = 0.0;
		for (int i = 0; i < 7; i++) {
			float at = 0.045 + float(i) * (0.05 + float(i) * 0.012);
			float wob = (noise(p * 2.2 + float(i) * 3.1) - 0.5) * 0.02;
			lining = max(lining, ink(abs(border - at - wob), 0.0065, fw) * (1.0 - float(i) * 0.11));
		}
		float open = smoothstep(0.35, 0.6, border);
		float ripple = marks(p, uv, to_uv, 0.2, 0.06, 0.005, 0.0, 0.25, 0.55, fw) * open;
		c = mix(c, sepia, max(lining, ripple) * 0.65);
		c = mix(c, sepia, scatter(p, uv, to_uv, 0.05, 0.006, 0.9, 0.35, fw) * open * 0.45);
		return mix(c, sepia, ink(edge, 0.022, fw));
	} else if (k == ROAD) {
		c = mix(c, sepia, ink(abs(edge - 0.05), 0.006, fw) * 0.35);
		return mix(c, sepia, ink(edge, 0.016, fw));
	} else if (k == BRIDGE) {
		float wall = (beside == WATER ? 1.0 : 0.0) * (1.0 - smoothstep(PARAPET - fw, PARAPET, edge));
		c = mix(c, sepia, hatch(dot(p, vec2(0.7071, 0.7071)), 0.03, 0.006, fw) * wall * 0.7);
		c = mix(c, sepia, ink(abs(edge - PARAPET), 0.01, fw) * (beside == WATER ? 1.0 : 0.0));
		return mix(c, sepia, ink(edge, 0.02, fw));
	} else if (k == EARTH) {
		c = mix(c, sepia, marks(p, uv, to_uv, 0.13, 0.025, 0.007, 0.6, 1.2, 0.6, fw) * 0.55);
	} else if (k == ROCK) {
		float crag = marks(p, uv, to_uv, 0.12, 0.05, 0.011, 2.1, 0.5, 0.9, fw);
		float cross = marks(p + 1.7, uv, to_uv, 0.12, 0.04, 0.008, 0.6, 0.5, 0.6, fw);
		c = mix(c, sepia, max(crag, cross * smoothstep(0.5, 0.7, fbm(p * 1.1))) * 0.85);
	} else if (k == SAND) {
		c = mix(c, sepia, scatter(p, uv, to_uv, 0.045, 0.0075, 0.95, 0.7, fw) * 0.75);
		rim = 0.5;
	} else if (k == SNOW) {
		c = mix(c, vec3(0.35, 0.42, 0.5), hatch(p.y, 0.09, 0.004, fw) * 0.35);
		rim = 0.6;
	}
	return mix(c, sepia, ink(edge, 0.016, fw) * rim);
}
