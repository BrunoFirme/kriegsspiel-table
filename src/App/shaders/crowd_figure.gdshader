shader_type spatial;
render_mode cull_disabled, shadows_disabled, specular_disabled;

const float LEG_SWING = 0.55;
const float ARM_SWING = 0.45;
const float SKIRT_SWING = 0.12;

varying flat vec3 outer;
varying flat vec3 under;
varying flat vec3 hair;
varying flat vec3 skin;

vec3 unpacked(float v) {
	float r = floor(v / 65536.0);
	float g = floor((v - r * 65536.0) / 256.0);
	float b = v - r * 65536.0 - g * 256.0;
	return pow(vec3(r, g, b) / 255.0, vec3(2.2));
}

vec3 tone(float t) {
	return pow(mix(vec3(0.93, 0.78, 0.66), vec3(0.36, 0.24, 0.17), t / 7.0), vec3(2.2));
}

void vertex() {
	float w = INSTANCE_CUSTOM.w;
	float moving = step(16.0, w);
	w -= 16.0 * moving;
	float t = floor(w * 0.5);
	float phase = w - 2.0 * t;
	outer = unpacked(INSTANCE_CUSTOM.x);
	under = unpacked(INSTANCE_CUSTOM.y);
	hair = unpacked(INSTANCE_CUSTOM.z);
	skin = tone(t);
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
	vec3 c = slot < 0.5 ? outer : slot < 1.5 ? under : slot < 2.5 ? skin : slot < 3.5 ? hair : mix(under, vec3(0.95, 0.93, 0.88), 0.6);
	ALBEDO = c;
	ROUGHNESS = 1.0;
}
