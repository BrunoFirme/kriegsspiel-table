shader_type sky;

uniform sampler2D panorama : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float energy = 1.0;

void sky() {
	vec2 uv = SKY_COORDS;
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);
	dx.x -= round(dx.x);
	dy.x -= round(dy.x);
	COLOR = textureGrad(panorama, uv, dx, dy).rgb * energy;
}
