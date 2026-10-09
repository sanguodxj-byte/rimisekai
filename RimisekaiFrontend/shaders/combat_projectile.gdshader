shader_type canvas_item;

// 战斗远射弹道 Shader：高速穿刺光束、能量箭羽与粒子拖尾。
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec4 color_beam : source_color = vec4(1.0, 0.85, 0.2, 1.0);
uniform float angle : hint_range(-3.1416, 3.1416) = 0.0;
// 光束粗细按 300px 方块标定；方块变大时按比例收细，保持屏幕上的粗细不变。
uniform float width_scale = 1.0;

void fragment() {
    vec2 uv = UV - vec2(0.5);

    float c = cos(-angle);
    float s = sin(-angle);
    vec2 r_uv = vec2(uv.x * c - uv.y * s, uv.x * s + uv.y * c);

    // 沿 X 轴穿刺光束：带高亮尖刺头部与长尾衰减
    float head_x = progress * 0.85 - 0.42;
    float dist_from_head = head_x - r_uv.x;

    if (dist_from_head < 0.0 || dist_from_head > 0.48) {
        discard;
    }

    float trail = 1.0 - (dist_from_head / 0.48);
    float beam_w = (0.035 * trail + 0.006) * width_scale;
    float dist_y = abs(r_uv.y) / beam_w;

    if (dist_y > 2.6) {
        discard;
    }

    float core = exp(-dist_y * dist_y * 7.0);
    float glow = exp(-dist_y * 1.6) * trail;

    vec4 col = mix(color_beam, vec4(1.0, 1.0, 1.0, 1.0), core);
    col.a = clamp(core + glow, 0.0, 1.0) * trail * sin(progress * 3.14159);

    if (col.a <= 0.01) {
        discard;
    }
    COLOR = col;
}
