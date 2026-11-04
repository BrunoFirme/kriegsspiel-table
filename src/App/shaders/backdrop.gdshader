shader_type sky;

uniform sampler2D panorama : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool sky_only = false;
uniform bool mirrored = false;
uniform vec3 below : source_color = vec3(0.05);
uniform float reflection = 0.55;
uniform float energy = 1.0;
uniform float fade = 0.03;

vec3 sky_at(float x, float y, vec2 dx, vec2 dy) {
	return textureGrad(panorama, vec2(x, clamp(y, 0.0005, 0.9995)), dx * vec2(1.0, 2.0), dy * vec2(1.0, 2.0)).rgb;
}

void sky() {
	vec2 uv = SKY_COORDS;
	vec2 dx = dFdx(uv);
	vec2 dy = dFdy(uv);
	dx.x -= round(dx.x);
	dy.x -= round(dy.x);
	vec3 colour;
	if (!sky_only) {
		colour = textureGrad(panorama, uv, dx, dy).rgb;
	} else {
		float y = uv.y * 2.0;
		if (y < 1.0) {
			colour = sky_at(uv.x, y, dx, dy);
			if (!mirrored) colour = mix(colour, below, smoothstep(1.0 - fade, 1.0, y));
		} else if (mirrored) {
			colour = mix(sky_at(uv.x, 2.0 - y, dx, -dy) * reflection, below, smoothstep(1.0, 2.0, y));
		} else {
			colour = below;
		}
	}
	COLOR = colour * energy;
}
