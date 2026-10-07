shader_type canvas_item;
// 复古转场二：CRT 换台。中段最重的雪花静态、竖直滚过的亮带、扫描线，
// 以及行撕裂位移——老电视换频道的全套动作。只动明度，不引新色相。
uniform sampler2D old_screen : filter_linear;
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
uniform float progress : hint_range(0.0, 1.0);
uniform float time_sec = 0.0;

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
	vec2 uv = SCREEN_UV;

	vec3 old = texture(old_screen, uv).rgb;
	vec3 neu = texture(SCREEN_TEXTURE, uv).rgb;
	vec3 col = mix(old, neu, smoothstep(0.0, 1.0, progress));

	// 柔和扫描线。
	col *= 0.92 + 0.08 * step(0.5, fract(uv.y * 540.0));

	COLOR = vec4(clamp(col, 0.0, 1.0), 1.0);
}
