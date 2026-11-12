shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D atlas : filter_linear, repeat_disable;
uniform sampler2D pages : filter_nearest, repeat_disable;
uniform float page_side = 1.0;
uniform float atlas_slots = 16.0;
uniform float tile_px = 256.0;
uniform float gutter_px = 2.0;
uniform vec3 paper = vec3(0.93, 0.9, 0.83);
uniform bool cut = false;
global uniform vec3 ground_gain;


vec3 to_linear(vec3 c) {
	c = clamp(c, vec3(0.0), vec3(1.0));
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	vec2 uv = clamp(UV, vec2(0.0), vec2(0.999999));
	vec4 e = round(texelFetch(pages, ivec2(floor(uv * page_side)), 0) * 255.0);
	vec3 c = paper;
	float a = cut ? 0.0 : 1.0;
	if (e.a > 0.5) {
		float n = exp2(e.b);
		vec2 local = fract(uv * n);
		vec2 inner = (gutter_px + local * (tile_px - 2.0 * gutter_px)) / tile_px;
		vec4 t = texture(atlas, (e.rg + inner) / atlas_slots);
		c = t.rgb;
		a = t.a;
	}
	if (cut && a < 0.5) discard;
	ALBEDO = to_linear(c) * ground_gain;
	ROUGHNESS = 1.0;
}
