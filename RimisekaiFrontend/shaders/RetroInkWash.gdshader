shader_type canvas_item;
// 复古转场四：墨迹扩散。两频噪声作阈值，旧画面被墨渍一块块吃掉，
// 前沿压深模拟墨晕，慢慢渗成新画面。与线稿界面的墨色同源。
uniform sampler2D old_screen : filter_linear;
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
uniform float progress : hint_range(0.0, 1.0);

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void fragment() {
	vec2 uv = SCREEN_UV;

	float field = vnoise(uv * vec2(7.0, 5.0)) * 0.7 + vnoise(uv * vec2(26.0, 17.0)) * 0.3;
	float th = progress * 1.5 - 0.25;
	// edge = 新画面占比：progress=0 全为旧画面、=1 全为新画面（墨渍吃掉的区域从无到有）。
	float edge = 1.0 - smoothstep(th - 0.12, th + 0.12, field);

	vec3 old = texture(old_screen, uv).rgb;
	vec3 neu = texture(SCREEN_TEXTURE, uv).rgb;
	vec3 col = mix(old, neu, edge);

	// 墨晕前沿：边缘自然压深，无额外回弹加亮白边。
	float front = smoothstep(0.12, 0.0, abs(field - th));
	col = mix(col, vec3(0.0), front * 0.4);

	COLOR = vec4(clamp(col, 0.0, 1.0), 1.0);
}
