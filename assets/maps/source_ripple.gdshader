shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform float age = 0.0;
uniform float strength = 0.6;
void fragment() {
	float radius = length(UV * 2.0 - 1.0);
	float ring = exp(-pow((radius - (0.08 + age * 0.85)) * 45.0, 2.0));
	ALBEDO = vec3(0.65, 0.72, 0.67);
	ALPHA = ring * max(0.0, 1.0 - age) * strength;
}
