float shaded(vec3 n, out float steep) {
	steep = clamp(length(n.xz) / max(n.y, 0.05), 0.0, 3.0);
	vec3 light = normalize(LIGHT_FROM);
	return clamp((1.0 - dot(n, light) / light.y) * 1.35, 0.0, 1.0);
}

float hachure_field(vec2 p, float h, vec2 down, float band_h, float spacing, float width, float keep, float fw) {
	vec2 across = vec2(-down.y, down.x);
	float level = h / band_h + (noise(p * 1.7) - 0.5) * 0.3;
	float row = floor(level);
	float within = fract(level);
	float fl = fwidth(level);
	float seam = 0.05 + fl;
	float band = smoothstep(0.0, seam, within) * (1.0 - smoothstep(1.0 - seam, 1.0, within));
	band = mix(band, 0.9, smoothstep(0.3, 0.6, fl));
	vec2 a = floor(p / ANCHOR);
	vec2 f = fract(p / ANCHOR);
	f = f * f * (3.0 - 2.0 * f);
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 cell = a + vec2(float(i), float(j));
			float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
			if (w < 0.01) continue;
			float s = dot(p - cell * ANCHOR, across) / spacing + hash(vec2(row, cell.x * 13.0 + cell.y)) * 7.0;
			float col = floor(s);
			vec2 id = vec2(col, row) + cell * 5.3;
			if (hash(id) > w * keep) continue;
			float wave = sin(within * 7.0 + hash(id + 2.2) * 6.3) * 0.1;
			float heft = width * (0.85 + 0.3 * hash(id + 4.4)) * (1.15 - 0.3 * within);
			m = max(m, ink(abs(fract(s) - 0.5 + wave) * spacing, heft, fw));
		}
	}
	float mean = keep * 2.0 * width / spacing * 0.9;
	return mix(m * band, mean, far_blend(fw, spacing));
}

float swiss_hachures(vec2 p, float h, vec2 down, float steep, float dark, float fw) {
	const float SX = 0.22;
	const float DH = 0.25;
	const float LONGEST = 1.2;
	float band_h = max(DH, steep * LONGEST / detail);
	return hachure_field(p, h, down, band_h, SX, SX * mix(0.05, 0.24, dark), mix(0.55, 1.0, dark), fw);
}

float rock_fill(vec2 p, float h, vec2 down, float steep, float dark, float sheer, float spacing_lit, float spacing_shade, float width_lit, float width_shade, float fw) {
	float cosine = inversesqrt(1.0 + steep * steep);
	float band_h = max(ROCK_BAND_LEAST, steep * ROCK_STROKE * cosine / detail);
	float spacing = mix(spacing_lit, spacing_shade, dark);
	float width = mix(width_lit, width_shade, dark);
	float keep = mix(0.6, 1.0, dark) * (1.0 - sheer * SHEER_THINS);
	return hachure_field(p, h, down, band_h, spacing, width, keep, fw);
}

vec3 survey_rock(vec3 c, vec2 p, float own, float h, vec2 down, float steep, float dark, float crest, float gully, float sheer, float foot, float fw) {
	vec3 sepia = SURVEY_INK;
	c = mix(c, c * vec3(0.9, 0.88, 0.84), 0.35 * (1.0 - foot));
	float fill = rock_fill(p, h, down, steep, dark, sheer, 0.28, 0.16, 0.012, 0.03, fw);
	fill *= 1.0 - crest * (1.0 - dark);
	float shape = gully * dark > 0.02 ? rock_fill(p + 7.3, h, down, steep, dark, 0.0, 0.24, 0.24, 0.028, 0.034, fw) * gully * dark : 0.0;
	c = mix(c, sepia, fill * mix(0.55, 1.0, dark) * (1.0 - foot));
	c = mix(c, sepia, shape * 0.9 * (1.0 - foot));
	c = mix(c, sepia, crest * mix(0.45, 0.9, dark) * (1.0 - foot));
	if (foot > 0.01) c = mix(c, sepia, scatter(p + 3.1, own, 0.09, 0.009, 0.9, 0.5, fw) * 0.6 * foot);
	return c;
}

