#include "res://shaders/ground_pages.gdshaderinc"
#include "res://shaders/ground_base.gdshaderinc"
#include "res://shaders/ground_cells.gdshaderinc"
#include "res://shaders/cloud_shade.gdshaderinc"

global uniform float heights_view;
global uniform float ground_relaxed;
global uniform float ground_relief;
global uniform vec4 sun_glint;
uniform float relief_level = 0.0;
uniform float relief_metre = 0.0;
uniform float relief_high = 0.0;
uniform vec2 sheet_size = vec2(1.0);

const float MOST_STRETCH = 4.0;
const int MOST_TAPS = 4;
const float SHARPER = 0.25;
const float LEAST_COVER = 1e-4;
const float RELIEF_DEPTH_M = 0.07;
const float RELIEF_STEP_M = 0.015;
const float RELIEF_FULL_M = 0.02;
const float RELIEF_GONE_M = 0.08;
const float RELIEF_MOST = 1.2;
const vec3 LUMA_OF = vec3(0.299, 0.587, 0.114);
const float WATER_WAVE_M = 0.7;
const float WATER_DRIFT_M = 0.35;
const float WATER_TILT = 0.09;
const float WATER_STILL_FROM_M = 30.0;
const float WATER_STILL_M = 120.0;
const float WATER_ROUGH = 0.1;
const float WATER_BLUR_ROUGH = 0.35;
const float WATER_SPECULAR = 0.2;
const float WATER_PIXEL_FROM = 0.2;
const float WATER_PIXEL_GONE = 0.6;
const float WATER_DARK = 0.55;
const float WATER_BANK_M = 1.2;
const float GLINT_POWER = 380.0;
const float GLINT = 1.4;
const vec3 GLINT_TINT = vec3(1.0, 0.95, 0.85);
const float HEIGHT_BANDS = 8.0;
const float HEIGHT_WASH = 0.6;
const float STEEP_TAN = 0.577;
const float STEEP_WASH = 0.72;
const vec3 STEEP = vec3(0.56, 0.16, 0.13);
const vec3 LOWEST = vec3(0.36, 0.44, 0.31);
const vec3 MIDDLE = vec3(0.72, 0.66, 0.45);
const vec3 HIGH = vec3(0.50, 0.35, 0.25);
const vec3 HIGHEST = vec3(0.86, 0.84, 0.80);

