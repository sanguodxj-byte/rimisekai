shader_type canvas_item;
// 复古转场二：CRT 换台。中段最重的雪花静态、竖直滚过的亮带、扫描线，
// 以及行撕裂位移——老电视换频道的全套动作。只动明度，不引新色相。
uniform sampler2D old_screen : filter_linear;
uniform float progress : hint_range(0.0, 1.0);
uniform float time_sec = 0.0;

float hash21(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
	vec2 uv = SCREEN_UV;

	// 中段行撕裂：整行水平错位。
	float tear = step(0.985, hash21(vec2(floor(uv.y * 90.0), floor(time_sec * 20.0))));
	float shift = tear * (hash21(vec2(floor(uv.y * 90.0), 3.0)) - 0.5) * 0.06
		* sin(progress * 3.14159);

	vec3 old = texture(old_screen, uv + vec2(shift, 0.0)).rgb;
	vec3 neu = texture(SCREEN_TEXTURE, uv + vec2(shift, 0.0)).rgb;
	vec3 col = mix(old, neu, smoothstep(0.0, 1.0, progress));

	// 雪花静态：换台中段最重。
	float stat = hash21(uv * vec2(720.0, 420.0) + floor(time_sec * 24.0));
	float statAmt = 0.45 * sin(progress * 3.14159);
	col = mix(col, vec3(stat), statAmt);

	// 竖直滚过的亮带。
	float roll = fract(uv.y - time_sec * 0.9);
	float bar = smoothstep(0.0, 0.06, roll) * smoothstep(0.16, 0.10, roll);
	col += bar * 0.18 * sin(progress * 3.14159);

	// 扫描线常驻。
	col *= 0.86 + 0.14 * step(0.5, fract(uv.y * 540.0));

	COLOR = vec4(clamp(col, 0.0, 1.0), 1.0);
}
