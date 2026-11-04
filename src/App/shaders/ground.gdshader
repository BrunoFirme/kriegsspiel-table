shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D kinds : filter_nearest, repeat_disable;
uniform sampler2D edges : filter_linear, repeat_disable;
uniform vec4 palette[33];
uniform bool painted_only = false;
uniform vec2 cells = vec2(64.0, 64.0);
uniform float cell_world = 0.2;
uniform float detail = 1.0;
global uniform int ground_style;
global uniform float contour_interval;

const int KINDS = 11;
const int GRASS = 1;
const int FIELD = 2;
const int WOOD = 3;
const int MARSH = 4;
const int WATER = 5;
const int ROAD = 6;
const int EARTH = 7;
const int ROCK = 8;
const int SAND = 9;
const int SNOW = 10;
const int SURVEY = 0;
const int ANCIENT = 1;
const int MODERN = 2;
const float SYMBOL = 0.9;
const float WARP = 0.55;
const float EDGE_STEPS = 16.0;
const float PARCEL = 5.0;
const vec3 SURVEY_PAPER = vec3(0.87, 0.82, 0.70);
const vec3 LIGHT_FROM = vec3(-0.55, 0.62, -0.55);
const float TONE = 0.78;

varying vec3 world;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

vec2 hash2(vec2 p) {
	return vec2(hash(p), hash(p + vec2(17.3, 41.9)));
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

float ink(float d, float width, float fw) {
	float w = max(width, fw * 0.5);
	return (1.0 - smoothstep(w - fw * 0.5, w + fw * 0.5, d)) * clamp(width / max(fw * 0.5, 1e-5), 0.0, 1.0);
}

int kind_of(vec2 cell) {
	return int(round(texture(kinds, clamp((cell + 0.5) / cells, vec2(0.0), vec2(1.0))).r * 255.0));
}

int kind_at(vec2 uv, out float border) {
	vec2 g = uv * cells - 0.5;
	g += (vec2(fbm(uv * cells * 0.45), fbm(uv * cells * 0.45 + vec2(5.2, 1.3))) - 0.5) * WARP * 2.0;
	vec2 base = floor(g + 0.5);
	float score[KINDS];
	for (int k = 0; k < KINDS; k++) score[k] = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = base + vec2(float(i), float(j));
			vec2 d = g - c;
			score[clamp(kind_of(c), 0, KINDS - 1)] += exp(-dot(d, d) * 1.6);
		}
	}
	int best = 0;
	float most = -1.0;
	for (int k = 0; k < KINDS; k++) {
		if (score[k] > most) { most = score[k]; best = k; }
	}
	float steps = texture(edges, clamp((g + 0.5) / cells, vec2(0.0), vec2(1.0))).r * 255.0 / EDGE_STEPS;
	border = max(0.0, steps - 0.5) * cell_world;
	return best;
}

vec3 voronoi(vec2 x) {
	vec2 n = floor(x);
	vec2 f = fract(x);
	float f1 = 8.0;
	float f2 = 8.0;
	vec2 id = vec2(0.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 o = hash2(n + g) * 0.85 + 0.075;
			float d = length(g + o - f);
			if (d < f1) { f2 = f1; f1 = d; id = n + g; }
			else if (d < f2) f2 = d;
		}
	}
	return vec3(f2 - f1, hash(id), hash(id + 9.1));
}

float strips(vec2 p, float spacing, float angle, float width, float fw) {
	vec2 dir = vec2(cos(angle), sin(angle));
	float t = dot(p, dir) / spacing;
	return ink(abs(fract(t) - 0.5) * spacing, width, fw);
}

float dashes(vec2 p, float spacing, float len, float width, float fw, float tilt, float keep) {
	vec2 cell = floor(p / spacing);
	vec2 centre = (cell + 0.25 + hash2(cell) * 0.5) * spacing;
	vec2 d = p - centre;
	float a = tilt + (hash(cell + 3.3) - 0.5) * 0.5;
	d = vec2(cos(a) * d.x + sin(a) * d.y, -sin(a) * d.x + cos(a) * d.y);
	return ink(length(vec2(max(abs(d.x) - len, 0.0), d.y)), width, fw) * step(1.0 - keep, hash(cell + 7.7));
}

