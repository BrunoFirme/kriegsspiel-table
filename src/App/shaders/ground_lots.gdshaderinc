float metres(float m) {
	return m * units_per_metre;
}

vec2 levels_at(vec2 o, vec2 p, vec2 uv, mat2 to_uv) {
	vec2 u = clamp(uv + to_uv * (o - p), vec2(0.0), vec2(1.0));
	float b = texel_of(floor(u * cells)).b;
	return vec2(mod(b, 16.0), floor(b / 16.0)) / float(LEVEL_STEPS);
}

struct Lot {
	vec2 id;
	vec2 site;
	float edge;
	vec2 across;
	float salt;
	float room;
	vec2 levels;
};

Lot lot_at(vec2 p, vec2 uv, mat2 to_uv, float size, float jitter, int k, float salt, vec2 levels) {
	Lot l;
	vec2 n = floor(p / size);
	vec2 f = p / size - n;
	float f1 = 8.0;
	vec2 near = vec2(0.0);
	vec2 sites[9];
	l.id = vec2(0.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 cell = seeded(n + g, salt);
			vec2 o = g + 0.5 + (hash2(cell) - 0.5) * jitter;
			sites[(j + 1) * 3 + i + 1] = o;
			float d = length(o - f);
			if (d < f1) { f1 = d; l.id = cell; near = o; }
		}
	}
	float best = 8.0;
	vec2 toward = vec2(1.0, 0.0);
	for (int s = 0; s < 9; s++) {
		vec2 o = sites[s];
		if (dot(o - near, o - near) < 1e-6) continue;
		float d = abs(dot(f - (near + o) * 0.5, normalize(o - near)));
		if (d < best) { best = d; toward = normalize(o - near); }
	}
	l.site = (n + near) * size;
	l.edge = best * size;
	l.across = vec2(-toward.y, toward.x);
	l.room = room_at(l.site, p, uv, to_uv, k, l.salt);
	l.levels = levels;
	return l;
}

float block(vec2 p, vec2 c, vec2 half_size, float turn, float fw) {
	vec2 d = abs(mat2(vec2(cos(turn), sin(turn)), vec2(-sin(turn), cos(turn))) * (p - c)) - half_size;
	return 1.0 - smoothstep(-fw * 0.5, fw * 0.5, max(d.x, d.y));
}

float dashed(float along, float across, float period, float share, float width, float fw) {
	float on = step(fract(along / period), share);
	return ink(across, width, fw) * mix(on, share, far_blend(fw, period));
}

float edge_line(int style, int kind, Lot l, vec2 p, float fw, float w1, float w2, float w3) {
	float along = dot(p - l.site, l.across);
	if (kind == HEDGE) {
		float bumps = metres(2.5);
		float r = ink(length(vec2(l.edge, (fract(along / bumps) - 0.5) * bumps)), bumps * 0.45, fw);
		return style == SURVEY ? max(r, ink(l.edge, w1, fw)) : ink(l.edge, w1, fw);
	}
	if (kind == FENCE) {
		float ticks = metres(6.0);
		float tick = ink(length(vec2(max(0.0, l.edge - w1 * 0.5), (fract(along / ticks) - 0.5) * ticks)), w1, fw) * step(l.edge, metres(2.0));
		float post = ink(max(abs(l.edge), abs((fract(along / ticks) - 0.5) * ticks)), w1 * 1.6, fw);
		if (style == ANCIENT) return ink(l.edge, w1, fw);
		return style == SURVEY ? max(ink(l.edge, w2 * 0.7, fw), post) : max(ink(l.edge, w1, fw), tick * 0.9);
	}
	if (kind == WALL) {
		float sides = max(ink(abs(l.edge - w1), w1 * 0.6, fw), ink(abs(l.edge + w1), w1 * 0.6, fw));
		return style == SURVEY ? max(sides, ink(l.edge, w1, fw) * step(0.5, fract(along / metres(8.0)))) : sides;
	}
	if (kind == DITCH) return dashed(along, l.edge, metres(8.0), 0.6, w1, fw);
	return max(dashed(along, abs(l.edge - w2), metres(10.0), 0.55, w1, fw), dashed(along, abs(l.edge + w2), metres(10.0), 0.55, w1, fw));
}

