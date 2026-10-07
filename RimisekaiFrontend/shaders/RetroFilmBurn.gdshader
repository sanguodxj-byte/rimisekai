shader_type canvas_item;
// 复古转场一：胶片烧灼。旧画面带门架晃动，被噪声孔洞逐块烧穿露出新画面，
// 烧灼前沿发亮，叠加竖直划痕与灰尘点，四角压暗角。全程只动明度，不引新色相。
uniform sampler2D old_screen : filter_linear;
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;
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

	// 纯净采样：严禁任何随机 UV 抖动或画面错位位移（彻底废除所谓门架晃动）。
	vec3 old = texture(old_screen, uv).rgb;
	vec3 neu = texture(SCREEN_TEXTURE, uv).rgb;

	// 纯净消散：基于平滑双频柏林噪声，旧画面干净自然地过渡到新画面。
	// 彻底废除高光火舌前沿（斜向亮白痕迹根源）、废除划痕、废除灰尘点、废除雪花噪点。
	float burn = vnoise(uv * vec2(10.0, 6.0)) * 0.7 + vnoise(uv * vec2(24.0, 15.0)) * 0.3;
	float th = progress * 1.3 - 0.15;
	float edge = 1.0 - smoothstep(th - 0.08, th + 0.08, burn);

	vec3 col = mix(old, neu, edge);

	COLOR = vec4(clamp(col, 0.0, 1.0), 1.0);
}
