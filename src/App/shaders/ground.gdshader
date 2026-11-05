shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D kinds : filter_nearest, repeat_disable;
uniform sampler2D edges : filter_linear, repeat_disable;
uniform sampler2D heights : filter_linear, repeat_disable;
uniform vec4 palette[39];
uniform bool painted_only = false;
uniform vec2 cells = vec2(64.0, 64.0);
uniform float cell_world = 0.2;
uniform float detail = 1.0;
global uniform int ground_style;
global uniform float contour_interval;
global uniform float units_per_metre;
global uniform float snow_line;

const int KINDS = 13;
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
const int TOWN = 12;
const float FIELD_STEPS = 4.0;
const float TOWN_STEPS = 8.0;
const float ROAD_STEPS = 4.0;
const int NO_FLOW = 8;
const int HEADINGS = 32;
const int CURRENT_BIT = 16384;
const int CULVERT_BIT = 32768;
const float FLOW_SPACING = 0.14;
const float FLOW_LEN = 0.2;
const float ARROW_SPACING = 1.1;
const float ARROW_LEN = 0.3;
const float RAPIDS_PITCH = 0.12;
const float FALL_STEP = 0.05;
const float CULVERT_REACH = 0.07;
const float CULVERT_GAP = 0.04;
const float TRACK_W_M = 5.0;
const float LANE_W_M = 16.0;
const float ROAD_W_M = 22.0;
const float CHAUSSEE_W_M = 25.0;
const float ANCIENT_ROAD_W = 0.06;
const float ANCIENT_LANE_W = 0.1;
const float ANCIENT_HIGHWAY_W = 0.16;
const float VERGE_WASH = 0.3;
const float RING_R = 0.22;
const float GLYPH = 0.45;
const float MEADOW_ROW = 1.25;
const float MEADOW_LEN = 0.25;
const float SHADOW_M = 1.5;
const float VILLAGE_RING = 0.6;
const float WET_LOT_M = 50.0;
const float WET_NEAR_M = 60.0;
const float FRONTS_M = 300.0;
const float VINE_ROW_M = 12.0;
const float VINE_STAKE_M = 8.0;
const float ORCHARD_M = 15.0;
const float FURROW_M = 8.0;
const vec3 GARTENGRUEN = vec3(0.62, 0.72, 0.42);
const vec2 PLATE_GREEN = vec2(1.5, -1.0);
const vec2 PLATE_BLUE = vec2(-1.0, 1.5);
const vec2 PLATE_RED = vec2(2.0, 1.0);
const vec2 PLATE_BROWN = vec2(1.0, 1.0);
const float PARAPET = 0.09;
const float SEAMLESS = 1.0;
const int SURVEY = 0;
const int ANCIENT = 1;
const int MODERN = 2;
const float WARP = 0.42;
const float EDGE_STEPS = 16.0;
const float ANCHOR = 3.0;
const vec2 NORTH = vec2(0.0, -1.0);
const vec2 SUN = vec2(-0.7071, -0.7071);
const vec3 LIGHT_FROM = vec3(-0.55, 0.62, -0.55);
const vec3 SURVEY_PAPER = vec3(0.929, 0.902, 0.827);
const vec3 ANCIENT_PAPER = vec3(0.847, 0.757, 0.584);
const vec3 MODERN_PAPER = vec3(0.953, 0.937, 0.878);
const float W1 = 0.012;
const float W2 = 0.025;
const float W3 = 0.05;
const float M1 = 0.03;
const float M2 = 0.06;
const float M3 = 0.09;
const vec3 SURVEY_INK = vec3(0.165, 0.145, 0.11);
const vec3 SURVEY_HACHURE = vec3(0.541, 0.494, 0.384);
const vec3 CARMINE = vec3(0.718, 0.192, 0.314);
const vec3 CINNABAR = vec3(0.784, 0.208, 0.118);
const vec3 WEGEBRAUN = vec3(0.545, 0.42, 0.29);
const vec3 GAMBOGE = vec3(0.851, 0.722, 0.29);
const vec3 POND = vec3(0.49, 0.612, 0.706);
const vec3 CONIFER_WASH = vec3(0.541, 0.604, 0.455);
const vec3 MODERN_BLACK = vec3(0.165, 0.157, 0.149);
const vec3 MODERN_BROWN = vec3(0.71, 0.443, 0.29);
const vec3 MODERN_GREEN = vec3(0.243, 0.541, 0.29);
const vec3 MODERN_RED = vec3(0.784, 0.204, 0.102);
const vec3 MODERN_SHADE = vec3(0.812, 0.765, 0.71);
const vec3 ANCIENT_INK = vec3(0.118, 0.106, 0.094);
const vec3 ANCIENT_UMBER = vec3(0.353, 0.243, 0.169);
const vec3 ANCIENT_RIDGE = vec3(0.533, 0.333, 0.2);
const vec3 RED_OCHRE = vec3(0.604, 0.29, 0.173);
const float HAND_CYCLES = 1.5;
const float HAND_SHIFT = 0.06;
const float WASH_SHIFT = 0.04;
const float CHAIN_LINES = 6.7;
const float LAID_LINES = 0.27;
const float SCREEN_PITCH = 0.095;
const float CENTURIA = 3.55;
const float CENTURY = 14.2;
const float HAND_WOBBLE = 0.06;
const float HAND_GRAIN = 3.0;
const float BLEED = 0.12;
const float BLEED_REACH = 2.2;
const float SNOW_BAND = 0.1;
const float NEATLINE = 0.15;
const float NEATLINE_GAP = 0.06;
const float MEANDER = 0.16;
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
const vec3 SURVEY_BLUE = vec3(0.2, 0.325, 0.443);
const vec3 MODERN_BLUE = vec3(0.255, 0.329, 0.49);
const float GENTLE = 0.268;
const float ROCK_FROM = 0.577;
const float ROCK_FULL = 0.839;
const float SHEER = 1.732;
const float SHEER_THINS = 0.7;
const float ROCK_STROKE = 1.0;
const float ROCK_BAND_LEAST = 0.15;
const float CURVE_STEP = 0.6;
const float GRAD_STEP = 0.35;
const float SLOPE_ROCK_WASH = 0.4;
const float CREST_FROM = 0.05;
const float CREST_FULL = 0.5;
const float LEDGE_TREES = 0.2;
const float FOOT_BAND = 0.2;
const float MARK_SHADE = 0.35;
const int LEVEL_STEPS = 15;
const float PLOT_OPEN_M = 350.0;
const float PLOT_SMALL_M = 80.0;
const float STAND_M = 250.0;
const float PADDOCK_M = 300.0;
const float MARSH_LOT_M = 200.0;
const float EARTH_LOT_M = 300.0;
const float WATER_LOT_M = 120.0;
const float ISLET_SHORE_M = 50.0;
const float TREES_OPEN_M = 80.0;
const float TREES_CLOSED_M = 50.0;
const float OPEN_WOOD_KEEP = 0.45;
const float RIDE = 0.5;
const int HEDGE = 1;
const int FENCE = 2;
const int WALL = 3;
const int DITCH = 4;
const int TRACK = 5;
const float TOWN_QUARTER_M = 150.0;
const float TOWN_BLOCK_M = 45.0;
const float STREET_M = 8.0;
const float LANE_M = 4.0;
const float HOUSE_M = 12.0;
const float HOUSE_GAP_M = 3.0;
const float HOUSE_DEPTH_M = 10.0;
const float HOUSE_SETBACK_M = 1.5;
const float SCATTERED_HOUSES = 0.3;
const float GARDEN_RING_M = 30.0;
const float GARDEN_HATCH_M = 10.0;
const float SQUARE_M = 40.0;
const float CHURCH_M = 25.0;
const float WALL_IN_M = 6.0;
const float FORT_IN_M = 45.0;
const float WALL_TOWERS_M = 60.0;
const float BASTIONS_M = 200.0;
const float BASTION_SHARE = 0.35;
const float TOWER_M = 4.0;
const float WALL_CELLS = 1.2;
const float OUTSIDE_WALLS = 0.7;
const float BASTION_OUT_M = 30.0;
const float DITCH_OUT_M = 8.0;
const float GLACIS_LINES_M = 3.0;
const float NEAR_TOWN_M = 60.0;
const float NEAR_TOWN_FAR_M = 140.0;
const float NEAR_TOWN_PLOTS = 0.8;
const float NEAR_TOWN_THINS = 0.4;

