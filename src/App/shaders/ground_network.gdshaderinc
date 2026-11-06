struct LaneHit {
	float d;
	float along;
	int kind;
};

LaneHit lane_at(vec2 uv, float texel_p) {
	LaneHit h;
	h.d = 1e9;
	h.along = 0.0;
	h.kind = 0;
	if (textureSize(network, 0) != ivec2(cells)) return h;
	vec2 q = uv * cells;
	vec2 base = floor(q - 0.5);
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 cell = base + vec2(float(i), float(j));
			if (cell.x < 0.0 || cell.y < 0.0 || cell.x >= cells.x || cell.y >= cells.y) continue;
			vec4 t = round(texelFetch(network, ivec2(cell), 0) * 255.0);
			if (t.b < 0.5) continue;
			float a = t.r / 255.0 * PI;
			vec2 dir = vec2(cos(a), sin(a));
			float s = abs(dot(q - cell - 0.5, vec2(-dir.y, dir.x)) - (t.g - 128.0) / LINE_OFFSET_SCALE);
			if (s < h.d) {
				h.d = s;
				h.kind = int(t.b + 0.5);
				h.along = dot(q, dir);
			}
		}
	}
	h.d *= texel_p;
	h.along *= texel_p;
	return h;
}

bool farm_site(vec2 uv, mat2 from_uv, vec2 p, out vec2 site, out vec2 seed, out float turn) {
	site = p;
	seed = vec2(0.0);
	turn = 0.0;
	if (textureSize(farm_sites, 0) != ivec2(cells)) return false;
	vec2 q = uv * cells;
	vec2 cell = clamp(floor(q), vec2(0.0), cells - 1.0);
	vec4 t = round(texelFetch(farm_sites, ivec2(cell), 0) * 255.0);
	if (t.a < 0.5) return false;
	vec2 at = cell + 0.5 + (t.rg - 128.0) / SITE_OFFSET_SCALE;
	site = p + from_uv * ((at - q) / cells);
	seed = vec2(t.b, t.a);
	float a = (t.a - 1.0) / 253.0 * TAU;
	vec2 d = from_uv * (vec2(cos(a), sin(a)) / cells);
	turn = atan(d.y, d.x);
	return true;
}

float dash_on(float along, float period, float share, float fw) {
	return mix(step(fract(along / period), share), share, far_blend(fw, period));
}

vec3 lanes_drawn(int style, vec3 c, vec3 sheet, vec2 p, LaneHit h, float fw) {
	float w = metres(LANE_HALF_M);
	bool lane = h.kind == NET_LANE;
	if (style == ANCIENT) return mix(c, RED_OCHRE, ink(h.d, lane ? W1 : W1 * 0.6, fw) * (lane ? 0.85 : 0.6));
	if (style == MODERN) {
		if (!lane) return mix(c, MODERN_BLACK, ink(h.d, M1 * 0.45, fw) * dash_on(h.along, metres(TRACK_DASH_M), 0.55, fw) * 0.9);
		c = mix(c, sheet, 1.0 - smoothstep(w - fw, w + fw, h.d));
		return mix(c, MODERN_BLACK, ink(abs(h.d - w), M1 * 0.5, fw) * 0.95);
	}
	if (!lane) return mix(c, WEGEBRAUN, ink(h.d, W2 * 0.8, fw) * dash_on(h.along, metres(TRACK_DASH_M), 0.55, fw) * 0.85);
	c = mix(c, wash(palette[ROAD].rgb, sheet, SURVEY_PAPER, p, w - h.d, 0.5), 1.0 - smoothstep(w - fw, w + fw, h.d));
	return mix(c, SURVEY_INK, hand_ink(abs(h.d - w + W1), W1, p, fw) * 0.9);
}

vec3 network_over(int style, vec3 c, vec3 sheet, vec2 p, vec2 uv, mat2 from_uv, float fw, bool farmland, float edge, inout float cover) {
	float texel_p = length(from_uv * vec2(1.0 / cells.x, 0.0));
	LaneHit h = lane_at(uv, texel_p);
	if (h.kind > 0) c = lanes_drawn(style, c, sheet, p, h, fw);
	vec2 site;
	vec2 seed;
	float turn;
	if (!farmland || !farm_site(uv, from_uv, p, site, seed, turn)) return c;
	if (style == ANCIENT) return ancient_farm(c, p, site, seed, turn, fw, cover);
	if (style == MODERN) return modern_farm(c, p, site, seed, turn, fw, cover);
	return survey_farm(c, sheet, p, site, seed, turn, edge, fw, cover);
}
