shader_type spatial;
render_mode cull_disabled, specular_disabled;

#include "res://shaders/ground_tiles.gdshaderinc"
#include "res://shaders/grid_lines.gdshaderinc"
#include "res://shaders/pending.gdshaderinc"

uniform vec3 paper = vec3(0.93, 0.9, 0.83);
uniform bool cut = false;

void fragment() {
	float level;
	vec4 t = ground_tiles(UV, cut ? vec4(0.0) : vec4(paper, 1.0), level);
	if (cut && t.a < 0.5) discard;
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz + table_shift;
	vec3 albedo = with_grid(with_pending(heights_tint(ground_lit(t), world, ground_gain), UV, ground_gain), world);
	float shade = cloud_shadow(world.xz);
	ROUGHNESS = 1.0;
	if (ground_relief > 0.0) {
		vec3 along_v;
		vec3 along_u = along_uv(VERTEX, NORMAL, UV, along_v);
		NORMAL = ground_relief_normal(UV, level, NORMAL, along_u, along_v);
		float water = water_at(UV) * t.a;
		if (water > 0.0) {
			float seen = ripple_seen(world.xz, length(VERTEX));
			NORMAL = normalize(mix(NORMAL, bumped(NORMAL, VERTEX, ripple_height(world.xz, seen)), water));
			ROUGHNESS = mix(1.0, mix(WATER_BLUR_ROUGH, WATER_ROUGH, seen), water);
			SPECULAR = mix(0.5, WATER_SPECULAR, water);
			albedo *= mix(1.0, WATER_DARK, water);
			EMISSION = water_glint(NORMAL, VERTEX, VIEW_MATRIX) * water * (1.0 - shade);
		}
	}
	ALBEDO = albedo * (1.0 - shade);
}
