shader_type spatial;
render_mode cull_disabled, shadows_disabled, specular_disabled;

const vec3 WOOD = vec3(0.416, 0.29, 0.188);
const vec3 METAL = vec3(0.54, 0.55, 0.565);
const vec3 BRASS = vec3(0.72, 0.59, 0.35);
const vec3 LAMP = vec3(1.0, 0.82, 0.48);

uniform float lit = 0.0;

varying flat vec3 palette0;
varying flat vec3 palette1;
varying flat vec3 palette2;
varying flat vec3 palette3;
varying flat vec3 palette4;
varying flat vec3 palette5;
varying flat vec3 palette6;
varying float shade;

vec3 unpacked(float v) {
	float r = floor(v / 65536.0);
	float g = floor((v - r * 65536.0) / 256.0);
	float b = v - r * 65536.0 - g * 256.0;
	return pow(vec3(r, g, b) / 255.0, vec3(2.2));
}

vec3 rx(vec3 p, float a, float py, float pz) {
	p.y -= py;
	p.z -= pz;
	float c = cos(a), s = sin(a);
	p = vec3(p.x, p.y * c - p.z * s, p.y * s + p.z * c);
	p.y += py;
	p.z += pz;
	return p;
}

vec3 rn(vec3 n, float a) {
	float c = cos(a), s = sin(a);
	return vec3(n.x, n.y * c - n.z * s, n.y * s + n.z * c);
}

void vertex() {
	palette0 = unpacked(COLOR.r);
	palette1 = unpacked(COLOR.g);
	palette2 = unpacked(COLOR.b);
	palette3 = unpacked(COLOR.a);
	palette4 = unpacked(INSTANCE_CUSTOM.x);
	palette5 = unpacked(INSTANCE_CUSTOM.y);
	palette6 = unpacked(INSTANCE_CUSTOM.z);
	shade = UV.y;
	int iw = int(INSTANCE_CUSTOM.w + 0.5);
	int L = int(CUSTOM0.x + 0.5);
	float pivotY = CUSTOM0.y, pivot2 = CUSTOM0.z, pivotZ = CUSTOM0.w;
	vec3 p = VERTEX;
	vec3 n = NORMAL;
	if (L == 20) {
		float a = float(iw % 4096) * 0.02 / max(pivot2, 0.05);
		p = rx(p, a, pivotY, pivotZ);
		n = rn(n, a);
	} else if (L >= 11 && L <= 19) {
		float phase = float(iw % 64) / 64.0;
		int flags = iw / 64;
		float moving = float(flags % 2), trot = float((flags / 2) % 2);
		int leg = L == 19 ? -1 : (L - 11) % 4;
		float off = leg == 0 ? (trot > 0.5 ? 0.0 : 0.25) : leg == 1 ? (trot > 0.5 ? 0.5 : 0.75) : leg == 2 ? (trot > 0.5 ? 0.5 : 0.0) : (trot > 0.5 ? 0.0 : 0.5);
		float s = sin((phase + off) * TAU), c = cos((phase + off) * TAU);
		float amp = moving * (trot > 0.5 ? 0.5 : 0.36);
		if (L == 19) {
			float a = moving * 0.05 * sin(phase * TAU * 2.0) + (1.0 - moving) * 0.03 * sin(TIME * 0.7 + float(INSTANCE_ID));
			p = rx(p, a, pivotY, pivotZ);
			n = rn(n, a);
		} else {
			if (L >= 15) {
				float k = moving * (leg < 2 ? -0.7 : 0.7) * max(0.0, c);
				p = rx(p, k, pivot2, pivotZ);
				n = rn(n, k);
			}
			p = rx(p, s * amp, pivotY, pivotZ);
			n = rn(n, s * amp);
		}
	}
	VERTEX = p;
	NORMAL = n;
}

void fragment() {
	float slot = UV.x;
	vec3 c = slot < 0.5 ? palette0 : slot < 1.5 ? palette1 : slot < 2.5 ? palette2 : slot < 3.5 ? palette3 : slot < 4.5 ? palette4 : slot < 5.5 ? palette5 : slot < 6.5 ? palette6
		: slot < 7.5 ? WOOD : slot < 8.5 ? METAL : slot < 9.5 ? BRASS : LAMP;
	ALBEDO = c * shade;
	ROUGHNESS = 1.0;
	if (slot > 9.5) EMISSION = LAMP * (0.15 + 0.85 * lit);
}
