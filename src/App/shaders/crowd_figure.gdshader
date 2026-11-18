shader_type spatial;
render_mode cull_disabled, shadows_disabled, specular_disabled;

const float LEG_SWING = 0.55;
const float ARM_SWING = 0.45;
const float SKIRT_SWING = 0.12;

varying flat vec3 palette0;
varying flat vec3 palette1;
varying flat vec3 palette2;
varying flat vec3 palette3;
varying flat vec3 palette4;
varying flat vec3 palette5;
varying flat vec3 palette6;

vec3 unpacked(float v) {
	float r = floor(v / 65536.0);
	float g = floor((v - r * 65536.0) / 256.0);
	float b = v - r * 65536.0 - g * 256.0;
	return pow(vec3(r, g, b) / 255.0, vec3(2.2));
}

void vertex() {
	palette0 = unpacked(COLOR.r);
	palette1 = unpacked(COLOR.g);
	palette2 = unpacked(COLOR.b);
	palette3 = unpacked(COLOR.a);
	palette4 = unpacked(INSTANCE_CUSTOM.x);
	palette5 = unpacked(INSTANCE_CUSTOM.y);
	palette6 = unpacked(INSTANCE_CUSTOM.z);
	float w = INSTANCE_CUSTOM.w;
	float moving = step(16.0, w);
	float phase = w - 16.0 * moving;
	float limb = UV2.x;
	float swing = moving * sin(phase * TAU);
	float angle = 0.0;
	if (limb > 0.5 && limb < 1.5) angle = swing * LEG_SWING;
	else if (limb > 1.5 && limb < 2.5) angle = -swing * LEG_SWING;
	else if (limb > 2.5 && limb < 3.5) angle = -swing * ARM_SWING;
	else if (limb > 3.5 && limb < 4.5) angle = swing * ARM_SWING;
	else if (limb > 4.5) angle = abs(swing) * SKIRT_SWING;
	if (angle != 0.0) {
		float c = cos(angle), s = sin(angle);
		vec2 yz = VERTEX.yz - vec2(UV2.y, 0.0);
		VERTEX.yz = vec2(yz.x * c - yz.y * s, yz.x * s + yz.y * c) + vec2(UV2.y, 0.0);
		NORMAL.yz = vec2(NORMAL.y * c - NORMAL.z * s, NORMAL.y * s + NORMAL.z * c);
	}
}

void fragment() {
	float slot = UV.x;
	vec3 c = slot < 0.5 ? palette0 : slot < 1.5 ? palette1 : slot < 2.5 ? palette2 : slot < 3.5 ? palette3 : slot < 4.5 ? palette4 : slot < 5.5 ? palette5 : palette6;
	ALBEDO = c;
	ROUGHNESS = 1.0;
}
