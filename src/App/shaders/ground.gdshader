shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D kinds : filter_nearest, repeat_disable;
uniform vec4 palette[11];
uniform vec4 plain : source_color = vec4(0.60, 0.62, 0.46, 1.0);
uniform bool painted_only = false;
uniform vec2 cells = vec2(64.0, 64.0);
uniform float detail = 1.0;

varying vec3 world;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += a * noise(p);
		p *= 2.03;
		a *= 0.5;
	}
	return v;
}

int kind_at(vec2 uv) {
	vec2 g = uv * cells - 0.5;
	vec2 base = floor(g);
	vec2 f = g - base;
	float rough = fbm(uv * cells * 1.7) - 0.5;
	int best = 0;
	float most = -10.0;
	for (int j = 0; j < 2; j++) {
		for (int i = 0; i < 2; i++) {
			vec2 corner = base + vec2(float(i), float(j));
			float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
			float score = w + rough * (hash(corner) - 0.5) * 1.6;
			if (score > most) {
				most = score;
				best = int(round(texture(kinds, clamp((corner + 0.5) / cells, vec2(0.0), vec2(1.0))).r * 255.0));
			}
		}
	}
	return best;
}

vec3 look(int k, vec3 base, vec2 p) {
	float n = fbm(p * 0.35);
	float fine = noise(p * 2.7);
	if (k == 1) return base * (0.86 + 0.24 * n + 0.06 * fine);
	if (k == 2) return base * (0.9 + 0.1 * step(0.5, fract(p.x * 0.6 + n * 0.4)) + 0.06 * fine);
	if (k == 3) {
		float crowns = smoothstep(0.42, 0.62, fbm(p * 1.1));
		return mix(base * 0.72, base * 1.18, crowns) * (0.92 + 0.12 * fine);
	}
	if (k == 4) return mix(base, palette[5].rgb, smoothstep(0.55, 0.7, fbm(p * 0.8)) * 0.6) * (0.9 + 0.1 * fine);
	if (k == 5) return base * (0.92 + 0.12 * noise(p * 0.5 + vec2(TIME * 0.05, 0.0)));
	if (k == 6) return base * (0.94 + 0.08 * fine);
	return base * (0.88 + 0.2 * n + 0.05 * fine);
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	int k = kind_at(UV);
	vec2 p = world.xz * detail;
	if (k <= 0 || k > 10) {
		if (painted_only) discard;
		ALBEDO = plain.rgb * (0.94 + 0.08 * fbm(p * 0.25));
	} else {
		ALBEDO = look(k, palette[k].rgb, p);
	}
	ROUGHNESS = k == 5 ? 0.35 : 1.0;
}
