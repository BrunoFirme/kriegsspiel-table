void stream_of(vec2 cell, out float direction, out float order, out vec2 node, out bool culvert) {
	ivec2 c = clamp(ivec2(cell), ivec2(0), ivec2(cells) - 1);
	vec4 t = texelFetch(heights, c, 0);
	int code = int(round(t.b));
	direction = float(code & 15);
	order = round(t.a);
	node = direction < float(NO_FLOW) ? (vec2(float((code >> 4) & 31), float((code >> 9) & 31)) / 31.0 - 0.5) * 0.9 : vec2(0.0);
	culvert = (code & CULVERT_BIT) != 0;
}

void current_of(vec2 cell, out vec2 heading, out float state) {
	ivec2 c = clamp(ivec2(cell), ivec2(0), ivec2(cells) - 1);
	int code = int(round(texelFetch(heights, c, 0).b));
	bool flows = (code & CURRENT_BIT) != 0;
	state = flows ? float((code >> 9) & 3) : 0.0;
	float a = float((code >> 4) & 31) / float(HEADINGS) * TAU;
	heading = flows ? vec2(cos(a), sin(a)) : vec2(0.0);
}

vec2 current_at(vec2 uv, out float rapids, out float fall) {
	vec2 q = clamp(uv, vec2(0.0), vec2(1.0)) * cells - 0.5;
	vec2 c = floor(q);
	vec2 f = q - c;
	vec2 sum = vec2(0.0);
	rapids = 0.0;
	fall = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y);
			vec2 h;
			float s;
			current_of(c + vec2(float(i), float(j)), h, s);
			sum += w * h;
			rapids += w * step(1.5, s);
			fall += w * step(2.5, s);
		}
	}
	return sum;
}

vec2 heading_world(vec2 h, mat2 from_uv) {
	vec2 a = from_uv * (h / cells);
	return length(a) > 1e-8 ? normalize(a) : vec2(0.0);
}

float flow_strokes(vec2 p, vec2 uv, mat2 to_uv, mat2 from_uv, float spacing, float len, float width, float keep, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 site = grid_point_at(p - spacing * 0.5, uv, to_uv, spacing, 0.6, i, j, id, salt) + spacing * 0.5;
			if (hash(id + 5.5) > keep) continue;
			float rapids, fall;
			vec2 h = current_at(uv + to_uv * (site - p), rapids, fall);
			if (length(h) < 0.3) continue;
			vec2 a = heading_world(h, from_uv);
			vec2 q = p - site;
			float along = dot(q, a);
			float across = dot(q, vec2(-a.y, a.x));
			float half_len = len * (0.35 + 0.3 * hash(id + 1.1));
			float t = clamp((along + half_len) / (2.0 * half_len), 0.0, 1.0);
			m = max(m, ink(length(vec2(max(abs(along) - half_len, 0.0), across)), width * (1.0 - 0.7 * t), fw));
		}
	}
	return m;
}

float current_arrows(vec2 p, vec2 uv, mat2 to_uv, mat2 from_uv, float spacing, float len, float width, float fw) {
	float m = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id;
			float salt;
			vec2 site = grid_point_at(p - spacing * 0.5, uv, to_uv, spacing, 0.4, i, j, id, salt) + spacing * 0.5;
			vec2 su = uv + to_uv * (site - p);
			if (distance_at(su) < len * 0.25) continue;
			float rapids, fall;
			vec2 h = current_at(su, rapids, fall);
			if (length(h) < 0.5) continue;
			vec2 a = heading_world(h, from_uv);
			vec2 n = vec2(-a.y, a.x);
			vec2 tail = site - a * len * 0.5;
			vec2 tip = site + a * len * 0.5;
			float barb = len * 0.3;
			float d = min(segment(p, tail, tip), min(segment(p, tip, tip - a * barb + n * barb * 0.6), segment(p, tip, tip - a * barb - n * barb * 0.6)));
			m = max(m, ink(d, width, fw));
		}
	}
	return m;
}

float fall_marks(vec2 p, vec2 uv, mat2 from_uv, float fw) {
	vec2 c0 = floor(clamp(uv, vec2(0.0), vec2(1.0)) * cells);
	float m = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = c0 + vec2(float(i), float(j));
			vec2 h;
			float s;
			current_of(c, h, s);
			if (s < 2.5) continue;
			vec2 a = heading_world(h, from_uv);
			vec2 q = p - (p + from_uv * ((c + 0.5) / cells - uv));
			float along = dot(q, a);
			float across = abs(dot(q, vec2(-a.y, a.x)));
			float span = 1.0 - smoothstep(cell_world * 1.2, cell_world * 1.5, across);
			m = max(m, max(ink(abs(along), W2 * 0.7, fw), ink(abs(along - FALL_STEP), W1 * 0.6, fw)) * span);
		}
	}
	return m;
}