float dotted_line(vec2 p, vec2 uv, mat2 to_uv, float at, float spacing, float width, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			vec2 o = grid_point(p - spacing * 0.5, 0.0, spacing, 0.0, i, j, id) + spacing * 0.5;
			float there = distance_at(clamp(uv + to_uv * (o - p), vec2(0.0), vec2(1.0)));
			m = max(m, ink(length(p - o), width, fw) * step(abs(there - at), spacing * 0.5));
		}
	}
	return m;
}

float farmstead(vec2 p, Lot l, float fw, out float yard, out float wooden) {
	float turn = hash(l.id + 11.3) * 3.1416;
	float house = metres(12.0);
	vec2 c = l.site;
	mat2 r = mat2(vec2(cos(turn), sin(turn)), vec2(-sin(turn), cos(turn)));
	float a = block(p, c + r * vec2(0.0, house * 0.9), vec2(house * 0.75, house * 0.3), turn, fw);
	float b = block(p, c + r * vec2(house * 0.9, -house * 0.4), vec2(house * 0.28, house * 0.75), turn, fw);
	float d = block(p, c + r * vec2(-house * 0.9, -house * 0.5), vec2(house * 0.28, house * 0.6), turn, fw) * step(0.35, hash(l.id + 2.6));
	yard = block(p, c, vec2(house * 1.8, house * 1.6), turn, fw);
	wooden = max(b, d);
	return max(a, max(b, d));
}

float haystacks(vec2 p, Lot l, float fw) {
	float m = 0.0;
	for (int i = 0; i < 3; i++) {
		vec2 o = l.site + (hash2(l.id + float(i) * 4.7) - 0.5) * metres(30.0);
		m = max(m, ink(length(p - o), metres(2.0), fw) * step(float(i) * 0.3, hash(l.id + 8.8)));
	}
	return m;
}

float windmill(vec2 p, Lot l, float fw) {
	float r = metres(6.0);
	float a = hash(l.id + 3.3) * 1.57;
	vec2 d1 = vec2(cos(a), sin(a));
	vec2 d2 = vec2(-d1.y, d1.x);
	float sails = max(ink(segment(p, l.site - d1 * r, l.site + d1 * r), r * 0.12, fw), ink(segment(p, l.site - d2 * r, l.site + d2 * r), r * 0.12, fw));
	return max(sails, ink(length(p - l.site), r * 0.25, fw));
}

float orchard(vec2 p, Lot l, float dot, float fw) {
	float gap = metres(10.0);
	mat2 r = mat2(l.across, vec2(-l.across.y, l.across.x));
	vec2 q = r * (p - l.site);
	vec2 c = (floor(q / gap) + 0.5) * gap;
	return ink(length(q - c), gap * dot, fw);
}

float vine_rows(vec2 p, Lot l, vec2 down, float fw) {
	vec2 run = length(down) > 0.5 ? down : l.across;
	vec2 side = vec2(-run.y, run.x);
	float rows = metres(3.0);
	float stakes = metres(2.0);
	float s = dot(p - l.site, side);
	float t = dot(p - l.site, run);
	float line = ink(abs(fract(s / rows) - 0.5) * rows, rows * 0.06, fw);
	float stake = ink(length(vec2(abs(fract(s / rows) - 0.5) * rows, (fract(t / stakes) - 0.5) * stakes)), rows * 0.12, fw);
	return max(line * 0.5, stake);
}

