shader_type spatial;
render_mode cull_disabled, specular_disabled;

global uniform float units_per_metre;
global uniform vec3 table_shift;
#include "res://shaders/cloud_shade.gdshaderinc"
uniform vec2 wind = vec2(0.8, 0.3);

const float SWAY_M = 0.05;
const float REED_SWAY_M = 0.12;
const float CROP_SWAY_M = 0.1;
const float GUST_SLOW = 1.3;
const float GUST_QUICK = 2.9;
const float ROOT_SHADE = 0.42;
const float TIP_LIGHT = 1.22;
const float CROP_ROOT = 0.55;
const float CROP_TIP = 1.15;
const float GRASS_DRY = 0.35;
const float REED_DRY = 0.5;
const float CROP_DRY = 0.6;
const float UPWARD = 0.5;
const float BACKLIT = 0.3;
const vec3 DRY_TIP = vec3(1.12, 1.05, 0.8);
const float CODES = 2.0;
const float REED_CODE = 1.0;
const float CROP_CODE = 2.0;

varying vec3 tint;
varying float rise;
varying float kind;
varying float shaded;

float phase_of(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453) * 6.2832;
}

void vertex() {
	rise = UV.y;
	tint = INSTANCE_CUSTOM.rgb;
	kind = round(INSTANCE_CUSTOM.a * CODES);
	vec3 across = MODEL_MATRIX[0].xyz;
	vec3 along = MODEL_MATRIX[2].xyz;
	vec2 root = MODEL_MATRIX[3].xz;
	shaded = cloud_shadow(root + table_shift.xz);
	float phase = phase_of(floor(root * 97.0));
	float gust = sin(TIME * GUST_SLOW + dot(root, vec2(0.7, 0.5)) / max(units_per_metre, 1e-6) * 0.15 + phase) * 0.6 + sin(TIME * GUST_QUICK + phase * 1.7) * 0.4;
	float sway = kind == REED_CODE ? REED_SWAY_M : kind == CROP_CODE ? CROP_SWAY_M : SWAY_M;
	vec3 push = vec3(wind.x, 0.0, wind.y) * sway * units_per_metre * (0.4 + gust * 0.6) * UV.y * UV.y;
	VERTEX.x += dot(push, across) / max(dot(across, across), 1e-12);
	VERTEX.z += dot(push, along) / max(dot(along, along), 1e-12);
}

void fragment() {
	float root_shade = kind == CROP_CODE ? CROP_ROOT : ROOT_SHADE;
	float tip_light = kind == CROP_CODE ? CROP_TIP : TIP_LIGHT;
	float dry = kind == CROP_CODE ? CROP_DRY : kind == REED_CODE ? REED_DRY : GRASS_DRY;
	vec3 c = tint * mix(root_shade, tip_light, smoothstep(0.0, 1.0, rise));
	c = mix(c, c * DRY_TIP, smoothstep(0.55, 1.0, rise) * dry);
	ALBEDO = c * (1.0 - shaded);
	NORMAL = normalize(mix(NORMAL, (VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz, UPWARD));
	BACKLIGHT = c * BACKLIT;
	ROUGHNESS = 1.0;
}
