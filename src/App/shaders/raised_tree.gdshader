shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform float ground_top = 0.0;
uniform vec2 wind = vec2(0.0);
global uniform float snow_line;
global uniform float units_per_metre;
global uniform vec3 table_shift;
#include "res://shaders/cloud_shade.gdshaderinc"

varying vec3 local;
varying float height;
varying float upward;
varying float leafy;
varying vec2 world_xz;

const float BARK_FLAG = 0.75;
const float TREE_TALL = 3.0;
const float SWAY_M = 0.14;
const float FLUTTER_M = 0.03;
const float SWAY_SLOW = 0.9;
const float SWAY_QUICK = 2.3;
const float FLUTTER_RATE = 6.0;
const float UNDER_LEAF = 0.6;
const float LEAF_BACKLIT = 0.35;

vec3 linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

float hash(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

float noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(hash(i), hash(i + vec3(1, 0, 0)), u.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), u.x), u.y);
	float b = mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), u.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), u.x), u.y);
	return mix(a, b, u.z);
}

void vertex() {
	local = VERTEX * 3.0;
	leafy = step(BARK_FLAG, COLOR.a);
	if (dot(wind, wind) > 0.0) {
		vec3 across = MODEL_MATRIX[0].xyz;
		vec3 along = MODEL_MATRIX[2].xyz;
		vec2 root = MODEL_MATRIX[3].xz;
		float phase = fract(sin(dot(floor(root * 37.0), vec2(12.9898, 78.233))) * 43758.5453) * 6.2832;
		float bend = pow(max(VERTEX.y, 0.0) / TREE_TALL, 2.0);
		float gust = sin(TIME * SWAY_SLOW + phase) * 0.7 + sin(TIME * SWAY_QUICK + phase * 1.3) * 0.3;
		vec3 push = vec3(wind.x, 0.0, wind.y) * units_per_metre * (SWAY_M * bend * (0.5 + 0.5 * gust) + leafy * FLUTTER_M * sin(TIME * FLUTTER_RATE + dot(VERTEX, vec3(7.0, 5.0, 3.0)) + phase));
		VERTEX.x += dot(push, across) / max(dot(across, across), 1e-12);
		VERTEX.z += dot(push, along) / max(dot(along, along), 1e-12);
	}
	height = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y - ground_top;
	upward = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz).y;
	world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz + table_shift.xz;
}

void fragment() {
	vec3 c;
	if (leafy > 0.5) {
		float leaf = 0.78 + 0.34 * noise(local) + 0.12 * noise(local * 3.7);
		c = COLOR.rgb * leaf * mix(UNDER_LEAF, 1.0, smoothstep(-0.7, 0.5, upward));
	} else {
		float around = atan(local.z, local.x);
		float furrow = noise(vec3(around * 5.0, local.y * 0.35, 0.0));
		c = COLOR.rgb * (0.72 + 0.4 * furrow) * (0.92 + 0.12 * noise(local * 6.0));
	}
	if (snow_line > 0.0) c = mix(c, vec3(0.86, 0.88, 0.92), smoothstep(snow_line * 0.9, snow_line * 1.1, height) * smoothstep(0.2, 0.6, upward) * step(0.5, noise(local * 0.7)) * 0.85);
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
		c *= 0.5;
	}
	ALBEDO = linear(clamp(c, 0.0, 1.0)) * (1.0 - cloud_shadow(world_xz));
	BACKLIGHT = ALBEDO * LEAF_BACKLIT * leafy;
	ROUGHNESS = 0.95;
	SPECULAR = 0.2;
}
