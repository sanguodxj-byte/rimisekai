shader_type canvas_item;

// 战斗法术爆破 Shader：全彩能量冲击波环、放射性裂波与烈焰扩散。
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec4 color_core : source_color = vec4(1.0, 1.0, 0.8, 1.0);
uniform vec4 color_outer : source_color = vec4(1.0, 0.25, 0.05, 1.0);

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
    vec2 uv = UV - vec2(0.5);
    float dist = length(uv);
    float ang = atan(uv.y, uv.x);

    // 冲击波环膨胀扩散
    float ring_r = progress * 0.44 + 0.03;
    float ring_thick = 0.045 * (1.0 - progress * 0.5);

    // 放射状能量抖动
    float ray = sin(ang * 14.0 + hash(vec2(floor(ang * 7.0), 1.0)) * 6.0) * 0.022;
    float d = abs(dist - ring_r - ray);

    float ring = exp(-d * d / (ring_thick * ring_thick * 0.5));
    float blast_center = exp(-dist * 9.0) * (1.0 - progress);

    // 飞散星芒粒子
    float sparks = step(0.94, hash(uv * 32.0 + progress * 2.5)) * (1.0 - progress) * exp(-dist * 2.5);

    vec4 col = mix(color_outer, color_core, clamp(ring * 0.7 + blast_center, 0.0, 1.0));
    col.a = clamp(ring * 1.3 + blast_center * 1.1 + sparks, 0.0, 1.0) * (1.0 - progress);

    if (col.a <= 0.01) {
        discard;
    }
    COLOR = col;
}
