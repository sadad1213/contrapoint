shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

uniform sampler2D sheet : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform bool soft_particle = false;
uniform bool dual_sequence = false;
instance uniform vec4 tint : source_color = vec4(1.0);
instance uniform vec4 frame_uv = vec4(0.0, 0.0, 1.0, 1.0);
instance uniform vec4 next_uv = vec4(0.0, 0.0, 1.0, 1.0);
instance uniform vec4 second_uv = vec4(0.0, 0.0, 1.0, 1.0);
instance uniform float frame_blend = 0.0;

void fragment() {
	vec4 a = texture(sheet, mix(frame_uv.xy, frame_uv.zw, UV));
	vec4 b = texture(sheet, mix(next_uv.xy, next_uv.zw, UV));
	vec4 tex = mix(a, b, frame_blend);
	if (dual_sequence) {
		vec4 detail = texture(sheet, mix(second_uv.xy, second_uv.zw, UV));
		tex.a *= detail.a;
	}
	float fade = 1.0;
	if (soft_particle) {
		float depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
		vec4 scene = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
		fade = clamp((-scene.z / scene.w + VERTEX.z) / 0.3, 0.0, 1.0);
	}
	ALBEDO = tex.rgb * tint.rgb;
	ALPHA = tex.a * tint.a * fade;
}
