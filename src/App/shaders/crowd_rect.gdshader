shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled;

uniform float pixel_size = 0.0;

varying flat vec3 coat;

vec3 unpacked(float v) {
	float r = floor(v / 65536.0);
	float g = floor((v - r * 65536.0) / 256.0);
	float b = v - r * 65536.0 - g * 256.0;
	return pow(vec3(r, g, b) / 255.0, vec3(2.2));
}

void vertex() {
	coat = unpacked(COLOR.r);
	float own = min(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[2].xyz));
	float far = PROJECTION_MATRIX[3][3] > 0.5 ? 1.0 / PROJECTION_MATRIX[1][1] : -(MODELVIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).z;
	VERTEX *= max(1.0, far * pixel_size / max(own, 0.000001));
}

void fragment() {
	ALBEDO = coat;
}
