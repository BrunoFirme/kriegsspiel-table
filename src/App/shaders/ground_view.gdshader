shader_type spatial;
render_mode cull_disabled, specular_disabled;

uniform sampler2D atlas : filter_linear, repeat_disable;
uniform sampler2D pages : filter_nearest, repeat_disable;
uniform float levels = 0.0;
uniform float atlas_slots = 16.0;
uniform float tile_px = 256.0;
uniform float gutter_px = 2.0;
uniform vec3 paper = vec3(0.93, 0.9, 0.83);
uniform bool cut = false;
global uniform vec3 ground_gain;

const float MOST_STRETCH = 4.0;
const int MOST_TAPS = 4;
const float SHARPER = 0.25;

vec3 to_linear(vec3 c) {
	c = clamp(c, vec3(0.0), vec3(1.0));
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

vec4 tile_at(vec2 uv, float level) {
	float side = exp2(level);
	vec4 e = round(texelFetch(pages, ivec2(floor(uv * side)) + ivec2(int(side), 0), 0) * 255.0);
	if (e.a < 0.5) return vec4(paper, cut ? 0.0 : 1.0);
	vec2 local = fract(uv * exp2(e.b));
	vec2 inner = (gutter_px + local * (tile_px - 2.0 * gutter_px)) / tile_px;
	return textureLod(atlas, (e.rg + inner) / atlas_slots, 0.0);
}

void fragment() {
	vec2 uv = clamp(UV, vec2(0.0), vec2(0.999999));
	vec2 dx = dFdx(UV);
	vec2 dy = dFdy(UV);
	float lx = length(dx);
	float ly = length(dy);
	vec2 along = lx > ly ? dx : dy;
	float major = max(max(lx, ly), 1e-9);
	float stretch = clamp(major / max(min(lx, ly), 1e-9), 1.0, MOST_STRETCH);
	float lod = clamp(SHARPER - log2(major / stretch * (tile_px - 2.0 * gutter_px)), 0.0, levels);
	float coarse = floor(lod);
	float fine = min(coarse + 1.0, levels);
	int taps = int(ceil(stretch));
	vec4 t = vec4(0.0);
	for (int i = 0; i < MOST_TAPS; i++) {
		if (i >= taps) break;
		vec2 at = clamp(uv + along * ((float(i) + 0.5) / float(taps) - 0.5), vec2(0.0), vec2(0.999999));
		t += mix(tile_at(at, coarse), tile_at(at, fine), lod - coarse);
	}
	t /= float(taps);
	if (cut && t.a < 0.5) discard;
	ALBEDO = to_linear(t.rgb) * ground_gain;
	ROUGHNESS = 1.0;
}