varying vec3 world;

#include "res://shaders/ground_marks.gdshaderinc"
#include "res://shaders/ground_lots.gdshaderinc"
#include "res://shaders/ground_town.gdshaderinc"
#include "res://shaders/ground_roads.gdshaderinc"
#include "res://shaders/ground_streams.gdshaderinc"
#include "res://shaders/ground_rock.gdshaderinc"
#include "res://shaders/ground_survey.gdshaderinc"
#include "res://shaders/ground_ancient.gdshaderinc"
#include "res://shaders/ground_modern.gdshaderinc"

vec3 snow_cap(int style, vec3 sheet, vec2 p, float fw, int k) {
	vec3 white = plain(style, palette[style * KINDS + SNOW].rgb, sheet, p, SEAMLESS);
	vec3 blue = style == MODERN ? MODERN_BLUE : style == ANCIENT ? ANCIENT_INK : SURVEY_BLUE;
	float form = style == ANCIENT ? 0.0 : marks(p, 0.0, 0.45, 0.14, W1 * 0.5, 0.15, 0.2, 0.45, fw) * 0.35;
	float ice = k == WATER ? max(hatch(p.x + p.y, 0.08, W1 * 0.3, fw), hatch(p.x - p.y, 0.08, W1 * 0.3, fw)) * 0.35 : 0.0;
	return mix(white, blue, max(form, ice));
}