vec4 ground_tiles(vec2 at_uv, vec4 missing, out float level) {
	vec2 uv = clamp(at_uv, vec2(0.0), vec2(0.999999));
	vec2 dx = dFdx(at_uv);
	vec2 dy = dFdy(at_uv);
	float lx = length(dx);
	float ly = length(dy);
	vec2 along = lx > ly ? dx : dy;
	float major = max(max(lx, ly), 1e-9);
	float ratio = major / max(min(lx, ly), 1e-9);
	float stretch = min(min(ratio, MOST_STRETCH), sqrt(MOST_STRETCH * ratio) * exp2(-ground_relaxed));
	float lod = clamp(SHARPER - log2(major / stretch * (tile_px - 2.0 * gutter_px)), 0.0, tile_levels);
	level = lod;
	float coarse = floor(lod);
	float fine = min(coarse + 1.0, tile_levels);
	int taps = int(ceil(max(stretch, 1.0)));
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

float paint_height(vec4 t) {
	return dot(t.rgb / max(t.a, LEAST_COVER), LUMA_OF);
}

vec3 ground_relief_normal(vec2 uv, float level, vec3 n, vec3 along_u, vec3 along_v) {
	if (ground_relief <= 0.0 || units_per_metre <= 0.0) return n;
	float baked = baked_level(uv, min(floor(level) + 1.0, tile_levels));
	if (baked < 0.0) return n;
	vec2 texel = vec2(1.0 / (exp2(baked) * (tile_px - 2.0 * gutter_px)));
	vec2 texel_m = texel * sheet_size / units_per_metre;
	float strength = ground_relief * (1.0 - smoothstep(RELIEF_FULL_M, RELIEF_GONE_M, max(texel_m.x, texel_m.y)));
	if (strength <= 0.0) return n;
	vec2 step_m = max(texel_m, vec2(RELIEF_STEP_M));
	vec2 step_uv = step_m * units_per_metre / sheet_size;
	vec4 here = tile_at(uv, baked, vec4(0.0));
	vec4 across = tile_at(uv + vec2(step_uv.x, 0.0), baked, vec4(0.0));
	vec4 down = tile_at(uv + vec2(0.0, step_uv.y), baked, vec4(0.0));
	if (min(here.a, min(across.a, down.a)) < 0.5) return n;
	float h = paint_height(here);
	vec2 slope = clamp(vec2(paint_height(across) - h, paint_height(down) - h) * RELIEF_DEPTH_M / step_m, -RELIEF_MOST, RELIEF_MOST) * strength;
	vec3 u = normalize(along_u - n * dot(along_u, n));
	vec3 v = normalize(along_v - n * dot(along_v, n));
	return normalize(n - slope.x * u - slope.y * v);
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

vec3 along_uv(vec3 vertex, vec3 n, vec2 uv, out vec3 along_v) {
	vec3 dp1 = dFdx(vertex);
	vec3 dp2 = dFdy(vertex);
	vec2 du1 = dFdx(uv);
	vec2 du2 = dFdy(uv);
	vec3 dp2perp = cross(dp2, n);
	vec3 dp1perp = cross(n, dp1);
	along_v = dp2perp * du1.y + dp1perp * du2.y;
	return dp2perp * du1.x + dp1perp * du2.x;
}

float water_at(vec2 uv) {
	if (ground_relief <= 0.0 || units_per_metre <= 0.0) return 0.0;
	vec2 g = clamp(uv, vec2(0.0), vec2(1.0)) * cells - 0.5;
	vec2 base = floor(g);
	int wet = int(kind_of(base) == WATER) + int(kind_of(base + vec2(1.0, 0.0)) == WATER) + int(kind_of(base + vec2(0.0, 1.0)) == WATER) + int(kind_of(base + vec2(1.0)) == WATER);
	if (wet == 0) return 0.0;
	if (wet == 4) return 1.0;
	float border;
	float margin;
	int beside;
	if (kind_at(uv, border, margin, beside) != WATER) return 0.0;
	return smoothstep(0.0, WATER_BANK_M * units_per_metre, border + cell_world * 0.5);
}

float ripple_seen(vec2 xz, float dist) {
	vec2 q = xz / (WATER_WAVE_M * units_per_metre);
	float pixel = max(length(dFdx(q)), length(dFdy(q)));
	return (1.0 - smoothstep(WATER_STILL_FROM_M, WATER_STILL_M, dist / units_per_metre)) * (1.0 - smoothstep(WATER_PIXEL_FROM, WATER_PIXEL_GONE, pixel));
}

float ripple_height(vec2 xz, float seen) {
	vec2 q = xz / (WATER_WAVE_M * units_per_metre);
	vec2 drift = vec2(TIME * WATER_DRIFT_M / WATER_WAVE_M, TIME * WATER_DRIFT_M * 0.6 / WATER_WAVE_M);
	return (noise(q + drift) + 0.5 * noise(q * 2.3 - drift * 1.7)) * WATER_TILT * WATER_WAVE_M * units_per_metre * seen;
}

vec3 bumped(vec3 n, vec3 vertex, float bump) {
	vec3 dx = dFdx(vertex);
	vec3 dy = dFdy(vertex);
	vec3 r1 = cross(dy, n);
	vec3 r2 = cross(n, dx);
	float det = dot(dx, r1);
	vec3 grad = sign(det) * (dFdx(bump) * r1 + dFdy(bump) * r2);
	return normalize(n - grad / max(abs(det), 1e-12));
}

vec3 water_glint(vec3 n, vec3 view_pos, mat4 view_matrix) {
	if (sun_glint.w <= 0.0) return vec3(0.0);
	vec3 l = normalize((view_matrix * vec4(sun_glint.xyz, 0.0)).xyz);
	vec3 r = reflect(normalize(view_pos), n);
	return GLINT_TINT * pow(max(dot(r, l), 0.0), GLINT_POWER) * GLINT * sun_glint.w;
}
