vec3 survey_wood(vec3 c, vec3 tint, vec2 p, vec2 uv, mat2 to_uv, float fw, float ledges, out float bump, out float cover) {
	const float S = 0.42;
	vec3 sepia = vec3(0.2, 0.17, 0.1);
	vec3 lit = tint * vec3(1.22, 1.2, 0.96);
	vec3 mid = tint * 0.88;
	vec3 dark = tint * vec3(0.42, 0.5, 0.56);
	bump = 0.0;
	cover = 0.0;
	if (far_blend(fw, S) >= 1.0) return mix(c, tint, 0.85);
	vec3 col = c;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 o = grid_point(p, uv, to_uv, S, 0.62, i, j, id, salt);
			float room = room_at(o, p, uv, to_uv, WOOD, salt);
			if (room < 0.0) continue;
			if (ledges > 0.5 && hash(id + 9.9) > LEDGE_TREES) continue;
			float r = min(S * 0.58 * (0.78 + 0.44 * hash(id + 3.1)), room * 0.95 + 0.03);
			vec2 q = p - o;
			float d = length(q);
			if (d > r * 1.75) continue;
			float a = atan(q.y, q.x);
			float h = hash(id + 7.7) * TAU;
			float rr = r * (1.0 + 0.12 * sin(a * 5.0 + h) + 0.07 * sin(a * 9.0 + h * 2.3) + 0.04 * sin(a * 15.0 + h * 4.1));
			float body = 1.0 - smoothstep(rr - fw * 0.75, rr + fw * 0.25, d);
			float cast = (1.0 - smoothstep(r * 0.95 - fw, r * 0.95 + fw, length(q - vec2(0.3, 0.34) * r))) * (1.0 - body);
			col = mix(col, col * vec3(0.55, 0.62, 0.62), cast * 0.6);
			float side = dot(q / max(rr, 1e-4), -SUN);
			float speck = noise(q * 38.0 + h) - 0.5;
			vec3 crown = mix(lit, mid, smoothstep(-0.75, 0.05, side + speck * 0.5));
			crown = mix(crown, dark, smoothstep(0.1, 0.9, side + speck * 0.35));
			col = mix(col, crown, body);
			col = mix(col, sepia, ink(abs(d - rr + 0.006), 0.009, fw) * smoothstep(-0.1, 0.6, side) * 0.75);
			bump = max(bump, body * sqrt(max(0.0, 1.0 - d * d / (rr * rr))) * rr);
			cover = max(cover, body);
		}
	}
	return mix(col, mix(c, tint, 0.85), far_blend(fw, S));
}

