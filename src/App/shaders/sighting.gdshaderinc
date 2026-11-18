const int CIRCLE = 0;

uniform int kind = 0;
uniform vec2 center;
uniform float radius = 10.0;
const int ROWS = 256;
const int ROW_EDGES = 128;

uniform vec2 poly[512];
uniform int poly_n = 0;
uniform sampler2D sight_rows : filter_nearest, repeat_disable;
uniform vec2 rows_from = vec2(0.0, 1.0);
uniform vec4 shade : source_color = vec4(0.0, 0.0, 0.0, 0.6);
uniform sampler2D seen : filter_linear, repeat_enable;
uniform int seen_on = 0;
uniform float seen_reach = 10.0;
const int MOST_EYES = 64;
uniform int eyes_n = 0;
uniform vec2 eyes[64];
uniform sampler2DArray eyes_seen : filter_linear, repeat_enable;
uniform int eyes_drawn = 0;

const float WHOLE_TURN = 6.28318530718;

float seen_at(vec2 p) {
	vec2 d = p - center;
	float half_ring = 0.5 / float(textureSize(seen, 0).y);
	float along = clamp(length(d) / max(seen_reach, 1e-6), half_ring, 1.0 - half_ring);
	return textureLod(seen, vec2(atan(-d.y, d.x) / WHOLE_TURN, along), 0.0).r;
}

bool eye_sees(vec2 p, int i) {
	vec2 d = p - eyes[i];
	float far = length(d);
	if (far >= seen_reach) return false;
	float half_ring = 0.5 / float(textureSize(eyes_seen, 0).y);
	float along = clamp(far / max(seen_reach, 1e-6), half_ring, 1.0 - half_ring);
	return textureLod(eyes_seen, vec3(atan(-d.y, d.x) / WHOLE_TURN, along, float(i)), 0.0).r > 0.5;
}

bool any_eye_sees(vec2 p) {
	for (int i = 0; i < MOST_EYES; i++) {
		if (i >= eyes_n) break;
		if (eye_sees(p, i)) return true;
	}
	return false;
}

float nearest_eye(vec2 p, int square) {
	float best = 1e30;
	for (int i = 0; i < MOST_EYES; i++) {
		if (i >= eyes_n) break;
		vec2 d = abs(p - eyes[i]);
		best = min(best, square == 1 ? max(d.x, d.y) : length(d));
	}
	return best;
}

bool crosses(vec2 a, vec2 b, vec2 p) {
	return ((a.y > p.y) != (b.y > p.y)) && (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x);
}

bool in_whole_poly(vec2 p) {
	bool c = false;
	int j = poly_n - 1;
	for (int i = 0; i < 512; i++) {
		if (i >= poly_n) break;
		if (crosses(poly[i], poly[j], p)) c = !c;
		j = i;
	}
	return c;
}

bool in_poly(vec2 p) {
	float r = (p.y - rows_from.x) * rows_from.y;
	if (r < 0.0 || r >= float(ROWS)) return false;
	int row = int(r);
	int held = int(texelFetch(sight_rows, ivec2(0, row), 0).r);
	if (held > ROW_EDGES) return in_whole_poly(p);
	bool c = false;
	for (int i = 1; i <= ROW_EDGES; i++) {
		if (i > held) break;
		vec4 edge = texelFetch(sight_rows, ivec2(i, row), 0);
		if (crosses(edge.xy, edge.zw, p)) c = !c;
	}
	return c;
}

bool in_sight(vec2 p) {
	if (eyes_n > 0) {
		if (eyes_drawn == 0) return any_eye_sees(p);
		return in_poly(p) && (seen_on == 0 || any_eye_sees(p));
	}
	float d = distance(p, center);
	bool inside;
	if (kind == CIRCLE) {
		inside = d <= radius;
	} else {
		bool no_radius = radius <= 0.0;
		inside = (no_radius || d <= radius) && in_poly(p);
	}
	if (inside && seen_on == 1) inside = seen_at(p) > 0.5;
	return inside;
}
