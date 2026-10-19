shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque;

// A small virtual studio reflection replaces Source's missing env_cubemap.
// Opaque, two-sided glass also closes the rear-facing lens disk on the AWP.
uniform sampler2D lens_texture : source_color, filter_linear_mipmap;

void fragment() {
	vec2 p = (UV - vec2(0.5)) * 2.0;
	vec3 reflected = normalize((INV_VIEW_MATRIX * vec4(reflect(-VIEW, normalize(NORMAL)), 0.0)).xyz);
	float sky = smoothstep(-0.4, 0.8, reflected.y);
	vec3 glass = mix(vec3(0.008, 0.012, 0.010), vec3(0.025, 0.034, 0.027), sky);
	glass += texture(lens_texture, UV).rgb * 0.35;
	vec2 highlight = (p - vec2(-0.28, -0.32) - reflected.xy * 0.12) * vec2(2.2, 4.8);
	float softbox = exp(-dot(highlight, highlight));
	float vignette = 1.0 - smoothstep(0.45, 1.05, length(p)) * 0.65;
	ALBEDO = (glass + vec3(0.075, 0.085, 0.070) * softbox) * vignette;
}
