shader_type canvas_item;

const float MARK = 64.0;
const float WASH = 32.0;
const vec3 GARDEN_MEAN = vec3(0.83, 0.79, 0.565);
const float GAUGE_M = 0.72;
const vec2 SHADE_TO = vec2(0.7071, 0.7071);

const int STREET = 1;
const int PAVING = 2;
const int ROAD = 3;
const int TRACK = 4;
const int YARD = 5;
const int FLAGGED = 6;
const int FLAGS = 7;
const int GRAVEL = 8;
const int TILE = 9;
const int BARREL = 10;
const int SLATE = 11;
const int THATCH = 12;
const int SHINGLE = 13;
const int LEAD = 14;
const int COPPER = 15;
const int MUD = 16;
const int TURF = 17;
const int STONE = 18;
const int FOLIAGE = 19;
const int WATER = 20;
const int GARDEN = 21;
const int MASONRY = 22;
const int PLANKS = 23;
const int CLOTH = 24;
const int HEWN = 25;
const int ROUNDED = 26;

varying float surface;
varying float washed;

float hash(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

vec2 hash2(vec2 p) {
	return vec2(hash(p), hash(p + vec2(17.31, 41.97)));
}

vec2 slant(vec2 i) {
	float a = hash(i) * 6.2832;
	return vec2(cos(a), sin(a));
}

float swell_of(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float a = dot(slant(i), f);
	float b = dot(slant(i + vec2(1.0, 0.0)), f - vec2(1.0, 0.0));
	float c = dot(slant(i + vec2(0.0, 1.0)), f - vec2(0.0, 1.0));
	float d = dot(slant(i + vec2(1.0, 1.0)), f - vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float grain(vec2 p) {
	return clamp(0.5 + swell_of(p) * 1.2, 0.0, 1.0);
}

float organic(vec2 q) {
	return clamp(0.5 + swell_of(q) * 0.8 + swell_of(q * 2.3 + 5.0) * 0.36 + swell_of(q * 4.9 + 9.0) * 0.16, 0.0, 1.0);
}

float cover(float sd, float e) {
	return 1.0 - smoothstep(-e, e, sd);
}

float seen(float fw, float size) {
	return 1.0 - smoothstep(0.45, 1.0, fw / size);
}

float segment(vec2 p, vec2 a, vec2 b) {
	vec2 pa = p - a;
	vec2 ba = b - a;
	return length(pa - ba * clamp(dot(pa, ba) / max(dot(ba, ba), 1e-8), 0.0, 1.0));
}

vec2 turned(vec2 q, float a) {
	return vec2(q.x * cos(a) - q.y * sin(a), q.x * sin(a) + q.y * cos(a));
}

float laid(vec2 q, vec2 size, float joint, float stagger, float fw, out float tone, out vec2 within) {
	float row = floor(q.y / size.y);
	float x = q.x / size.x + stagger * mod(row, 2.0) + (hash(vec2(row, 7.3)) - 0.5) * 0.3 * stagger;
	within = vec2(fract(x), fract(q.y / size.y));
	tone = hash(vec2(floor(x), row) + 3.1) - 0.5;
	vec2 edge = min(within, 1.0 - within) * size;
	return (1.0 - smoothstep(joint * 0.5, joint * 0.5 + max(fw, 0.003), min(edge.x, edge.y))) * clamp(joint / max(fw, 1e-5), 0.0, 1.0);
}

float cracked(vec2 q, float size, float width, float fw, out float tone) {
	vec2 g = q / size;
	vec2 cell = floor(g);
	float first = 8.0;
	float second = 8.0;
	tone = 0.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 c = cell + vec2(float(x), float(y));
			float d = length(g - c - hash2(c + 41.3));
			if (d < first) {
				second = first;
				first = d;
				tone = hash(c + 9.7) - 0.5;
			} else if (d < second) second = d;
		}
	}
	return (1.0 - smoothstep(width * 0.5, width * 0.5 + max(fw, 0.003), (second - first) * size * 0.5)) * clamp(width / max(fw, 1e-5), 0.0, 1.0);
}

bool strewn(vec2 q, float spacing, float keep, float salt, out vec2 local, out vec2 id) {
	vec2 g = q / spacing;
	float best = 1e9;
	bool any = false;
	local = vec2(0.0);
	id = vec2(0.0);
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 c = floor(g - 0.5) + vec2(float(i), float(j));
			if (hash(c + salt + 5.1) > keep) continue;
			vec2 o = c + 0.5 + (hash2(c + salt) - 0.5) * 0.7;
			if (length(g - o) >= best) continue;
			best = length(g - o);
			local = (g - o) * spacing;
			id = c + salt;
			any = true;
		}
	}
	return any;
}

