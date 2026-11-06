const int CIRCLE = 0;

uniform int kind = 0;
uniform vec2 center;
uniform float radius = 10.0;
uniform vec2 poly[512];
uniform int poly_n = 0;
uniform vec4 dabs[96];
uniform int dab_n = 0;
uniform vec4 shade : source_color = vec4(0.0, 0.0, 0.0, 0.6);

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

bool in_poly(vec2 p) {
	bool c = false;
	int j = poly_n - 1;
	for (int i = 0; i < 512; i++) {
		if (i >= poly_n) break;
		vec2 a = poly[i];
		vec2 b = poly[j];
		if (((a.y > p.y) != (b.y > p.y)) && (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)) c = !c;
		j = i;
	}
	return c;
}

bool in_sight(vec2 p) {
	float d = distance(p, center);
	bool inside;
	if (kind == CIRCLE) {
		inside = d <= radius;
	} else {
		bool no_radius = radius <= 0.0;
		inside = in_poly(p) && (no_radius || d <= radius);
	}
	for (int i = 0; i < 96; i++) {
		if (i >= dab_n) break;
		vec2 dab_centre = dabs[i].xy;
		float dab_radius = dabs[i].z;
		if (distance(p, dab_centre) < dab_radius) inside = false;
	}
	return inside;
}
