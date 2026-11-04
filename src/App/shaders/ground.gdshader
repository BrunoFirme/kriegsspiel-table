shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D kinds : filter_nearest, repeat_disable;
uniform vec4 palette[33];
global uniform int ground_style;
uniform vec4 plain : source_color = vec4(0.60, 0.62, 0.46, 1.0);
uniform bool painted_only = false;
uniform vec2 cells = vec2(64.0, 64.0);
uniform float detail = 1.0;

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
const vec3 UMBER = vec3(0.25, 0.19, 0.12);
const vec3 PRUSSIAN = vec3(0.18, 0.30, 0.42);
const vec3 SEPIA = vec3(0.17, 0.11, 0.06);
const vec3 VERDIGRIS = vec3(0.12, 0.30, 0.32);
const vec3 PRINT = vec3(0.10, 0.10, 0.11);
const vec3 TOPO_BLUE = vec3(0.08, 0.32, 0.62);
const int SURVEY = 0;
const int ANCIENT = 1;
const int MODERN = 2;
const float SYMBOL = 0.9;
const float WARP = 0.55;

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

int kind_of(vec2 cell) {
	return int(round(texture(kinds, clamp((cell + 0.5) / cells, vec2(0.0), vec2(1.0))).r * 255.0));
}

vec3 kind_at(vec2 uv, out float edge) {
	vec2 g = uv * cells - 0.5;
	g += (vec2(fbm(uv * cells * 0.45), fbm(uv * cells * 0.45 + vec2(5.2, 1.3))) - 0.5) * WARP * 2.0;
	vec2 base = floor(g + 0.5);
	float score[KINDS];
	for (int k = 0; k < KINDS; k++) score[k] = 0.0;
	float total = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = base + vec2(float(i), float(j));
			vec2 d = g - c;
			float w = exp(-dot(d, d) * 1.6);
			int k = clamp(kind_of(c), 0, KINDS - 1);
			score[k] += w;
			total += w;
		}
	}
	int best = 0;
	float most = -1.0;
	float next = 0.0;
	for (int k = 0; k < KINDS; k++) {
		if (score[k] > most) { next = most; most = score[k]; best = k; }
		else if (score[k] > next) next = score[k];
	}
	edge = 1.0 - smoothstep(0.0, 0.55, (most - max(next, 0.0)) / max(total, 1e-4));
	return vec3(float(best), most, total);
}

float line_aa(float d, float width, float fw) {
	return 1.0 - smoothstep(width - fw, width + fw, d);
}

float trees(vec2 p, float fw, out float shade, out float ring) {
	vec2 cell = floor(p / SYMBOL);
	float crown = 0.0;
	shade = 0.0;
	ring = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = cell + vec2(float(i), float(j));
			vec2 centre = (c + 0.2 + hash2(c) * 0.6) * SYMBOL;
			float r = SYMBOL * (0.22 + 0.10 * hash(c + 3.1));
			float d = length(p - centre);
			crown = max(crown, line_aa(d, r, fw));
			ring = max(ring, line_aa(abs(d - r), 0.035, fw));
			shade = max(shade, line_aa(length(p - centre - vec2(0.10, 0.12) * SYMBOL), r, fw) * (1.0 - line_aa(d, r, fw)));
		}
	}
	return crown;
}

float strips(vec2 p, float spacing, float angle, float width, float fw) {
	vec2 dir = vec2(cos(angle), sin(angle));
	float t = dot(p, dir) / spacing;
	return line_aa(abs(fract(t) - 0.5) * spacing, width, fw);
}

float dashes(vec2 p, float spacing, float len, float width, float fw, float tilt) {
	vec2 cell = floor(p / spacing);
	vec2 centre = (cell + 0.25 + hash2(cell) * 0.5) * spacing;
	vec2 d = p - centre;
	float c = cos(tilt), s = sin(tilt);
	d = vec2(c * d.x + s * d.y, -s * d.x + c * d.y);
	float along = max(abs(d.x) - len, 0.0);
	return line_aa(length(vec2(along, d.y)), width, fw) * step(0.25, hash(cell + 7.7));
}

float dots(vec2 p, float spacing, float r, float fw) {
	vec2 cell = floor(p / spacing);
	vec2 centre = (cell + 0.2 + hash2(cell) * 0.6) * spacing;
	return line_aa(length(p - centre), r, fw) * step(0.35, hash(cell + 2.9));
}

vec3 wash(vec3 base, vec2 p, int style) {
	float bloom = fbm(p * 0.18) - 0.5;
	float grain = noise(p * 9.0) - 0.5;
	if (style == MODERN) return base * (1.0 + grain * 0.015);
	if (style == ANCIENT) return base * (0.93 + bloom * 0.2 + grain * 0.07 + (fbm(p * 0.045) - 0.5) * 0.22);
	return base * (1.0 + bloom * 0.12 + grain * 0.05);
}