vec3 stone(vec3 t, vec3 c, vec2 l, float r, float e, float amount) {
	float own = fract(r * 531.7) * 6.2832;
	vec2 lying = turned(l, own) * vec2(1.0, 1.0 + 0.6 * fract(r * 211.3));
	float a = atan(lying.y, lying.x);
	float rough = r * (0.8 + 0.13 * cos(a * 3.0 + own) + 0.07 * cos(a * 5.0 - own * 2.0));
	float body = cover(length(lying) - rough, e);
	float cast = cover(length(l - SHADE_TO * r * 0.35) - r * 0.85, e + r * 0.25);
	t = mix(t, t * 0.72, cast * (1.0 - body) * amount * 0.6);
	return mix(t, c * mix(0.98, 0.78, smoothstep(-0.5, 0.6, dot(l, SHADE_TO) / max(r, 1e-4))), body * amount);
}

vec3 litter(vec3 t, vec3 c, vec2 q, float fw, float much) {
	vec2 l;
	vec2 id;
	float fine = seen(fw, 0.03);
	if (fine <= 0.001) return t;
	if (strewn(q + 3.7, 0.55, 0.5, 1.0, l, id)) t = stone(t, c * (0.85 + 0.3 * hash(id + 7.0)), l, 0.012 + 0.04 * hash(id + 2.0) * hash(id + 4.0), fw, fine);
	if (strewn(q + 9.2, 0.7, 0.45 * much, 3.0, l, id)) {
		vec2 lie = turned(l, hash(id + 1.7) * 6.2832);
		t = mix(t, c * vec3(1.32, 1.24, 0.92), cover(segment(lie, vec2(-0.07, 0.0), vec2(0.07, 0.012)) - 0.006, fw) * fine * 0.8);
	}
	return t;
}

vec3 prints(vec3 t, vec2 q, float fw, float keep, float salt) {
	vec2 l;
	vec2 id;
	float fine = seen(fw, 0.05);
	if (fine <= 0.001 || !strewn(q + 13.1, 1.1, keep, salt, l, id)) return t;
	vec2 lie = turned(l, hash(id + 1.7) * 6.2832);
	float shoe = max(abs(length(lie) - 0.06) - 0.016, lie.x - 0.02);
	return mix(t, t * 0.78, cover(shoe, fw) * fine);
}

vec3 rutted(vec3 t, vec3 c, vec2 q, float fw, float gauge, float depth) {
	float wander = swell_of(vec2(q.x / 7.0, gauge * 3.0)) * 0.22;
	float track = abs(abs(q.y + wander) - gauge) + (organic(q * vec2(1.6, 9.0) + gauge) - 0.5) * 0.07;
	float worn = 0.45 + 0.55 * smoothstep(0.3, 0.7, organic(vec2(q.x / 2.4, q.y / 0.5) + gauge * 5.0));
	float groove = (1.0 - smoothstep(0.045, 0.085 + fw, track)) * worn;
	float near = seen(fw, 0.2);
	t = mix(t, c * vec3(0.74, 0.7, 0.64), groove * depth * near);
	t = mix(t, c * 1.1, (1.0 - smoothstep(0.0, 0.03, abs(track - 0.12))) * 0.3 * depth * worn * seen(fw, 0.05));
	float pool = groove * smoothstep(0.7, 0.76, organic(vec2(q.x / 1.9, q.y / 0.4) + gauge * 9.0));
	return mix(t, c * vec3(0.5, 0.5, 0.5), pool * 0.85 * near);
}