vec3 ancient_rock(vec3 c, vec2 p, float h, vec2 down, float steep, float dark, float crest, float sheer, float fw) {
	vec3 umber = ANCIENT_RIDGE;
	float strokes = rock_fill(p, h, down, steep, dark, sheer, 0.2, 0.2, 0.012, 0.016, fw);
	c = mix(c, umber, strokes * 0.75);
	c = mix(c, umber, crest * mix(0.2, 0.9, dark));
	return c;
}

vec3 modern_rock(vec3 c, vec2 p, float h, vec2 down, float steep, float dark, float crest, float sheer, float foot, float own, float fw) {
	vec3 black = MODERN_BLACK;
	float ticks = rock_fill(p, h, down, steep, dark, sheer, 0.12, 0.12, 0.011, 0.013, fw);
	c = mix(c, black, ticks * 0.85 * (1.0 - foot));
	c = mix(c, black, crest * 0.9 * (1.0 - foot));
	c = mix(c, black, screen(p, own, 0.1, 0.012, fw) * 0.7 * foot);
	return c;
}

vec3 rock_face(int style, vec3 c, vec2 p, vec2 uv, mat2 to_uv, float h, vec2 down, float steep, float dark, float crest, float gully, float sheer, float foot, float own, float fw) {
	if (style == MODERN) return modern_rock(c, p, h, down, steep, dark, crest, sheer, foot, own, fw);
	if (style == ANCIENT) return ancient_rock(c, p, h, down, steep, dark, crest, sheer, fw);
	return survey_rock(c, p, own, h, down, steep, dark, crest, gully, sheer, foot, fw);
}

vec3 hachures(vec3 c, vec3 n, vec2 p, float h, float fw, vec3 tone, float mask) {
	float steep;
	float dark = shaded(n, steep);
	float amount = smoothstep(0.04, 0.35, steep);
	if (amount <= 0.001 || mask <= 0.001) return c;
	float stroke = swiss_hachures(p, h, normalize(n.xz), steep, dark * amount, fw) * mask;
	c *= 1.0 - dark * amount * 0.1;
	return mix(c, tone, stroke * amount * (0.5 + 0.5 * dark));
}

vec3 engraved_relief(vec3 c, vec3 n, vec2 p, float fw, vec3 tone, float mask) {
	float steep;
	float dark = shaded(n, steep);
	float amount = smoothstep(0.04, 0.35, steep);
	if (amount <= 0.001 || mask <= 0.001) return c;
	float wobble = noise(p * 1.2) * 0.05;
	float hand = 0.7 + 0.6 * noise(p * 4.0 + 3.0);
	float broken = smoothstep(0.3, 0.45, noise(vec2(p.x + p.y, p.x - p.y) * vec2(1.6, 9.0)));
	float shade = smoothstep(0.08, 0.6, dark) * amount;
	float one = hatch(p.x - p.y + wobble, 0.2, (0.006 + 0.028 * dark) * hand, fw) * shade * broken;
	float two = hatch(p.x - p.y + wobble + 0.1, 0.2, (0.004 + 0.02 * dark) * hand, fw) * smoothstep(0.55, 0.9, dark) * amount * broken;
	c = mix(c, c * vec3(0.84, 0.78, 0.68), dark * amount * 0.3);
	return mix(c, tone, max(one, two) * 0.85 * mask);
}

vec3 hillshade(vec3 c, vec3 n) {
	float steep;
	float dark = shaded(n, steep);
	return mix(c, c * (MODERN_SHADE / MODERN_PAPER), dark);
}

vec3 slopes(vec3 c, vec3 n, vec2 p, float h, float fw, int style, float mask) {
	if (style == MODERN) return hillshade(c, n);
	if (style == ANCIENT) return c;
	return hachures(c, n, p, h, fw, SURVEY_HACHURE, mask);
}
