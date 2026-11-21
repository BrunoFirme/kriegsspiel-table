uniform vec4 cover_boxes[8];
uniform vec2 cover_halves[8];
uniform int cover_count = 0;

bool under_higher_sheet(vec3 w) {
	for (int i = 0; i < cover_count; i++) {
		vec2 d = w.xz - cover_boxes[i].xy;
		vec2 along = cover_boxes[i].zw;
		if (abs(dot(d, along)) < cover_halves[i].x && abs(dot(d, vec2(-along.y, along.x))) < cover_halves[i].y) return true;
	}
	return false;
}