vec3 dirt(vec3 c, vec2 q, float fw, bool lane, float grassy, float stony) {
	vec2 l;
	vec2 id;
	vec3 t = c * (1.0 + (organic(q / 2.3) - 0.5) * 0.16 * seen(fw, 1.4));
	t *= 1.0 + (grain(q / 0.06 + 2.0) - 0.5) * 0.16 * seen(fw, 0.05) + (grain(q / 0.016 + 5.0) - 0.5) * 0.14 * seen(fw, 0.012);
	if (strewn(q + 1.3, 0.16, 0.45, 11.0, l, id)) t = stone(t, c * (0.8 + 0.4 * hash(id + 7.0)), l, 0.008 + 0.022 * hash(id + 2.0), fw, seen(fw, 0.02));
	if (lane) {
		t = rutted(t, c, q, fw, GAUGE_M, 0.75);
		t = rutted(t, c, q, fw, GAUGE_M * 3.0, 0.4);
		float middle = 1.0 - smoothstep(0.12, 0.42, abs(q.y + swell_of(vec2(q.x / 7.0, GAUGE_M * 3.0)) * 0.22));
		t = prints(t, q, fw, 0.75 * middle, 5.0);
		float sward = grassy * middle * smoothstep(0.3, 0.6, organic(q / 1.4 + 17.0));
		t = mix(t, c * vec3(0.72, 0.9, 0.55), sward * 0.75 * seen(fw, 0.5));
		t *= 1.0 + (grain(q / 0.03 + 2.0) - 0.5) * 0.3 * sward * seen(fw, 0.02);
	} else {
		float damp = smoothstep(0.68, 0.8, organic(q / 3.4 + 61.0));
		t = mix(t, c * vec3(0.66, 0.68, 0.72), damp * 0.6 * seen(fw, 0.6));
		t = prints(t, q, fw, 0.35, 7.0);
	}
	if (stony > 0.0) {
		float tone;
		float pack = cracked(q, 0.05, 0.008, fw, tone);
		float fine = seen(fw, 0.025) * stony;
		t *= 1.0 + tone * 0.34 * fine;
		t = mix(t, c * 0.62, pack * 0.5 * fine);
	}
	return litter(t, c, q, fw, lane ? 0.5 : 1.0);
}

vec3 setts(vec3 c, vec2 q, float fw, bool lane) {
	float tone;
	vec2 within;
	float joint = laid(q.yx + vec2(0.0, swell_of(q / 1.3) * 0.05), vec2(0.19, 0.12), 0.016, 0.5, fw, tone, within);
	float fine = seen(fw, 0.055);
	vec3 t = c * (1.0 + (organic(q / 1.9) - 0.5) * 0.16 * seen(fw, 1.2));
	t = mix(t, c * 0.86, smoothstep(0.6, 0.78, organic(q / 4.3 + 21.0)) * 0.5 * seen(fw, 2.0));
	t *= 1.0 + tone * 0.2 * fine + (0.5 - within.y) * 0.08 * fine;
	t = mix(t, c * 0.5, joint * 0.75 * fine);
	if (lane) {
		float kennel = 1.0 - smoothstep(0.05, 0.16, abs(q.y + swell_of(vec2(q.x / 9.0, 2.0)) * 0.06));
		t = mix(t, c * 0.68, kennel * 0.5 * seen(fw, 0.12));
		float worn = 1.0 - smoothstep(0.1, 0.3, abs(abs(q.y) - GAUGE_M));
		t *= 1.0 + worn * 0.07 * seen(fw, 0.3);
		vec2 l;
		vec2 id;
		if (strewn(q + 21.3, 4.5, 0.3, 9.0, l, id))
			t = mix(t, vec3(0.3, 0.24, 0.16), cover(length(l * vec2(1.0, 1.4)) - 0.07 - 0.05 * organic(l * 16.0 + id), fw + 0.005) * seen(fw, 0.06) * 0.9);
	}
	t *= 1.0 + (grain(q / 0.014 + 5.0) - 0.5) * 0.08 * seen(fw, 0.01);
	return litter(t, c, q, fw, 0.35);
}

