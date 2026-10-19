shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled;
uniform sampler2D base_texture : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D lightmap_texture : filter_linear, repeat_disable;
uniform vec3 tint = vec3(1.0);
uniform bool unlit = false;

void fragment() {
	vec4 base = texture(base_texture, UV);
	ALBEDO = base.rgb * tint * (unlit ? vec3(1.0) : texture(lightmap_texture, UV2).rgb);
	ALPHA = base.a;
}
