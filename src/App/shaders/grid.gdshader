shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;

const int NO_GRID = 0;
const int SQUARES = 1;

uniform int kind = 0;
uniform float pitch = 10.0;
uniform int subdivisions = 1;
uniform vec4 line_color : source_color = vec4(0.1, 0.1, 0.12, 0.5);
uniform float width = 0.06;
uniform vec2 origin = vec2(0.0);

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float antialiased_ink(float dist, float half_width) {
	float aa = fwidth(dist) + 0.0001;
	return 1.0 - smoothstep(half_width - aa, half_width + aa, dist);
}

float distance_to_parallel_lines(vec2 p, vec2 normal, float step) {
	float t = dot(p, normal) / step;
	return abs(t - round(t)) * step;
}

const vec2 HEX_CELL = vec2(1.0, 1.7320508);

vec2 offset_from_hex_centre(vec2 p) {
	vec4 hc = floor(vec4(p, p - vec2(0.5, 1.0)) / HEX_CELL.xyxy) + 0.5;
	vec4 h = vec4(p - hc.xy * HEX_CELL, p - (hc.zw + 0.5) * HEX_CELL);
	return dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
}

float hex_distance_reaching_half_at_edges(vec2 p) {
	p = abs(p);
	return max(p.x, dot(p, vec2(0.5, 0.8660254)));
}

float distance_to_square_lines(vec2 p, float step) {
	return min(distance_to_parallel_lines(p, vec2(1.0, 0.0), step), distance_to_parallel_lines(p, vec2(0.0, 1.0), step));
}

float distance_to_hex_edges(vec2 p) {
	return abs(0.5 - hex_distance_reaching_half_at_edges(offset_from_hex_centre(p / pitch))) * pitch;
}

void fragment() {
	if (kind == NO_GRID) discard;
	vec2 p = wpos.xz - origin;
	bool squares_divided = kind == SQUARES && subdivisions > 1;
	float heavy = kind == SQUARES ? distance_to_square_lines(p, pitch) : distance_to_hex_edges(p);
	float m = antialiased_ink(heavy, width);
	if (squares_divided) m = max(m, antialiased_ink(distance_to_square_lines(p, pitch / float(subdivisions)), width * 0.55) * 0.45);
	if (m <= 0.002) discard;
	ALBEDO = line_color.rgb;
	ALPHA = line_color.a * m;
}