vec3 survey(int k, vec3 base, vec3 c, vec2 p, float fw, float edge, float fade, vec3 water) {
	vec3 ink = mix(base * 0.5, UMBER, 0.4);
	if (k == FIELD) {
		float parcel = floor(fbm(floor(p / 6.0) * 0.7) * 6.0);
		float angle = parcel * 0.52 + hash(floor(p / 6.0)) * 0.3;
		c = mix(c, ink, strips(p, SYMBOL * 0.45, angle, 0.035, fw) * 0.35 * fade);
	} else if (k == WOOD) {
		float shade, ring;
		float crown = trees(p, fw, shade, ring);
		c = mix(c, ink, shade * 0.55 * fade);
		c = mix(c, base * 0.74, crown * fade);
	} else if (k == MARSH) {
		c = mix(c, PRUSSIAN, dashes(p, SYMBOL * 0.7, SYMBOL * 0.18, 0.03, fw, 0.0) * 0.6 * fade);
		c = mix(c, water, smoothstep(0.58, 0.72, fbm(p * 0.4)) * 0.35);
	} else if (k == WATER) {
		c = mix(c, PRUSSIAN, strips(p + vec2(0.0, fbm(p * 0.3) * 0.6), SYMBOL * 0.55, 0.0, 0.025, fw) * 0.25 * fade);
	} else if (k == EARTH) {
		c = mix(c, ink, strips(p, SYMBOL * 0.35, 0.3, 0.03, fw) * 0.25 * fade);
	} else if (k == ROCK) {
		c = mix(c, ink, dashes(p, SYMBOL * 0.5, SYMBOL * 0.12, 0.028, fw, 0.8) * 0.75 * fade);
	} else if (k == SAND) {
		c = mix(c, ink, dots(p, SYMBOL * 0.32, 0.05, fw) * 0.55 * fade);
	} else if (k == GRASS) {
		c = mix(c, ink, dashes(p, SYMBOL * 1.1, SYMBOL * 0.07, 0.022, fw, 1.57) * 0.25 * fade);
	}
	float rim = k == WATER ? 0.95 : k == ROAD ? 0.95 : k == WOOD ? 0.8 : k == MARSH ? 0.55 : k == GRASS ? 0.0 : 0.35;
	vec3 edge_ink = k == WATER || k == MARSH || k == SNOW ? PRUSSIAN : ink;
	return mix(c, edge_ink, smoothstep(0.45, 0.9, edge) * rim);
}

vec3 ancient(int k, vec3 base, vec3 c, vec2 p, float fw, float edge, float fade, vec3 water) {
	vec3 ink = mix(base * 0.35, SEPIA, 0.65);
	if (k == FIELD) {
		c = mix(c, ink, dots(p, SYMBOL * 0.55, 0.06, fw) * 0.5 * fade);
	} else if (k == WOOD) {
		float shade, ring;
		float crown = trees(p, fw, shade, ring);
		c = mix(c, base * 0.8, crown * fade);
		c = mix(c, ink, ring * 0.8 * fade);
	} else if (k == MARSH) {
		c = mix(c, VERDIGRIS, dashes(p, SYMBOL * 0.6, SYMBOL * 0.2, 0.035, fw, 0.0) * 0.7 * fade);
	} else if (k == WATER) {
		c = mix(c, VERDIGRIS, strips(p + vec2(0.0, sin(p.x * 2.2) * 0.12), SYMBOL * 0.5, 1.5708, 0.03, fw) * 0.45 * fade);
	} else if (k == ROCK) {
		c = mix(c, ink, dashes(p, SYMBOL * 0.45, SYMBOL * 0.14, 0.035, fw, 0.8) * 0.85 * fade);
	} else if (k == SAND) {
		c = mix(c, ink, dots(p, SYMBOL * 0.3, 0.05, fw) * 0.6 * fade);
	}
	float rim = k == GRASS ? 0.3 : k == WATER ? 1.0 : 0.85;
	vec3 edge_ink = k == WATER || k == MARSH ? VERDIGRIS * 0.7 : ink;
	return mix(c, edge_ink, smoothstep(0.4, 0.85, edge) * rim);
}

vec3 modern(int k, vec3 base, vec3 c, vec2 p, float fw, float edge, float fade, vec3 water) {
	if (k == MARSH) {
		c = mix(c, TOPO_BLUE, dashes(p, SYMBOL * 0.7, SYMBOL * 0.2, 0.025, fw, 0.0) * 0.8 * fade);
	} else if (k == ROCK) {
		c = mix(c, PRINT, dashes(p, SYMBOL * 0.5, SYMBOL * 0.12, 0.02, fw, 0.8) * 0.6 * fade);
	} else if (k == SAND) {
		c = mix(c, PRINT, dots(p, SYMBOL * 0.35, 0.04, fw) * 0.45 * fade);
	}
	float rim = k == WATER ? 1.0 : k == ROAD ? 1.0 : k == WOOD ? 0.85 : k == GRASS ? 0.0 : 0.45;
	vec3 edge_ink = k == WATER || k == MARSH || k == SNOW ? TOPO_BLUE : k == WOOD ? base * 0.55 : PRINT;
	return mix(c, edge_ink, smoothstep(0.5, 0.9, edge) * rim);
}

vec3 look(int k, int style, vec2 p, float fw, float edge) {
	int at = style * KINDS;
	vec3 base = palette[at + k].rgb;
	vec3 c = wash(base, p, style);
	float fade = 1.0 - smoothstep(0.08, 0.22, fw / SYMBOL);
	vec3 water = palette[at + WATER].rgb;
	if (style == ANCIENT) return ancient(k, base, c, p, fw, edge, fade, water);
	if (style == MODERN) return modern(k, base, c, p, fw, edge, fade, water);
	return survey(k, base, c, p, fw, edge, fade, water);
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float edge;
	vec3 at = kind_at(UV, edge);
	int k = int(at.x);
	vec2 p = world.xz * detail;
	float fw = max(fwidth(p.x), fwidth(p.y));
	if (k <= 0 || k > 10) {
		if (painted_only) discard;
		ALBEDO = plain.rgb * (0.94 + 0.08 * fbm(p * 0.25));
	} else {
		ALBEDO = look(k, clamp(ground_style, SURVEY, MODERN), p, fw, edge);
	}
	ROUGHNESS = k == WATER ? 0.45 : 1.0;
}
