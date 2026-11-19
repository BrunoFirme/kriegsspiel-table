shader_type particles;
render_mode disable_velocity, disable_force;

#include "res://shaders/ground_base.gdshaderinc"
#include "res://shaders/ground_cells.gdshaderinc"
#include "res://shaders/ground_pages.gdshaderinc"

uniform vec3 sheet_origin;
uniform vec3 sheet_u;
uniform vec3 sheet_v;
uniform vec3 sheet_up;
uniform vec2 uv_across;
uniform vec2 uv_down;
uniform vec2 sheet_size = vec2(1.0);
uniform int across = 120;
uniform float spacing_m = 0.2;
uniform float inner_m = 0.0;
uniform float outer_m = 12.0;
uniform bool last = true;
uniform float ring_scale = 1.0;

const float PAINT_TEXEL_M = 0.25;
const float HIGHEST_M = 30.0;
const float SUNK_M = 0.02;
const float HOUSE_ROOM_M = 0.4;
const float WAY_ROOM_M = 0.15;
const float STEEPEST = 1.0;
const float EDGE_THIN_FROM = 0.05;
const float EDGE_THIN_TO = 0.35;
const float BAND = 0.15;
const float SHRINK_FROM = 0.75;
const float GRASS_SHORT_M = 0.12;
const float GRASS_TALL_M = 0.34;
const float REED_SHORT_M = 0.45;
const float REED_TALL_M = 0.95;
const float REED_WIDTH = 0.8;
const float CROP_SHORT_M = 0.7;
const float CROP_TALL_M = 1.05;
const float CROP_WIDTH = 0.9;
const float CROP_DARK = 0.82;
const float CROP_LOSS = 0.15;
const float PATCH_M = 4.0;
const float PATCH_LOW = 0.6;
const float PATCH_HIGH = 1.25;
const float CROP_PATCH_LOW = 0.88;
const float CROP_PATCH_HIGH = 1.04;
const float MARSH_KEEP = 0.6;
const float GREEN_LOSS = 0.06;
const float TONE_LOW = 0.8;
const float TONE_HIGH = 1.14;
const vec3 LUMA_OF = vec3(0.299, 0.587, 0.114);
const float GRASS_CODE = 0.0;
const float REED_CODE = 1.0;
const float CROP_CODE = 2.0;
const float CODES = 2.0;
const float FALLOW_DRIFT = -0.04;

vec2 uv_at(vec2 xz) {
	vec2 d = xz - sheet_origin.xz;
	return vec2(dot(uv_across, d), dot(uv_down, d));
}

float rise_at(vec2 uv) {
	return textureLod(heights, (clamp(uv, vec2(0.0), vec2(1.0)) * (cells - 1.0) + 0.5) / cells, 0.0).r;
}

vec3 ground_point(vec2 uv) {
	return sheet_origin + sheet_u * uv.x + sheet_v * uv.y + sheet_up * rise_at(uv);
}

