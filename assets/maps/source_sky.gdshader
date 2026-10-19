shader_type sky;
uniform sampler2D front_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D back_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D left_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D right_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D up_tex : source_color, filter_linear, repeat_disable;
uniform sampler2D down_tex : source_color, filter_linear, repeat_disable;

void sky() {
	vec3 d = vec3(EYEDIR.x, -EYEDIR.z, EYEDIR.y);
	vec3 a = abs(d);
	vec2 uv;
	vec3 color;
	if (a.z >= a.x && a.z >= a.y) {
		if (d.z > 0.0) {
			uv = vec2(d.x, -d.y) / a.z * 0.5 + 0.5;
			color = texture(up_tex, uv).rgb;
		} else {
			uv = vec2(d.y, -d.x) / a.z * 0.5 + 0.5;
			color = texture(down_tex, uv).rgb;
		}
	} else if (a.x >= a.y) {
		if (d.x > 0.0) {
			uv = vec2(d.y, -d.z) / a.x * 0.5 + 0.5;
			color = texture(front_tex, uv).rgb;
		} else {
			uv = vec2(-d.y, -d.z) / a.x * 0.5 + 0.5;
			color = texture(back_tex, uv).rgb;
		}
	} else {
		if (d.y > 0.0) {
			uv = vec2(-d.x, -d.z) / a.y * 0.5 + 0.5;
			color = texture(left_tex, uv).rgb;
		} else {
			uv = vec2(d.x, -d.z) / a.y * 0.5 + 0.5;
			color = texture(right_tex, uv).rgb;
		}
	}
	COLOR = color;
}
