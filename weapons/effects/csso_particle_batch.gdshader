shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

uniform sampler2D sheet : source_color, filter_linear, repeat_disable;
uniform sampler2D frame_rects : filter_nearest, repeat_disable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform bool soft_particle = false;
uniform bool dual_sequence = false;
varying vec2 frame_coords;
varying vec2 next_coords;
varying vec2 second_coords;
varying flat float frame_blend;

void vertex() {
	// CPU selects frames with Source's variable frame durations and boundary rules.
	// texelFetch prevents interpolation between unrelated atlas rectangles.
	vec4 frame = texelFetch(frame_rects, ivec2(int(INSTANCE_CUSTOM.x), 0), 0);
	vec4 next = texelFetch(frame_rects, ivec2(int(INSTANCE_CUSTOM.y), 0), 0);
	vec4 second = texelFetch(frame_rects, ivec2(int(INSTANCE_CUSTOM.z), 0), 0);
	frame_coords = mix(frame.xy, frame.zw, UV);
	next_coords = mix(next.xy, next.zw, UV);
	second_coords = mix(second.xy, second.zw, UV);
	frame_blend = INSTANCE_CUSTOM.w;
}

void fragment() {
	// The instance color is read here, after Godot applies the MultiMesh color.
	vec4 tint = COLOR;
	vec4 a = texture(sheet, frame_coords);
	vec4 b = texture(sheet, next_coords);
	vec4 tex = mix(a, b, frame_blend);
	if (dual_sequence) {
		vec4 detail = texture(sheet, second_coords);
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