vec3 neatline(int style, vec3 c, vec2 uv, float fw) {
	vec2 size = (cells - 1.0) * cell_world;
	vec2 inset = min(uv, 1.0 - uv) * size;
	float m = min(inset.x, inset.y);
	if (m > NEATLINE * 2.0) return c;
	float along = inset.x < inset.y ? uv.y * size.y : uv.x * size.x;
	if (style == MODERN) {
		float line = ink(abs(m - NEATLINE), M1 * 0.6, fw);
		float km = metres(1000.0);
		float tick = units_per_metre > 0.0 ? ink(abs(fract(along / km) - 0.5) * km, M1 * 0.5, fw) * step(NEATLINE - 0.06, m) * step(m, NEATLINE) : 0.0;
		return mix(c, MODERN_BLACK, max(line, tick));
	}
	if (style == ANCIENT) {
		float s = along / MEANDER;
		float outer = NEATLINE + MEANDER * 0.35;
		float inner = NEATLINE - MEANDER * 0.35;
		float level = mix(inner, outer, step(0.5, fract(s)));
		float rail = ink(abs(m - level), W1, fw);
		float band = step(inner - W1, m) * step(m, outer + W1);
		float joins = max(ink(abs(fract(s) - 0.5) * MEANDER, W1, fw), ink(min(fract(s), 1.0 - fract(s)) * MEANDER, W1, fw)) * band;
		float frame = ink(abs(m - outer - MEANDER * 0.25), W1, fw);
		return mix(c, ANCIENT_INK, max(max(rail, frame), joins));
	}
	float twin = max(hand_ink(abs(m - NEATLINE), W1, uv * size, fw), hand_ink(abs(m - NEATLINE - NEATLINE_GAP), W1, uv * size, fw));
	return mix(c, SURVEY_INK, twin);
}

float smooth_height(vec2 uv) {
	return textureLod(heights, (clamp(uv, vec2(0.0), vec2(1.0)) * (cells - 1.0) + 0.5) / cells, 0.0).g;
}

vec3 contour_ink(int style, int k, vec3 ground) {
	if (style == ANCIENT) return ANCIENT_UMBER;
	if (k == WATER || k == SNOW) return style == MODERN ? MODERN_BLUE : SURVEY_BLUE;
	vec3 tone = style == MODERN ? MODERN_BROWN : SURVEY_INK;
	return tone * mix(INK_ON_DARK, 1.0, smoothstep(0.2, 0.7, dot(ground, LUMA)));
}

