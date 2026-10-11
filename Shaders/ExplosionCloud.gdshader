shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded;

// Smoke for a space explosion, drawn as soft camera-facing sprites. Many of them overlap into
// one cloud: none has an outline of its own, unlike solid puffs. Driven per particle by COLOR:
// COLOR.r is the glowing heat, COLOR.a the density as the smoke thins out. Unshaded, so the
// smoke keeps its own colour and only its faked lit side, whatever lights the scene.

uniform sampler2D cloud_tex : source_color;       // soft cloud mask (density)
uniform sampler2D noise_tex;                       // cellular/marble noise (heat and erosion)
uniform vec3 smoke_color : source_color = vec3(0.8, 0.79, 0.78);
uniform float emission_strength = 10.0;
uniform float cloud_speed = 0.2;                   // how fast the texture drifts inside a sprite
uniform float cloud_contrast = 4.0;                // lifts the dark cloud mask into density
uniform float opacity = 0.5;

// Fakes a lit and a shadowed side, as if each sprite were a sphere of smoke.
uniform vec3 light_dir = vec3(0.4, 0.7, 0.5);
uniform float shade_strength = 0.75;

varying float seed;

// Blackbody-ish temperature gradient: 1.0 = white-hot core, cooling through
// yellow and orange to deep red, then to unlit smoke at 0.
vec3 heat_color(float t) {
	vec3 red    = vec3(0.7, 0.06, 0.02);
	vec3 orange = vec3(1.0, 0.35, 0.08);
	vec3 yellow = vec3(1.0, 0.8, 0.3);
	vec3 white  = vec3(1.0, 0.95, 0.85);
	vec3 c = mix(red, orange, smoothstep(0.0, 0.4, t));
	c = mix(c, yellow, smoothstep(0.4, 0.7, t));
	c = mix(c, white, smoothstep(0.7, 1.0, t));
	return c;
}

void vertex() {
	// Faces the camera at the particle's own size, turned by its own angle.
	float a = INSTANCE_CUSTOM.x;
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	world = world * mat4(vec4(cos(a), -sin(a), 0.0, 0.0), vec4(sin(a), cos(a), 0.0, 0.0),
		vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world * mat4(
		vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	// Each sprite reads its own part of the textures, so no two look alike.
	seed = fract(float(INSTANCE_ID) * 0.6180339);
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);

	// Two noise layers drifting apart churn the inside; the cloud mask gives it body.
	vec2 offset = vec2(seed, fract(seed * 7.3));
	float turbulence = mix(texture(noise_tex, UV * 0.4 + offset + TIME * cloud_speed * 0.1).r,
		texture(noise_tex, UV * 0.8 - offset - TIME * cloud_speed * 0.07).r, 0.5);
	float cloud = clamp(texture(cloud_tex, UV * 0.5 + offset).r * cloud_contrast, 0.0, 1.0);

	// A soft round falloff that the noise eats into, so the edge frays instead of ending in a line.
	float falloff = 1.0 - smoothstep(0.0, 1.0, r + (turbulence - 0.5) * 0.3);
	float density = falloff * mix(0.75, 1.0, cloud) * mix(0.8, 1.0, turbulence);
	ALPHA = clamp(density * COLOR.a * opacity, 0.0, 1.0);

	// The sprite shaded as if it were a ball of smoke facing the camera.
	vec3 dome = normalize(vec3(p, sqrt(max(1.0 - r * r, 0.0)) + 0.3));
	vec3 light_view = normalize((VIEW_MATRIX * vec4(normalize(light_dir), 0.0)).xyz);
	float shade = mix(1.0 - shade_strength, 1.0, clamp(dot(dome, light_view) * 0.5 + 0.5, 0.0, 1.0));
	// Heat glows patchily, strongest in the middle of each sprite.
	float heat = COLOR.r * smoothstep(0.25, 0.85, turbulence) * falloff;
	ALBEDO = smoke_color * shade + heat_color(clamp(heat, 0.0, 1.0)) * emission_strength * heat * heat;
}