vec3 slabs(vec3 c, vec2 q, float fw, bool lane) {
	float tone;
	vec2 within;
	float joint = lane ? cracked(q + (vec2(swell_of(q / 0.9), swell_of(q / 0.9 + 4.0))) * 0.12, 0.7, 0.03, fw, tone) : laid(q, vec2(1.3, 0.87), 0.03, 0.5, fw, tone, within);
	float fine = seen(fw, lane ? 0.2 : 0.4);
	vec3 t = c * (1.0 + (organic(q / 2.6) - 0.5) * 0.14 * seen(fw, 1.6));
	t *= 1.0 + tone * 0.26 * fine;
	t = mix(t, c * 0.5, joint * 0.8 * fine);
	if (lane) {
		float groove = 1.0 - smoothstep(0.03, 0.09, abs(abs(q.y) - GAUGE_M));
		t = mix(t, c * 0.66, groove * 0.55 * seen(fw, 0.12));
	}
	t = mix(t, c * vec3(1.06, 1.08, 0.94), smoothstep(0.66, 0.8, organic(q / 0.8 + 50.0)) * 0.25 * seen(fw, 0.2));
	t *= 1.0 + (grain(q / 0.016 + 5.0) - 0.5) * 0.08 * seen(fw, 0.01);
	return litter(t, c, q, fw, 0.2);
}

vec3 lapped(vec3 t, vec3 c, float within, float deep, float fine) {
	t = mix(t, c * 0.52, (1.0 - smoothstep(0.0, deep, within)) * 0.6 * fine);
	return t * (1.0 + (within - 0.5) * 0.09 * fine);
}

vec3 weathered(vec3 c, vec2 q, float fw) {
	vec3 t = c * (1.0 + (organic(q * vec2(1.1, 0.4) + 7.0) - 0.5) * 0.2 * seen(fw, 1.4));
	t = mix(t, c * vec3(0.78, 0.74, 0.7), smoothstep(0.55, 0.85, organic(q * vec2(2.0, 0.3) + 60.0)) * 0.28 * seen(fw, 0.7));
	return mix(t, c * vec3(0.8, 0.94, 0.62), smoothstep(0.7, 0.84, organic(q / 1.3 + 31.0)) * 0.3 * seen(fw, 0.5));
}

vec3 tiles(vec3 c, vec2 q, float fw, bool barrel) {
	float course = barrel ? 0.42 : 0.3;
	float wide = barrel ? 0.27 : 0.21;
	float fine = seen(fw, wide * 0.5);
	vec2 cell = vec2(floor(q.x / wide), floor(q.y / course));
	float across = fract(q.x / wide);
	float odd = hash(cell + 9.7);
	vec3 t = weathered(c, q, fw);
	t *= 1.0 + (hash(cell + 3.1) - 0.5) * 0.2 * fine;
	t = mix(t, c * vec3(0.7, 0.68, 0.66), step(0.93, odd) * fine * 0.8);
	t = mix(t, c * vec3(1.16, 1.04, 0.92), step(odd, 0.07) * fine * 0.7);
	t *= 1.0 + cos((across - (barrel ? 0.5 : 0.3)) * 6.2832) * (barrel ? 0.2 : 0.08) * fine;
	if (barrel) t = mix(t, c * 0.6, (1.0 - smoothstep(0.0, 0.1, across)) * 0.55 * fine);
	return lapped(t, c, fract(q.y / course), 0.16, fine);
}

