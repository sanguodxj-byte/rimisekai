shader_type canvas_item;
// 复古转场一：胶片烧灼。旧画面带门架晃动，被噪声孔洞逐块烧穿露出新画面，
// 烧灼前沿发亮，叠加竖直划痕与灰尘点，四角压暗角。全程只动明度，不引新色相。
uniform sampler2D old_screen : filter_linear;
uniform float progress : hint_range(0.0, 1.0);
uniform float time_sec = 0.0;

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

	// 门架晃动：整幅旧画面随进度衰减地左右微晃。
	float weave = (hash21(vec2(floor(time_sec * 13.0), 7.0)) - 0.5) * 0.005 * (1.0 - progress);
	vec3 old = texture(old_screen, uv + vec2(weave, 0.0)).rgb;
	vec3 neu = texture(SCREEN_TEXTURE, uv).rgb;

	// 烧灼孔洞：两频噪声叠加，阈值随进度推进。
	float burn = vnoise(uv * vec2(14.0, 9.0)) * 0.65 + vnoise(uv * vec2(38.0, 25.0)) * 0.35;
	float th = progress * 1.35;
	float edge = smoothstep(th - 0.09, th + 0.09, burn);

	vec3 col = mix(old, neu, edge);

	// 烧灼前沿：阈值附近提亮一圈，像火舌。
	float rim = smoothstep(0.09, 0.0, abs(burn - th));
	col += rim * 0.35 * (1.0 - progress * 0.5);

	// 竖直划痕：随机列，随进度变多变亮。
	float sc = step(0.992, hash21(vec2(floor(uv.x * 260.0), floor(time_sec * 18.0))));
	col += sc * 0.5 * (1.0 - abs(progress * 2.0 - 1.0));

	// 灰尘点。
	float dust = step(0.9975, hash21(uv * vec2(640.0, 400.0) + floor(time_sec * 11.0)));
	col += dust * 0.45;

	// 颗粒与暗角。
	col += (hash21(uv * 1024.0 + time_sec) - 0.5) * 0.05;
	float vig = smoothstep(1.05, 0.35, length(uv - 0.5));
	col *= mix(1.0, vig, 0.55);

	COLOR = vec4(clamp(col, 0.0, 1.0), 1.0);
}
