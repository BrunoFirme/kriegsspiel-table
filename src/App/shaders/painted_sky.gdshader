shader_type sky;

uniform int kind = 1;
uniform float reflected = 1.0;
uniform vec3 sun_at = vec3(0.0, 1.0, 0.0);
uniform vec3 moon_at = vec3(0.0, -1.0, 0.0);
uniform vec3 pole = vec3(0.0, 0.7, -0.7);
uniform float dark = 0.0;
uniform float glow = 0.0;
uniform float turn = 0.0;
uniform float moonlight = 0.0;

const int BLOCKY = 2;
const int AURORA = 4;
const int TRUE = 5;

vec3 paint(float r, float g, float b) {
	return pow(vec3(r, g, b) / 255.0, vec3(2.2));
}

float salt(vec3 p) {
	uvec3 q = uvec3(ivec3(floor(p))) * uvec3(1597334673u, 3812015801u, 2798796415u);
	uint n = (q.x ^ q.y ^ q.z) * 1597334673u;
	n ^= n >> 15u;
	n *= 2246822519u;
	return float(n >> 8u) / 16777216.0;
}

float grain(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	float a = mix(mix(salt(i), salt(i + vec3(1.0, 0.0, 0.0)), f.x), mix(salt(i + vec3(0.0, 1.0, 0.0)), salt(i + vec3(1.0, 1.0, 0.0)), f.x), f.y);
	float b = mix(mix(salt(i + vec3(0.0, 0.0, 1.0)), salt(i + vec3(1.0, 0.0, 1.0)), f.x), mix(salt(i + vec3(0.0, 1.0, 1.0)), salt(i + vec3(1.0, 1.0, 1.0)), f.x), f.y);
	return mix(a, b, f.z);
}

float billow(vec3 p, int octaves) {
	float sum = 0.0;
	float gain = 0.5;
	for (int i = 0; i < octaves; i++) {
		sum += gain * grain(p);
		p = p * 2.03 + vec3(11.7, 3.1, 5.3);
		gain *= 0.5;
	}
	return sum;
}

vec3 on_face(vec3 d) {
	vec3 a = abs(d);
	if (a.y >= a.x && a.y >= a.z) return vec3(d.x / a.y, d.z / a.y, d.y > 0.0 ? 0.0 : 1.0);
	if (a.x >= a.z) return vec3(d.z / a.x, d.y / a.x, d.x > 0.0 ? 2.0 : 3.0);
	return vec3(d.x / a.z, d.y / a.z, d.z > 0.0 ? 4.0 : 5.0);
}

vec3 star_tint(float pick) {
	if (pick < 0.62) return paint(240.0, 238.0, 226.0);
	if (pick < 0.86) return paint(176.0, 204.0, 244.0);
	if (pick < 0.96) return paint(248.0, 214.0, 158.0);
	return paint(236.0, 170.0, 150.0);
}

vec3 turned(vec3 d, float by) {
	float c = cos(by);
	return d * c + cross(pole, d) * sin(by) + pole * dot(pole, d) * (1.0 - c);
}

vec3 off_face(vec2 uv, float face) {
	if (face < 0.5) return vec3(uv.x, 1.0, uv.y);
	if (face < 1.5) return vec3(uv.x, -1.0, uv.y);
	if (face < 2.5) return vec3(1.0, uv.y, uv.x);
	if (face < 3.5) return vec3(-1.0, uv.y, uv.x);
	if (face < 4.5) return vec3(uv.x, uv.y, 1.0);
	return vec3(uv.x, uv.y, -1.0);
}

float true_light(float rank) {
	return min(1.4, 0.2 * inversesqrt(1.002 - rank));
}

