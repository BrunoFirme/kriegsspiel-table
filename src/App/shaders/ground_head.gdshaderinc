#include "res://shaders/ground_base.gdshaderinc"
#include "res://shaders/ground_cells.gdshaderinc"

#include "res://shaders/ground_marks.gdshaderinc"
#include "res://shaders/ground_lots.gdshaderinc"
#include "res://shaders/ground_streams.gdshaderinc"
#include "res://shaders/ground_rock.gdshaderinc"
#include "res://shaders/ground_survey.gdshaderinc"
#include "res://shaders/ground_ancient.gdshaderinc"
#include "res://shaders/ground_modern.gdshaderinc"
#include "res://shaders/ground_medieval.gdshaderinc"

#include "res://shaders/ground_near.gdshaderinc"

vec3 one_ink(vec3 c) {
	float tone = clamp(dot(c, LUMA) / dot(SURVEY_PAPER, LUMA), 0.0, 1.0);
	tone = mix(tone, 1.0, STAFF_WASH_LIFT * smoothstep(STAFF_WASH_FROM, STAFF_WASH_FULL, tone));
	return STAFF_PAPER * tone;
}

vec3 snow_cap(int style, vec3 sheet, vec2 p, float fw, int k) {
	vec3 white = plain(style, palette[style * KINDS + SNOW].rgb, sheet, p, SEAMLESS);
	vec3 blue = style == MODERN ? MODERN_BLUE : style == ANCIENT ? ANCIENT_INK : style == MEDIEVAL ? MEDIEVAL_BLUE : SURVEY_BLUE;
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
	if (style == MEDIEVAL) {
		c = mix(c, MEDIEVAL_RUBRIC, hand_ink(abs(m - NEATLINE - NEATLINE_GAP), W2, uv * size, fw));
		return mix(c, MEDIEVAL_INK, hand_ink(abs(m - NEATLINE), W1, uv * size, fw));
	}
	float twin = max(hand_ink(abs(m - NEATLINE), W1, uv * size, fw), hand_ink(abs(m - NEATLINE - NEATLINE_GAP), W1, uv * size, fw));
	return mix(c, SURVEY_INK, twin);
}

vec3 contour_ink(int style, int k, vec3 ground) {
	if (style == ANCIENT) return ANCIENT_UMBER;
	if (style == MEDIEVAL) return k == WATER || k == SNOW ? MEDIEVAL_WAVE : MEDIEVAL_CONTOUR;
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
	if (style == SURVEY || style == MEDIEVAL) {
		px *= SURVEY_CONTOUR_PX * (1.0 + HAND_WOBBLE * (noise(p * HAND_GRAIN) - 0.5) * 2.0);
		strength *= SURVEY_CONTOUR_INK;
	}
	if (style == SURVEY && !index) strength *= smoothstep(BREAK_FROM, BREAK_TO, noise(p * 2.6));
	if (style == ANCIENT) {
		px = LINE_PX * ANCIENT_CONTOUR_PX * (1.0 + ANCIENT_WIDTH_PLAY * noise(p * 1.3 + 7.0));
		strength = ANCIENT_CONTOUR_INK;
	}
	float apart = contour_interval / fh;
	float crowded = 1.0 - smoothstep(CROWD_PX, CROWD_PX * 3.0, index ? apart * float(INDEX_EVERY) : apart);
	float level_ground = smoothstep(FLAT_SPAN, FLAT_SPAN * 2.0, contour_interval / max(steep, 1e-5));
	strength *= index ? 1.0 - crowded * (1.0 - INDEX_KEEP) : (1.0 - crowded) * (1.0 - level_ground) * (1.0 - rock);
	halo = (1.0 - smoothstep(px + HALO_PX - 0.5, px + HALO_PX + 0.5, d)) * strength;
	float line = (1.0 - smoothstep(px - 0.5, px + 0.5, d)) * strength;
	if (style == SURVEY) line = max(line, (1.0 - smoothstep(px, px * BLEED_REACH, d)) * BLEED * strength);
	return line;
}