vec3 coursed(vec3 c, vec2 q, float fw, vec2 size, float joint, float play, vec3 cast) {
	float tone;
	vec2 within;
	float gap = laid(q, size, joint, 0.5, fw, tone, within);
	float fine = seen(fw, min(size.x, size.y) * 0.5);
	vec3 t = weathered(c, q, fw);
	t *= 1.0 + tone * play * fine;
	t = mix(t, t * cast, step(0.2, tone) * fine);
	t = mix(t, c * 0.5, gap * 0.7 * fine);
	return lapped(t, c, within.y, 0.14, fine);
}

vec3 thatch(vec3 c, vec2 q, float fw) {
	vec3 t = c * (1.0 + (organic(q * vec2(2.0, 0.6) + 7.0) - 0.5) * 0.22 * seen(fw, 0.9));
	t *= 1.0 + (grain(vec2(q.x / 0.022, q.y / 0.8)) - 0.5) * 0.3 * seen(fw, 0.025);
	t *= 1.0 + (grain(vec2(q.x / 0.09, q.y / 1.7) + 4.0) - 0.5) * 0.16 * seen(fw, 0.1);
	float layer = fract(q.y / 0.95 + swell_of(vec2(q.x / 1.6, 2.0)) * 0.12);
	t = mix(t, c * 0.66, (1.0 - smoothstep(0.0, 0.13, layer)) * 0.5 * seen(fw, 0.25));
	t = mix(t, c * vec3(0.76, 0.76, 0.74), smoothstep(0.56, 0.8, organic(q / 2.2 + 40.0)) * 0.4 * seen(fw, 1.2));
	return mix(t, c * vec3(0.74, 0.92, 0.56), smoothstep(0.72, 0.84, organic(q / 0.9 + 31.0)) * 0.4 * seen(fw, 0.4));
}

vec3 sheeted(vec3 c, vec2 q, float fw, bool green) {
	vec3 t = c * (1.0 + (organic(q / 1.7 + 7.0) - 0.5) * 0.14 * seen(fw, 1.2));
	if (green) t = mix(t, c * vec3(0.86, 1.1, 1.0), smoothstep(0.4, 0.8, organic(q * vec2(3.0, 0.5) + 33.0)) * 0.4 * seen(fw, 0.8));
	float roll = (fract(q.x / 0.65) - 0.5) * 0.65;
	float fine = seen(fw, 0.14);
	t = mix(t, c * 1.22, cover(abs(roll + 0.014) - 0.012, fw) * fine * 0.8);
	t = mix(t, c * 0.66, cover(abs(roll - 0.02) - 0.012, fw) * fine * 0.8);
	float lap = fract(q.y / 2.2 + hash(vec2(floor(q.x / 0.65), 5.0)));
	t = mix(t, c * 0.7, (1.0 - smoothstep(0.0, 0.012, lap)) * 0.6 * fine);
	return mix(t, c * 0.8, smoothstep(0.55, 0.9, grain(vec2(q.x / 0.3, q.y / 2.0) + 9.0)) * 0.2 * seen(fw, 0.3));
}

vec3 rendered(vec3 c, vec2 q, float fw) {
	float tone;
	vec3 t = c * (1.0 + (organic(q / 1.5 + 3.0) - 0.5) * 0.16 * seen(fw, 1.2));
	t = mix(t, c * 1.08, smoothstep(0.55, 0.75, organic(q / 0.7 + 12.0)) * 0.4 * seen(fw, 0.3));
	t = mix(t, c * 0.62, cracked(q + swell_of(q / 0.6) * 0.2, 1.3, 0.012, fw, tone) * 0.5 * seen(fw, 0.06));
	t = mix(t, c * 0.8, smoothstep(0.7, 0.86, organic(q / 2.4 + 44.0)) * 0.35 * seen(fw, 0.8));
	return t * (1.0 + (grain(q / 0.02 + 5.0) - 0.5) * 0.08 * seen(fw, 0.012));
}

