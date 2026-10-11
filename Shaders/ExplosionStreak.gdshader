shader_type spatial;
render_mode blend_add, cull_disabled, unshaded, depth_draw_never, fog_disabled;

// Hot streaks shot out of the blast. Each quad points along its particle's motion (Align Y in
// the particle flags) and turns about that line to face the camera, so it is never seen edge-on.
// Drawn as a glowing beam: a white-hot core fading to soft orange edges, narrowing to a point.

uniform vec3 core_color : source_color = vec3(1.0, 0.95, 0.85);
uniform vec3 edge_color : source_color = vec3(1.0, 0.45, 0.1);
uniform float emission_strength = 10.0;

void vertex() {
	vec3 axis = MODEL_MATRIX[1].xyz;   // along the motion, as long as the streak
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 to_camera = normalize(CAMERA_POSITION_WORLD - origin);
	vec3 side = normalize(cross(axis, to_camera)) * length(MODEL_MATRIX[0].xyz);
	vec3 facing = normalize(cross(side, axis));
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(side, 0.0), vec4(axis, 0.0), vec4(facing, 0.0), vec4(origin, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	// UV.y runs from the outer tip (0) back to the blast (1).
	float tip = 1.0 - UV.y;
	float half_width = mix(1.0, 0.15, tip);
	float beam = clamp(1.0 - abs(UV.x * 2.0 - 1.0) / half_width, 0.0, 1.0);
	beam *= beam;
	// Fades in just off the blast and out toward the tip.
	float along = smoothstep(0.0, 0.15, tip) * (1.0 - smoothstep(0.4, 1.0, tip));
	float intensity = beam * along * COLOR.a;

	ALBEDO = mix(edge_color, core_color, beam) * emission_strength * intensity;
	ALPHA = clamp(intensity, 0.0, 1.0);
}
