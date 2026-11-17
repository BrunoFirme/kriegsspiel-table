uniform sampler2D pending : hint_default_black, filter_linear, repeat_disable;

const vec3 PENDING = vec3(1.0, 0.82, 0.2);
const float PENDING_WASH = 0.22;

vec3 with_pending(vec3 colour, vec2 uv, vec3 gain) {
	float share = texture(pending, uv).r;
	return share <= 0.0 ? colour : mix(colour, to_linear(PENDING) * gain, PENDING_WASH * share);
}
