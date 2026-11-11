shader_type canvas_item;
render_mode unshaded, blend_mix;

#include "res://shaders/sighting.gdshaderinc"

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float reach = 10.0;
uniform float sharp = 1.0;
uniform float most = 0.12;
uniform float doublings = 6.0;
uniform int square = 0;
uniform float ground = 0.0;
uniform float veil = 0.0;
uniform vec3 near_corner;
uniform vec3 near_across;
uniform vec3 near_down;
uniform vec3 far_corner;
uniform vec3 far_across;
uniform vec3 far_down;

const vec2 TAPS[8] = vec2[](vec2(1.0, 0.0), vec2(0.707, 0.707), vec2(0.0, 1.0), vec2(-0.707, 0.707),
	vec2(-1.0, 0.0), vec2(-0.707, -0.707), vec2(0.0, -1.0), vec2(0.707, -0.707));

float away(vec2 p) {
	vec2 d = abs(p - center);
	return square == 1 ? max(d.x, d.y) : length(p - center);
}

void fragment() {
	vec3 near = near_corner + near_across * SCREEN_UV.x + near_down * SCREEN_UV.y;
	vec3 ray = far_corner + far_across * SCREEN_UV.x + far_down * SCREEN_UV.y - near;
	float along = abs(ray.y) > 1e-6 ? (ground - near.y) / ray.y : -1.0;
	if (along < 0.0) discard;
	vec2 under = (near + ray * along).xz;
	float per_pixel = max(length(fwidth(under)) * 0.7071, 1e-6);
	if (!in_sight(under)) {
		COLOR = vec4(shade.rgb, veil);
	} else {
		float start = sharp * reach;
		float t = clamp((away(under) - start) / max(reach - start, 1e-4), 0.0, 1.0);
		if (t <= 0.0) discard;
		float blur = most * reach * (exp2(doublings * t) - 1.0) / (exp2(doublings) - 1.0);
		float px = blur / per_pixel;
		float lod = log2(max(px * 0.5, 1.0));
		vec2 spread = px * 0.5 * SCREEN_PIXEL_SIZE;
		vec3 sum = textureLod(screen_tex, SCREEN_UV, lod).rgb * 2.0;
		for (int i = 0; i < 8; i++) sum += textureLod(screen_tex, SCREEN_UV + TAPS[i] * spread, lod).rgb;
		COLOR = vec4(sum / 10.0, smoothstep(0.0, 0.15, t));
	}
}
