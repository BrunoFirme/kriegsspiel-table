shader_type sky;

uniform sampler2D panorama : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D ground : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool halves = false;
uniform float energy = 1.0;

vec3 half_at(sampler2D picture, float x, float y, vec2 dx, vec2 dy) {
	return textureGrad(picture, vec2(x, clamp(y, 0.0005, 0.9995)), dx * vec2(1.0, 2.0), dy * vec2(1.0, 2.0)).rgb;
}

void sky() {
	vec2 uv = SKY_COORDS;
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);
	dx.x -= round(dx.x);
	dy.x -= round(dy.x);
	vec3 colour;
	if (!halves) {
		colour = textureGrad(panorama, uv, dx, dy).rgb;
	} else {
		float y = uv.y * 2.0;
		colour = y < 1.0 ? half_at(panorama, uv.x, y, dx, dy) : half_at(ground, uv.x, y - 1.0, dx, dy);
	}
	COLOR = colour * energy;
}
