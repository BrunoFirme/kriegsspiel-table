float shaded(vec3 n, out float steep) {
	steep = clamp(length(n.xz) / max(n.y, 0.05), 0.0, 3.0);
	vec3 light = normalize(LIGHT_FROM);
	return clamp((1.0 - dot(n, light) / light.y) * 1.35, 0.0, 1.0);
}

float swiss_hachures(vec2 p, float h, vec2 down, float steep, float dark, float fw) {
	const float SX = 0.22;
	const float DH = 0.25;
	const float LONGEST = 1.2;
	vec2 across = vec2(-down.y, down.x);
	float band_h = max(DH, steep * LONGEST / detail);
	float level = h / band_h + (noise(p * 1.7) - 0.5) * 0.3;
	float row = floor(level);
	float within = fract(level);
	float fl = fwidth(level);
	float seam = 0.05 + fl;
	float band = smoothstep(0.0, seam, within) * (1.0 - smoothstep(1.0 - seam, 1.0, within));
	band = mix(band, 0.9, smoothstep(0.3, 0.6, fl));
	float width = SX * mix(0.05, 0.24, dark);
	float keep = mix(0.55, 1.0, dark);
	vec2 a = floor(p / ANCHOR);
	vec2 f = fract(p / ANCHOR);
	f = f * f * (3.0 - 2.0 * f);
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 cell = a + vec2(float(i), float(j));
			float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
			if (w < 0.01) continue;
			float s = dot(p - cell * ANCHOR, across) / SX + hash(vec2(row, cell.x * 13.0 + cell.y)) * 7.0;
			float col = floor(s);
			vec2 id = vec2(col, row) + cell * 5.3;
			if (hash(id) > w * keep) continue;
			float wave = sin(within * 7.0 + hash(id + 2.2) * 6.3) * 0.1;
			float heft = width * (0.85 + 0.3 * hash(id + 4.4)) * (1.15 - 0.3 * within);
			m = max(m, ink(abs(fract(s) - 0.5 + wave) * SX, heft, fw));
		}
	}
	float mean = keep * 2.0 * width / SX * 0.9;
	return mix(m * band, mean, far_blend(fw, SX));
}

vec3 hachures(vec3 c, vec3 n, vec2 p, float h, float fw, vec3 tone) {
	float steep;
	float dark = shaded(n, steep);
	float amount = smoothstep(0.04, 0.35, steep);
	if (amount <= 0.001) return c;
	float stroke = swiss_hachures(p, h, normalize(n.xz), steep, dark * amount, fw);
	c *= 1.0 - dark * amount * 0.1;
	return mix(c, tone, stroke * amount * (0.5 + 0.5 * dark));
}

vec3 engraved_relief(vec3 c, vec3 n, vec2 p, float fw, vec3 tone) {
	float steep;
	float dark = shaded(n, steep);
	float amount = smoothstep(0.04, 0.35, steep);
	if (amount <= 0.001) return c;
	float wobble = noise(p * 1.2) * 0.05;
	float hand = 0.7 + 0.6 * noise(p * 4.0 + 3.0);
	float broken = smoothstep(0.3, 0.45, noise(vec2(p.x + p.y, p.x - p.y) * vec2(1.6, 9.0)));
	float shade = smoothstep(0.08, 0.6, dark) * amount;
	float one = hatch(p.x - p.y + wobble, 0.2, (0.006 + 0.028 * dark) * hand, fw) * shade * broken;
	float two = hatch(p.x - p.y + wobble + 0.1, 0.2, (0.004 + 0.02 * dark) * hand, fw) * smoothstep(0.55, 0.9, dark) * amount * broken;
	c = mix(c, c * vec3(0.84, 0.78, 0.68), dark * amount * 0.3);
	return mix(c, tone, max(one, two) * 0.85);
}

vec3 hillshade(vec3 c, vec3 n) {
	float steep;
	float dark = shaded(n, steep);
	vec3 light = normalize(LIGHT_FROM);
	float lit = clamp((dot(n, light) / light.y - 1.0) * 1.2, 0.0, 1.0);
	c = mix(c, c * vec3(0.5, 0.52, 0.6), dark * 0.75);
	return mix(c, c * vec3(1.06, 1.04, 0.98), lit * 0.3);
}

vec3 slopes(vec3 c, vec3 n, vec2 p, float h, float fw, int style) {
	if (style == MODERN) return hillshade(c, n);
	if (style == ANCIENT) return engraved_relief(c, n, p, fw, vec3(0.25, 0.15, 0.08));
	return hachures(c, n, p, h, fw, vec3(0.3, 0.2, 0.12));
}
