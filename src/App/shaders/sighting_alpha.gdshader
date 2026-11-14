shader_type canvas_item;
render_mode unshaded, blend_disabled;

#include "res://shaders/sighting.gdshaderinc"

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform float ground = 0.0;
uniform vec3 near_corner;
uniform vec3 near_across;
uniform vec3 near_down;
uniform vec3 far_corner;
uniform vec3 far_across;
uniform vec3 far_down;

const vec2 SPOTS[4] = vec2[](vec2(0.125, 0.375), vec2(-0.375, 0.125), vec2(-0.125, -0.375), vec2(0.375, -0.125));

void fragment() {
	vec3 near = near_corner + near_across * SCREEN_UV.x + near_down * SCREEN_UV.y;
	vec3 ray = far_corner + far_across * SCREEN_UV.x + far_down * SCREEN_UV.y - near;
	float along = abs(ray.y) > 1e-6 ? (ground - near.y) / ray.y : -1.0;
	vec2 under = (near + ray * along).xz;
	vec2 across = dFdx(under);
	vec2 down = dFdy(under);
	float kept = 0.0;
	if (along >= 0.0) {
		for (int i = 0; i < 4; i++) {
			if (in_sight(under + across * SPOTS[i].x + down * SPOTS[i].y)) kept += 0.25;
		}
	}
	if (kept >= 1.0) discard;
	vec4 there = textureLod(screen_tex, SCREEN_UV, 0.0);
	COLOR = kept <= 0.0 ? vec4(0.0) : vec4(there.rgb, there.a * kept);
}