vec3 survey(int k, int beside, vec3 tint, vec3 sheet, vec2 p, vec2 uv, mat2 to_uv, float own, vec2 along, float follow, float ledges, float fw, float edge, float border, out float bump, out float cover) {
	vec3 sepia = vec3(0.3, 0.2, 0.12);
	vec3 prussian = vec3(0.15, 0.3, 0.45);
	vec3 c = wash(tint, sheet, SURVEY_PAPER, p, edge, 1.0);
	bump = 0.0;
	cover = 0.0;
	float rim = 0.0;
	vec3 rim_ink = sepia;
	if (k == GRASS) {
		c = mix(c, tint * vec3(0.5, 0.66, 0.42), marks(p, uv, to_uv, 0.22, 0.05, 0.011, 0.0, 0.35, 0.75, fw) * 0.8);
	} else if (k == FIELD) {
		float strip_edge, block_edge, angle;
		vec3 v = parcels(p, uv, to_uv, along, follow, strip_edge, block_edge, angle);
		vec3 crop = v.x < 0.35 ? vec3(0.92, 0.84, 0.64) : v.x < 0.7 ? vec3(0.93, 0.89, 0.74) : vec3(0.86, 0.86, 0.66);
		c = wash(tint * mix(vec3(1.0), crop / vec3(0.9, 0.86, 0.68), 0.5), sheet, SURVEY_PAPER, p, min(edge, strip_edge * 2.5), 0.5);
		float furrow = hatch(dot(p, vec2(-sin(angle), cos(angle))), 0.07, 0.006, fw) * step(0.6, v.y);
		c = mix(c, sepia, furrow * 0.2);
		c = mix(c, sepia, ink(strip_edge, 0.005, fw) * 0.45);
		c = mix(c, sepia, ink(block_edge, 0.012, fw) * 0.75);
		rim = 0.8;
	} else if (k == WOOD) {
		c = wash(tint * vec3(1.3, 1.25, 1.05), sheet, SURVEY_PAPER, p, edge, 1.0);
		c = survey_wood(c, tint, p, uv, to_uv, fw, ledges, bump, cover);
		rim = 0.55;
		rim_ink = tint * 0.4;
	} else if (k == MARSH) {
		float pools = smoothstep(0.58, 0.64, fbm(p * 0.35 + 7.0));
		c = mix(c, wash(palette[WATER].rgb, sheet, SURVEY_PAPER, p, edge, 1.0), pools * 0.8);
		c = mix(c, prussian, marks(p, uv, to_uv, 0.2, 0.07, 0.01, 0.0, 0.12, 0.85, fw) * 0.65);
		c = mix(c, tint * vec3(0.45, 0.6, 0.45), tufts(p, uv, to_uv, 0.42, 0.07, 0.009, 0.6, fw) * 0.8);
		rim = 0.35;
		rim_ink = prussian;
	} else if (k == WATER) {
		float deep = smoothstep(0.0, 0.9, border);
		c = mix(c * 0.86, c * 1.04, deep);
		c = mix(c, prussian, ink(abs(border - 0.16), 0.008, fw) * 0.4);
		rim = 1.0;
		rim_ink = prussian;
		return mix(c, rim_ink, ink(edge, 0.028, fw) * rim);
	} else if (k == ROAD) {
		c = mix(c, c * 0.9, ink(abs(edge - 0.08), 0.03, fw) * 0.35);
		return mix(c, sepia, ink(edge, 0.014, fw));
	} else if (k == BRIDGE) {
		float wall = (beside == WATER ? 1.0 : 0.0) * (1.0 - smoothstep(PARAPET - fw, PARAPET, edge));
		c = mix(c, mix(c, sepia, 0.5), wall);
		c = mix(c, sepia, ink(abs(edge - PARAPET), 0.012, fw) * (beside == WATER ? 1.0 : 0.0));
		return mix(c, sepia, ink(edge, 0.02, fw));
	} else if (k == EARTH) {
		c = mix(c, sepia, scatter(p, uv, to_uv, 0.11, 0.012, 0.9, 0.55, fw) * 0.55);
		rim = 0.6;
	} else if (k == ROCK) {
		float crag = marks(p, uv, to_uv, 0.16, 0.05, 0.014, 1.9, 1.1, 0.8, fw);
		c = mix(c, c * 0.82, smoothstep(0.5, 0.8, fbm(p * 1.4)) * 0.4);
		c = mix(c, sepia, crag * 0.85);
		c = mix(c, sepia, scatter(p + 3.1, uv, to_uv, 0.09, 0.009, 0.9, 0.5, fw) * 0.6);
		bump = crag * 0.02;
		cover = crag;
		rim = 0.7;
	} else if (k == SAND) {
		float near = 1.0 - smoothstep(0.0, 0.5, border);
		c = mix(c, sepia, scatter(p, uv, to_uv, 0.07, 0.008, 0.95, 0.45 + 0.4 * near, fw) * 0.6);
		rim = 0.35;
	} else if (k == SNOW) {
		c = mix(c, prussian, marks(p, uv, to_uv, 0.3, 0.12, 0.006, 0.15, 0.2, 0.5, fw) * 0.25);
		rim = 0.6;
		rim_ink = prussian * 1.4;
	}
	return mix(c, rim_ink, ink(edge, 0.012, fw) * rim);
}
