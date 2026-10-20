shader_type spatial;
render_mode unshaded, cull_back;

uniform sampler2D normal_frames : filter_linear, repeat_disable;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform int frame_count = 29;
uniform float frame_rate = 16.0;
uniform vec3 fog_color = vec3(0.1, 0.12, 0.05);
uniform vec3 refract_tint = vec3(1.0);
uniform vec3 reflect_tint = vec3(1.0);
uniform float fog_start = 0.127;
uniform float fog_end = 1.27;
uniform float refract_amount = 0.1;
uniform vec3 sky_color = vec3(0.33, 0.43, 0.48);
uniform sampler2D reflection_map : source_color, filter_linear, repeat_disable;
uniform bool has_reflection_map = false;
uniform bool settings_simple_water = false;

varying vec3 world_position;

vec3 sample_reflection(vec3 d) {
	vec3 a = abs(d);
	vec2 uv;
	float face;
	if (a.x >= a.y && a.x >= a.z) {
		face = d.x > 0.0 ? 0.0 : 1.0;
		uv = vec2(d.x > 0.0 ? -d.z : d.z, -d.y) / a.x;
	} else if (a.y >= a.z) {
		face = d.y > 0.0 ? 2.0 : 3.0;
		uv = vec2(d.x, d.y > 0.0 ? d.z : -d.z) / a.y;
	} else {
		face = d.z > 0.0 ? 4.0 : 5.0;
		uv = vec2(d.z > 0.0 ? d.x : -d.x, -d.y) / a.z;
	}
	uv = clamp(uv * 0.5 + 0.5, vec2(0.008), vec2(0.992));
	return texture(reflection_map, vec2((face + uv.x) / 6.0, uv.y)).rgb;
}
void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (settings_simple_water) {
		ALBEDO = mix(fog_color, sky_color * reflect_tint, 0.15);
	} else {
	float frame = mod(floor(TIME * frame_rate), float(frame_count));
	vec2 flow_uv = fract(UV + vec2(0.007071) * TIME);
	vec3 ripple = texture(normal_frames, vec2((flow_uv.x + frame) / float(frame_count), flow_uv.y)).rgb * 2.0 - 1.0;
	vec2 offset = ripple.xy * refract_amount * 0.025;
	vec2 sample_uv = clamp(SCREEN_UV + offset, vec2(0.001), vec2(0.999));
	float depth = texture(depth_texture, sample_uv).r;
	vec4 scene = INV_PROJECTION_MATRIX * vec4(sample_uv * 2.0 - 1.0, depth, 1.0);
	float water_depth = max(0.0, -scene.z / scene.w + VERTEX.z);
	// Reject refraction samples in front of the surface (pool rims and plants).
	if (water_depth <= 0.001) {
		sample_uv = SCREEN_UV;
		depth = texture(depth_texture, sample_uv).r;
		scene = INV_PROJECTION_MATRIX * vec4(sample_uv * 2.0 - 1.0, depth, 1.0);
		water_depth = max(0.0, -scene.z / scene.w + VERTEX.z);
	}
	float fog = clamp((water_depth - fog_start) / max(fog_end - fog_start, 0.001), 0.0, 1.0);
	vec3 refracted = mix(texture(screen_texture, sample_uv).rgb * refract_tint, fog_color, fog);
	vec3 normal = normalize(NORMAL + vec3(ripple.x, ripple.y, 0.0) * 0.12);
	float fresnel = 0.02 + 0.98 * pow(1.0 - max(dot(normal, VIEW), 0.0), 5.0);
	vec3 world_reflection = mat3(INV_VIEW_MATRIX) * reflect(-VIEW, normal);
	// Source cubemaps use +X,+Y,+Z, while the world converts (x,z,-y).
	vec3 source_direction = vec3(world_reflection.x, -world_reflection.z, world_reflection.y);
	vec3 reflection = has_reflection_map ? sample_reflection(source_direction) : sky_color;
	ALBEDO = mix(refracted, reflection * reflect_tint, fresnel * 0.7);
	}
}
