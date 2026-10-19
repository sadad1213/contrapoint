shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform sampler2D sheet : filter_linear, repeat_disable;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
instance uniform vec4 tint = vec4(1.0);
void fragment() {
	vec4 normal_tex = texture(sheet, UV);
	vec2 bend = normal_tex.rg * 2.0 - 1.0;
	float mask = normal_tex.a * smoothstep(0.5, 0.25, length(UV - 0.5));
	ALBEDO = textureLod(screen_texture, SCREEN_UV - bend * 0.025 * tint.a * mask, 0.0).rgb;
	ALPHA = mask * tint.a;
}
