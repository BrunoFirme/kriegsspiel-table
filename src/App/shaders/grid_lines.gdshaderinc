global uniform int grid_kind;
global uniform float grid_pitch;
global uniform int grid_divisions;
global uniform int grid_hex_flat;
global uniform vec4 grid_ink;

const int GRID_SQUARES = 1;
const int GRID_HEXES = 2;
const float GRID_LINE_PX = 1.0;
const float GRID_SHOWN_FROM_PX = 4.0;
const float GRID_FULL_FROM_PX = 12.0;
const float GRID_DIVIDED_SHARE = 0.45;
const vec2 GRID_HEX_CELL = vec2(1.0, 1.7320508);

float grid_parallel(vec2 p, vec2 normal, float step) {
	float t = dot(p, normal) / step;
	return abs(t - round(t)) * step;
}

float grid_squares(vec2 p, float step) {
	return min(grid_parallel(p, vec2(1.0, 0.0), step), grid_parallel(p, vec2(0.0, 1.0), step));
}

vec2 grid_hex_offset(vec2 p) {
	vec4 hc = floor(vec4(p, p - vec2(0.5, 1.0)) / GRID_HEX_CELL.xyxy) + 0.5;
	vec4 h = vec4(p - hc.xy * GRID_HEX_CELL, p - (hc.zw + 0.5) * GRID_HEX_CELL);
	return dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
}

float grid_hexes(vec2 p, float step) {
	vec2 q = abs(grid_hex_offset(p / step));
	return abs(0.5 - max(q.x, dot(q, vec2(0.5, 0.8660254)))) * step;
}

float grid_coverage(float dist, float per_px, float cell) {
	float shown = smoothstep(GRID_SHOWN_FROM_PX, GRID_FULL_FROM_PX, cell / per_px);
	return clamp(GRID_LINE_PX * 0.5 + 0.5 - dist / per_px, 0.0, 1.0) * shown;
}

vec3 grid_linear(vec3 c) {
	c = clamp(c, vec3(0.0), vec3(1.0));
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

float grid_at(vec3 world) {
	if (grid_kind != GRID_SQUARES && grid_kind != GRID_HEXES || grid_pitch <= 0.0) return 0.0;
	vec2 p = world.xz;
	float per_px = max(max(length(dFdx(p)), length(dFdy(p))), 1e-6);
	float heavy = grid_kind == GRID_SQUARES ? grid_squares(p, grid_pitch) : grid_hexes(grid_hex_flat == 1 ? p.yx : p, grid_pitch);
	float m = grid_coverage(heavy, per_px, grid_pitch);
	if (grid_kind == GRID_SQUARES && grid_divisions > 1) {
		float step = grid_pitch / float(grid_divisions);
		m = max(m, grid_coverage(grid_squares(p, step), per_px, step) * GRID_DIVIDED_SHARE);
	}
	return m * grid_ink.a;
}

vec3 with_grid(vec3 albedo, vec3 world) {
	return mix(albedo, grid_linear(grid_ink.rgb), grid_at(world));
}
