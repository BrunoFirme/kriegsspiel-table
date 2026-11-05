shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D kinds : filter_nearest, repeat_disable;
uniform sampler2D edges : filter_linear, repeat_disable;
uniform sampler2D heights : filter_linear, repeat_disable;
uniform vec4 palette[36];
uniform bool painted_only = false;
uniform vec2 cells = vec2(64.0, 64.0);
uniform float cell_world = 0.2;
uniform float detail = 1.0;
global uniform int ground_style;
global uniform float contour_interval;

const int KINDS = 12;
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
const int BRIDGE = 11;
const float PARAPET = 0.09;
const float SEAMLESS = 1.0;
const int SURVEY = 0;
const int ANCIENT = 1;
const int MODERN = 2;
const float WARP = 0.42;
const float EDGE_STEPS = 16.0;
const float BLOCK = 7.0;
const float ANCHOR = 3.0;
const vec2 NORTH = vec2(0.0, -1.0);
const vec2 SUN = vec2(-0.7071, -0.7071);
const vec3 LIGHT_FROM = vec3(-0.55, 0.62, -0.55);
const vec3 SURVEY_PAPER = vec3(0.925, 0.895, 0.81);
const vec3 ANCIENT_PAPER = vec3(0.85, 0.77, 0.6);
const vec3 MODERN_PAPER = vec3(0.945, 0.93, 0.875);
const float SHEET_GAIN = 0.93;
const int INDEX_EVERY = 5;
const float LINE_PX = 0.72;
const float INDEX_PX = 1.28;
const float LINE_INK = 0.85;
const float INDEX_INK = 1.0;
const float HALO_PX = 1.0;
const float HALO = 0.6;
const float WOBBLE = 0.05;
const float ANCIENT_WOBBLE = 2.0;
const float ANCIENT_WIDTH_PLAY = 0.5;
const float FLAT_SPAN = 20.0;
const float CROWD_PX = 4.0;
const float INDEX_KEEP = 0.5;
const float BREAK_FROM = 0.26;
const float BREAK_TO = 0.34;
const float INK_ON_DARK = 0.4;
const vec3 LUMA = vec3(0.3, 0.59, 0.11);
const vec3 SURVEY_BLUE = vec3(0.15, 0.3, 0.45);
const vec3 MODERN_BLUE = vec3(0.1, 0.4, 0.7);

varying vec3 world;

#include "res://shaders/ground_marks.gdshaderinc"
#include "res://shaders/ground_rock.gdshaderinc"
#include "res://shaders/ground_survey.gdshaderinc"
#include "res://shaders/ground_ancient.gdshaderinc"
#include "res://shaders/ground_modern.gdshaderinc"

vec3 plain(int style, vec3 tint, vec3 sheet, vec2 p, float edge) {
	if (style == MODERN) return tint * (sheet / MODERN_PAPER);
	if (style == ANCIENT) return wash(tint, sheet, ANCIENT_PAPER, p, edge, 0.4);
	return wash(tint, sheet, SURVEY_PAPER, p, edge, 1.0);
}

float smooth_height(vec2 uv) {
	return textureLod(heights, (clamp(uv, vec2(0.0), vec2(1.0)) * (cells - 1.0) + 0.5) / cells, 0.0).g;
}

vec3 contour_ink(int style, int k, vec3 ground) {
	if (style == ANCIENT) return vec3(0.38, 0.23, 0.12);
	if (k == WATER || k == SNOW) return style == MODERN ? MODERN_BLUE : SURVEY_BLUE;
	vec3 brown = style == MODERN ? vec3(0.66, 0.38, 0.16) : vec3(0.46, 0.29, 0.18);
	return brown * mix(INK_ON_DARK, 1.0, smoothstep(0.2, 0.7, dot(ground, LUMA)));
}

