shader_type spatial;
render_mode unshaded, blend_mul, depth_draw_never, cull_disabled;
uniform sampler2D base_texture : filter_linear_mipmap_anisotropic, repeat_disable;

void fragment() {
	// Source DecalModulate uses gamma-space two-times multiplication.
	vec3 multiplier = texture(base_texture, UV).rgb * 2.0;
	ALBEDO = pow(multiplier, vec3(2.2));
}
