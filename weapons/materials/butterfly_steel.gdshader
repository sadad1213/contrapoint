shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx, ambient_light_disabled, fog_disabled;

// Installed CSSO knife VMT: base-alpha phong mask, exponent texture and
// env_cubemap tint .8. The reflection is a baked CSSO Dust II cubemap.
uniform sampler2D base_texture : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D exponent_texture : filter_linear_mipmap;
uniform sampler2D env_px : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D env_nx : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D env_py : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D env_ny : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D env_pz : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D env_nz : source_color, filter_linear_mipmap, repeat_disable;
varying float phong_exponent;
varying float phong_mask;

vec3 source_reflection(vec3 d, float lod) {
	vec3 a = abs(d);
	vec2 uv;
	if (a.x >= a.y && a.x >= a.z) {
		uv = vec2(d.x > 0.0 ? -d.z : d.z, -d.y) / a.x * .5 + .5;
		return d.x > 0.0 ? textureLod(env_px, uv, lod).rgb : textureLod(env_nx, uv, lod).rgb;
	}
	if (a.y >= a.z) {
		uv = vec2(d.x, d.y > 0.0 ? d.z : -d.z) / a.y * .5 + .5;
		return d.y > 0.0 ? textureLod(env_py, uv, lod).rgb : textureLod(env_ny, uv, lod).rgb;
	}
	uv = vec2(d.z > 0.0 ? d.x : -d.x, -d.y) / a.z * .5 + .5;
	return d.z > 0.0 ? textureLod(env_pz, uv, lod).rgb : textureLod(env_nz, uv, lod).rgb;
}

void fragment() {
	vec4 base = texture(base_texture, UV);
	float exponent = 1.0 + texture(exponent_texture, UV).r * 255.0;
	float mask = clamp(base.a * 4.0, 0.0, 1.0);
	phong_exponent = exponent;
	phong_mask = base.a;
	// The Source diffuse is cool-tinted too. Keep the steel neutral through
	// diffuse, ambient and direct specular, rather than only desaturating envmap.
	float base_luma = dot(base.rgb, vec3(.2126, .7152, .0722));
	ALBEDO = vec3(base_luma);
	ROUGHNESS = clamp(sqrt(2.0 / (exponent + 2.0)), .11, .3);
	// Use the authored cubemap and masked Phong highlights. The extra PBR sky
	// reflection made the blade blue even when the Source reflection was neutral.
	METALLIC = 0.0;
	SPECULAR = 0.0;
	vec3 reflected = (INV_VIEW_MATRIX * vec4(reflect(-VIEW, normalize(NORMAL)), 0.0)).xyz;
	vec3 source_direction = vec3(reflected.x, -reflected.z, reflected.y);
	float fresnel = mix(.45, 1.0, pow(1.0 - max(dot(normalize(NORMAL), VIEW), 0.0), 4.0));
	vec3 reflection = source_reflection(source_direction, ROUGHNESS * 5.0);
	float reflection_luma = dot(reflection, vec3(.2126, .7152, .0722));
	EMISSION = vec3(base_luma * .2 + reflection_luma * .8 * mask * fresnel);
}

void light() {
	float ndotl = max(dot(NORMAL, LIGHT), 0.0);
	float light_luma = dot(LIGHT_COLOR, vec3(.2126, .7152, .0722));
	DIFFUSE_LIGHT += vec3(light_luma) * ATTENUATION * ndotl / PI;
	vec3 half_vector = normalize(LIGHT + VIEW);
	float highlight = pow(max(dot(NORMAL, half_vector), 0.0), phong_exponent);
	vec3 steel_light = vec3(light_luma);
	SPECULAR_LIGHT += steel_light * ATTENUATION * highlight * phong_mask * 9.0 * step(0.001, ndotl);
}