float contour(float h, vec2 p, int style, float steep, out float halo) {
	halo = 0.0;
	float fh = max(fwidth(h), 1e-6);
	if (contour_interval <= 0.0 || h < contour_interval * 0.5) return 0.0;
	if (style != MODERN) h += (noise(p * 0.9) - 0.5) * WOBBLE * steep * (style == ANCIENT ? ANCIENT_WOBBLE : 1.0);
	float level = h / contour_interval;
	float d = abs(fract(level + 0.5) - 0.5) * contour_interval / fh;
	bool index = abs(mod(round(level), float(INDEX_EVERY))) < 0.5;
	float px = index ? INDEX_PX : LINE_PX;
	float strength = index ? INDEX_INK : LINE_INK;
	if (style == SURVEY && !index) strength *= smoothstep(BREAK_FROM, BREAK_TO, noise(p * 2.6));
	if (style == ANCIENT) px *= 1.0 + ANCIENT_WIDTH_PLAY * noise(p * 1.3 + 7.0);
	float apart = contour_interval / fh;
	float crowded = 1.0 - smoothstep(CROWD_PX, CROWD_PX * 3.0, index ? apart * float(INDEX_EVERY) : apart);
	float level_ground = smoothstep(FLAT_SPAN, FLAT_SPAN * 2.0, contour_interval / max(steep, 1e-5));
	strength *= index ? 1.0 - crowded * (1.0 - INDEX_KEEP) : (1.0 - crowded) * (1.0 - level_ground);
	halo = (1.0 - smoothstep(px + HALO_PX - 0.5, px + HALO_PX + 0.5, d)) * strength;
	return (1.0 - smoothstep(px - 0.5, px + 0.5, d)) * strength;
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float border;
	float margin;
	int beside;
	int k = kind_at(UV, border, margin, beside);
	vec2 p = world.xz * detail;
	float fw = max(max(fwidth(p.x), fwidth(p.y)), 1e-5);
	float edge = margin / max(fwidth(margin), 1e-5) * fw;
	if ((k == ROAD && beside == BRIDGE) || (k == BRIDGE && beside == ROAD)) edge = SEAMLESS;
	float shore = min(edge, border + cell_world * 0.5);
	mat2 dp = mat2(dFdx(p), dFdy(p));
	mat2 du = mat2(dFdx(UV), dFdy(UV));
	mat2 from_screen = abs(determinant(dp)) > 1e-14 ? inverse(dp) : mat2(0.0);
	mat2 to_uv = du * from_screen;
	int style = clamp(ground_style, SURVEY, MODERN);
	float h = smooth_height(UV);
	float own = salt_at(p, p, UV, to_uv);
	float steep = length(vec2(dFdx(h), dFdy(h)) * from_screen);
	float halo;
	float lines = contour(h, p, style, steep, halo);
	vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	float bump = 0.0;
	vec3 sheet = paper(style, p);
	vec3 c;
	if (k <= 0 || k >= KINDS) {
		if (painted_only && lines < 0.02) discard;
		c = painted_only ? contour_ink(style, k, sheet) : slopes(sheet, n, p, world.y, fw, style);
	} else {
		vec3 tint = palette[style * KINDS + k].rgb;
		if (style == ANCIENT) c = ancient(k, beside, tint, sheet, p, UV, to_uv, own, fw, edge, shore);
		else if (style == MODERN) c = modern(k, beside, tint, sheet, p, UV, to_uv, own, fw, edge, shore);
		else c = survey(k, beside, tint, sheet, p, UV, to_uv, own, fw, edge, shore, bump);
		c = mix(c, plain(style, tint, sheet, p, edge), halo * HALO);
		if (k != WATER && k != ROAD && k != BRIDGE) c = slopes(c, n, p, world.y, fw, style);
		if (k == WATER && beside == BRIDGE) c *= 1.0 - 0.45 * (1.0 - smoothstep(0.0, PARAPET * 1.5, edge));
	}
	c = mix(c, contour_ink(style, k, c), lines);
	ALBEDO = to_linear(c) * SHEET_GAIN;
	if (bump > 0.0) {
		vec3 dx = dFdx(VERTEX);
		vec3 dy = dFdy(VERTEX);
		vec3 r1 = cross(dy, NORMAL);
		vec3 r2 = cross(NORMAL, dx);
		float det = dot(dx, r1);
		vec3 grad = sign(det) * (dFdx(bump) * r1 + dFdy(bump) * r2);
		NORMAL = normalize(NORMAL - grad / max(abs(det), 1e-12) * 0.6);
	}
	ROUGHNESS = k == WATER ? 0.5 : 1.0;
}