vec3 sward(vec3 c, vec2 q, float fw) {
	vec3 t = c * (1.0 + (organic(q / 1.2 + 3.0) - 0.5) * 0.24 * seen(fw, 1.0));
	t *= 1.0 + (grain(vec2(q.x / 0.02, q.y / 0.07)) - 0.5) * 0.3 * seen(fw, 0.02);
	vec2 l;
	vec2 id;
	if (strewn(q, 0.6, 0.3, 2.0, l, id)) t = mix(t, vec3(0.9, 0.88, 0.7), cover(length(l) - 0.025, fw) * seen(fw, 0.03));
	return t;
}

vec4 foliage(vec3 c, vec2 at, float fw) {
	vec2 cell = floor((at + 4.0) / 8.0);
	vec2 l = at - cell * 8.0;
	float own = hash(cell + 2.3) * 6.2832;
	float a = atan(l.y, l.x);
	float r = length(l);
	float edge = 0.84 + 0.08 * cos(a * 5.0 + own) + 0.05 * cos(a * 9.0 - own * 2.0) + (organic(l * 2.6 + cell) - 0.5) * 0.22;
	float clump = organic(l * 3.1 + cell * 3.0);
	float tuft = organic(l * 9.0 + cell * 5.0);
	float lit = dot(l, -SHADE_TO);
	vec3 t = c * (0.86 + lit * 0.26 + (clump - 0.5) * 0.5);
	t = mix(t, c * 0.5, smoothstep(0.6, 0.75, 1.0 - clump) * 0.5);
	t *= 1.0 + (tuft - 0.5) * 0.45 * seen(fw, 0.12);
	t = mix(t, c * vec3(1.25, 1.3, 0.95), smoothstep(0.7, 0.85, tuft) * smoothstep(-0.2, 0.5, lit) * 0.4 * seen(fw, 0.08));
	t *= 1.0 + (grain(l * 34.0 + cell * 7.0) - 0.5) * 0.5 * seen(fw, 0.04);
	t = mix(t, c * 0.45, smoothstep(0.7, 0.85, grain(l * 13.0 + cell * 9.0)) * 0.45 * seen(fw, 0.06));
	t *= 1.0 - smoothstep(edge - 0.22, edge, r) * 0.3 * smoothstep(-0.3, 0.6, -lit);
	return vec4(t, cover(r - edge, fw + 0.01));
}

vec3 pool(vec3 c, vec2 q, float fw) {
	vec3 t = c * (1.0 + (organic(q / 3.0 + 3.0) - 0.5) * 0.16 * seen(fw, 2.0));
	float ripple = sin(q.y / 0.14 + organic(q / 2.2 + 9.0) * 5.0) * smoothstep(0.4, 0.7, organic(q / 3.5 + 40.0));
	t *= 1.0 + ripple * 0.035 * seen(fw, 0.06);
	vec2 l;
	vec2 id;
	if (strewn(q, 0.8, 0.5 * smoothstep(0.6, 0.75, organic(q / 4.0 + 33.0)), 4.0, l, id)) {
		vec2 lie = turned(l, hash(id + 1.7) * 6.2832);
		t = mix(t, c * vec3(0.7, 1.05, 0.55), cover(max(length(lie) - 0.15, -(abs(lie.y) - lie.x * 0.35)), fw) * seen(fw, 0.08));
	}
	return t;
}

