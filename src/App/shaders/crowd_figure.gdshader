shader_type spatial;
render_mode cull_disabled, shadows_disabled, specular_disabled;

const float LEG_SWING = 0.55;
const float ARM_SWING = 0.45;
const float SKIRT_SWING = 0.12;
const float RATE = 1.6;
const vec4 ARMS[15] = vec4[](vec4(0.0), vec4(18.0, 18.0, 0.0, 0.0), vec4(0.0, 0.0, 0.0, 0.0), vec4(10.0, 10.0, 0.0, 0.0), vec4(150.0, 150.0, 3.0, 0.0),
	vec4(45.0, 45.0, 0.0, 0.0), vec4(25.0, 25.0, 25.0, 0.8), vec4(35.0, 75.0, 65.0, 1.1), vec4(85.0, 80.0, 0.0, 0.0), vec4(50.0, 120.0, 70.0, 0.6),
	vec4(75.0, 75.0, 10.0, 1.2), vec4(160.0, 160.0, 14.0, 2.5), vec4(150.0, 150.0, 0.0, 0.0), vec4(40.0, 30.0, 0.0, 0.0), vec4(25.0, 25.0, 0.0, 0.0));
const vec4 LEGS[15] = vec4[](vec4(0.0), vec4(88.0, 88.0, 0.25, 0.0), vec4(0.0, 0.0, 0.0, 1.0), vec4(85.0, -10.0, 0.24, 0.0), vec4(70.0, 70.0, 0.33, 0.0),
	vec4(28.0, -18.0, 0.05, 0.0), vec4(10.0, 10.0, 0.03, 0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(10.0, -10.0, 0.0, 0.0), vec4(0.0), vec4(0.0), vec4(0.0), vec4(0.0));

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
	int act = int(floor(w / 32.0));
	w -= float(act) * 32.0;
	float moving = step(16.0, w);
	float phase = w - 16.0 * moving;
	act = clamp(act, 0, 14);
	vec4 arms = ARMS[act];
	vec4 legs = LEGS[act];
	float limb = UV2.x;
	float swing = act == 0 || act > 6 ? moving * sin(phase * TAU) : 0.0;
	float beat = arms.z * sin(TIME * TAU * arms.w * RATE * 0.4 + float(INSTANCE_ID));
	float angle = 0.0;
	if (limb > 0.5 && limb < 1.5) angle = swing * LEG_SWING - radians(legs.x);
	else if (limb > 1.5 && limb < 2.5) angle = -swing * LEG_SWING - radians(legs.y);
	else if (limb > 2.5 && limb < 3.5) angle = -swing * ARM_SWING - radians(arms.x + (act == 7 || act == 9 ? 0.0 : beat));
	else if (limb > 3.5 && limb < 4.5) angle = swing * ARM_SWING - radians(arms.y + beat);
	else if (limb > 4.5) angle = abs(swing) * SKIRT_SWING;
	if (angle != 0.0) {
		float c = cos(angle), s = sin(angle);
		vec2 yz = VERTEX.yz - vec2(UV2.y, 0.0);
		VERTEX.yz = vec2(yz.x * c - yz.y * s, yz.x * s + yz.y * c) + vec2(UV2.y, 0.0);
		NORMAL.yz = vec2(NORMAL.y * c - NORMAL.z * s, NORMAL.y * s + NORMAL.z * c);
	}
	VERTEX.y -= legs.z;
	if (legs.w > 0.5) {
		VERTEX.yz = vec2(VERTEX.z + 0.08, -VERTEX.y);
		NORMAL.yz = vec2(NORMAL.z, -NORMAL.y);
	}
}

void fragment() {
	float slot = UV.x;
	vec3 c = slot < 0.5 ? palette0 : slot < 1.5 ? palette1 : slot < 2.5 ? palette2 : slot < 3.5 ? palette3 : slot < 4.5 ? palette4 : slot < 5.5 ? palette5 : palette6;
	ALBEDO = c;
	ROUGHNESS = 1.0;
}