vec3 current(int style, vec3 c, vec2 p, vec2 uv, mat2 to_uv, mat2 from_uv, float border, float fw) {
	float rapids, fall;
	vec2 h = current_at(uv, rapids, fall);
	vec3 tint = style == MODERN ? MODERN_BLUE : style == ANCIENT ? ANCIENT_INK : SURVEY_BLUE;
	float inside = smoothstep(0.02, 0.08, border);
	float m = 0.0;
	float calm = inside * (1.0 - smoothstep(0.35, 0.65, rapids));
	if (style == SURVEY) m = flow_strokes(p, uv, to_uv, from_uv, FLOW_SPACING, FLOW_LEN, W1 * 0.7, 0.7, fw) * 0.6 * calm;
	else if (style == ANCIENT) m = flow_strokes(p, uv, to_uv, from_uv, FLOW_SPACING * 1.4, FLOW_LEN * 0.6, W1 * 0.6, 0.5, fw) * 0.8 * calm;
	if (style == MODERN) {
		float arrow = current_arrows(p, uv, to_uv, from_uv, ARROW_SPACING, ARROW_LEN, M1 * 0.6, fw);
		c = mix(c, MODERN_PAPER, current_arrows(p, uv, to_uv, from_uv, ARROW_SPACING, ARROW_LEN, M1 * 1.6, fw) * (1.0 - arrow));
		m = max(m, arrow * 0.9);
	} else if (style == SURVEY) m = max(m, current_arrows(p, uv, to_uv, from_uv, ARROW_SPACING, ARROW_LEN, W1, fw) * 0.9);
	if (length(h) > 0.3) {
		vec2 a = heading_world(h, from_uv);
		m = max(m, hatch(dot(p, a) + (noise(p * 2.5) - 0.5) * RAPIDS_PITCH * 0.3, RAPIDS_PITCH, style == MODERN ? M1 * 0.5 : W1 * 0.7, fw) * smoothstep(0.35, 0.65, rapids) * inside * 0.9);
	}
	m = max(m, fall_marks(p, uv, from_uv, fw) * inside);
	return mix(c, tint, m);
}

vec2 flow_step(float direction) {
	int d = int(direction);
	if (d == 0) return vec2(1.0, 0.0);
	if (d == 1) return vec2(1.0, 1.0);
	if (d == 2) return vec2(0.0, 1.0);
	if (d == 3) return vec2(-1.0, 1.0);
	if (d == 4) return vec2(-1.0, 0.0);
	if (d == 5) return vec2(-1.0, -1.0);
	if (d == 6) return vec2(0.0, -1.0);
	return vec2(1.0, -1.0);
}

float stream_width(int style, float order) {
	float one = style == MODERN ? M1 * 0.7 : W1;
	return order >= 2.0 ? one * 1.8 : one;
}

float streams(int style, vec2 p, vec2 uv, mat2 from_uv, float fw, float mid, float road_w, out float order_here, out float culvert) {
	vec2 c0 = floor(clamp(uv, vec2(0.0), vec2(1.0)) * cells);
	float dir_here;
	vec2 node_here;
	bool culvert_here;
	stream_of(c0, dir_here, order_here, node_here, culvert_here);
	float tick_w = style == MODERN ? M1 * 0.5 : W1 * 0.8;
	float m = 0.0;
	culvert = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = c0 + vec2(float(i), float(j));
			float dir, order, dir_next, order_next;
			vec2 node, node_next;
			bool cul, cul_next;
			stream_of(c, dir, order, node, cul);
			if (order < 0.5 || dir >= float(NO_FLOW)) continue;
			vec2 next = c + flow_step(dir);
			stream_of(next, dir_next, order_next, node_next, cul_next);
			vec2 a = p + from_uv * ((c + 0.5 + node) / cells - uv);
			vec2 b = p + from_uv * ((next + 0.5 + node_next) / cells - uv);
			float d = segment(p, a, b);
			float w = stream_width(style, order);
			if (order >= 3.0) m = max(m, ink(abs(d - w * 1.6), w * 0.6, fw));
			else m = max(m, ink(d, w, fw));
			if ((cul || cul_next) && road_w >= 0.0) culvert = max(culvert, ink(abs(abs(mid) - road_w - CULVERT_GAP), tick_w, fw) * (1.0 - smoothstep(CULVERT_REACH - fw, CULVERT_REACH + fw, d)));
		}
	}
	return m;
}
