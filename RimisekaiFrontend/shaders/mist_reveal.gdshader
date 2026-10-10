shader_type canvas_item;

// 继续游戏的入场雾（2026-10-11）：一层灰白浓雾盖住领地画面，progress 0→1 时自正中向四周化开、
// 边缘拉成丝向外飘散，最后整层散尽。只用骨白灰阶，不新增色相。
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec4 mist_color : source_color = vec4(0.82, 0.85, 0.83, 1.0);
uniform vec4 deep_color : source_color = vec4(0.07, 0.08, 0.08, 1.0);
uniform vec2 rect_size = vec2(1080.0, 2340.0);

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 5; i++) {
        v += a * noise(p);
        p = p * 2.03 + vec2(1.7, 9.2);
        a *= 0.5;
    }
    return v;
}

void fragment() {
    vec2 aspect = vec2(1.0, rect_size.y / rect_size.x);
    vec2 p = UV * aspect;
    vec2 c = vec2(0.5) * aspect;
    vec2 away = p - c;
    float dist = length(away) / length(c);
    // 雾团随进度向外推、向上飘：越往后丝越长。
    vec2 drift = normalize(away + 1e-4) * progress * 0.35 + vec2(0.0, -progress * 0.25);
    float n = fbm((p - drift) * 3.2 + TIME * 0.05);
    float wisps = fbm((p - drift * 1.6) * 7.0 - TIME * 0.08);
    // 化开门限：正中先散，四角最后；噪声把边缘撕成不规则的雾缘。
    float field = dist * 0.55 + n * 0.55 + wisps * 0.15;
    float edge = progress * 1.45 - 0.05;
    float alpha = smoothstep(edge - 0.02, edge + 0.22, field);
    // 浓处偏暗、雾缘偏亮：散开的那一圈有一道发亮的雾边。
    float rim = 1.0 - smoothstep(0.0, 0.18, abs(field - edge - 0.06));
    vec3 col = mix(deep_color.rgb, mist_color.rgb, clamp(n * 0.9 + rim * 0.6, 0.0, 1.0));
    COLOR = vec4(col, alpha);
}