float contour(float h, vec2 p, int style, float steep, float rock, out float halo) {
	halo = 0.0;
	float fh = max(fwidth(h), 1e-6);
	if (contour_interval <= 0.0 || h < contour_interval * 0.5) return 0.0;
	if (style != MODERN) h += (noise(p * 0.9) - 0.5) * WOBBLE * steep * (style == ANCIENT ? ANCIENT_WOBBLE : 1.0);
	float level = h / contour_interval;
	float d = abs(fract(level + 0.5) - 0.5) * contour_interval / fh;
	bool index = abs(mod(round(level), float(INDEX_EVERY))) < 0.5;
	float px = index ? INDEX_PX : LINE_PX;
	float strength = index ? INDEX_INK : LINE_INK;
	if (style == SURVEY) px *= 1.0 + HAND_WOBBLE * (noise(p * HAND_GRAIN) - 0.5) * 2.0;
	if (style == SURVEY && !index) strength *= smoothstep(BREAK_FROM, BREAK_TO, noise(p * 2.6));
	if (style == ANCIENT) px *= 1.0 + ANCIENT_WIDTH_PLAY * noise(p * 1.3 + 7.0);
	float apart = contour_interval / fh;
	float crowded = 1.0 - smoothstep(CROWD_PX, CROWD_PX * 3.0, index ? apart * float(INDEX_EVERY) : apart);
	float level_ground = smoothstep(FLAT_SPAN, FLAT_SPAN * 2.0, contour_interval / max(steep, 1e-5));
	strength *= index ? 1.0 - crowded * (1.0 - INDEX_KEEP) : (1.0 - crowded) * (1.0 - level_ground) * (1.0 - rock);
	halo = (1.0 - smoothstep(px + HALO_PX - 0.5, px + HALO_PX + 0.5, d)) * strength;
	float line = (1.0 - smoothstep(px - 0.5, px + 0.5, d)) * strength;
	if (style == SURVEY) line = max(line, (1.0 - smoothstep(px, px * BLEED_REACH, d)) * BLEED * strength);
	return line;
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
	mat2 from_uv = abs(determinant(to_uv)) > 1e-14 ? inverse(to_uv) : mat2(0.0);
	int style = clamp(ground_style, SURVEY, MODERN);
	float h = smooth_height(UV);
	vec3 own_texel = texel_of(floor(clamp(UV, vec2(0.0), vec2(1.0)) * cells));
	float own = own_texel.g;
	vec2 levels = vec2(mod(own_texel.b, 16.0), floor(own_texel.b / 16.0)) / float(LEVEL_STEPS);
	float h_ne = smooth_height(UV + to_uv * vec2(GRAD_STEP, GRAD_STEP));
	float h_se = smooth_height(UV + to_uv * vec2(GRAD_STEP, -GRAD_STEP));
	vec2 grad = vec2(h_ne + h_se - 2.0 * h, h_ne - h_se) / (2.0 * GRAD_STEP);
	float steep = length(grad);
	vec2 down = steep > 1e-4 ? -grad / steep : vec2(0.0);
	float rockness = k == ROCK ? 1.0 : smoothstep(ROCK_FROM, ROCK_FULL, steep);
	float curve = rockness > 0.0 ? (smooth_height(UV + to_uv * down * CURVE_STEP) + smooth_height(UV - to_uv * down * CURVE_STEP) - 2.0 * h) / (CURVE_STEP * CURVE_STEP) : 0.0;
	float convex = smoothstep(CREST_FROM, CREST_FULL, -curve);
	float gully = smoothstep(CREST_FROM, CREST_FULL, curve);
	float crest = 4.0 * rockness * (1.0 - rockness) * convex;
	float sheer = smoothstep(ROCK_FULL * 1.4, SHEER, steep);
	float foot = smoothstep(ROCK_FROM - FOOT_BAND, ROCK_FROM, steep) * (1.0 - rockness) * (k == ROCK ? 0.0 : 1.0);
	float follow = smoothstep(GENTLE, GENTLE * 1.5, steep);
	vec3 n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	float steep_n;
	float dark = shaded(n, steep_n);
	float halo;
	float h_lines = style == MODERN ? smooth_height(UV + to_uv * (PLATE_BROWN * fw)) : h;
	float lines = contour(h_lines, p, style, steep, rockness, halo);
	float near_town = 1.0 - smoothstep(metres(NEAR_TOWN_M), metres(NEAR_TOWN_FAR_M), town_at(UV));
	float mid = 0.0;
	float road_w = -1.0;
	float road_class = levels.x;
	bool over_water = false;
	if (k == ROAD || k == BRIDGE) {
		mid = road_mid(UV, to_uv);
		road_w = road_width(style, levels.x);
		vec2 o = beyond_band(UV, p);
		int beyond = kind_of(floor(clamp(UV + to_uv * (o - p), vec2(0.0), vec2(1.0)) * cells));
		if (beyond <= 0 || beyond == ROAD || beyond == BRIDGE) beyond = beside;
		over_water = beyond == WATER;
		if (beyond > 0 && beyond < KINDS && beyond != ROAD && beyond != BRIDGE) {
			k = beyond;
			beside = ROAD;
			own = salt_at(o, p, UV, to_uv);
			levels = levels_at(o, p, UV, to_uv);
			edge = SEAMLESS;
			shore = SEAMLESS;
			border = cell_world * 2.0;
		}
	}
	float bump = 0.0;
	float cover = 0.0;
	vec3 sheet = paper(style, p, fw);
	vec3 rock = sheet;
	if (rockness + foot > 0.001) rock = rock_face(style, mix(sheet, plain(style, palette[style * KINDS + ROCK].rgb, sheet, p, SEAMLESS), k == ROCK ? 1.0 : SLOPE_ROCK_WASH), p, UV, to_uv, h, down, steep, dark, crest, gully, sheer, foot, own, fw);
	vec3 c;
	if (k <= 0 || k >= KINDS) {
		if (painted_only && lines < 0.02) discard;
		c = painted_only ? contour_ink(style, k, sheet) : mix(slopes(sheet, n, p, world.y, fw, style, 1.0 - rockness), rock, rockness);
	} else {
		vec3 tint = palette[style * KINDS + k].rgb;
		bool bare = k != WATER && k != ROAD && k != BRIDGE;
		if (bare && rockness >= 1.0 - 1e-3 && k != WOOD) c = rock;
		else {
			if (style == ANCIENT) c = ancient(k, beside, tint, sheet, p, UV, to_uv, own, levels, near_town, mid, road_w, down, follow, rockness, fw, edge, shore, cover);
			else if (style == MODERN) c = modern(k, beside, tint, sheet, p, UV, to_uv, own, levels, near_town, mid, road_w, down, follow, rockness, fw, edge, shore, cover);
			else c = survey(k, beside, tint, sheet, p, UV, to_uv, own, levels, near_town, mid, road_w, down, follow, rockness, fw, edge, shore, bump, cover);
			float culvert = 0.0;
			if (k != WATER) {
				float order_here;
				float brook = streams(style, p, UV, from_uv, fw, mid, road_w, order_here, culvert);
				if (brook > 0.001) c = mix(c, style == MODERN ? MODERN_BLUE : style == ANCIENT ? ANCIENT_INK : SURVEY_BLUE, brook);
			} else c = current(style, c, p, UV, to_uv, from_uv, border, fw);
			if (road_w >= 0.0) c = road_over(style, c, sheet, p, UV, to_uv, road_class, mid, fw, over_water);
			if (culvert > 0.001) c = mix(c, style == MODERN ? MODERN_BLACK : style == ANCIENT ? ANCIENT_INK : SURVEY_INK, culvert);
			if (halo > 0.001) c = mix(c, plain(style, tint, sheet, p, edge), halo * HALO);
			c = mix(c, c * (1.0 - MARK_SHADE), dark * cover * smoothstep(0.04, 0.35, steep_n));
			if (bare) c = slopes(c, n, p, world.y, fw, style, (1.0 - cover) * (1.0 - rockness));
			if (k == WATER && beside == BRIDGE) c *= 1.0 - 0.45 * (1.0 - smoothstep(0.0, PARAPET * 1.5, edge));
			float keep = k == WOOD ? cover : 0.0;
			if (bare) c = mix(c, rock, rockness * (1.0 - keep));
		}
	}
	if (snow_line > 0.0) {
		float band = snow_line * SNOW_BAND;
		float snow = smoothstep(snow_line - band, snow_line + band, h + (noise(p * 0.7) - 0.5) * band * 2.0);
		if (snow > 0.001) c = mix(c, snow_cap(style, sheet, p, fw, k), snow * (1.0 - 0.5 * cover) * (1.0 - 0.6 * rockness));
	}
	c = mix(c, contour_ink(style, k, c), lines);
	if (!painted_only) c = neatline(style, c, UV, fw);
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