float tufts(vec2 p, float spacing, float len, float width, float fw, float keep) {
	vec2 cell = floor(p / spacing);
	vec2 centre = (cell + 0.25 + hash2(cell) * 0.5) * spacing;
	vec2 d = p - centre;
	float m = 0.0;
	for (int i = -1; i <= 1; i++) {
		float a = float(i) * 0.5;
		vec2 dir = vec2(sin(a), -cos(a));
		float along = clamp(dot(d, dir), 0.0, len);
		m = max(m, ink(length(d - dir * along), width, fw));
	}
	return m * step(1.0 - keep, hash(cell + 5.1));
}

float dots(vec2 p, float spacing, float r, float fw, float keep) {
	vec2 cell = floor(p / spacing);
	vec2 centre = (cell + 0.2 + hash2(cell) * 0.6) * spacing;
	return ink(length(p - centre), r, fw) * step(1.0 - keep, hash(cell + 2.9));
}

float circles(vec2 p, float spacing, float r, float width, float fw, float keep) {
	vec2 cell = floor(p / spacing);
	vec2 centre = (cell + 0.25 + hash2(cell) * 0.5) * spacing;
	return ink(abs(length(p - centre) - r), width, fw) * step(1.0 - keep, hash(cell + 4.4));
}

float trees(vec2 p, float spacing, float size, float fw, out float shade, out float ring, out float dome) {
	vec2 cell = floor(p / spacing);
	float crown = 0.0;
	shade = 0.0;
	ring = 0.0;
	dome = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = cell + vec2(float(i), float(j));
			vec2 centre = (c + 0.5 + (hash2(c) - 0.5) * 0.55 + vec2(mod(c.y, 2.0) * 0.5, 0.0)) * spacing;
			float r = spacing * size * (0.8 + 0.4 * hash(c + 3.1));
			float d = length(p - centre);
			float body = ink(d, r, fw);
			crown = max(crown, body);
			ring = max(ring, ink(abs(d - r), spacing * 0.035, fw));
			shade = max(shade, ink(length(p - centre - vec2(0.32, 0.38) * r), r, fw) * (1.0 - body));
			dome = max(dome, body * sqrt(max(0.0, 1.0 - (d * d) / (r * r))) * r);
		}
	}
	return crown;
}

vec3 paper(int style, vec2 p) {
	float fibre = noise(p * vec2(14.0, 3.0)) - 0.5;
	float tooth = noise(p * 11.0) - 0.5;
	if (style == ANCIENT) {
		float stain = fbm(p * 0.035) - 0.5;
		float blot = smoothstep(0.62, 0.8, fbm(p * 0.11 + 3.7));
		return vec3(0.83, 0.72, 0.50) * (1.0 + stain * 0.28 + fibre * 0.05 + tooth * 0.04) * (1.0 - blot * 0.12);
	}
	if (style == MODERN) return vec3(0.93, 0.925, 0.89) * (1.0 + tooth * 0.012);
	float fox = smoothstep(0.66, 0.82, fbm(p * 0.07 + 11.0));
	return SURVEY_PAPER * (1.0 + (fbm(p * 0.05) - 0.5) * 0.08 + fibre * 0.035 + tooth * 0.03) * (1.0 - fox * 0.06);
}

vec3 wash(vec3 tint, vec3 sheet, vec2 p, float border) {
	float bloom = fbm(p * 0.22) - 0.5;
	float pool = 1.0 - smoothstep(0.0, 0.35, border);
	return tint * (sheet / SURVEY_PAPER) * (1.0 + bloom * 0.14) * (1.0 - pool * 0.14);
}

