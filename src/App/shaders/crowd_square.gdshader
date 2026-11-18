shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

uniform float pixel_size = 0.0;
uniform float adult_size = 0.0;

varying flat vec3 coat;

vec3 unpacked(float v) {
	float r = floor(v / 65536.0);
	float g = floor((v - r * 65536.0) / 256.0);
	float b = v - r * 65536.0 - g * 256.0;
	return pow(vec3(r, g, b) / 255.0, vec3(2.2));
}

void vertex() {
	coat = unpacked(COLOR.r);
	float own = length(MODEL_MATRIX[0].xyz);
	float far = PROJECTION_MATRIX[3][3] > 0.5 ? 1.0 / PROJECTION_MATRIX[1][1] : -(MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).z;
	float grown = adult_size > 0.0 ? adult_size : own;
	float size = own * max(1.0, far * pixel_size / max(grown, 0.000001));
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	VERTEX = vec3(VERTEX.x, VERTEX.z, 0.5) * size;
}

void fragment() {
	ALBEDO = coat;
}
