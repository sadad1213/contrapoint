shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform sampler2D source_texture : source_color, filter_linear, repeat_enable;
uniform int frame_count = 1;
uniform float frame_rate = 20.0;
uniform float scroll_rate = 0.05;
uniform vec4 tint : source_color = vec4(0.5, 0.5, 0.5, 0.78);
void fragment() {
	float frame = mod(floor(TIME * frame_rate), float(frame_count));
	vec2 uv = vec2((UV.x + frame) / float(frame_count), fract(UV.y - TIME * scroll_rate));
	vec4 smoke = texture(source_texture, uv);
	ALBEDO = smoke.rgb * tint.rgb;
	ALPHA = smoke.a * tint.a;
}