vec3 ground_colour(vec2 uv, vec3 at, vec3 n, out float bump, out int kind, out float there) {
	there = 1.0;
	kind = 0;
	float border;
	float margin;
	int beside;
	int k = kind_at(uv, border, margin, beside);
	kind = k;
	vec2 p = at.xz * detail;
	float fw = max(max(fwidth(p.x), fwidth(p.y)), 1e-5);
	float edge = margin / max(fwidth(margin), 1e-5) * fw;
	float shore = min(edge, border + cell_world * 0.5);
	mat2 dp = mat2(dFdx(p), dFdy(p));
	mat2 du = mat2(dFdx(uv), dFdy(uv));
	mat2 from_screen = abs(determinant(dp)) > 1e-14 ? inverse(dp) : mat2(0.0);
	mat2 to_uv = du * from_screen;
	mat2 from_uv = abs(determinant(to_uv)) > 1e-14 ? inverse(to_uv) : mat2(0.0);
	bool staff = ground_style == STAFF;
	int style = staff ? SURVEY : clamp(ground_style, SURVEY, MEDIEVAL);
	float h = smooth_height(uv);
	vec3 own_texel = texel_of(floor(clamp(uv, vec2(0.0), vec2(1.0)) * cells));
	float own = own_texel.g;
	vec2 levels = vec2(mod(own_texel.b, 16.0), floor(own_texel.b / 16.0)) / float(LEVEL_STEPS);
	float h_ne = smooth_height(uv + to_uv * vec2(GRAD_STEP, GRAD_STEP));
	float h_se = smooth_height(uv + to_uv * vec2(GRAD_STEP, -GRAD_STEP));
	vec2 grad = vec2(h_ne + h_se - 2.0 * h, h_ne - h_se) / (2.0 * GRAD_STEP);
	float steep = length(grad);
	vec2 down = steep > 1e-4 ? -grad / steep : vec2(0.0);
	float rockness = k == ROCK ? 1.0 : smoothstep(ROCK_FROM, ROCK_FULL, steep);
	float hill = style == ANCIENT ? smoothstep(HILL_FROM, HILL_FULL, steep) : 0.0;
	float curve = rockness + hill > 0.0 ? (smooth_height(uv + to_uv * down * CURVE_STEP) + smooth_height(uv - to_uv * down * CURVE_STEP) - 2.0 * h) / (CURVE_STEP * CURVE_STEP) : 0.0;
	float convex = smoothstep(CREST_FROM, CREST_FULL, -curve);
	float gully = smoothstep(CREST_FROM, CREST_FULL, curve);
	float crest = 4.0 * rockness * (1.0 - rockness) * convex;
	float sheer = smoothstep(ROCK_FULL * 1.2, SHEER, steep);
	float foot = smoothstep(ROCK_FROM - FOOT_BAND, ROCK_FROM, steep) * (1.0 - rockness) * (k == ROCK ? 0.0 : 1.0);
	float follow = smoothstep(GENTLE, GENTLE * 1.5, steep);
	float steep_n;
	float dark = shaded(n, steep_n);
	vec2 rise = vec2(dFdx(at.y), dFdy(at.y)) * from_screen * detail;
	float lean = min(length(rise), MOST_LEAN);
	vec2 facing = lean > 1e-4 ? normalize(rise) : vec2(0.0);
	float wall = smoothstep(ROCK_FROM, ROCK_FULL, lean);
	float face = max(wall, smoothstep(ROCK_FROM, ROCK_FULL, steep));
	float on_face = fw * sqrt(1.0 + lean * lean);
	float halo;
	float h_lines = style == MODERN ? smooth_height(uv + to_uv * (PLATE_BROWN * fw)) : h;
	float lines = contour(h_lines, p, style, steep, rockness, halo) * (1.0 - ground_world);
	halo *= 1.0 - ground_world;
	bump = 0.0;
	float cover = 0.0;
	vec3 sheet = paper(style, p, fw);
	vec3 c;
	if (k <= 0 || k >= KINDS) {
		if (painted_only) there = lines < LEAST_LINE ? 0.0 : lines;
		if (there <= 0.0) return vec3(0.0);
		c = painted_only ? contour_ink(style, k, sheet) : mix(slopes(sheet, n, p, at.y, fw, style, 1.0 - rockness), sheet, ground_eye);
		if (!painted_only && rockness + foot > 0.001) c = rock_face(style, c, p, h, down, steep, dark, crest, gully, sheer, foot, own, fw, max(rockness, foot));
	} else {
		vec2 near = built_at(uv);
		if ((k == FIELD || k == MARSH) && near.r + (noise(p / metres(3.0) + 9.0) - 0.5) * 5.0 < CLOSES_M || k == FIELD && near.g + (noise(p / metres(2.0) + 4.0) - 0.5) * 2.0 < VERGE_M) {
			k = GRASS;
			levels = vec2(0.0);
		}
		vec3 tint = palette[style * KINDS + k].rgb;
		bool bare = k != WATER;
		vec2 lie = vec2(h, steep);
		if (style == ANCIENT) c = ancient(k, beside, tint, sheet, p, uv, to_uv, own, levels, down, follow, lie, rockness, fw, edge, shore, cover);
		else if (style == MEDIEVAL) c = medieval(k, beside, tint, sheet, p, uv, to_uv, own, levels, down, follow, lie, rockness, fw, edge, shore, cover);
		else if (style == MODERN) c = modern(k, beside, tint, sheet, p, uv, to_uv, own, levels, down, follow, lie, rockness, fw, edge, shore, cover);
		else c = survey(k, beside, tint, sheet, p, uv, to_uv, own, levels, down, follow, lie, rockness, fw, edge, shore, bump, cover);
		float canopy = 0.0;
		if (k == WOOD) c = real_wood(c * forest_tint(levels.y), p, uv, to_uv, own, levels.x, rockness, fw, canopy);
		if (k == GRASS || k == MARSH) c = real_grass(c, p, own, fw, style, k == MARSH, near, palette[style * KINDS + EARTH].rgb / max(palette[style * KINDS + GRASS].rgb, vec3(0.01)));
		if (k == WATER) c = real_water(c * (1.0 - (1.0 - smoothstep(1.0, WORKED_M, near.g)) * 0.2), p, fw, levels.x > 0.0, border);
		if (k == SAND) c = real_sand(c, p, own, fw, beside == WATER ? border : 1e3, style);
		if (k == ROCK) c = real_rock(c, p, own, fw, at.y / max(units_per_metre, 1e-6), 1.0);
		if (k == FIELD) c = real_field(c, p, fw);
		if (k == EARTH) c = real_earth(c, p, own, fw, style);
		if (k == SNOW) c = real_snow(c, p, own, fw);
		if (!bare && levels.x <= 0.0) c = current(style, c, p, uv, to_uv, from_uv, border, fw);
		if (halo > 0.001) c = mix(c, plain(style, tint, sheet, p, edge), halo * HALO);
		c = mix(c, c * (1.0 - MARK_SHADE), dark * cover * smoothstep(0.04, 0.35, steep_n) * (1.0 - ground_eye));
		float keep = k == WOOD ? max(cover, canopy) * (1.0 - smoothstep(ROCK_FULL, SHEER, lean)) : 0.0;
		if (bare && face > 0.001) c = mix(c, stone(plain(style, palette[style * KINDS + ROCK].rgb, sheet, p, SEAMLESS), p, at.y, facing, wall, own, on_face), face * (1.0 - keep));
		if (bare) {
			vec3 sloped = mix(slopes(c, n, p, at.y, fw, style, (1.0 - cover) * (1.0 - rockness) * (k == WOOD ? UNDER_WOOD : 1.0)), c, ground_eye);
			c = beside == WATER ? mix(c, sloped, smoothstep(cell_world * 0.5, cell_world * BANK_CELLS, border)) : sloped;
		}
		if (bare && hill > 0.001) c = ancient_hills(c, p, n, hill, convex, k == WOOD ? 0.0 : HILL_FILL, fw);
		if (bare && rockness + foot > 0.001) c = rock_face(style, c, p, h, down, steep, dark, crest, gully, sheer, foot, own, fw, max(rockness, foot) * (1.0 - keep) * symbols(on_face));
	}
	if (snow_line > 0.0) {
		float band = snow_line * SNOW_BAND;
		float snow = smoothstep(snow_line - band, snow_line + band, h + (noise(p * 0.7) - 0.5) * band * 2.0);
		if (snow > 0.001) c = mix(c, snow_cap(style, sheet, p, fw, k), snow * (1.0 - 0.5 * cover) * (1.0 - 0.6 * rockness));
	}
	if (staff) c = one_ink(c);
	c = mix(c, contour_ink(style, k, c), lines);
	if (!painted_only && ground_world < 0.5) c = neatline(style, c, uv, fw);
	return c;
}