vec3 slopes(vec3 c, vec3 n, vec2 p, int style) {
	float steep = clamp(length(n.xz) / max(n.y, 0.05), 0.0, 3.0);
	if (style == MODERN) {
		vec3 light = normalize(LIGHT_FROM);
		float shadow = clamp((light.y - dot(n, light)) * 1.6, 0.0, 1.0);
		float sun = clamp((dot(n, light) - light.y) * 1.6, 0.0, 1.0);
		c = mix(c, c * vec3(0.62, 0.64, 0.76), shadow * 0.75);
		return mix(c, c * vec3(1.06, 1.03, 0.9), sun * 0.5);
	}
	vec2 down = length(n.xz) > 1e-4 ? normalize(n.xz) : vec2(0.0, 1.0);
	vec2 across = vec2(-down.y, down.x);
	float streak = noise(vec2(dot(p, across) * 2.4, dot(p, down) * 0.35));
	float amount = smoothstep(0.08, 1.2, steep) * (0.55 + 0.45 * streak);
	vec3 shade = style == ANCIENT ? vec3(0.52, 0.36, 0.22) : vec3(0.56, 0.38, 0.33);
	return mix(c, c * shade, amount * 0.55);
}

vec3 survey(int k, vec3 tint, vec3 sheet, vec2 p, float fw, float border, float fade, out float bump) {
	vec3 sepia = vec3(0.32, 0.22, 0.14);
	vec3 c = wash(tint, sheet, p, border);
	bump = 0.0;
	if (k == GRASS) {
		c = mix(c, vec3(0.35, 0.47, 0.34), tufts(p, SYMBOL * 1.3, SYMBOL * 0.13, 0.018, fw, 0.35) * 0.55 * fade);
	} else if (k == FIELD) {
		vec3 v = voronoi(p / PARCEL);
		c *= 0.95 + 0.1 * v.z;
		c = mix(c, vec3(0.6, 0.36, 0.28), strips(p, SYMBOL * 0.32, v.y * 3.1416, 0.022, fw) * 0.35 * fade);
		c = mix(c, sepia, ink(v.x * PARCEL, 0.035, fw) * 0.6);
	} else if (k == WOOD) {
		float shade, ring, dome;
		float crown = trees(p, SYMBOL * 0.55, 0.34, fw, shade, ring, dome);
		c = mix(c, vec3(0.24, 0.32, 0.2), shade * 0.45 * fade);
		c = mix(c, tint * 0.62, crown * fade);
		c = mix(c, vec3(0.2, 0.28, 0.17), ring * 0.7 * fade);
		bump = dome * fade;
	} else if (k == MARSH) {
		c = mix(c, vec3(0.3, 0.42, 0.5), tufts(p, SYMBOL * 0.7, SYMBOL * 0.14, 0.02, fw, 0.75) * 0.7 * fade);
		c = mix(c, palette[WATER].rgb, smoothstep(0.6, 0.72, fbm(p * 0.45)) * 0.55);
	} else if (k == WATER) {
		float lines = 0.0;
		for (int i = 1; i <= 5; i++) {
			float at = 0.12 * float(i) * (1.0 + 0.35 * float(i));
			lines = max(lines, ink(abs(border - at), 0.02, fw) * (1.0 - float(i) * 0.14));
		}
		c = mix(c, vec3(0.17, 0.32, 0.45), lines * 0.85);
		c = mix(c, vec3(0.16, 0.3, 0.42), ink(border, 0.05, fw));
	} else if (k == ROAD) {
		c = mix(c, sepia, ink(border, 0.035, fw) * 0.85);
	} else if (k == EARTH) {
		c = mix(c, vec3(0.5, 0.33, 0.22), strips(p, SYMBOL * 0.28, 0.4, 0.02, fw) * 0.3 * fade);
	} else if (k == ROCK) {
		float marks = dashes(p, SYMBOL * 0.38, SYMBOL * 0.12, 0.022, fw, 2.2, 0.8);
		c = mix(c, sepia, marks * 0.8 * fade);
		bump = marks * 0.03 * fade;
	} else if (k == SAND) {
		c = mix(c, vec3(0.55, 0.42, 0.26), dots(p, SYMBOL * 0.26, 0.035, fw, 0.7) * 0.6 * fade);
	}
	float rim = k == WOOD ? 0.9 : k == MARSH ? 0.4 : k == FIELD ? 0.55 : k == EARTH ? 0.45 : k == ROCK ? 0.6 : k == SAND ? 0.35 : k == SNOW ? 0.5 : 0.0;
	vec3 edge_ink = k == WOOD ? vec3(0.2, 0.28, 0.17) : k == SNOW || k == MARSH ? vec3(0.3, 0.42, 0.5) : sepia;
	return mix(c, edge_ink, ink(border, 0.03, fw) * rim);
}

