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
// SETTINGS_BEGIN
uniform float settings_texture_bias = 0.0;
uniform int settings_filter_mode = 5;
uniform sampler2D settings_base_linear : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D settings_second_linear : source_color, filter_linear_mipmap, repeat_enable;

vec4 settings_sample(sampler2D anisotropic_tex, sampler2D linear_tex, vec2 uv) {
	vec2 dimensions = vec2(textureSize(anisotropic_tex, 0));
	vec2 dx = dFdx(uv) * dimensions;
	vec2 dy = dFdy(uv) * dimensions;
	float footprint = max(dot(dx, dx), dot(dy, dy));
	// Texture quality is a minimum mip level, including close-up surfaces.
	float lod = max(0.0, 0.5 * log2(max(footprint, 0.00001))) + settings_texture_bias;
	if (settings_filter_mode == 0) {
		return textureLod(linear_tex, uv, floor(lod + 0.5));
	}
	if (settings_filter_mode == 1) {
		return textureLod(linear_tex, uv, lod);
	}
	if (lod <= settings_texture_bias + 0.01) {
		return textureLod(anisotropic_tex, uv, settings_texture_bias);
	}
	return texture(anisotropic_tex, uv, settings_texture_bias);
}
// SETTINGS_END

void fragment() {
	vec4 base = settings_sample(base_texture, settings_base_linear, UV);
	if (blend_ground) {
		base = mix(base, settings_sample(second_texture, settings_second_linear, UV), COLOR.a);
	}
	if (alpha_test && base.a < alpha_cutoff) {
		discard;
	}
	vec3 lighting = vertex_lighting ? COLOR.rgb : texture(lightmap_texture, UV2).rgb;
	if (unlit) { lighting = vec3(1.0); }
	ALBEDO = base.rgb * tint * lighting * exposure;
}
