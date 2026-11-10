shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, blend_mix, shadows_disabled;

#include "res://shaders/sighting.gdshaderinc"

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float reach = 10.0;
uniform float sharp = 1.0;
uniform float most = 0.12;
uniform float bend = 1.8;
uniform int square = 0;

const vec2 TAPS[8] = vec2[](vec2(1.0, 0.0), vec2(0.707, 0.707), vec2(0.0, 1.0), vec2(-0.707, 0.707),
	vec2(-1.0, 0.0), vec2(-0.707, -0.707), vec2(0.0, -1.0), vec2(0.707, -0.707));

float away(vec2 p) {
	vec2 d = abs(p - center);
	return square == 1 ? max(d.x, d.y) : length(p - center);
}

void fragment() {
	float start = sharp * reach;
	float t = clamp((away(wpos.xz) - start) / max(reach - start, 1e-4), 0.0, 1.0);
	if (t <= 0.0 || !in_sight(wpos.xz)) discard;
	float blur = most * reach * pow(t, bend);
	float per_pixel = max(length(fwidth(wpos.xz)) * 0.7071, 1e-6);
	float px = blur / per_pixel;
	float lod = log2(max(px * 0.5, 1.0));
	vec2 spread = px * 0.5 / VIEWPORT_SIZE;
	vec3 sum = textureLod(screen_tex, SCREEN_UV, lod).rgb * 2.0;
	for (int i = 0; i < 8; i++) sum += textureLod(screen_tex, SCREEN_UV + TAPS[i] * spread, lod).rgb;
	ALBEDO = sum / 10.0;
	ALPHA = smoothstep(0.0, 0.15, t);
}