vec3 ancient(int k, vec3 tint, vec3 sheet, vec2 p, float fw, float border, float fade, out float bump) {
	vec3 sepia = vec3(0.24, 0.14, 0.07);
	vec3 verdigris = vec3(0.2, 0.38, 0.34);
	vec3 c = wash(tint, sheet, p, border);
	bump = 0.0;
	if (k == GRASS) {
		c = mix(c, sepia, tufts(p, SYMBOL * 1.6, SYMBOL * 0.12, 0.02, fw, 0.25) * 0.45 * fade);
	} else if (k == FIELD) {
		vec3 v = voronoi(p / (PARCEL * 1.3));
		c *= 0.93 + 0.12 * v.z;
		c = mix(c, sepia, dots(p, SYMBOL * 0.45, 0.04, fw, 0.6) * 0.5 * fade);
	} else if (k == WOOD) {
		float shade, ring, dome;
		float crown = trees(p, SYMBOL * 0.75, 0.3, fw, shade, ring, dome);
		c = mix(c, tint * 0.72, crown * fade);
		c = mix(c, sepia, ring * 0.85 * fade);
		c = mix(c, sepia, shade * 0.18 * fade);
		bump = dome * fade;
	} else if (k == MARSH) {
		c = mix(c, verdigris, tufts(p, SYMBOL * 0.65, SYMBOL * 0.15, 0.024, fw, 0.75) * 0.75 * fade);
	} else if (k == WATER) {
		float wave = 0.0;
		for (int i = 0; i < 2; i++) {
			vec2 q = p * (1.0 + float(i) * 0.37) + vec2(float(i) * 3.1, 0.0);
			float row = floor(q.y / (SYMBOL * 0.6));
			float y = (fract(q.y / (SYMBOL * 0.6)) - 0.5) * SYMBOL * 0.6 - sin(q.x * 3.2 + row * 1.7) * 0.06;
			wave = max(wave, ink(abs(y), 0.02, fw) * step(0.4, noise(q * 0.8 + row)));
		}
		c = mix(c, verdigris * 0.75, wave * 0.55 * fade);
		float scallop = abs(sin((p.x + p.y) * 4.0)) * 0.05;
		c = mix(c, sepia, ink(max(0.0, border - scallop), 0.045, fw));
	} else if (k == ROAD) {
		c = mix(c, tint * 0.75, ink(border, 0.025, fw));
	} else if (k == ROCK) {
		float marks = dashes(p, SYMBOL * 0.42, SYMBOL * 0.14, 0.03, fw, 0.9, 0.85);
		c = mix(c, sepia, marks * 0.8 * fade);
		bump = marks * 0.03 * fade;
	} else if (k == SAND) {
		c = mix(c, sepia, dots(p, SYMBOL * 0.28, 0.04, fw, 0.65) * 0.55 * fade);
	}
	float rim = k == GRASS ? 0.25 : k == WATER || k == ROAD ? 0.0 : 0.85;
	return mix(c, sepia, ink(border, 0.04, fw) * rim);
}