vec3 garden(vec3 c, vec2 q, float fw) {
	float bed = floor(q.y / 1.7);
	float inbed = fract(q.y / 1.7) * 1.7;
	float path = smoothstep(1.32, 1.4, inbed);
	float crop = hash(vec2(bed, 3.0));
	vec3 soil = c * vec3(0.72, 0.58, 0.46) * (1.0 + (grain(q / 0.07) - 0.5) * 0.16 * seen(fw, 0.04) + (organic(q / 1.1) - 0.5) * 0.14);
	vec3 t = mix(soil, c * vec3(1.08, 0.98, 0.78) * (1.0 + (organic(q / 0.7 + 8.0) - 0.5) * 0.12), path);
	float row = floor(inbed / 0.44);
	float off = abs(inbed - (row + 0.5) * 0.44);
	float apart = mix(0.24, 0.55, crop);
	float along = q.x / apart + hash(vec2(bed, row)) * 3.0;
	float size = apart * (0.26 + 0.16 * hash(vec2(floor(along), bed * 7.0 + row)));
	float plant = length(vec2((fract(along) - 0.5) * apart, off)) - size;
	vec3 leaf = c * mix(vec3(0.7, 1.02, 0.58), vec3(1.05, 1.12, 0.66), crop) * (0.9 + 0.3 * hash(vec2(floor(along), bed + row * 3.0)));
	t = mix(t, t * 0.74, cover(plant - 0.03, fw + 0.02) * (1.0 - path) * 0.5);
	t = mix(t, leaf, cover(plant, fw) * (1.0 - path) * step(row, 2.5));
	return mix(c * GARDEN_MEAN, t, seen(fw, 0.3));
}

vec3 planked(vec3 c, vec2 q, float fw) {
	float tone;
	vec2 within;
	float gap = laid(q.yx, vec2(2.6, 0.22), 0.014, 0.5, fw, tone, within);
	float fine = seen(fw, 0.1);
	vec3 t = c * (1.0 + tone * 0.26 * fine);
	t *= 1.0 + (grain(vec2(q.y / 0.5, q.x / 0.012)) - 0.5) * 0.14 * seen(fw, 0.012);
	return mix(t, c * 0.45, gap * 0.8 * fine);
}

vec3 cloth(vec3 c, vec2 q, float fw) {
	float fold = sin(q.y / 0.21 + swell_of(q / 0.9) * 2.4);
	vec3 t = c * (1.0 + fold * 0.07 * seen(fw, 0.08) + (organic(q / 0.8 + 3.0) - 0.5) * 0.14 * seen(fw, 0.5));
	float weave = grain(vec2(q.x / 0.006, q.y / 0.05)) + grain(vec2(q.x / 0.05, q.y / 0.006) + 7.0) - 1.0;
	t *= 1.0 + weave * 0.1 * seen(fw, 0.006);
	return mix(t, c * 0.8, smoothstep(0.62, 0.82, organic(q / 0.5 + 21.0)) * 0.25 * seen(fw, 0.2));
}

vec3 hewn(vec3 c, vec2 q, float fw) {
	vec3 t = c * (1.0 + (organic(q / 0.6 + 3.0) - 0.5) * 0.2 * seen(fw, 0.4));
	t *= 1.0 + (grain(q / 0.03 + 5.0) - 0.5) * 0.18 * seen(fw, 0.025) + (grain(q / 0.008 + 9.0) - 0.5) * 0.1 * seen(fw, 0.006);
	t = mix(t, c * vec3(1.08, 1.1, 0.92), smoothstep(0.68, 0.82, organic(q / 0.35 + 50.0)) * 0.35 * seen(fw, 0.15));
	return mix(t, c * 0.7, smoothstep(0.72, 0.86, organic(q / 0.5 + 77.0)) * 0.3 * seen(fw, 0.2));
}

vec3 rounded(vec3 c, vec2 at, float fw) {
	vec2 cell = floor((at + 4.0) / 8.0);
	vec2 l = at - cell * 8.0;
	vec3 t = c * (1.0 + dot(l, -SHADE_TO) * 0.2) * (1.0 + (grain(l * 9.0 + cell) - 0.5) * 0.16 * seen(fw, 0.2) + (grain(l * 31.0 + cell * 3.0) - 0.5) * 0.12 * seen(fw, 0.05));
	return t * (1.0 - smoothstep(0.55, 1.0, length(l)) * 0.22);
}

