shader_type sky;

uniform sampler2D panorama : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float energy = 1.0;
uniform bool pictured = false;
uniform float reflected = 1.0;
uniform vec3 above : source_color = vec3(0.5);
uniform vec3 around : source_color = vec3(0.5);
uniform vec3 beside : source_color = vec3(0.5);
uniform vec3 below : source_color = vec3(0.5);
uniform float above_curve = 0.15;
uniform float below_curve = 0.02;

vec3 plain(float rise) {
	if (rise < 0.0) return mix(beside, below, clamp(1.0 - pow(1.0 + rise, 1.0 / below_curve), 0.0, 1.0));
	return mix(around, above, clamp(1.0 - pow(1.0 - rise, 1.0 / above_curve), 0.0, 1.0));
}

void sky() {
	vec3 seen = plain(asin(clamp(EYEDIR.y, -1.0, 1.0)) / (PI * 0.5));
	if (pictured) {
		vec2 uv = SKY_COORDS;
		vec2 dx = dFdx(uv);
		vec2 dy = dFdy(uv);
		dx.x -= round(dx.x);
		dy.x -= round(dy.x);
		seen = textureGrad(panorama, uv, dx, dy).rgb;
	}
	COLOR = seen * energy * (AT_CUBEMAP_PASS ? reflected : 1.0);
}