vec3 stars(vec3 d, float cells, float share, float size, bool blocky, float real, float wash) {
	vec3 f = on_face(turned(d, turn));
	vec2 cell = floor((f.xy * 0.5 + 0.5) * cells);
	vec3 key = vec3(cell, f.z * 17.0 + cells);
	if (salt(key) > share) return vec3(0.0);
	vec2 at = 0.3 + 0.4 * vec2(salt(key + 7.0), salt(key + 13.0));
	float rank = salt(key + 21.0);
	vec3 star = turned(normalize(off_face((cell + at) / cells * 2.0 - 1.0, f.z)), -turn);
	vec3 side = normalize(cross(vec3(0.0, 1.0, 0.0), star) + vec3(0.00001, 0.0, 0.0));
	vec3 off = d - star;
	vec2 q = abs(vec2(dot(off, side), dot(off, cross(star, side))));
	float edge = length(fwidth(d)) * 0.6;
	float lit;
	if (blocky) {
		float half_side = size * (rank > 0.93 ? 2.6 : rank > 0.7 ? 1.7 : 1.0);
		lit = 1.0 - smoothstep(half_side - edge, half_side + edge, max(q.x, q.y));
		lit *= max(mix(0.3 + 0.7 * rank * rank, true_light(rank), real) - wash, 0.0);
	} else {
		float r = max(size * (0.4 + 1.6 * rank * rank * rank), edge * 0.9);
		lit = (1.0 - smoothstep(r * 0.5, r + edge, length(q))) * max(mix(0.3 + 0.7 * rank * rank, true_light(rank), real) - wash, 0.0) * min(1.0, r / max(edge, 0.000001));
	}
	vec3 tint = star_tint(salt(key + 3.0));
	if (kind == TRUE) return mix(vec3(0.9), tint, 0.3) * lit;
	return tint * lit;
}

vec3 banded(vec3 low, vec3 mid, vec3 high, float rise, vec3 d) {
	float wobble = (billow(d * vec3(3.0, 9.0, 3.0), 3) - 0.44) * 0.16;
	float t = clamp(rise + wobble * (1.0 - rise), 0.0, 1.0);
	float steps = 9.0;
	float held = (floor(t * steps) + smoothstep(0.35, 0.65, fract(t * steps))) / steps;
	t = mix(t, held, 0.6);
	return t < 0.35 ? mix(low, mid, t / 0.35) : mix(mid, high, pow((t - 0.35) / 0.65, 0.8));
}

vec4 clouds(vec3 d, float cover, vec3 top, vec3 body, vec3 under, vec3 towards, float flatness, float fine) {
	if (d.y < -0.02) return vec4(0.0);
	vec3 p = vec3(d.x, d.y * flatness, d.z) / (0.42 + d.y) * fine;
	float mass = billow(p + vec3(4.0, 0.0, 9.0), 5);
	float sunward = billow(p + towards * 0.22 + vec3(4.0, 0.0, 9.0), 4);
	float there = smoothstep(cover, cover + 0.012, mass) * smoothstep(-0.02, 0.07, d.y);
	float lit = smoothstep(0.016, 0.03, mass - sunward);
	float deep = smoothstep(cover + 0.09, cover + 0.11, mass);
	vec3 tone = mix(mix(body, top, lit), under, deep * (1.0 - lit) * 0.8);
	return vec4(tone, there);
}

vec3 ribbons(vec3 d) {
	if (d.y < 0.02) return vec3(0.0);
	vec2 around = normalize(d.xz + vec2(0.00001, 0.0));
	float height = asin(clamp(d.y, 0.0, 1.0));
	vec3 sum = vec3(0.0);
	for (int i = 0; i < 2; i++) {
		float k = float(i);
		float foot = 0.2 + k * 0.17 + (billow(vec3(around * (1.3 + k * 0.9), 3.0 + k * 7.0), 3) - 0.45) * 0.8;
		float there = smoothstep(0.35, 0.5, billow(vec3(around * 0.9, 20.0 + k * 5.0), 2));
		float rays = (0.5 + 0.5 * grain(vec3(around * 26.0, k))) * (0.6 + 0.4 * grain(vec3(around * 70.0, k + 3.0)));
		float tall = 0.2 + 0.26 * rays;
		float over = height - foot;
		float shine = smoothstep(-0.012, 0.012, over) * exp(-max(over, 0.0) / tall * 2.2) * there * (0.4 + 0.6 * rays);
		shine = mix(shine, floor(shine * 6.0 + 0.5) / 6.0, 0.4);
		vec3 tone = mix(mix(paint(70.0, 224.0, 150.0), paint(40.0, 170.0, 176.0), smoothstep(0.0, tall * 0.5, over)), paint(150.0, 90.0, 210.0), smoothstep(tall * 0.35, tall, over));
		sum += tone * shine;
	}
	return sum * 0.7;
}

