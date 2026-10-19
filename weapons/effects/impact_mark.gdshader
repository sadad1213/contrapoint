shader_type spatial;
render_mode unshaded, blend_mul, cull_disabled, depth_draw_never, fog_disabled;

// Source DecalModulate: neutral grey multiplies the surface by one.
uniform sampler2D mark_texture : filter_linear_mipmap, repeat_disable;
uniform float opacity = 1.0;

void fragment() {
	vec4 mark = texture(mark_texture, UV);
	ALBEDO = mix(vec3(1.0), clamp(mark.rgb * 2.0, vec3(0.0), vec3(1.0)), mark.a * opacity);
#if CURRENT_RENDERER == RENDERER_MOBILE
	// Mobile encodes colour at half luminance in its default RGB10A2 target.
	// A multiplicative factor must reach blending unchanged: neutral stays one.
	ALBEDO *= 2.0;
#endif
	ALPHA = 1.0;
}
