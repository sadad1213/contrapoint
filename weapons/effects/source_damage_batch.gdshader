shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

uniform sampler2D sheet : source_color, filter_linear_mipmap, repeat_disable;

void fragment() {
	vec4 tex = texture(sheet, UV);
	ALBEDO = tex.rgb * COLOR.rgb;
	ALPHA = tex.a * COLOR.a;
}
