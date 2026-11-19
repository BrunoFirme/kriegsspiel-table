uniform sampler2D atlas : filter_linear, repeat_disable;
uniform sampler2D pages : filter_nearest, repeat_disable;
uniform float tile_levels = 0.0;
global uniform float atlas_slots;
uniform float tile_px = 256.0;
uniform float gutter_px = 2.0;
uniform float full_levels = 99.0;
uniform float deep_reach = 64.0;

vec4 page_entry(ivec2 at) {
	return round(texelFetch(pages, at, 0) * 255.0);
}

vec4 page_at(vec2 uv, float level) {
	int whole = int(exp2(full_levels));
	int reach = int(deep_reach);
	int a_row = whole / reach;
	for (float deep = level; deep > full_levels; deep -= 1.0) {
		int k = int(deep - full_levels) - 1;
		vec4 o = page_entry(ivec2(whole - 1, whole - 1 - k));
		vec2 local = floor(uv * exp2(deep)) - vec2(o.r + o.g * 256.0, o.b + o.a * 256.0);
		if (all(greaterThanEqual(local, vec2(0.0))) && all(lessThan(local, vec2(deep_reach)))) {
			return page_entry(ivec2(local) + ivec2(k % a_row * reach, whole / 2 + k / a_row * reach));
		}
	}
	float side = exp2(min(level, full_levels));
	return page_entry(ivec2(floor(uv * side)) + ivec2(int(side), 0));
}

vec4 tile_of(vec2 uv, vec4 e) {
	vec2 local = fract(uv * exp2(e.b));
	vec2 inner = (gutter_px + local * (tile_px - 2.0 * gutter_px)) / tile_px;
	return textureLod(atlas, (e.rg + inner) / atlas_slots, 0.0);
}

vec4 tile_at(vec2 uv, float level, vec4 missing) {
	vec4 e = page_at(uv, level);
	if (e.a < 0.5) return missing;
	vec4 t = tile_of(uv, e);
	if (e.a > 254.5 || e.b < 0.5) return t;
	vec4 under = page_at(uv, e.b - 1.0);
	if (under.a < 0.5) return t;
	return mix(tile_of(uv, under), t, e.a / 255.0);
}

float baked_level(vec2 uv, float level) {
	vec4 e = page_at(uv, level);
	return e.a < 0.5 ? -1.0 : e.b;
}