vec4 moon(vec3 d, vec3 from, float size) {
	float off = acos(clamp(dot(d, from), -1.0, 1.0));
	float edge = fwidth(off);
	float disc = 1.0 - smoothstep(size - edge, size + edge, off);
	if (disc <= 0.0) return vec4(0.0);
	vec3 side = normalize(cross(from, vec3(0.0, 1.0, 0.0)) + vec3(0.00001, 0.0, 0.0));
	vec3 up = cross(side, from);
	vec2 q = vec2(dot(d, side), dot(d, up)) / size;
	float seas = smoothstep(0.5, 0.56, billow(vec3(q * 1.7, 4.0), 3));
	vec3 face = mix(paint(236.0, 230.0, 206.0), paint(188.0, 190.0, 184.0), seas * 0.8);
	return vec4(face, disc);
}

vec3 sun(vec3 d, vec3 from, vec3 tone, float size) {
	float off = acos(clamp(dot(d, from), -1.0, 1.0));
	float edge = fwidth(off);
	float disc = 1.0 - smoothstep(size - edge, size + edge, off);
	float ring = (1.0 - smoothstep(size * 1.5, size * 2.6, off)) * 0.22;
	return tone * (disc + ring) + tone * 0.35 * exp(-off * 5.0);
}

void sky() {
	vec3 d = normalize(EYEDIR);
	float rise = clamp(d.y, 0.0, 1.0);
	vec3 night_low = paint(34.0, 56.0, 96.0);
	vec3 night_mid = paint(16.0, 28.0, 64.0);
	vec3 night_high = paint(8.0, 13.0, 36.0);
	if (kind == AURORA) {
		night_low = paint(22.0, 58.0, 70.0);
		night_mid = paint(12.0, 30.0, 52.0);
		night_high = paint(6.0, 12.0, 30.0);
	} else if (kind == TRUE) {
		night_low = mix(paint(13.0, 17.0, 26.0), paint(40.0, 54.0, 82.0), moonlight);
		night_mid = mix(paint(6.0, 8.0, 14.0), paint(24.0, 34.0, 60.0), moonlight);
		night_high = mix(paint(2.0, 3.0, 6.0), paint(13.0, 19.0, 38.0), moonlight);
	}
	vec3 sunward = normalize(vec3(sun_at.x, 0.0, sun_at.z) + vec3(0.0001, 0.0, 0.0));
	float facing = clamp(dot(normalize(vec3(d.x, 0.0, d.z) + vec3(0.0001, 0.0, 0.0)), sunward) * 0.5 + 0.5, 0.0, 1.0);
	facing = pow(facing, mix(4.0, 1.5, smoothstep(-0.2, 0.05, sun_at.y)));
	vec3 low = mix(mix(paint(206.0, 214.0, 204.0), night_low, dark), mix(paint(150.0, 96.0, 110.0), paint(236.0, 150.0, 70.0), facing), glow * mix(0.5, 1.0, facing));
	vec3 mid = mix(mix(paint(120.0, 164.0, 200.0), night_mid, dark), mix(paint(84.0, 70.0, 116.0), paint(168.0, 86.0, 104.0), facing), glow * 0.85 * mix(0.4, 1.0, facing));
	vec3 high = mix(mix(paint(38.0, 84.0, 150.0), night_high, dark), paint(28.0, 40.0, 92.0), glow * 0.6);
	vec3 seen = banded(low, mid, high, rise, d);
	bool close = !AT_CUBEMAP_PASS && d.y > -0.01;
	if (dark > 0.002 && close) {
		vec3 fixed_stars = turned(d, turn);
		float real = kind == TRUE ? 1.0 : 0.75;
		float wash = moonlight * (kind == TRUE ? 0.6 : 0.22);
		float clear = mix(smoothstep(0.0, 0.16, d.y), smoothstep(0.0, 0.26, d.y), real) * dark;
		vec3 axis = normalize(vec3(0.55, 0.62, 0.56));
		float lane = exp(-pow(dot(fixed_stars, axis) * 3.4, 2.0));
		if (kind == TRUE) {
			float haze = lane * smoothstep(0.3, 0.75, billow(fixed_stars * 5.0 + vec3(2.0), 5)) * (1.0 - 0.7 * smoothstep(0.48, 0.6, billow(fixed_stars * 8.0 + vec3(5.0), 4)));
			seen += paint(120.0, 126.0, 140.0) * haze * 0.16 * clear * (1.0 - 0.7 * moonlight);
		} else if (kind != AURORA) {
			float dust = billow(fixed_stars * 4.5 + vec3(2.0), 5);
			float river = lane * smoothstep(0.34, 0.7, dust);
			vec3 cold = paint(46.0, 92.0, 140.0);
			vec3 warm = paint(70.0, 120.0, 130.0);
			seen += mix(cold, warm, smoothstep(0.4, 0.62, billow(fixed_stars * 2.2 + vec3(9.0), 3))) * river * 0.24 * clear;
		}
		vec3 field = vec3(0.0);
		if (kind == TRUE) {
			field += stars(d, 110.0, 0.1 + 0.12 * lane, 0.0006, false, real, wash);
			field += stars(d, 70.0, 0.05, 0.0008, false, real, wash) * 1.2;
			field += stars(d, 24.0, 0.08, 0.001, false, real, wash) * 1.5;
		} else if (kind == BLOCKY) {
			field += stars(d, 64.0, 0.1 + 0.14 * lane, 0.0016, true, real, wash);
			field += stars(d, 19.0, 0.16, 0.0027, true, real, wash) * 1.3;
		} else {
			field += stars(d, 80.0, 0.14 + 0.3 * lane, 0.0012, false, real, wash);
			field += stars(d, 26.0, 0.22, 0.0016, false, real, wash) * 1.2;
		}
		if (kind == AURORA) seen += ribbons(d) * dark;
		seen += field * clear * (kind == AURORA ? 0.75 : 1.0);
	}
	if (close && moon_at.y > -0.12) {
		float away = acos(clamp(dot(d, moon_at), -1.0, 1.0));
		seen += paint(120.0, 150.0, 200.0) * (kind == TRUE ? 0.1 + 0.25 * moonlight : 0.2) * exp(-away * (kind == TRUE ? 5.0 : 9.0)) * dark;
		vec4 orb = moon(d, moon_at, kind == BLOCKY ? 0.062 : kind == TRUE ? 0.016 : 0.045);
		seen = mix(seen, orb.rgb * mix(0.75, 1.15, dark), orb.a * mix(0.45, 1.0, dark));
	}
	if (close && sun_at.y > -0.25) {
		float up = smoothstep(0.0, 0.35, sun_at.y);
		seen += sun(d, sun_at, mix(paint(255.0, 170.0, 96.0), paint(255.0, 246.0, 214.0), up), mix(0.05, 0.035, up)) * (1.0 - dark) * smoothstep(-0.25, -0.05, sun_at.y);
	}
	if (d.y > -0.02) {
		float lit_side = glow * mix(0.5, 1.0, facing);
		vec3 top = mix(mix(paint(246.0, 240.0, 224.0), night_mid * 1.9 + night_low * 0.3, dark), paint(238.0, 150.0, 104.0), lit_side);
		vec3 body = mix(mix(paint(214.0, 214.0, 212.0), night_mid * 1.5, dark), paint(120.0, 70.0, 98.0), glow * 0.9);
		vec3 under = mix(mix(paint(128.0, 146.0, 172.0), night_high * 1.4, dark), paint(56.0, 42.0, 80.0), glow * 0.9);
		vec3 towards = normalize(sun_at - d * dot(sun_at, d) + vec3(0.0, -0.0001, 0.0));
		vec4 heap = clouds(d, mix(0.53, 0.6, dark), top, body, under, towards, 3.6, 2.7);
		seen = mix(seen, heap.rgb, heap.a * mix(0.55, 1.0, smoothstep(0.0, 0.25, d.y)));
	}
	if (d.y < 0.0) {
		vec3 haze = banded(low, mid, high, 0.0, d);
		seen = mix(haze, mix(haze, mid, 0.5) * mix(0.6, 0.55, dark), smoothstep(0.0, -0.22, d.y));
	}
	COLOR = seen * (AT_CUBEMAP_PASS ? reflected : 1.0);
}