vec4 surfaced(int k, vec3 c, vec2 q, float fw) {
	if (k == STREET) return vec4(setts(c, q, fw, true), 1.0);
	if (k == PAVING) return vec4(setts(c, q, fw, false), 1.0);
	if (k == ROAD) return vec4(dirt(c, q, fw, true, 0.0, 0.0), 1.0);
	if (k == TRACK) return vec4(dirt(c, q, fw, true, 1.0, 0.0), 1.0);
	if (k == YARD) return vec4(dirt(c, q, fw, false, 0.0, 0.0), 1.0);
	if (k == GRAVEL) return vec4(dirt(c, q, fw, true, 0.0, 1.0), 1.0);
	if (k == FLAGGED) return vec4(slabs(c, q, fw, true), 1.0);
	if (k == FLAGS) return vec4(slabs(c, q, fw, false), 1.0);
	if (k == TILE) return vec4(tiles(c, q, fw, false), 1.0);
	if (k == BARREL) return vec4(tiles(c, q, fw, true), 1.0);
	if (k == SLATE) return vec4(coursed(c, q, fw, vec2(0.3, 0.22), 0.012, 0.16, vec3(0.94, 0.97, 1.06)), 1.0);
	if (k == SHINGLE) return vec4(coursed(c, q, fw, vec2(0.17, 0.3), 0.014, 0.32, vec3(1.06, 1.0, 0.9)), 1.0);
	if (k == STONE) return vec4(coursed(c, q, fw, vec2(0.62, 0.42), 0.02, 0.24, vec3(1.04, 1.03, 0.94)), 1.0);
	if (k == MASONRY) return vec4(coursed(c, q, fw, vec2(0.5, 0.26), 0.02, 0.2, vec3(1.04, 1.02, 0.96)), 1.0);
	if (k == THATCH) return vec4(thatch(c, q, fw), 1.0);
	if (k == LEAD) return vec4(sheeted(c, q, fw, false), 1.0);
	if (k == COPPER) return vec4(sheeted(c, q, fw, true), 1.0);
	if (k == MUD) return vec4(rendered(c, q, fw), 1.0);
	if (k == TURF) return vec4(sward(c, q, fw), 1.0);
	if (k == FOLIAGE) return foliage(c, q, fw);
	if (k == WATER) return vec4(pool(c, q, fw), 1.0);
	if (k == GARDEN) return vec4(garden(c, q, fw), 1.0);
	if (k == PLANKS) return vec4(planked(c, q, fw), 1.0);
	if (k == CLOTH) return vec4(cloth(c, q, fw), 1.0);
	if (k == HEWN) return vec4(hewn(c, q, fw), 1.0);
	if (k == ROUNDED) return vec4(rounded(c, q, fw), 1.0);
	return vec4(c, 1.0);
}

void vertex() {
	float code = round((1.0 - COLOR.a) * 255.0);
	surface = 0.0;
	washed = 0.0;
	if (UV.x > MARK * 0.5 && code >= 1.0 && code < 2.0 * WASH) {
		washed = step(WASH, code);
		surface = code - washed * WASH;
		COLOR.a = 1.0;
	}
}

void fragment() {
	int k = int(round(surface));
	if (k > 0) {
		vec2 q = UV - vec2(MARK, 0.0);
		float fw = max(length(dFdx(UV)), length(dFdy(UV)));
		vec4 made = surfaced(k, COLOR.rgb, q, fw);
		vec3 afar = k == GARDEN ? COLOR.rgb * GARDEN_MEAN : COLOR.rgb;
		COLOR = vec4(mix(afar, made.rgb, washed > 0.5 ? seen(fw, 0.5) : 1.0), made.a);
	}
}
