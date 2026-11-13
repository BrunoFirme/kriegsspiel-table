uniform sampler2D atlas : filter_linear, repeat_disable;
uniform sampler2D pages : filter_nearest, repeat_disable;
uniform float levels = 0.0;
uniform float atlas_slots = 16.0;
uniform float tile_px = 256.0;
uniform float gutter_px = 2.0;
global uniform vec3 ground_gain;
global uniform float heights_view;
global uniform float units_per_metre;
uniform float relief_level = 0.0;
uniform float relief_metre = 0.0;
uniform float relief_high = 0.0;

const float MOST_STRETCH = 4.0;
const int MOST_TAPS = 4;
const float SHARPER = 0.25;
const float LEAST_COVER = 1e-4;
const float HEIGHT_BANDS = 8.0;
const float HEIGHT_WASH = 0.6;
const float STEEP_TAN = 0.577;
const float STEEP_WASH = 0.72;
const vec3 STEEP = vec3(0.56, 0.16, 0.13);
const vec3 LOWEST = vec3(0.36, 0.44, 0.31);
const vec3 MIDDLE = vec3(0.72, 0.66, 0.45);
const vec3 HIGH = vec3(0.50, 0.35, 0.25);
const vec3 HIGHEST = vec3(0.86, 0.84, 0.80);

vec3 to_linear(vec3 c) {
	c = clamp(c, vec3(0.0), vec3(1.0));
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

vec4 tile_at(vec2 uv, float level, vec4 missing) {
	float side = exp2(level);
	vec4 e = round(texelFetch(pages, ivec2(floor(uv * side)) + ivec2(int(side), 0), 0) * 255.0);
	if (e.a < 0.5) return missing;
	vec2 local = fract(uv * exp2(e.b));
	vec2 inner = (gutter_px + local * (tile_px - 2.0 * gutter_px)) / tile_px;
	return textureLod(atlas, (e.rg + inner) / atlas_slots, 0.0);
}

vec4 ground_tiles(vec2 at_uv, vec4 missing) {
	vec2 uv = clamp(at_uv, vec2(0.0), vec2(0.999999));
	vec2 dx = dFdx(at_uv);
	vec2 dy = dFdy(at_uv);
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
		t += mix(tile_at(at, coarse, missing), tile_at(at, fine, missing), lod - coarse);
	}
	return t / float(taps);
}

vec3 ground_lit(vec4 t) {
	return to_linear(t.rgb / max(t.a, LEAST_COVER)) * ground_gain;
}

vec3 height_ramp(float t) {
	return t < 0.4 ? mix(LOWEST, MIDDLE, t / 0.4) : t < 0.8 ? mix(MIDDLE, HIGH, (t - 0.4) / 0.4) : mix(HIGH, HIGHEST, (t - 0.8) / 0.2);
}

vec3 heights_tint(vec3 colour, vec3 world, vec3 gain) {
	if (heights_view < 0.5 || relief_metre <= 0.0) return colour;
	float metres = (world.y - relief_level) / relief_metre;
	float band = clamp(floor(metres / max(relief_high, 0.001) * HEIGHT_BANDS), 0.0, HEIGHT_BANDS - 1.0);
	vec3 tinted = mix(colour, to_linear(height_ramp((band + 0.5) / HEIGHT_BANDS)) * gain, HEIGHT_WASH);
	vec3 facing = normalize(cross(dFdx(world), dFdy(world)));
	float up = max(abs(facing.y), 1e-4);
	float grade = sqrt(max(0.0, 1.0 - up * up)) / up * units_per_metre / relief_metre;
	return grade > STEEP_TAN ? mix(tinted, to_linear(STEEP) * gain, STEEP_WASH) : tinted;
}