float strips(vec2 p, Lot l, vec2 along, float follow, float fw, out float furrow) {
	float width = metres(mix(28.0, 58.0, hash(l.id + 4.4)));
	vec2 dir = l.across;
	dir = normalize(mix(dir, along * (dot(dir, along) < 0.0 ? -1.0 : 1.0), follow * step(0.5, length(along))));
	float s = dot(p - l.site, dir) / width + hash(l.id) * 10.0;
	float strip = floor(s);
	float edge = min(fract(s), 1.0 - fract(s)) * width;
	furrow = hatch(dot(p - l.site, vec2(-dir.y, dir.x)), metres(3.5), metres(0.3), fw) * step(0.6, hash(l.id + strip * 0.71 + 8.5));
	return ink(edge, metres(0.25), fw);
}

float islet(vec2 p, Lot l, float fw, out float body) {
	float r = metres(mix(12.0, 30.0, hash(l.id + 6.6)));
	float d = length(p - l.site);
	body = 1.0 - smoothstep(r - fw, r + fw, d);
	return ink(abs(d - r), metres(0.6), fw);
}

float pond(vec2 p, Lot l, float fw, out float body) {
	float r = metres(mix(8.0, 18.0, hash(l.id + 5.5)));
	float d = length((p - l.site) * vec2(1.0, 1.3));
	body = 1.0 - smoothstep(r - fw, r + fw, d);
	return ink(abs(d - r), metres(0.6), fw);
}

float lone_tree(vec2 p, Lot l, float fw, out float body) {
	float r = metres(6.0);
	float d = length(p - l.site);
	body = 1.0 - smoothstep(r - fw, r + fw, d);
	return max(ink(abs(d - r), metres(0.5), fw), ink(segment(p, l.site, l.site - NORTH * r * 1.3), metres(0.5), fw));
}

float hut(vec2 p, Lot l, float fw) {
	float s = metres(3.0);
	vec2 d = abs(p - l.site) - s;
	return ink(max(d.x, d.y), metres(0.4), fw) * step(max(d.x, d.y), s * 0.2);
}

float pit(vec2 p, Lot l, float fw) {
	float r = metres(10.0);
	vec2 q = p - l.site;
	float d = length(q);
	float rim = ink(abs(d - r), metres(0.5), fw) * step(0.0, q.y);
	float a = atan(q.y, q.x);
	float ticks = ink(abs(d - r * 0.8), metres(0.3), fw) * step(0.6, fract(a * 4.0)) * step(0.0, q.y);
	return max(rim, ticks);
}

float spruce(vec2 p, vec2 o, float r, float width, float fw) {
	vec2 top = o + NORTH * r * 1.3;
	float m = ink(segment(p, o, top), width, fw);
	for (int b = 0; b < 3; b++) {
		float at = 0.25 + 0.3 * float(b);
		float reach = r * (0.8 - 0.22 * float(b));
		vec2 from = mix(o, top, at);
		m = max(m, ink(segment(p, from, from + vec2(reach, r * 0.35)), width, fw));
		m = max(m, ink(segment(p, from, from + vec2(-reach, r * 0.35)), width, fw));
	}
	return m;
}

float conifer_share(vec2 o, float salt) {
	return smoothstep(0.42, 0.58, noise(o / max(metres(STAND_M), 1e-3) + salt * 0.37));
}

float clearing_at(vec2 o, float salt) {
	return smoothstep(0.78, 0.84, noise(o / max(metres(STAND_M * 0.8), 1e-3) + 4.1 + salt * 0.11));
}

float coppice_at(vec2 o, float salt) {
	return smoothstep(0.72, 0.8, noise(o / max(metres(STAND_M), 1e-3) + 9.3 + salt * 0.23));
}

float dunes(vec2 p, vec2 along, float fw) {
	if (length(along) < 0.5) return 0.0;
	return hatch(dot(p, vec2(-along.y, along.x)) + noise(p * 2.0) * metres(3.0), metres(12.0), metres(0.5), fw);
}
