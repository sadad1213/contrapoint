shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D base_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D second_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D lightmap_texture : filter_linear, repeat_disable;
uniform vec3 tint = vec3(1.0);
uniform bool blend_ground = false;
uniform bool vertex_lighting = false;
uniform bool unlit = false;
uniform bool alpha_test = false;
uniform float alpha_cutoff = 0.5;
uniform float exposure = 1.0;

void fragment() {
	vec4 base = texture(base_texture, UV);
	if (blend_ground) {
		base = mix(base, texture(second_texture, UV), COLOR.a);
	}
	if (alpha_test && base.a < alpha_cutoff) {
		discard;
	}
	vec3 lighting = vertex_lighting ? COLOR.rgb : texture(lightmap_texture, UV2).rgb;
	if (unlit) { lighting = vec3(1.0); }
	ALBEDO = base.rgb * tint * lighting * exposure;
}
