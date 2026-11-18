shader_type spatial;
render_mode cull_disabled, shadows_disabled, specular_disabled;

const float M = 0.588;
const vec3 WOOD = vec3(0.416, 0.29, 0.188);
const vec3 METAL = vec3(0.54, 0.55, 0.565);
const vec3 BRASS = vec3(0.72, 0.59, 0.35);
const vec3 LAMP = vec3(1.0, 0.82, 0.48);

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
	float phase = float(iw % 64) / 64.0;
	iw /= 64;
	float moving = float(iw % 2);
	iw /= 2;
	int ampCode = iw % 4;
	iw /= 4;
	int mode = iw % 8;
	iw /= 8;
	int arm = iw % 16;
	int whole = iw / 16;
	float legAmp = ampCode == 2 ? 0.5 : ampCode == 1 ? 0.8 : 1.0;
	float seed = fract(float(INSTANCE_ID) * 0.6180339);
	int L = int(CUSTOM0.x + 0.5);
	float pivotY = CUSTOM0.y, pivot2 = CUSTOM0.z, sw = CUSTOM0.w;
	float lean = 0.0, drop = 0.0, pitch = 0.0, gun = 0.0, gunY = 0.97, gunUp = 0.0;
	bool legv = L == 1 || L == 2 || L == 5 || L == 6 || L == 9;
	vec3 p = VERTEX;
	vec3 n = NORMAL;
	float ph = phase * TAU, s = sin(ph), c = cos(ph), amp = 0.42 * moving * legAmp;
	float hipL = -s * amp, hipR = s * amp;
	float kneeL = 0.8 * moving * legAmp * pow(max(0.0, c), 1.5), kneeR = 0.8 * moving * legAmp * pow(max(0.0, -c), 1.5);
	float idle = 1.0 - moving;
	float shL = s * 0.36 * moving + idle * 0.03 * sin(TIME * 1.1 + seed * 7.0), shR = -s * 0.36 * moving + idle * 0.03 * sin(TIME * 1.3 + seed * 5.0);
	float elL = -0.18 - 0.4 * moving * max(0.0, -shL), elR = -0.18 - 0.4 * moving * max(0.0, -shR);
	float bob = moving * (abs(c) * 0.016 - 0.008) * M;
	if (mode == 1 && moving < 0.5) { shR = -1.15 - 0.85 * max(sin(TIME * 5.5 + seed * 9.0), -0.2); elR = -0.55; }
	if (mode == 2 || mode == 4) { shR = 0.0; elR = -0.06; }
	if (mode == 3) { shL = -0.08; elL = -0.55; }
	if (mode == 4) { shL = -0.1; elL = -0.9; }
	if (mode == 5) { shR = -0.15; elR = -2.2; }
	if (mode == 6) { shL = 0.0; shR = 0.0; elL = -0.3; elR = -0.3; }
	if (whole > 0 || arm > 0) {
		float R = TIME, sd = seed * 40.0, still = 1.0 - moving;
		if (whole == 1) { hipL = -2.2; hipR = -2.05; kneeL = 2.0; kneeR = 1.9; drop = 0.74 * M; lean = -0.12; shL = -0.75; shR = -0.75; elL = -0.5; elR = -0.5; bob = 0.0; }
		else if (whole == 2) { pitch = seed > 0.5 ? 1.5708 : -1.5708; shL = -0.5; shR = 0.35; hipL = -0.2; kneeL = 0.5; hipR = 0.0; kneeR = 0.0; bob = 0.0; }
		else if (whole == 3) { hipL = -1.45; kneeL = 1.45; hipR = 0.12; kneeR = 1.5; drop = 0.44 * M; bob = 0.0; shL = -0.25; elL = -0.9; shR = -0.1; elR = -0.3; }
		else if (whole == 4) { hipL = -1.9; hipR = -1.9; kneeL = 2.1; kneeR = 2.1; drop = 0.58 * M; lean = 0.95 + 0.025 * sin(R * 23.0 + sd); shL = -2.7; shR = -2.7; elL = -1.7; elR = -1.7; bob = 0.0; }
		else if (whole == 5) {
			lean = 0.3;
			drop = 0.07 * still * M;
			hipL = mix(hipL, -0.5, still);
			kneeL = mix(kneeL, 0.45, still);
			hipR = mix(hipR, 0.42, still);
			kneeR = mix(kneeR, 0.12, still);
			if (mode == 2) { shR = 0.8; elR = -0.1; shL = -1.1; elL = -0.7; }
			else if (mode == 3) { shR = 0.2; elR = -1.0; shL = -0.7; elL = -0.5; gun = 1.0; gunY = 0.6; gunUp = 0.52; }
			else { shL = -1.2; shR = -1.2; elL = -0.6; elR = -0.6; }
		}
		else if (whole == 6) { shR = -1.15 - 0.85 * max(sin(R * 5.5 + seed * 9.0), -0.2); elR = -0.55; lean = 0.1; }
		if (arm == 1) { float k = sin(R * 6.5 + sd); shR = -1.6 + 0.9 * k; elR = -0.7 + 0.4 * sin(R * 6.5 + sd + 1.0); shL = -0.6; elL = -1.2; lean += 0.12 + 0.1 * k; hipL = mix(hipL, -0.3, still); hipR = mix(hipR, 0.25, still); }
		else if (arm == 2 && mode == 3) { shL = -1.1; elL = -0.75; shR = -0.2; elR = -2.2; gun = 1.0; gunY = 0.97; gunUp = 0.0; lean += 0.06; }
		else if (arm == 3 || arm == 2) {
			float k = fract(R * 0.55 + seed * 7.0);
			shR = k < 0.6 ? mix(-0.6, -3.0, k / 0.6) : k < 0.72 ? mix(-3.0, -0.9, (k - 0.6) / 0.12) : mix(-0.9, -0.6, (k - 0.72) / 0.28);
			elR = -0.6;
			lean += k < 0.6 ? -0.15 * k / 0.6 : 0.2 * (1.0 - (k - 0.6) / 0.4);
			shL = -0.5;
			elL = -0.6;
		}
		else if (arm == 4) { float k = sin(R * 4.0 + sd); shL = -1.35 + 0.12 * k; shR = shL; elL = -0.55 - 0.35 * k; elR = elL; lean += 0.3 + 0.06 * k; hipL = mix(hipL, -0.4, still); hipR = mix(hipR, 0.35, still); }
		else if (arm == 5) {
			float k = sin(R * 7.5 + sd);
			shR = -2.65 + 0.3 * k;
			elR = -0.5 - 0.3 * k;
			if (seed > 0.45) { shL = -2.6 - 0.3 * k; elL = -0.5 + 0.3 * k; } else { shL = -0.3; elL = -0.9; }
			bob += 0.03 * abs(sin(R * 3.7 + sd)) * still * M;
		}
		else if (arm == 6) { shL = -2.85; shR = -2.85; elL = -0.35; elR = -0.35; }
		else if (arm == 7) { if (mode != 2 && mode != 3) { shR = -0.5; elR = -1.4; shL = -0.6; elL = -1.2; } }
		else if (arm == 8) { shL = -0.75; shR = -0.75; elL = -1.25; elR = -1.25; }
	}
	if (mode == 4 && (arm == 1 || arm == 7 || arm == 4)) { shL = -0.1; elL = -0.9; }
	if (mode == 3 && (arm == 1 || arm == 4)) { shL = -0.08; elL = -0.55; }
	if (mode == 2 && arm == 1) { shR = -1.25 + 0.35 * sin(TIME * 6.5 + seed * 40.0); elR = -0.2; }
	bool gunv = gun > 0.5 && L == 10;
	if (gunv) {
		p = rx(p, -0.55, pivot2, 0.0);
		n = rn(n, -0.55);
		p = rx(p, -0.08, pivotY, 0.0);
		n = rn(n, -0.08);
		float gs = pivotY, ga = 1.839 - gunUp;
		vec3 q = p - vec3(0.237 * gs, 0.566 * gs, 0.205 * gs);
		q = rx(q, ga, 0.0, 0.0);
		n = rn(n, ga);
		q = vec3(q.x * 0.9818 + q.z * 0.1898, q.y, -q.x * 0.1898 + q.z * 0.9818);
		n = vec3(n.x * 0.9818 + n.z * 0.1898, n.y, -n.x * 0.1898 + n.z * 0.9818);
		p = q + vec3(-0.11 * gs, gunY * gs, 0.07 * M);
		L = 0;
	}
	if (L == 1 || L == 5) { if (L == 5) { p = rx(p, kneeL, pivot2, 0.0); n = rn(n, kneeL); } p = rx(p, hipL, pivotY, 0.0); n = rn(n, hipL); }
	else if (L == 2 || L == 6) { if (L == 6) { p = rx(p, kneeR, pivot2, 0.0); n = rn(n, kneeR); } p = rx(p, hipR, pivotY, 0.0); n = rn(n, hipR); }
	else if (L == 3 || L == 7 || L == 10) { if (L != 3) { p = rx(p, elL, pivot2, 0.0); n = rn(n, elL); } p = rx(p, shL, pivotY, 0.0); n = rn(n, shL); }
	else if (L == 4 || L == 8) { if (L == 8) { p = rx(p, elR, pivot2, 0.0); n = rn(n, elR); } p = rx(p, shR, pivotY, 0.0); n = rn(n, shR); }
	else if (L == 9) { float wl = clamp(0.5 + 0.5 * pivot2, 0.0, 1.0); float a = mix(hipR, hipL, wl) * 0.85 * sw; if (whole != 0) a = clamp(a, -0.4, 0.4); p = rx(p, a, pivotY, 0.0); n = rn(n, a); }
	if (gun > 0.5 && !gunv) { if (L == 3 || L == 7) p.x -= 0.85 * max(p.z, 0.0); else if (L == 4 || L == 8) p.x += 0.6 * max(p.z, 0.0); }
	p.y += bob;
	if (lean != 0.0 && !legv) { p = rx(p, lean, 0.86 * M, 0.0); n = rn(n, lean); }
	p.y -= drop;
	if (pitch != 0.0) { p = rx(p, pitch, 0.0, 0.0); n = rn(n, pitch); p.z -= sign(pitch) * 0.85 * M; p.y += 0.15 * M; }
	VERTEX = p;
	NORMAL = n;
}

void fragment() {
	float slot = UV.x;
	vec3 c = slot < 0.5 ? palette0 : slot < 1.5 ? palette1 : slot < 2.5 ? palette2 : slot < 3.5 ? palette3 : slot < 4.5 ? palette4 : slot < 5.5 ? palette5 : slot < 6.5 ? palette6
		: slot < 7.5 ? WOOD : slot < 8.5 ? METAL : slot < 9.5 ? BRASS : LAMP;
	ALBEDO = c * shade;
	ROUGHNESS = 1.0;
	if (slot > 9.5) EMISSION = LAMP * 0.6;
}
