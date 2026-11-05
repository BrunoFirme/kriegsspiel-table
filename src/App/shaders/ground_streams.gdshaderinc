void stream_of(vec2 cell, out float direction, out float order, out vec2 node) {
	ivec2 c = clamp(ivec2(cell), ivec2(0), ivec2(cells) - 1);
	vec4 t = texelFetch(heights, c, 0);
	int code = int(round(t.b));
	direction = float(code & 15);
	order = round(t.a);
	node = (vec2(float((code >> 4) & 31), float((code >> 9) & 31)) / 31.0 - 0.5) * 0.9;
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

float streams(int style, vec2 p, vec2 uv, mat2 from_uv, float fw, out float order_here) {
	vec2 c0 = floor(clamp(uv, vec2(0.0), vec2(1.0)) * cells);
	float dir_here;
	vec2 node_here;
	stream_of(c0, dir_here, order_here, node_here);
	float m = 0.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = c0 + vec2(float(i), float(j));
			float dir, order, dir_next, order_next;
			vec2 node, node_next;
			stream_of(c, dir, order, node);
			if (order < 0.5 || dir >= float(NO_FLOW)) continue;
			vec2 next = c + flow_step(dir);
			stream_of(next, dir_next, order_next, node_next);
			vec2 a = p + from_uv * ((c + 0.5 + node) / cells - uv);
			vec2 b = p + from_uv * ((next + 0.5 + node_next) / cells - uv);
			float d = segment(p, a, b);
			float w = stream_width(style, order);
			if (order >= 3.0) m = max(m, ink(abs(d - w * 1.6), w * 0.6, fw));
			else m = max(m, ink(d, w, fw));
		}
	}
	return m;
}
