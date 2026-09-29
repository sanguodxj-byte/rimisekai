shader_type canvas_item;
// 复古转场三：书页翻页。旧画面是一页从右边缘掀起的纸：
// 页缘附近有卷曲阴影与高光，页影投在新画面上。与铜版书页气质同源。
uniform sampler2D old_screen : filter_linear;
uniform float progress : hint_range(0.0, 1.0);

void fragment() {
	vec2 uv = SCREEN_UV;

	float curl = 0.22;                 // 卷曲影响区宽度
	float edge = 1.0 - progress * (1.0 + curl);  // 页缘 x 位置
	float d = uv.x - edge;             // 页缘右侧为旧页，左侧为新画面

	vec3 old = texture(old_screen, uv).rgb;
	vec3 neu = texture(SCREEN_TEXTURE, uv).rgb;

	// 旧页掀到页影阈值内才显示。
	vec3 col = mix(neu, old, smoothstep(-0.002, 0.002, d));

	// 卷曲阴影：页背面靠近页缘处压暗，外侧（已掀起的部分）稍提亮。
	float shade = smoothstep(-curl, 0.0, d) * (1.0 - smoothstep(0.0, curl * 0.6, d));
	col *= 1.0 - shade * 0.55;

	// 页影投在新画面上（页缘前方一条暗带）。
	float cast = smoothstep(0.0, 0.12, d) * (1.0 - smoothstep(0.12, 0.22, d));
	col *= 1.0 - cast * 0.3;

	// 页缘高光线。
	float rim = smoothstep(0.004, 0.0, abs(d));
	col += rim * 0.25;

	COLOR = vec4(clamp(col, 0.0, 1.0), 1.0);
}
