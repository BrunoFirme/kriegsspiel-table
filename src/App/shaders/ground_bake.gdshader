shader_type canvas_item;
render_mode blend_disabled;

#include "res://shaders/ground_head.gdshaderinc"

uniform vec4 uv_rect = vec4(0.0, 0.0, 1.0, 1.0);
uniform vec2 world_origin;
uniform vec2 world_u;
uniform vec2 world_v;
uniform float sheet_level;
uniform float normal_step = 1.0;

float raw_height(vec2 uv) {
	return textureLod(heights, (clamp(uv, vec2(0.0), vec2(1.0)) * (cells - 1.0) + 0.5) / cells, 0.0).r;
}

void fragment() {
	vec2 uv = uv_rect.xy + UV * uv_rect.zw;
	vec2 xz = world_origin + world_u * uv.x + world_v * uv.y;
	vec2 step_uv = normal_step / max(cells - 1.0, vec2(1.0));
	float dh_u = raw_height(uv + vec2(step_uv.x, 0.0)) - raw_height(uv - vec2(step_uv.x, 0.0));
	float dh_v = raw_height(uv + vec2(0.0, step_uv.y)) - raw_height(uv - vec2(0.0, step_uv.y));
	vec3 along_u = vec3(world_u.x * step_uv.x * 2.0, dh_u, world_u.y * step_uv.x * 2.0);
	vec3 along_v = vec3(world_v.x * step_uv.y * 2.0, dh_v, world_v.y * step_uv.y * 2.0);
	vec3 n = normalize(cross(along_v, along_u));
	if (n.y < 0.0) n = -n;
	float bump;
	int k;
	float there;
	vec3 c = ground_colour(uv, vec3(xz.x, sheet_level + raw_height(uv), xz.y), n, bump, k, there);
	COLOR = vec4(c * there, there);
}