bool grow(int i, vec3 from, out mat4 xf, out vec4 custom) {
	xf = mat4(0.0);
	custom = vec4(0.0);
	if (units_per_metre <= 0.0) return false;
	vec3 shift = vec3(table_shift.x, 0.0, table_shift.z);
	vec3 eye = from + shift;
	vec2 eye_uv = uv_at(eye.xz);
	if (eye.y - ground_point(clamp(eye_uv, vec2(0.0), vec2(1.0))).y > HIGHEST_M * units_per_metre) return false;
	float spacing = spacing_m * units_per_metre;
	vec2 cell = floor(eye.xz / spacing) + vec2(float(i % across), float(i / across)) - float(across / 2);
	vec2 at = (cell + hash2(cell * 0.913 + 17.0)) * spacing;
	float d = length(at - eye.xz) / units_per_metre;
	if (d < inner_m * (1.0 - BAND) || d > (last ? outer_m : outer_m * (1.0 + BAND))) return false;
	float fade = inner_m > 0.0 ? smoothstep(inner_m * (1.0 - BAND), inner_m * (1.0 + BAND), d) : 1.0;
	if (!last) fade *= 1.0 - smoothstep(outer_m * (1.0 - BAND), outer_m * (1.0 + BAND), d);
	if (hash(cell * 2.31 + 7.7) > fade) return false;
	vec2 uv = uv_at(at);
	if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) return false;
	float border;
	float margin;
	int beside;
	int k = kind_at(uv, border, margin, beside);
	vec2 near = built_at(uv);
	vec2 p = at * detail;
	if ((k == FIELD || k == MARSH) && near.r + (noise(p / (3.0 * units_per_metre) + 9.0) - 0.5) * 5.0 < CLOSES_M || k == FIELD && near.g + (noise(p / (2.0 * units_per_metre) + 4.0) - 0.5) * 2.0 < VERGE_M) k = GRASS;
	if (!(k == GRASS || k == MARSH || k == FIELD)) return false;
	if (near.r < HOUSE_ROOM_M || near.g < WAY_ROOM_M) return false;
	float keep = smoothstep(EDGE_THIN_FROM, EDGE_THIN_TO, margin);
	if (k == MARSH) keep *= MARSH_KEEP;
	if (hash(cell * 1.37 + 3.1) > keep) return false;
	vec2 step_uv = 1.0 / max(cells - 1.0, vec2(1.0));
	vec3 root = ground_point(uv);
	vec2 slope = vec2(rise_at(uv + vec2(step_uv.x, 0.0)) - rise_at(uv), rise_at(uv + vec2(0.0, step_uv.y)) - rise_at(uv)) / max(cell_world, 1e-6);
	if (length(slope) > STEEPEST) return false;
	if (snow_line > 0.0 && rise_at(uv) > snow_line) return false;
	float want = clamp(ceil(log2(sheet_size.x / units_per_metre / (PAINT_TEXEL_M * (tile_px - 2.0 * gutter_px)))), 0.0, tile_levels);
	vec4 paint = tile_at(uv, want, vec4(0.0));
	if (paint.a < 0.5) return false;
	vec3 painted = paint.rgb / paint.a;
	vec3 meant = palette[(ground_style == STAFF ? SURVEY : clamp(ground_style, SURVEY, MEDIEVAL)) * KINDS + k].rgb;
	float drift = (painted.r - painted.g) - (meant.r - meant.g);
	if (k == FIELD && drift < FALLOW_DRIFT) k = GRASS;
	else if (k == FIELD && (drift > CROP_LOSS || dot(painted, LUMA_OF) < dot(meant, LUMA_OF) * CROP_DARK)) return false;
	if ((k == GRASS || k == MARSH) && drift > GREEN_LOSS) return false;
	float pick = hash(cell + 5.0);
	float tall;
	float wide = 1.0;
	float code = GRASS_CODE;
	float patch = noise(at / (PATCH_M * units_per_metre));
	if (k == MARSH) {
		tall = mix(REED_SHORT_M, REED_TALL_M, pick) * mix(PATCH_LOW, PATCH_HIGH, patch);
		wide = REED_WIDTH;
		code = REED_CODE;
	} else if (k == FIELD) {
		tall = mix(CROP_SHORT_M, CROP_TALL_M, noise(uv * cells * 0.5 + 2.0)) * mix(CROP_PATCH_LOW, CROP_PATCH_HIGH, pick);
		wide = CROP_WIDTH;
		code = CROP_CODE;
	} else {
		tall = mix(GRASS_SHORT_M, GRASS_TALL_M, pick * pick) * mix(PATCH_LOW, PATCH_HIGH, patch);
	}
	if (last) tall *= 1.0 - smoothstep(outer_m * SHRINK_FROM, outer_m, d);
	float yaw = hash(cell + 9.0) * 6.2832;
	float s = units_per_metre * ring_scale * wide;
	root.y -= SUNK_M * units_per_metre;
	xf = mat4(vec4(cos(yaw) * s, 0.0, -sin(yaw) * s, 0.0), vec4(0.0, tall * units_per_metre, 0.0, 0.0), vec4(sin(yaw) * s, 0.0, cos(yaw) * s, 0.0), vec4(root - shift, 1.0));
	custom = vec4(to_linear(painted) * ground_gain * mix(TONE_LOW, TONE_HIGH, hash(cell + 13.0)), code / CODES);
	return true;
}

void process() {
	mat4 xf;
	vec4 custom;
	grow(int(INDEX), EMISSION_TRANSFORM[3].xyz, xf, custom);
	TRANSFORM = xf;
	CUSTOM = custom;
}
