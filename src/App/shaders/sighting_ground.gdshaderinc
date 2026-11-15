#include "res://shaders/sighting.gdshaderinc"
global uniform vec3 table_shift;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz + table_shift;
}