vec3 modern(int k, vec3 tint, vec2 p, float fw, float border, float fade) {
	vec3 black = vec3(0.1, 0.1, 0.11);
	vec3 blue = vec3(0.1, 0.42, 0.7);
	vec3 c = tint;
	if (k == WOOD) {
		float edge_dots = dots(p, SYMBOL * 0.22, 0.035, fw, 1.0) * (1.0 - smoothstep(0.04, 0.09, border));
		c = mix(c, black, edge_dots * 0.85);
		c = mix(c, tint * 0.78, ink(border, 0.018, fw));
	} else if (k == GRASS) {
		c = mix(c, vec3(0.2, 0.45, 0.2), circles(p, SYMBOL * 2.4, SYMBOL * 0.09, 0.014, fw, 0.2) * 0.8 * fade);
	} else if (k == MARSH) {
		c = mix(c, blue, dashes(p, SYMBOL * 0.5, SYMBOL * 0.16, 0.016, fw, 0.0, 0.9) * 0.9 * fade);
	} else if (k == WATER) {
		c = mix(c, blue, ink(abs(border - 0.14), 0.012, fw) * 0.5);
		c = mix(c, blue, ink(abs(border - 0.3), 0.01, fw) * 0.3);
		c = mix(c, blue, ink(border, 0.035, fw));
	} else if (k == ROAD) {
		c = mix(c, black, ink(border, 0.03, fw));
	} else if (k == ROCK) {
		c = mix(c, black, dashes(p, SYMBOL * 0.35, SYMBOL * 0.1, 0.014, fw, 2.3, 0.75) * 0.7 * fade);
	} else if (k == SAND) {
		c = mix(c, vec3(0.45, 0.4, 0.3), dots(p, SYMBOL * 0.24, 0.025, fw, 0.7) * 0.6 * fade);
	} else if (k == SNOW) {
		c = mix(c, blue, ink(border, 0.015, fw) * 0.6);
	}
	return c;
}

vec3 contour_ink(int style) {
	if (style == MODERN) return vec3(0.69, 0.4, 0.17);
	if (style == ANCIENT) return vec3(0.42, 0.27, 0.15);
	return vec3(0.5, 0.33, 0.22);
}

float contour(float h, vec2 p, int style) {
	if (contour_interval <= 0.0 || h < contour_interval * 0.5) return 0.0;
	float fh = max(fwidth(h), 1e-6);
	if (style != MODERN) h += (noise(p * 0.9) - 0.5) * contour_interval * (style == ANCIENT ? 0.16 : 0.08);
	float level = h / contour_interval;
	float d = abs(fract(level + 0.5) - 0.5) * contour_interval / fh;
	bool index = abs(mod(round(level), 5.0)) < 0.5;
	float px = index ? 1.1 : 0.55;
	float strength = index ? 0.9 : 0.7;
	if (style == SURVEY && !index) strength *= smoothstep(0.32, 0.42, noise(p * 2.6));
	if (style == ANCIENT) px *= 0.55 + 0.9 * noise(p * 1.3 + 7.0);
	float crowded = clamp(fh / contour_interval * 4.0, 0.0, 1.0);
	return (1.0 - smoothstep(px - 0.5, px + 0.5, d)) * strength * (1.0 - crowded * 0.85);
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float border;
	int k = kind_at(UV, border);
	vec2 p = world.xz * detail;
	float fw = max(fwidth(p.x), fwidth(p.y));
	float fade = 1.0 - smoothstep(0.06, 0.2, fw / SYMBOL);
	int style = clamp(ground_style, SURVEY, MODERN);
	float lines = contour(world.y, p, style) * (k == WATER ? 0.0 : 1.0);
	vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	float bump = 0.0;
	vec3 sheet = paper(style, p);
	vec3 c;
	if (k <= 0 || k > 10) {
		if (painted_only && lines < 0.02) discard;
		c = painted_only ? contour_ink(style) : slopes(sheet, n, p, style);
	} else {
		vec3 tint = palette[style * KINDS + k].rgb;
		if (style == ANCIENT) c = ancient(k, tint, sheet, p, fw, border, fade, bump);
		else if (style == MODERN) c = modern(k, tint, p, fw, border, fade);
		else c = survey(k, tint, sheet, p, fw, border, fade, bump);
		if (k != WATER && k != ROAD) c = slopes(c, n, p, style);
	}
	ALBEDO = mix(c, contour_ink(style), lines) * (style == MODERN ? 0.86 : TONE);
	if (bump > 0.0) {
		vec3 dx = dFdx(VERTEX);
		vec3 dy = dFdy(VERTEX);
		vec3 r1 = cross(dy, NORMAL);
		vec3 r2 = cross(NORMAL, dx);
		float det = dot(dx, r1);
		vec3 grad = sign(det) * (dFdx(bump) * r1 + dFdy(bump) * r2);
		NORMAL = normalize(abs(det) * NORMAL - grad * 1.6);
	}
	ROUGHNESS = k == WATER ? 0.5 : 1.0;
}
